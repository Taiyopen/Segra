#!/usr/bin/env python3
"""Part C: voice chat, outputs events, stop/dispose, helpers, encoders, Linux."""
from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Backend" / "Recorder" / "OBSService.cs"
text = OUT.read_text(encoding="utf-8")


def must_replace(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"FAIL [{label}]: expected 1 occurrence, found {count}")
    text = text.replace(old, new, 1)
    print(f"OK [{label}]")


def must_insert_before(anchor: str, insertion: str, label: str) -> None:
    global text
    count = text.count(anchor)
    if count != 1:
        raise SystemExit(f"FAIL [{label}]: anchor count={count}")
    text = text.replace(anchor, insertion + anchor, 1)
    print(f"OK [{label}]")


# ===== Voice chat creation replacing Discord-only =====
must_replace(
    """                        foreach (var micSource in existingSharedAudio.MicSources)
                            AttachSharedAudioSourceToScene(pl.MainScene!, micSource, "microphone");
                        foreach (var desktopSource in existingSharedAudio.DesktopSources)
                            AttachSharedAudioSourceToScene(pl.MainScene!, desktopSource, "desktop");
                        if (existingSharedAudio.DiscordAudioSource != null)
                            AttachSharedAudioSourceToScene(pl.MainScene!, existingSharedAudio.DiscordAudioSource, "discord");
""",
    """                        foreach (var micSource in existingSharedAudio.MicSources)
                            AttachSharedAudioSourceToScene(pl.MainScene!, micSource, "microphone");
                        foreach (var desktopSource in existingSharedAudio.DesktopSources)
                            AttachSharedAudioSourceToScene(pl.MainScene!, desktopSource, "desktop");
                        foreach (var (voiceName, _, voiceSource) in existingSharedAudio.VoiceChatSources)
                            AttachSharedAudioSourceToScene(pl.MainScene!, voiceSource, voiceName);
""",
    "attach shared voicechat",
)

must_replace(
    """                                    micSource.Volume = deviceSetting.Volume;

                                    pl.MainScene!.AddSource(micSource);
""",
    """                                    micSource.Volume = deviceSetting.Volume * eff.VolumeMultiplier;

                                    pl.MainScene!.AddSource(micSource);
""",
    "mic volume multiplier",
)

must_replace(
    """                                    desktopSource.Volume = deviceSetting.Volume;

                                    pl.MainScene!.AddSource(desktopSource);
""",
    """                                    desktopSource.Volume = deviceSetting.Volume * eff.VolumeMultiplier;

                                    pl.MainScene!.AddSource(desktopSource);
""",
    "desktop volume multiplier",
)

must_replace(
    """                        // In GameAndDiscord mode, also create Discord application audio capture (starts muted until game hooks)
                        if (audioOutputMode == AudioOutputMode.GameAndDiscord && pl.GameCapture != null)
                        {
                            try
                            {
                                pl.DiscordAudioSource = new Source("wasapi_process_output_capture", ObsName(slot, "Discord Audio"));
                                pl.DiscordAudioSource.Update(s =>
                                {
                                    s.Set("window", "Discord:Chrome_WidgetWin_1:Discord.exe");
                                    s.Set("priority", 2);
                                });
                                pl.DiscordAudioSource.IsMuted = true;
                                pl.MainScene!.AddSource(pl.DiscordAudioSource);
                                Log.Information("Added Discord application audio capture source (muted until game hooks)");
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"Failed to create Discord audio capture source: {ex.Message}");
                                pl.DiscordAudioSource = null;
                            }
                        }
""",
    """                        // In GameAndDiscord mode, capture voice-chat apps (muted until game hooks).
                        // Additional apps that launch mid-recording are added via OnVoiceChatAppStarted.
                        if (audioOutputMode == AudioOutputMode.GameAndDiscord && pl.GameCapture != null)
                        {
                            foreach (var app in VoiceChatApps)
                                TryAddVoiceChatSource(pl, app, muted: true);
                        }
""",
    "voice chat create",
)

