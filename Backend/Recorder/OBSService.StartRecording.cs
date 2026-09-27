using ObsKit.NET;
using ObsKit.NET.Encoders;
using ObsKit.NET.Native.Types;
using ObsKit.NET.Outputs;
using ObsKit.NET.Sources;
using Segra.Backend.App;
using Segra.Backend.Core.Models;
using Segra.Backend.Games;
using Segra.Backend.Shared;
using Segra.Backend.Windows.Storage;
using Serilog;
using System.Diagnostics;
using static Segra.Backend.App.MessageService;
#if WINDOWS
using Segra.Backend.Windows.Display;
#endif

namespace Segra.Backend.Recorder
{
    // Starting a recording: builds one slot's scene, encoders and outputs, then starts them.
    public static partial class OBSService
    {
        public static bool StartRecording(string name = "Manual Recording", string exePath = "Unknown", bool startManually = false, int? pid = null, int? reservedSlot = null)
        {
            // Wait for pending StopRecording to complete before starting. Prevents race conditions where a new recording starts before cleanup finishes.
            // Also stops the always-on buffer so this recording can take slot 0.
            _stopRecordingSemaphore.Wait();
            try
            {
                // Counted while still holding the semaphore, so the buffer can't slip back into slot 0 before this start owns it
                Interlocked.Increment(ref _recordingStartsInFlight);
                Task.Run(StopAlwaysOnBufferCoreAsync).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop the always-on replay buffer before recording");
            }
            finally
            {
                _stopRecordingSemaphore.Release();
            }

            bool started = false;
            try
            {
                started = StartRecordingCore(name, exePath, startManually, pid, reservedSlot, alwaysOn: false);
                return started;
            }
            finally
            {
                Interlocked.Decrement(ref _recordingStartsInFlight);
                if (!started)
                    SyncAlwaysOnBuffer();
            }
        }

