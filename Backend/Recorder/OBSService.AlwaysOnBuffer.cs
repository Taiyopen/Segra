using ObsKit.NET;
using Segra.Backend.App;
using Segra.Backend.Core.Models;
using Serilog;
using System.Text.Json;

namespace Segra.Backend.Recorder
{
    // The always-on display replay buffer: runs in slot 0 whenever no slot records or is about to record.
    // It is not a recording (no card, tray state or integrations); any recording start stops it first.
    public static partial class OBSService
    {
        public const int AlwaysOnBufferSlot = 0;
        private const string AlwaysOnBufferName = "Manual Recording";

        private sealed class AlwaysOnBufferInfo
        {
            public required DateTime StartTime { get; init; }
            public List<string>? AudioTrackNames { get; init; }
        }

        // Set while the buffer owns slot 0.
        private static volatile AlwaysOnBufferInfo? _alwaysOnBuffer;
        // Configuration it was last started with; kept after a failed start so it isn't retried unchanged.
        private static volatile string? _alwaysOnBufferKey;
        private static volatile bool _isExiting;
        // Recording starts between releasing _stopRecordingSemaphore and owning their slot; the buffer must not start then.
        private static int _recordingStartsInFlight;

        private static readonly object _alwaysOnPreviewLock = new();
        private static bool _alwaysOnPreviewActive;

        public static bool IsAlwaysOnBufferActive => _alwaysOnBuffer != null;

        /// <summary>
        /// Brings the always-on buffer in line with its setting: running while nothing records, restarted when its
        /// configuration changes. A failed start is only retried after the configuration changes, a recording has
        /// run or the setting is toggled. Safe to call from anywhere.
        /// </summary>
        public static void SyncAlwaysOnBuffer()
        {
            // Nothing to do while it's off, unless it is running or has a failed start to forget
            if (!Settings.Instance.AlwaysOnReplayBuffer && _alwaysOnBuffer == null && _alwaysOnBufferKey == null)
                return;

            _ = Task.Run(async () =>
            {
                await _stopRecordingSemaphore.WaitAsync();
                try
                {
                    bool shouldRun = AlwaysOnBufferPolicy.ShouldRun(
                        Settings.Instance.AlwaysOnReplayBuffer,
                        IsInitialized,
                        _isExiting,
                        anyRecording: IsRecorderBusyForAlwaysOnBuffer(),
                        anyPreRecording: AnyPreRecording());
                    string key = GetAlwaysOnBufferKey();

                    switch (AlwaysOnBufferPolicy.Decide(shouldRun, _alwaysOnBuffer != null, key, _alwaysOnBufferKey))
                    {
                        case AlwaysOnBufferAction.Stop:
                            await StopAlwaysOnBufferAsync(failed: false);
                            break;
                        case AlwaysOnBufferAction.Restart:
                            Log.Information("Always-on replay buffer settings changed, restarting it");
                            await StopAlwaysOnBufferAsync(failed: false);
                            StartAlwaysOnBufferCore(key);
                            break;
                        case AlwaysOnBufferAction.Start:
                            StartAlwaysOnBufferCore(key);
                            break;
                    }

                    // Forget a failed start once it's no longer wanted, so the next time it is gets a fresh try
                    if (!shouldRun)
                        _alwaysOnBufferKey = null;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to update the always-on replay buffer");
                }
                finally
                {
                    _stopRecordingSemaphore.Release();
                }
            });
        }