must_replace(
    """                    if (pl.DiscordAudioSource != null)
                        ApplyAudioTrackMask(pl.DiscordAudioSource, singleTrackMask, "discord");
""",
    """                    foreach (var (voiceName, _, voiceSource) in EnumerateVoiceChatSources())
                        ApplyAudioTrackMask(voiceSource, singleTrackMask, voiceName);
""",
    "single track voice",
)

must_replace(
    """                    if (pl.DiscordAudioSource != null)
                    {
                        ApplyAudioTrackMask(
                            pl.DiscordAudioSource,
                            SanitizeAudioTrackMask(Settings.Instance.DiscordAudioTrackMask),
                            "discord");
                    }

                    recordingTracksMask = 0;
                    foreach (var device in inputDevices.Where(d => !string.IsNullOrEmpty(d.Id)))
                        recordingTracksMask |= SanitizeAudioTrackMask(device.AudioTrackMask);
                    foreach (var device in outputDevices.Where(d => !string.IsNullOrEmpty(d.Id)))
                        recordingTracksMask |= SanitizeAudioTrackMask(device.AudioTrackMask);
                    if (pl.GameCapture != null)
                        recordingTracksMask |= SanitizeAudioTrackMask(Settings.Instance.GameAudioTrackMask);
                    // Discord may live on the other slot; still encode its track from the global mix.
                    if (pl.DiscordAudioSource != null || FindDiscordAudioSource() != null)
                        recordingTracksMask |= SanitizeAudioTrackMask(Settings.Instance.DiscordAudioTrackMask);
""",
    """                    uint voiceMask = SanitizeAudioTrackMask(Settings.Instance.DiscordAudioTrackMask);
                    pl.VoiceChatMixerMask = voiceMask;
                    foreach (var (voiceName, _, voiceSource) in EnumerateVoiceChatSources())
                        ApplyAudioTrackMask(voiceSource, voiceMask, voiceName);

                    recordingTracksMask = 0;
                    foreach (var device in inputDevices.Where(d => !string.IsNullOrEmpty(d.Id)))
                        recordingTracksMask |= SanitizeAudioTrackMask(device.AudioTrackMask);
                    foreach (var device in outputDevices.Where(d => !string.IsNullOrEmpty(d.Id)))
                        recordingTracksMask |= SanitizeAudioTrackMask(device.AudioTrackMask);
                    if (pl.GameCapture != null)
                        recordingTracksMask |= SanitizeAudioTrackMask(Settings.Instance.GameAudioTrackMask);
                    if (EnumerateVoiceChatSources().Any())
                        recordingTracksMask |= voiceMask;
""",
    "multi track voice",
)

# ===== Outputs: eff buffer size, Saved/Stopped events, hybrid try/catch =====
must_replace(
    """                    pl.BufferOutput = new ReplayBuffer(ObsName(slot, "replay_buffer"), Settings.Instance.ReplayBufferDuration, Settings.Instance.ReplayBufferMaxSize);
                    pl.BufferOutput.SetDirectory(bufferDir);
                    pl.BufferOutput.SetFilenameFormat("%CCYY-%MM-%DD_%hh-%mm-%ss");
                    pl.BufferOutput.Update(s => s.Set("extension", "mp4").Set("tracks", (long)bufferTracksMask));

                    if (pl.RecordingCanvas != null)
                        pl.BufferOutput.WithVideoEncoder(pl.VideoEncoder, pl.RecordingCanvas);
                    else
                        pl.BufferOutput.WithVideoEncoder(pl.VideoEncoder);

                    for (int t = 0; t < pl.AudioEncoders.Count; t++)
                    {
                        pl.BufferOutput.WithAudioEncoder(pl.AudioEncoders[t], track: t);
                    }

                    pl.ReplaySavedConnection = pl.BufferOutput.ConnectSignal(OutputSignal.Saved, _ => OnReplaySaved(slot));
                }
""",
    """                    pl.BufferOutput = new ReplayBuffer(ObsName(slot, "replay_buffer"), eff.ReplayBufferDuration, eff.ReplayBufferMaxSize);
                    pl.BufferOutput.SetDirectory(bufferDir);
                    pl.BufferOutput.SetFilenameFormat("%CCYY-%MM-%DD_%hh-%mm-%ss");
                    pl.BufferOutput.Update(s => s.Set("extension", "mp4").Set("tracks", (long)bufferTracksMask));

                    if (pl.RecordingCanvas != null)
                        pl.BufferOutput.WithVideoEncoder(pl.VideoEncoder, pl.RecordingCanvas);
                    else
                        pl.BufferOutput.WithVideoEncoder(pl.VideoEncoder);

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
""",
    "buffer output events",
)