        /// <summary>
        /// Builds and starts one slot. <paramref name="alwaysOn"/> starts the always-on display buffer in slot 0 instead of a
        /// recording: Buffer mode, no disk checks, sounds, modals, recording card or integrations. Caller holds
        /// <c>_stopRecordingSemaphore</c> for an always-on start.
        /// </summary>
        private static bool StartRecordingCore(string name, string exePath, bool startManually, int? pid, int? reservedSlot, bool alwaysOn)
        {
            if (!IsOBSInstalled())
            {
                Log.Information("OBS is not installed. Skipping recording.");
                return false;
            }

            if (!IsInitialized)
            {
                Log.Information("OBS is not initialized. Skipping recording.");
                return false;
            }

            EffectiveRecordingSettings eff = GameSettingsService.Resolve(exePath);
            if (alwaysOn)
                eff.RecordingMode = RecordingMode.Buffer;

            bool isReplayBufferMode = eff.RecordingMode == RecordingMode.Buffer;

            string fileName = Path.GetFileName(exePath);

            // Prevent starting if any of the system, recording or temp drives are almost full.
            // The always-on buffer lives in memory until a save, so it skips this check.
            List<StorageService.FullDrive> fullDrives = alwaysOn ? [] : StorageService.GetFullDrives();
            if (fullDrives.Count > 0)
            {
                string drivesText = string.Join(", ", fullDrives.Select(d => $"{d.Label} ({d.Root.TrimEnd('\\')}) is {d.UsedPercent:F1}% full"));
                Log.Error($"Cannot start recording, drive(s) over {StorageService.DriveFullThresholdPercent:F0}% full: {drivesText}");
                GameDetectionService.PreventRetryRecording = true;
                Task.Run(() => ShowModal("Not enough disk space", $"Recording cannot start because {drivesText}. Free up some space and try again.", "error"));
                Task.Run(() => PlaySound("error"));
                ClearAllPendingPreRecordings();
                return false;
            }

            int slot;
            if (reservedSlot.HasValue)
            {
                slot = reservedSlot.Value;
                if (slot < 0 || slot >= MaxSessionSlots)
                {
                    Log.Information("Cannot start recording: invalid reserved slot {Slot}.", slot);
                    ClearAllPendingPreRecordings();
                    return false;
                }
                if (AppState.Instance.GetRecording(slot) != null)
                {
                    Log.Information("Cannot start recording: slot {Slot} already has an active recording.", slot);
                    if (!alwaysOn)
                        ClearPendingPreRecordingForSlot(slot);
                    return false;
                }
            }
            else
            {
                slot = AllocateSessionSlotIndex(startManually);
                if (slot < 0)
                {
                    Log.Information("Cannot start recording: no free output slot or invalid mode.");
                    ClearAllPendingPreRecordings();
                    return false;
                }
            }

            _slotLifecycleLocks[slot].Wait();
            try
            {
                if (!startManually && reservedSlot.HasValue && AppState.Instance.GetPreRecording(slot) == null)
                {
                    Log.Information("Start recording for slot {Slot} was cancelled.", slot);
                    return false;
                }

                _isStoppingSlot[slot] = false;
                var pl = Pipeline(slot);
                pl.EffectiveSettings = eff;
                pl.UnexpectedStopHandled = 0;
                bool anotherSlotActive = IsAnotherSlotRecording(slot);
                bool usesDedicatedCanvas = slot >= 1;
                uint? dedicatedCanvasWidth = null;
                uint? dedicatedCanvasHeight = null;

                // OBS video settings are global; slot 1+ uses a dedicated canvas and must not touch global video.
                // Slot 0 HDR + ResetVideoSettings runs below once HDR is decided.
                if (slot == 0 && anotherSlotActive)
                    Log.Information("Skipping global video settings change for slot {Slot}: another slot is already recording", slot);
                else if (slot != 0)
                    Log.Information("Skipping global video settings change for slot {Slot}: uses dedicated canvas", slot);

                // Secondary slots always resolve game dimensions before creating their canvas.
                if (!startManually && usesDedicatedCanvas)
                {
                    if (!WindowUtils.GetWindowDimensionsByPreRecordingExeOrPid(out uint windowWidth, out uint windowHeight, slot))
                    {
                        CleanupPartialSlot(slot);
                        return false;
                    }

                    (windowWidth, windowHeight) = EnsureEvenDimensions(windowWidth, windowHeight);
                    dedicatedCanvasWidth = windowWidth;
                    dedicatedCanvasHeight = windowHeight;
                    Log.Information(
                        "Using dedicated canvas dimensions for slot {Slot}: {Width}x{Height}",
                        slot, windowWidth, windowHeight);
                }

                // Decide HDR up front when this start owns the global canvas (slot 0, no other active).
                if (slot == 0 && !anotherSlotActive)
                {
                    DecideHdrForGlobalCanvas(eff, startManually);
                    ResetVideoSettings(out _, customFps: (uint)eff.FrameRate, customResolution: eff.Resolution);
                }

                CreateRecordingScene(slot, pl, dedicatedCanvasWidth, dedicatedCanvasHeight);

                // The helpers below clean up the partial slot themselves before returning false.
                if (!AddCaptureSources(slot, pl, eff, fileName, startManually, anotherSlotActive, usesDedicatedCanvas, dedicatedCanvasWidth, dedicatedCanvasHeight, alwaysOn))
                    return false;

                // Only slot 0 drives the main program mix. Additional slots record from their own canvas.
                if (slot == 0)
                    Obs.SetOutputSource(0, pl.MainScene);

                string encoderId = eff.Codec!.InternalEncoderId;
                if (_isHdrRecording && _hdrEncoderId != null && slot == 0 && !anotherSlotActive)
                    encoderId = _hdrEncoderId;
                Log.Information($"Using encoder: {encoderId}{(_isHdrRecording && slot == 0 && !anotherSlotActive ? " (HDR)" : "")}");

                using var videoEncoderSettings = new ObsKit.NET.Core.Settings();
                ConfigureVideoEncoderSettings(videoEncoderSettings, encoderId, eff);
                pl.VideoEncoder = new VideoEncoder(encoderId, ObsName(slot, "Segra Recorder"), videoEncoderSettings);

                AddAudioSources(slot, pl, eff);

                var (recordingTracksMask, actualAudioTrackNames) = CreateAudioEncoders(slot, pl);

                string? videoOutputPath = CreateOutputs(slot, pl, eff, name, recordingTracksMask);

                fileName = pl.HookedExecutableFileName ?? fileName;

                if (!StartOutputs(slot, pl, alwaysOn, out DateTime? startTime))
                    return false;

                // The always-on buffer is not a recording: no card, PiP, tray state, priority boost or integrations
                if (alwaysOn)
                {
                    MarkAlwaysOnBufferStarted(actualAudioTrackNames);
                    return true;
                }

                string? gameImage = GameIconUtils.ExtractIconAsBase64(exePath);

                var newRecording = new Recording()
                {
                    Slot = slot,
                    StartTime = startTime ?? DateTime.Now,
                    Game = name,
                    FilePath = videoOutputPath,
                    FileName = fileName,
                    Pid = pid,
                    IsUsingGameHook = IsSlotCaptureHooked(slot),
                    GameImage = gameImage,
                    ExePath = exePath,
                    CoverImageId = GameUtils.GetCoverImageIdFromExePath(exePath),
                    AudioTrackNames = actualAudioTrackNames,
                };
                AppState.Instance.SetRecording(slot, newRecording);

                ClearPendingPreRecordingForSlot(slot);
                Program.ShowMonitoringWindowIfClosed();
                uint previewFps = (uint)Math.Max(1, Settings.Instance.FrameRate);
                _ = Task.Run(async () =>
                {
                    await MessageService.SendSettingsToFrontend("OBS Start recording");
                    if (_isStoppingSlot[slot] || AppState.Instance.GetRecording(slot) == null)
                        return;
                    RecordingPreviewService.OnRecordingStarted(previewFps, slot);
                });

                Segra.Backend.App.NotifyIconService.SetNotifyIconStatus(Segra.Backend.App.NotifyIconState.Recording);

                Log.Information("Recording started: " + videoOutputPath);
                GeneralUtils.SetProcessPriority(ProcessPriorityClass.High);
                if (!isReplayBufferMode)
                {
                    _ = GameIntegrationService.Start(GameUtils.GetIgdbIdFromExePath(exePath), name, exePath, slot: slot);
                }
                else if (Settings.Instance.GameIntegrations.VrChat.Enabled &&
                         !string.IsNullOrEmpty(exePath) &&
                         exePath.EndsWith("VRChat.exe", StringComparison.OrdinalIgnoreCase))
                {
                    _ = GameIntegrationService.Start(GameUtils.GetIgdbIdFromExePath(exePath), name, exePath, slot: slot);
                }
                StartDiskSpaceMonitor();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "StartRecording failed for slot {Slot}", slot);
                // A game may have reserved slot 0 while the always-on buffer was starting; leave its reservation alone
                CleanupPartialSlot(slot, clearPreRecording: !alwaysOn);
                return false;
            }
            finally
            {
                _slotLifecycleLocks[slot].Release();
            }
        }