        // Caller holds _stopRecordingSemaphore.
        private static void StartAlwaysOnBufferCore(string key)
        {
            Log.Information("Starting always-on replay buffer");
            _alwaysOnBufferKey = key;

            bool started = false;
            try
            {
                started = StartRecordingCore(AlwaysOnBufferName, "Unknown", startManually: true, pid: null, reservedSlot: AlwaysOnBufferSlot, alwaysOn: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start the always-on replay buffer");
            }

            if (!started)
                Log.Warning("Always-on replay buffer did not start; it stays off until its settings change or a recording has run");
        }

        // Called by StartRecordingCore once the buffer output is running.
        private static void MarkAlwaysOnBufferStarted(List<string> audioTrackNames)
        {
            _alwaysOnBuffer = new AlwaysOnBufferInfo
            {
                StartTime = DateTime.Now,
                AudioTrackNames = audioTrackNames
            };
            AppState.Instance.AlwaysOnBufferActive = true;
            Log.Information("Always-on replay buffer started");
            SyncAlwaysOnPreview();
        }

        // Caller holds _stopRecordingSemaphore. Used by StartRecording to free slot 0 for a recording.
        private static Task StopAlwaysOnBufferCoreAsync() => StopAlwaysOnBufferAsync(failed: false);

        /// <summary>
        /// Stops the buffer and frees slot 0. Caller holds _stopRecordingSemaphore. A failed buffer keeps its key so it
        /// isn't restarted with the same configuration. Unlike StopRecordingSlotCore this leaves slot 0's
        /// pre-recording alone: a game may have just reserved the slot and be waiting for it.
        /// </summary>
        private static async Task StopAlwaysOnBufferAsync(bool failed)
        {
            if (!failed)
                _alwaysOnBufferKey = null;

            if (_alwaysOnBuffer == null)
                return;

            Log.Information("Stopping always-on replay buffer");
            await _slotLifecycleLocks[AlwaysOnBufferSlot].WaitAsync();
            _isStoppingSlot[AlwaysOnBufferSlot] = true;
            try
            {
                var pl = Pipeline(AlwaysOnBufferSlot);
                await WaitForInFlightReplaySaveAsync(AlwaysOnBufferSlot);
                if (pl.BufferOutput != null && !pl.BufferOutput.Stop(waitForCompletion: true, timeoutMs: 30000))
                {
                    Log.Warning("Always-on replay buffer did not stop within timeout. Forcing stop.");
                    pl.BufferOutput.ForceStop();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error stopping the always-on replay buffer");
            }
            finally
            {
                DisposeAlwaysOnBufferSlot();
                _isStoppingSlot[AlwaysOnBufferSlot] = false;
                _slotLifecycleLocks[AlwaysOnBufferSlot].Release();
            }
        }

        private static void DisposeAlwaysOnBufferSlot()
        {
            _alwaysOnBuffer = null;
            SyncAlwaysOnPreview();

            try
            {
                DisposeOutput(AlwaysOnBufferSlot);
                DisposeSources(AlwaysOnBufferSlot);
                DisposeEncoders(AlwaysOnBufferSlot);
                Obs.ClearOutputSource(0);
                DisposeRecordingCanvas(AlwaysOnBufferSlot);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error disposing the always-on replay buffer");
            }

            var pl = Pipeline(AlwaysOnBufferSlot);
            pl.HookedExecutableFileName = null;
            pl.EffectiveSettings = null;
            ClearCapturedWindowDimensions(AlwaysOnBufferSlot);
            _isHdrRecording = false;
            _hdrEncoderId = null;
            AppState.Instance.AlwaysOnBufferActive = false;
        }

        /// <summary>
        /// The buffer output stopped on its own or couldn't restart after a save. One that ran for a while likely hit
        /// something transient (e.g. a GPU reset) and gets one retry; one that fails soon after starting stays off.
        /// </summary>
        private static void OnAlwaysOnBufferFailed()
        {
            var failed = _alwaysOnBuffer;
            if (failed == null)
                return;

            _ = Task.Run(async () =>
            {
                bool retry = AlwaysOnBufferPolicy.ShouldRetryAfterFailure(DateTime.Now - failed.StartTime);
                Log.Warning("Always-on replay buffer failed after {Minutes:F1} min; {Next}",
                    (DateTime.Now - failed.StartTime).TotalMinutes, retry ? "retrying shortly" : "leaving it off until its settings change");

                await _stopRecordingSemaphore.WaitAsync();
                try
                {
                    if (_alwaysOnBuffer != failed)
                        return;

                    await StopAlwaysOnBufferAsync(failed: !retry);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to stop the failed always-on replay buffer");
                }
                finally
                {
                    _stopRecordingSemaphore.Release();
                }

                if (retry)
                {
                    await Task.Delay(AlwaysOnBufferPolicy.RetryDelay);
                    SyncAlwaysOnBuffer();
                }
            });
        }

        /// <summary>Stops the buffer before OBS is torn down and keeps it from starting again.</summary>
        private static void StopAlwaysOnBufferForExit()
        {
            _isExiting = true;
            if (_alwaysOnBuffer == null)
                return;

            // A wedged libobs must never keep Segra from exiting
            bool locked = _stopRecordingSemaphore.Wait(TimeSpan.FromSeconds(5));
            try
            {
                if (!Task.Run(StopAlwaysOnBufferCoreAsync).Wait(TimeSpan.FromSeconds(10)))
                    Log.Warning("Always-on replay buffer did not stop in time during shutdown");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping the always-on replay buffer during shutdown");
            }
            finally
            {
                if (locked)
                    _stopRecordingSemaphore.Release();
            }
        }

        /// <summary>
        /// The PiP window shows the buffer's preview only while it is open, so an idle Segra doesn't keep encoding
        /// preview frames nobody sees.
        /// </summary>
        public static void SyncAlwaysOnPreview()
        {
            bool wanted = _alwaysOnBuffer != null && Program.IsMonitoringWindowOpen;
            lock (_alwaysOnPreviewLock)
            {
                if (wanted == _alwaysOnPreviewActive)
                    return;
                _alwaysOnPreviewActive = wanted;
            }

            if (wanted)
                RecordingPreviewService.OnRecordingStarted((uint)Math.Max(1, Settings.Instance.FrameRate), AlwaysOnBufferSlot);
            else
                RecordingPreviewService.OnRecordingStopped(AlwaysOnBufferSlot);
        }

        // Anything recording, about to record, or still holding a slot's OBS objects keeps the buffer off.
        private static bool IsRecorderBusyForAlwaysOnBuffer()
        {
            if (Volatile.Read(ref _recordingStartsInFlight) > 0)
                return true;

            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (AppState.Instance.GetRecording(i) != null)
                    return true;
                if (i == AlwaysOnBufferSlot && _alwaysOnBuffer != null)
                    continue;
                if (SlotHasPipelineResources(i))
                    return true;
            }
            return false;
        }

        private static bool AnyPreRecording()
        {
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (AppState.Instance.GetPreRecording(i) != null)
                    return true;
            }
            return false;
        }

        // Everything the buffer is built from; any change restarts it
        private static string GetAlwaysOnBufferKey()
        {
            var s = Settings.Instance;
            return JsonSerializer.Serialize(new object?[]
            {
                s.Resolution, s.FrameRate, s.RateControl, s.Bitrate, s.MinBitrate, s.MaxBitrate, s.CrfValue, s.CqLevel,
                s.Encoder, s.Codec?.InternalEncoderId, s.EnableHdr, s.Stretch4By3, s.ReplayBufferDuration, s.ReplayBufferMaxSize,
                s.InputDevices, s.OutputDevices, s.ForceMonoInputSources, s.InputNoiseSuppression, s.EnableSeparateAudioTracks,
                s.AudioOutputMode, s.RecordingAudioBitrate, s.RecordingAudioTrackNames, s.GameAudioTrackMask, s.DiscordAudioTrackMask,
                s.GameAudioVolume, s.DiscordAudioVolume,
                s.SelectedDisplay, s.DisplayCaptureMethod, s.ContentFolder, AppState.Instance.Displays
            });
        }
    }
}