must_replace(
    """                    bool useHybridMp4 = SupportsHybridMp4();
                    Log.Information($"Using recording output type: {(useHybridMp4 ? "mp4_output" : "ffmpeg_muxer")} (Hybrid MP4: {useHybridMp4})");

                    if (useHybridMp4)
                    {
                        pl.SessionOutput = new RecordingOutput($"simple_output_{slot}", videoOutputPath);
                        pl.SessionOutput.SetFormat(RecordingFormat.HybridMp4);
                    }
                    else
                    {
                        pl.SessionOutput = new RecordingOutput($"simple_output_{slot}", videoOutputPath, "mp4");
                    }
                    pl.SessionOutput.Update(s => s.Set("tracks", (long)recordTracksMask));

                    if (pl.RecordingCanvas != null)
                        pl.SessionOutput.WithVideoEncoder(pl.VideoEncoder, pl.RecordingCanvas);
                    else
                        pl.SessionOutput.WithVideoEncoder(pl.VideoEncoder);

                    for (int t = 0; t < pl.AudioEncoders.Count; t++)
                    {
                        pl.SessionOutput.WithAudioEncoder(pl.AudioEncoders[t], track: t);
                    }
                }
""",
    """                    bool useHybridMp4 = true;
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
                        pl.SessionOutput.WithVideoEncoder(pl.VideoEncoder, pl.RecordingCanvas);
                    else
                        pl.SessionOutput.WithVideoEncoder(pl.VideoEncoder);

                    for (int t = 0; t < pl.AudioEncoders.Count; t++)
                    {
                        pl.SessionOutput.WithAudioEncoder(pl.AudioEncoders[t], track: t);
                    }

                    int sessionSlot = slot;
                    pl.SessionStoppedHandler = (sender, e) => OnOutputStopped(sessionSlot, sender, e);
                    pl.SessionOutput.Stopped += pl.SessionStoppedHandler;
                }
""",
    "session output hybrid+stopped",
)

must_replace(
                """                Task.Run(KeybindCaptureService.Start);
                return true;
""",
                """                StartDiskSpaceMonitor();
                return true;
""",
    "start disk monitor not keybind",
)