        /// <summary>Picks HDR or SDR for a start that owns the global canvas (slot 0 with no other slot recording).</summary>
        private static void DecideHdrForGlobalCanvas(EffectiveRecordingSettings eff, bool startManually)
        {
            _isHdrRecording = false;
            _hdrEncoderId = null;
#if WINDOWS
            try
            {
                if (!eff.EnableHdr)
                {
                    Log.Information("HDR recording is disabled in settings; recording in SDR.");
                }
                else
                {
                    string? hdrTargetDeviceId = startManually
                        ? GetCaptureTargetDeviceId()
                        : ResolveGameHdrTargetDeviceId();

                    if (DisplayConfigService.IsDisplayHdrActive(hdrTargetDeviceId))
                    {
                        string userEncoderId = eff.Codec?.InternalEncoderId ?? string.Empty;
                        string? hdrEncoderId = EncoderInfo.FindHdrCapable(userEncoderId)?.Id;
                        if (hdrEncoderId != null)
                        {
                            _isHdrRecording = true;
                            _hdrEncoderId = hdrEncoderId;
                            if (!string.Equals(hdrEncoderId, userEncoderId, StringComparison.OrdinalIgnoreCase))
                                Log.Information($"HDR display detected; using HDR-capable encoder '{hdrEncoderId}' instead of '{userEncoderId}'");
                            Log.Information("Recording in HDR (Rec.2100 PQ, 10-bit P010)");
                        }
                        else
                        {
                            Log.Warning("HDR display detected but no HDR-capable (HEVC/AV1) encoder is available; recording in SDR.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"HDR detection failed, recording in SDR: {ex.Message}");
                _isHdrRecording = false;
                _hdrEncoderId = null;
            }
#endif
        }

        /// <summary>
        /// Adds display capture (plus game capture unless manual) to the slot's scene and fits them to the canvas.
        /// Returns false, after cleaning up the slot, when the game window's size can't be found.
        /// </summary>
        private static bool AddCaptureSources(
            int slot,
            SessionPipeline pl,
            EffectiveRecordingSettings eff,
            string fileName,
            bool startManually,
            bool anotherSlotActive,
            bool usesDedicatedCanvas,
            uint? dedicatedCanvasWidth,
            uint? dedicatedCanvasHeight,
            bool alwaysOn = false)
        {
            // For manual recording, use display capture directly without game hooking
            if (startManually)
            {
                Log.Information(alwaysOn ? "Always-on replay buffer - using display capture" : "Manual recording started - using display capture");
                // The always-on buffer restarts often; a missing display shouldn't raise a modal each time
                AddMonitorCapture(slot, warnIfNotFound: !alwaysOn);
                pl.DisplayItem?.SetBounds(ObsBoundsType.ScaleInner, _currentBaseWidth, _currentBaseHeight).SetPosition(0, 0);
                return true;
            }

            AddMonitorCapture(slot);

            try
            {
                pl.GameCapture = new GameCapture($"gameplay_{slot}", GameCapture.CaptureMode.SpecificWindow);
                pl.GameCapture.SetWindow($"*:*:{fileName}");
                pl.GameCapture.Volume = eff.VolumeMultiplier;

                if (_isHdrRecording && slot == 0 && !anotherSlotActive)
                {
                    pl.GameCapture.Update(s => s.Set("rgb10a2_space", "2100pq"));
                    Log.Information("Game capture color space set to Rec.2100 PQ (HDR)");
                }

                if (Settings.Instance.AudioOutputMode != AudioOutputMode.All)
                {
                    pl.GameCapture.Update(s => s.Set("capture_audio", true));
                    Log.Information($"Game capture audio enabled (mode: {Settings.Instance.AudioOutputMode})");
                }

                Log.Information($"Game capture configured for: {fileName}");

                pl.GameCaptureItem = pl.MainScene!.AddSource(pl.GameCapture);

                StartGameCaptureHookTimeoutTimer(slot);

                pl.HookedSubscription = c => OnGameCaptureHookedEvent(c, slot);
                pl.UnhookedSubscription = c => OnGameCaptureUnhookedEvent(c, slot);
                pl.GameCapture.Hooked += pl.HookedSubscription;
                pl.GameCapture.Unhooked += pl.UnhookedSubscription;
            }
            catch (Exception ex)
            {
                Log.Warning($"Game Capture source not available: {ex.Message}. Using Display Capture only.");
                pl.GameCapture = null;
            }

            if (dedicatedCanvasWidth.HasValue && dedicatedCanvasHeight.HasValue)
            {
                pl.GameCaptureItem?.SetBounds(
                    ObsBoundsType.ScaleInner,
                    dedicatedCanvasWidth.Value,
                    dedicatedCanvasHeight.Value).SetPosition(0, 0);
                pl.DisplayItem?.SetBounds(
                    ObsBoundsType.ScaleInner,
                    dedicatedCanvasWidth.Value,
                    dedicatedCanvasHeight.Value).SetPosition(0, 0);
            }
            else if (WindowUtils.GetWindowDimensionsByPreRecordingExeOrPid(out uint windowWidth, out uint windowHeight, slot))
            {
                if (slot == 0 && !anotherSlotActive)
                {
                    ResetVideoSettings(
                        out bool is4by3,
                        customFps: (uint)eff.FrameRate,
                        customOutputWidth: windowWidth,
                        customOutputHeight: windowHeight,
                        customResolution: eff.Resolution
                    );

                    var boundsType = is4by3 ? ObsBoundsType.Stretch : ObsBoundsType.ScaleInner;
                    pl.GameCaptureItem?.SetBounds(boundsType, _currentBaseWidth, _currentBaseHeight).SetPosition(0, 0);
                    pl.DisplayItem?.SetBounds(boundsType, _currentBaseWidth, _currentBaseHeight).SetPosition(0, 0);
                }
                else if (usesDedicatedCanvas)
                {
                    (windowWidth, windowHeight) = EnsureEvenDimensions(windowWidth, windowHeight);
                    Log.Information(
                        "Applying dedicated canvas bounds for slot {Slot} ({Width}x{Height})",
                        slot, windowWidth, windowHeight);
                    pl.GameCaptureItem?.SetBounds(ObsBoundsType.ScaleInner, windowWidth, windowHeight).SetPosition(0, 0);
                    pl.DisplayItem?.SetBounds(ObsBoundsType.ScaleInner, windowWidth, windowHeight).SetPosition(0, 0);
                }
                else
                {
                    uint fitWidth = _currentBaseWidth > 0 ? _currentBaseWidth : windowWidth;
                    uint fitHeight = _currentBaseHeight > 0 ? _currentBaseHeight : windowHeight;
                    Log.Information(
                        "Fitting slot {Slot} onto existing canvas {CanvasW}x{CanvasH} (game window {Width}x{Height})",
                        slot, fitWidth, fitHeight, windowWidth, windowHeight);
                    pl.GameCaptureItem?.SetBounds(ObsBoundsType.ScaleInner, fitWidth, fitHeight).SetPosition(0, 0);
                    pl.DisplayItem?.SetBounds(ObsBoundsType.ScaleInner, fitWidth, fitHeight).SetPosition(0, 0);
                }
            }
            else
            {
                CleanupPartialSlot(slot);
                return false;
            }

            return true;
        }

        private static void ConfigureVideoEncoderSettings(ObsKit.NET.Core.Settings videoEncoderSettings, string encoderId, EffectiveRecordingSettings eff)
        {
            videoEncoderSettings.Set("keyint_sec", 1);

            if (IsVaapiEncoder(encoderId))
            {
                ConfigureVaapiVideoEncoder(videoEncoderSettings, encoderId, eff);
                return;
            }

            videoEncoderSettings.Set("preset", "Quality");
            videoEncoderSettings.Set("profile", "high");
            videoEncoderSettings.Set("use_bufsize", true);
            videoEncoderSettings.Set("rate_control", eff.RateControl);

            switch (eff.RateControl)
            {
                case "CBR":
                    int targetBitrateKbps = eff.Bitrate * 1000;
                    videoEncoderSettings.Set("bitrate", targetBitrateKbps);
                    videoEncoderSettings.Set("max_bitrate", targetBitrateKbps);
                    videoEncoderSettings.Set("bufsize", targetBitrateKbps);
                    break;

                case "VBR":
                    int minBitrateKbps = eff.MinBitrate * 1000;
                    int maxBitrateKbps = eff.MaxBitrate * 1000;
                    videoEncoderSettings.Set("bitrate", minBitrateKbps);
                    videoEncoderSettings.Set("max_bitrate", maxBitrateKbps);
                    videoEncoderSettings.Set("bufsize", maxBitrateKbps);
                    break;

                case "CRF":
                    videoEncoderSettings.Set("crf", eff.CrfValue);
                    break;

                case "CQP":
                    videoEncoderSettings.Set("cqp", eff.CqLevel);
                    videoEncoderSettings.Set("qp", eff.CqLevel);
                    break;

                case "CQVBR":
                    if (!encoderId.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
                    {
                        ClearAllPendingPreRecordings();
                        throw new Exception("CQVBR is only supported with NVIDIA NVENC encoders (OBS 31+).");
                    }

                    int cqvbrMaxKbps = eff.MaxBitrate * 1000;
                    videoEncoderSettings.Set("max_bitrate", cqvbrMaxKbps);
                    int maxTq = encoderId.Contains("av1", StringComparison.OrdinalIgnoreCase) ? 63 : 51;
                    int targetQ = Math.Clamp(eff.CqLevel, 1, maxTq);
                    videoEncoderSettings.Set("target_quality", targetQ);
                    break;

                default:
                    ClearAllPendingPreRecordings();
                    throw new Exception("Unsupported Rate Control method.");
            }

            ApplyNvencBFrameLimit(videoEncoderSettings, encoderId);
        }

        /// <summary>Adds mic, desktop and voice-chat audio, reusing the other slot's sources when it already has them.</summary>
        private static void AddAudioSources(int slot, SessionPipeline pl, EffectiveRecordingSettings eff)
        {
            var audioOutputMode = Settings.Instance.AudioOutputMode;

            // Mic/desktop WASAPI + Discord must be created once across dual slots (game capture_audio may duplicate).
            lock (_sharedAudioLock)
            {
                var existingSharedAudio = FindSharedAudioOwner(excludeSlot: slot);
                if (existingSharedAudio != null)
                {
                    Log.Information(
                        "Skipping new WASAPI/Discord audio sources for slot {Slot}: reusing sources from the other recording slot",
                        slot);

                    foreach (var micSource in existingSharedAudio.MicSources)
                        AttachSharedAudioSourceToScene(pl.MainScene!, micSource, "microphone");
                    foreach (var desktopSource in existingSharedAudio.DesktopSources)
                        AttachSharedAudioSourceToScene(pl.MainScene!, desktopSource, "desktop");
                    foreach (var (voiceName, _, voiceSource) in existingSharedAudio.VoiceChatSources)
                        AttachSharedAudioSourceToScene(pl.MainScene!, voiceSource, voiceName);
                    return;
                }

                // Create audio sources and add to scene
                if (Settings.Instance.InputDevices != null && Settings.Instance.InputDevices.Count > 0)
                {
                    foreach (var deviceSetting in Settings.Instance.InputDevices)
                    {
                        if (!string.IsNullOrEmpty(deviceSetting.Id))
                        {
                            string sourceName = ObsName(slot, $"Microphone_{pl.MicSources.Count + 1}");
                            var micSource = deviceSetting.Id == "default"
                                ? AudioInputCapture.FromDefault(sourceName)
                                : AudioInputCapture.FromDevice(deviceSetting.Id, sourceName);

                            SetForceMono(micSource, Settings.Instance.ForceMonoInputSources);

                            micSource.Volume = deviceSetting.Volume * eff.VolumeMultiplier;

                            pl.MainScene!.AddSource(micSource);
                            pl.MicSources.Add(micSource);

                            if (Settings.Instance.InputNoiseSuppression)
                            {
                                try
                                {
                                    var noiseSuppression = new Source("noise_suppress_filter", $"{sourceName}_NoiseSuppression");
                                    noiseSuppression.Update(s =>
                                    {
                                        s.Set("method", "rnnoise");
                                        s.Set("suppress_level", -30L);
                                    });
                                    micSource.AddFilter(noiseSuppression);
                                    Log.Information($"Added RNNoise noise suppression filter to {sourceName}");
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning($"Failed to add noise suppression filter to {sourceName}: {ex.Message}");
                                }
                            }

                            Log.Information($"Added input device: {deviceSetting.Id} as {sourceName} with volume {deviceSetting.Volume}");
                        }
                    }
                }

                // Always add desktop audio sources - they serve as fallback until game hooks in GameOnly/GameAndDiscord modes
                if (Settings.Instance.OutputDevices != null && Settings.Instance.OutputDevices.Count > 0)
                {
                    foreach (var deviceSetting in Settings.Instance.OutputDevices)
                    {
                        if (!string.IsNullOrEmpty(deviceSetting.Id))
                        {
                            string sourceName = ObsName(slot, $"DesktopAudio_{pl.DesktopSources.Count + 1}");
                            var desktopSource = deviceSetting.Id == "default"
                                ? AudioOutputCapture.FromDefault(sourceName)
                                : AudioOutputCapture.FromDevice(deviceSetting.Id, sourceName);

                            desktopSource.Volume = deviceSetting.Volume * eff.VolumeMultiplier;

                            pl.MainScene!.AddSource(desktopSource);
                            pl.DesktopSources.Add(desktopSource);

                            Log.Information($"Added output device: {deviceSetting.Name} ({deviceSetting.Id}) as {sourceName} with volume {deviceSetting.Volume}");
                        }
                    }
                }

                // In GameAndDiscord mode, capture voice-chat apps (muted until game hooks).
                // Additional apps that launch mid-recording are added via OnVoiceChatAppStarted.
                if (audioOutputMode == AudioOutputMode.GameAndDiscord && pl.GameCapture != null)
                {
                    foreach (var app in VoiceChatApps)
                        TryAddVoiceChatSource(pl, app, muted: true);
                }
            }
        }

        /// <summary>
        /// Routes every audio source to its mixer tracks and creates one AAC encoder per recorded track.
        /// Returns the output's track mask and the track names shown in the editor.
        /// </summary>
        private static (uint TracksMask, List<string> TrackNames) CreateAudioEncoders(int slot, SessionPipeline pl)
        {
            bool separateTracks = Settings.Instance.EnableSeparateAudioTracks;
            pl.AudioEncoders.Clear();
            var actualAudioTrackNames = new List<string>();
            int recordingAudioKbps = GetRecordingAudioBitrateKbps();

            if (!separateTracks)
            {
                const uint singleTrackMask = 1u;
                ApplyAudioTrackMask(pl.MicSources, singleTrackMask, "microphone");
                ApplyAudioTrackMask(pl.DesktopSources, singleTrackMask, "desktop");
                if (pl.GameCapture != null)
                    ApplyAudioTrackMask(pl.GameCapture, singleTrackMask, "game");
                foreach (var (voiceName, _, voiceSource) in EnumerateVoiceChatSources())
                    ApplyAudioTrackMask(voiceSource, singleTrackMask, voiceName);

                actualAudioTrackNames.Add("Full Mix");
                pl.AudioEncoders.Add(AudioEncoder.CreateAac(ObsName(slot, "Full Mix"), recordingAudioKbps, 0));
                return (1u, actualAudioTrackNames);
            }

            var inputDevices = Settings.Instance.InputDevices ?? new List<DeviceSetting>();
            for (int i = 0; i < pl.MicSources.Count; i++)
            {
                uint mask = i < inputDevices.Count
                    ? SanitizeAudioTrackMask(inputDevices[i].AudioTrackMask)
                    : 1u;
                ApplyAudioTrackMask(pl.MicSources[i], mask, $"microphone {i + 1}");
            }

            var outputDevices = Settings.Instance.OutputDevices ?? new List<DeviceSetting>();
            for (int i = 0; i < pl.DesktopSources.Count; i++)
            {
                uint mask = i < outputDevices.Count
                    ? SanitizeAudioTrackMask(outputDevices[i].AudioTrackMask)
                    : 1u;
                ApplyAudioTrackMask(pl.DesktopSources[i], mask, $"desktop {i + 1}");
            }

            if (pl.GameCapture != null)
            {
                ApplyAudioTrackMask(
                    pl.GameCapture,
                    SanitizeAudioTrackMask(Settings.Instance.GameAudioTrackMask),
                    "game");
            }

            uint voiceMask = SanitizeAudioTrackMask(Settings.Instance.DiscordAudioTrackMask);
            pl.VoiceChatMixerMask = voiceMask;
            foreach (var (voiceName, _, voiceSource) in EnumerateVoiceChatSources())
                ApplyAudioTrackMask(voiceSource, voiceMask, voiceName);

            uint recordingTracksMask = AudioTrackLayout.ComputeTracksMask(
                inputDevices,
                outputDevices,
                gameMask: pl.GameCapture != null ? Settings.Instance.GameAudioTrackMask : null,
                voiceMask: EnumerateVoiceChatSources().Any() ? voiceMask : null);

            foreach (string trackName in AudioTrackLayout.ResolveTrackNames(Settings.Instance.RecordingAudioTrackNames))
            {
                pl.AudioEncoders.Add(AudioEncoder.CreateAac(ObsName(slot, trackName), recordingAudioKbps, actualAudioTrackNames.Count));
                actualAudioTrackNames.Add(trackName);
            }

            Log.Information(
                $"Multi-track recording: output mask 0x{recordingTracksMask:X}, {AudioTrackLayout.MaxTracks} encoder(s).");

            return (recordingTracksMask, actualAudioTrackNames);
        }

        /// <summary>Creates the replay buffer and/or session output for the recording mode. Returns the session file path, if any.</summary>
        private static string? CreateOutputs(int slot, SessionPipeline pl, EffectiveRecordingSettings eff, string name, uint recordingTracksMask)
        {
            bool isReplayBufferMode = eff.RecordingMode == RecordingMode.Buffer;
            bool isSessionMode = eff.RecordingMode == RecordingMode.Session;
            bool isHybridMode = eff.RecordingMode == RecordingMode.Hybrid;

            // Paths for session recordings and buffer, organized by game
            string sanitizedGameName = StorageService.SanitizeGameNameForFolder(name);
            string sessionDir = Path.Combine(Settings.Instance.ContentFolder, FolderNames.Sessions, sanitizedGameName);
            string bufferDir = Path.Combine(Settings.Instance.ContentFolder, FolderNames.Buffers, sanitizedGameName);
            if (!Directory.Exists(sessionDir)) Directory.CreateDirectory(sessionDir);
            if (!Directory.Exists(bufferDir)) Directory.CreateDirectory(bufferDir);

            string? videoOutputPath = null; // only set for session/hybrid session output

            // Configure outputs depending on mode
            if (isReplayBufferMode || isHybridMode)
            {
                uint bufferTracksMask = recordingTracksMask;

                pl.BufferOutput = new ReplayBuffer(ObsName(slot, "replay_buffer"), eff.ReplayBufferDuration, eff.ReplayBufferMaxSize);
                pl.BufferOutput.SetDirectory(bufferDir);
                pl.BufferOutput.SetFilenameFormat("%CCYY-%MM-%DD_%hh-%mm-%ss");
                pl.BufferOutput.Update(s => s.Set("extension", "mp4").Set("tracks", (long)bufferTracksMask));

                if (pl.RecordingCanvas != null)
                    pl.BufferOutput.WithVideoEncoder(pl.VideoEncoder!, pl.RecordingCanvas);
                else
                    pl.BufferOutput.WithVideoEncoder(pl.VideoEncoder!);

                for (int t = 0; t < pl.AudioEncoders.Count; t++)
                {
                    pl.BufferOutput.WithAudioEncoder(pl.AudioEncoders[t], track: t);
                }

                int bufferSlot = slot;
                pl.ReplaySavedHandler = (sender, e) => OnReplaySaved(bufferSlot, sender, e);
                pl.BufferStoppedHandler = (sender, e) => OnOutputStopped(bufferSlot, sender, e);
                pl.BufferOutput.Saved += pl.ReplaySavedHandler;
                pl.BufferOutput.Stopped += pl.BufferStoppedHandler;
            }

            if (isSessionMode || isHybridMode)
            {
                videoOutputPath = $"{sessionDir}/{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";

                uint recordTracksMask = recordingTracksMask;

                bool useHybridMp4 = true;
                pl.SessionOutput = new RecordingOutput($"simple_output_{slot}", videoOutputPath);
                try
                {
                    pl.SessionOutput.SetFormat(RecordingFormat.HybridMp4);
                }
                catch (NotSupportedException)
                {
                    useHybridMp4 = false;
                }
                Log.Information($"Using recording output type: {(useHybridMp4 ? "mp4_output" : "ffmpeg_muxer")} (Hybrid MP4: {useHybridMp4})");
                pl.SessionOutput.Update(s => s.Set("tracks", (long)recordTracksMask));

                if (pl.RecordingCanvas != null)
                    pl.SessionOutput.WithVideoEncoder(pl.VideoEncoder!, pl.RecordingCanvas);
                else
                    pl.SessionOutput.WithVideoEncoder(pl.VideoEncoder!);

                for (int t = 0; t < pl.AudioEncoders.Count; t++)
                {
                    pl.SessionOutput.WithAudioEncoder(pl.AudioEncoders[t], track: t);
                }

                int sessionSlot = slot;
                pl.SessionStoppedHandler = (sender, e) => OnOutputStopped(sessionSlot, sender, e);
                pl.SessionOutput.Stopped += pl.SessionStoppedHandler;
            }

            return videoOutputPath;
        }

        /// <summary>
        /// Starts the session output, then the replay buffer, playing the start sound once.
        /// Returns false, after showing an error and cleaning up the slot, if either fails to start.
        /// </summary>
        private static bool StartOutputs(int slot, SessionPipeline pl, bool alwaysOn, out DateTime? startTime)
        {
            startTime = null;
            bool hasPlayedStartSound = false;

            if (pl.SessionOutput != null)
            {
                if (!pl.SessionOutput.Start())
                {
                    string error = pl.SessionOutput.LastError ?? "Unknown error";
                    Log.Error($"Failed to start recording: {error}");
                    Task.Run(() => ShowModal("Recording failed", "Failed to start recording. Check the log for more details.", "error"));
                    Task.Run(() => PlaySound("error", 500));
                    ClearPendingPreRecordingForSlot(slot);
                    CleanupPartialSlot(slot);
                    return false;
                }

                // Set the exact start time for session recording (Full Session has bookmarks)
                startTime = DateTime.Now;
                _ = Task.Run(() => PlaySound("start"));
                hasPlayedStartSound = true;

                Log.Information("Session recording started successfully");
            }

            if (pl.BufferOutput != null)
            {
                if (!pl.BufferOutput.Start())
                {
                    string error = pl.BufferOutput.LastError ?? "Unknown error";
                    Log.Error($"Failed to start replay buffer (slot {slot}): {error}");
                    if (!alwaysOn)
                    {
                        Task.Run(() => ShowModal("Replay buffer failed", "Failed to start replay buffer. Check the log for more details.", "error"));
                        Task.Run(() => PlaySound("error", 500));
                        ClearPendingPreRecordingForSlot(slot);
                    }
                    CleanupPartialSlot(slot, clearPreRecording: !alwaysOn);
                    return false;
                }

                if (!hasPlayedStartSound && !alwaysOn)
                {
                    _ = Task.Run(() => PlaySound("start"));
                    hasPlayedStartSound = true;
                }

                Log.Information("Replay buffer started successfully (slot {Slot})", slot);
            }

            return true;
        }
    }
}