# ===== AddMonitorCapture + UpdateMonitorCapture =====
must_replace(
    """        public static void AddMonitorCapture(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.MainScene == null)
            {
                Log.Warning("Cannot add monitor capture: scene not created");
                return;
            }

            int monitorIndex = 0;

            if (Settings.Instance.SelectedDisplay != null)
            {
                int? foundIndex = AppState.Instance.Displays
                    .Select((d, i) => new { Display = d, Index = i })
                    .Where(x => x.Display.DeviceId == Settings.Instance.SelectedDisplay?.DeviceId)
                    .Select(x => (int?)x.Index)
                    .FirstOrDefault();

                if (foundIndex.HasValue)
                {
                    monitorIndex = foundIndex.Value;
                }
                else
                {
                    _ = MessageService.ShowModal("Display recording", $"Could not find selected display. Defaulting to first automatically detected display.", "warning");
                }
            }

            var captureMethod = Settings.Instance.DisplayCaptureMethod switch
            {
                DisplayCaptureMethod.DXGI => MonitorCaptureMethod.DesktopDuplication,
                DisplayCaptureMethod.WGC => MonitorCaptureMethod.WindowsGraphicsCapture,
                _ => MonitorCaptureMethod.Auto
            };

            pl.DisplaySource = MonitorCapture.FromMonitor(monitorIndex, ObsName(slot, "display"))
                .SetCaptureMethod(captureMethod);

            pl.DisplayItem = pl.MainScene.AddSource(pl.DisplaySource);

            Log.Information($"Display capture added for monitor {monitorIndex} using {Settings.Instance.DisplayCaptureMethod} method");
        }
""",
    """        public static void AddMonitorCapture(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.MainScene == null)
            {
                Log.Warning("Cannot add monitor capture: scene not created");
                return;
            }

            int monitorIndex = ResolveSelectedMonitorIndex(warnIfNotFound: true);

#if WINDOWS
            var captureMethod = Settings.Instance.DisplayCaptureMethod switch
            {
                DisplayCaptureMethod.DXGI => MonitorCaptureMethod.DesktopDuplication,
                DisplayCaptureMethod.WGC => MonitorCaptureMethod.WindowsGraphicsCapture,
                _ => MonitorCaptureMethod.Auto
            };

            pl.DisplaySource = MonitorCapture.FromMonitor(monitorIndex, ObsName(slot, "display"))
                .SetCaptureMethod(captureMethod);

            Log.Information($"Display capture added for monitor {monitorIndex} using {Settings.Instance.DisplayCaptureMethod} method");
#else
            bool isWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            if (isWayland)
            {
                pl.DisplaySource = MonitorCapture.FromMonitor(monitorIndex, ObsName(slot, "display"));
                Log.Information($"Display capture added for monitor {monitorIndex} using PipeWire (portal)");
            }
            else
            {
                var xshm = new Source("xshm_input", ObsName(slot, "display"));
                xshm.Update(s =>
                {
                    s.Set("screen", monitorIndex);
                    s.Set("show_cursor", true);
                });
                pl.DisplaySource = xshm;
                Log.Information($"Display capture added for screen {monitorIndex} using X11 (xshm)");
            }
#endif

            pl.DisplayItem = pl.MainScene.AddSource(pl.DisplaySource);
        }

        public static void UpdateMonitorCapture()
        {
            // Dual-slot: update every live display capture that is still active.
            bool updatedAny = false;
            for (int slot = 0; slot < MaxSessionSlots; slot++)
            {
                var pl = Pipeline(slot);
                if (pl.DisplaySource == null)
                    continue;

                int monitorIndex = ResolveSelectedMonitorIndex(warnIfNotFound: !updatedAny);
                if (pl.DisplaySource is MonitorCapture monitorCapture)
                {
                    monitorCapture.SetMonitor(monitorIndex);
                    Log.Information($"Updated live display capture to monitor {monitorIndex} (slot {slot})");
                    updatedAny = true;
                }
                else
                {
                    Log.Information("Monitor selection changed; will apply on the next recording (slot {Slot}).", slot);
                }
            }

            if (!updatedAny)
                Log.Information("Monitor selection changed but no active display capture to update; it will apply on the next recording.");
        }

        private static int ResolveSelectedMonitorIndex(bool warnIfNotFound)
        {
            if (Settings.Instance.SelectedDisplay == null)
                return 0;

            int? foundIndex = AppState.Instance.Displays
                .Select((d, i) => new { Display = d, Index = i })
                .Where(x => x.Display.DeviceId == Settings.Instance.SelectedDisplay?.DeviceId)
                .Select(x => (int?)x.Index)
                .FirstOrDefault();

            if (foundIndex.HasValue)
                return foundIndex.Value;

            if (warnIfNotFound)
                _ = MessageService.ShowModal("Display recording", "Could not find selected display. Defaulting to first automatically detected display.", "warning");

            return 0;
        }
""",
    "AddMonitorCapture/UpdateMonitorCapture",
)

# ===== FinalizeSessionFileAsync discard from ActiveEffectiveSettings =====
must_replace(
    """            bool hasManualBookmarks = rec.Bookmarks.Any(b => b.Type == BookmarkType.Manual);
            if (Settings.Instance.DiscardSessionsWithoutBookmarks && !hasManualBookmarks)
""",
    """            bool hasManualBookmarks = rec.Bookmarks.Any(b => b.Type == BookmarkType.Manual);
            bool discardWithoutBookmarks = Pipeline(rec.Slot).EffectiveSettings?.DiscardSessionsWithoutBookmarks
                ?? ActiveEffectiveSettings?.DiscardSessionsWithoutBookmarks
                ?? Settings.Instance.DiscardSessionsWithoutBookmarks;
            if (discardWithoutBookmarks && !hasManualBookmarks)
""",
    "finalize discard eff",
)

# ===== StopRecordingSlotCore =====
must_replace(
    """                var pl = Pipeline(slot);
                var recording = AppState.Instance.GetRecording(slot);

                bool isReplayBufferMode = Settings.Instance.RecordingMode == RecordingMode.Buffer;
                bool isHybridMode = Settings.Instance.RecordingMode == RecordingMode.Hybrid;
                bool isSessionMode = Settings.Instance.RecordingMode == RecordingMode.Session;

                StopGameCaptureHookTimeoutTimer(slot);

                if (recording != null)
                    AppState.Instance.UpdateRecordingEndTime(DateTime.Now, slot);

                if ((isReplayBufferMode || isHybridMode) && pl.BufferOutput != null)
                {
                    Log.Information(isHybridMode ? "Hybrid: Stopping replay buffer (slot {Slot})..." : "Stopping replay buffer (slot {Slot})...", slot);
""",
    """                var pl = Pipeline(slot);
                var recording = AppState.Instance.GetRecording(slot);

                RecordingMode effectiveMode = pl.EffectiveSettings?.RecordingMode ?? Settings.Instance.RecordingMode;
                bool isReplayBufferMode = effectiveMode == RecordingMode.Buffer;
                bool isHybridMode = effectiveMode == RecordingMode.Hybrid;
                bool isSessionMode = effectiveMode == RecordingMode.Session;

                StopGameCaptureHookTimeoutTimer(slot);

                if (recording != null)
                    AppState.Instance.UpdateRecordingEndTime(DateTime.Now, slot);

                if ((isReplayBufferMode || isHybridMode) && pl.BufferOutput != null)
                {
                    await WaitForInFlightReplaySaveAsync(slot);
                    Log.Information(isHybridMode ? "Hybrid: Stopping replay buffer (slot {Slot})..." : "Stopping replay buffer (slot {Slot})...", slot);
""",
    "stop core mode+wait",
)

must_replace(
    """                if (slot == 0)
                    Obs.SetOutputSource(0, (Scene?)null);
                DisposeRecordingCanvas(slot);
                pl.HookedExecutableFileName = null;
""",
    """                if (slot == 0)
                    Obs.ClearOutputSource(0);
                DisposeRecordingCanvas(slot);
                pl.HookedExecutableFileName = null;
                pl.EffectiveSettings = null;
""",
    "ClearOutputSource stop",
)

must_replace(
    """                if (!AppState.Instance.HasAnyRecording())
                {
                    GeneralUtils.SetProcessPriority(ProcessPriorityClass.Normal);
                    KeybindCaptureService.Stop();
                    NotifyIconService.SetNotifyIconStatus(NotifyIconState.Idle);
                    CapturedWindowWidth = null;
                    CapturedWindowHeight = null;
                    ClearAllCapturedWindowDimensions();
                    await VrChatVvmwIntegration.FlushDeferredClipsAsync();
                }
""",
    """                if (!AppState.Instance.HasAnyRecording())
                {
                    StopDiskSpaceMonitor();
                    GeneralUtils.SetProcessPriority(ProcessPriorityClass.Normal);
                    NotifyIconService.SetNotifyIconStatus(NotifyIconState.Idle);
                    CapturedWindowWidth = null;
                    CapturedWindowHeight = null;
                    ClearAllCapturedWindowDimensions();
                    _isHdrRecording = false;
                    _hdrEncoderId = null;
                    await VrChatVvmwIntegration.FlushDeferredClipsAsync();
                }
""",
    "stop last slot cleanup",
)

# CleanupPartialSlot ClearOutputSource
must_replace(
    """                Obs.SetOutputSource(0, (Scene?)null);
""",
    """                Obs.ClearOutputSource(0);
""",
    "ClearOutputSource cleanup partial",
)

# ===== Hooked/Unhooked voice chat =====
must_replace(
    """                    var discordSource = FindDiscordAudioSource();
                    if (audioOutputMode == AudioOutputMode.GameAndDiscord && discordSource != null)
                    {
                        try { discordSource.IsMuted = false; }
                        catch (Exception ex) { Log.Warning($"Failed to unmute Discord source: {ex.Message}"); }
                        Log.Information("Unmuted Discord audio source (game hooked)");
                    }
""",
    """                    if (audioOutputMode == AudioOutputMode.GameAndDiscord)
                    {
                        foreach (var (voiceName, _, voiceSource) in EnumerateVoiceChatSources())
                        {
                            try { voiceSource.IsMuted = false; Log.Information($"Unmuted {voiceName} audio source (game hooked)"); }
                            catch (Exception ex) { Log.Warning($"Failed to unmute {voiceName} source: {ex.Message}"); }
                        }
                    }
""",
    "hooked unmute voice",
)

must_replace(
    """                    var discordSource = FindDiscordAudioSource();
                    if (audioOutputMode == AudioOutputMode.GameAndDiscord && discordSource != null)
                    {
                        try { discordSource.IsMuted = true; }
                        catch (Exception ex) { Log.Warning($"Failed to mute Discord source: {ex.Message}"); }
                        Log.Information("Muted Discord audio source (game unhooked)");
                    }
""",
    """                    if (audioOutputMode == AudioOutputMode.GameAndDiscord)
                    {
                        foreach (var (voiceName, _, voiceSource) in EnumerateVoiceChatSources())
                        {
                            try { voiceSource.IsMuted = true; Log.Information($"Muted {voiceName} audio source (game unhooked)"); }
                            catch (Exception ex) { Log.Warning($"Failed to mute {voiceName} source: {ex.Message}"); }
                        }
                    }
""",
    "unhooked mute voice",
)

# ===== OnReplaySaved / OnOutputStopped =====
must_replace(
    """        private static void OnReplaySaved(int slot)
        {
            Log.Information("Replay buffer saved callback received (slot {Slot})", slot);
        }
""",
    """        private static void OnReplaySaved(int slot, object? sender, ReplaySavedEventArgs e)
        {
            Log.Information("Replay buffer saved callback received (slot {Slot})", slot);

            string? path = e.Path;
            lock (_replaySaveLock)
            {
                if (_activeReplaySave == null || _activeReplaySave.Slot != slot || _activeReplaySave.Signal.Task.IsCompleted)
                {
                    Log.Warning($"Replay 'saved' signal arrived with no pending request for slot {slot} (file: {path ?? "unknown"}); leaving it for recovery.");
                    _previousSaveIndeterminate = false;
                    return;
                }

                if (string.IsNullOrEmpty(path))
                {
                    _activeReplaySave.FailureReason = "OBS reported the replay as saved but did not return its path.";
                    _activeReplaySave.Signal.TrySetResult(null);
                }
                else
                {
                    _activeReplaySave.Signal.TrySetResult(path);
                }
            }
        }

        private static void OnOutputStopped(int slot, object? sender, OutputStoppedEventArgs e)
        {
            if (e.IsSuccess)
                return;

            var code = e.Code;
            if (_isStoppingSlot[slot])
            {
                Log.Warning($"Output stopped with code {code} while already stopping (slot {slot}).");
                return;
            }

            var pl = Pipeline(slot);
            if (Interlocked.CompareExchange(ref pl.UnexpectedStopHandled, 1, 0) != 0)
            {
                Log.Warning($"Output stopped with code {code}; unexpected stop already handled (slot {slot}).");
                return;
            }

            string? lastError = e.LastError;
            Log.Error($"OBS stopped recording output unexpectedly on slot {slot} (code {code}); last error: {lastError ?? "(none)"}");
            _ = Task.Run(() => HandleUnexpectedOutputStop(slot, code, lastError));
        }

        private static async Task HandleUnexpectedOutputStop(int slot, ObsOutputStopCode code, string? lastError)
        {
            try
            {
                GameDetectionService.PreventRetryRecording = true;
                var (title, description) = MapOutputStopToMessage(code, lastError);
                await ShowModal(title, description, "error");
                _ = Task.Run(() => PlaySound("error"));
            }
            catch (Exception ex)
            {
                Log.Error($"Error notifying frontend of unexpected output stop: {ex.Message}");
            }
            finally
            {
                await StopRecordingSlot(slot);
            }
        }

        private static (string Title, string Description) MapOutputStopToMessage(ObsOutputStopCode code, string? lastError)
        {
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                if (lastError.Contains("No space left", StringComparison.OrdinalIgnoreCase)
                    || lastError.Contains("not enough space", StringComparison.OrdinalIgnoreCase)
                    || lastError.Contains("disk full", StringComparison.OrdinalIgnoreCase))
                {
                    return ("Recording stopped: disk full",
                        $"OBS stopped the recording because the disk is full. Free up space and try again.\\n\\nDetails: {lastError}");
                }

                return ("Recording stopped unexpectedly",
                    $"OBS stopped the recording.\\n\\nDetails: {lastError}");
            }

            return code switch
            {
                ObsOutputStopCode.EncodeError => ("Recording stopped: encoder error",
                    "OBS reported an encoder error and stopped the recording. Check that your GPU drivers are up to date and try a different encoder in Settings."),
                ObsOutputStopCode.Error => ("Recording stopped unexpectedly",
                    "OBS stopped the recording due to an error. Check the logs for details."),
                _ => ("Recording stopped unexpectedly",
                    $"OBS stopped the recording (code {code}). Check the logs for details.")
            };
        }
""",
    "OnReplaySaved/OnOutputStopped",
)

# Fix double-escaped newlines in MapOutputStopToMessage
text = text.replace("try again.\\\\n\\\\nDetails:", "try again.\\n\\nDetails:")
text = text.replace("the recording.\\\\n\\\\nDetails:", "the recording.\\n\\nDetails:")

# ===== DisposeOutput =====
must_replace(
    """        public static void DisposeOutput(int slot)
        {
            var pl = Pipeline(slot);
            pl.ReplaySavedConnection?.Dispose();
            pl.ReplaySavedConnection = null;
            pl.BufferOutput = null;
            pl.SessionOutput = null;
        }
""",
    """        public static void DisposeOutput(int slot)
        {
            var pl = Pipeline(slot);

            // Fail any in-flight save tied to this slot before tearing the buffer down.
            lock (_replaySaveLock)
            {
                if (_activeReplaySave != null && _activeReplaySave.Slot == slot && !_activeReplaySave.Signal.Task.IsCompleted)
                {
                    _activeReplaySave.FailureReason = "Recording stopped before the replay finished saving.";
                    _activeReplaySave.Signal.TrySetResult(null);
                }
                if (_previousSaveIndeterminate)
                    _previousSaveIndeterminate = false;
            }

            if (pl.BufferOutput != null)
            {
                if (pl.ReplaySavedHandler != null)
                    pl.BufferOutput.Saved -= pl.ReplaySavedHandler;
                if (pl.BufferStoppedHandler != null)
                    pl.BufferOutput.Stopped -= pl.BufferStoppedHandler;
            }
            if (pl.SessionOutput != null && pl.SessionStoppedHandler != null)
                pl.SessionOutput.Stopped -= pl.SessionStoppedHandler;

            pl.ReplaySavedHandler = null;
            pl.BufferStoppedHandler = null;
            pl.SessionStoppedHandler = null;
            pl.BufferOutput = null;
            pl.SessionOutput = null;
        }
""",
    "DisposeOutput",
)

OUT.write_text(text, encoding="utf-8", newline="\n")
print(f"Part C1 written ({len(text.splitlines())} lines)")
