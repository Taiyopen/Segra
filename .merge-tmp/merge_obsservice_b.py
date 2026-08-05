#!/usr/bin/env python3
"""Part B: continue hybrid merge on Backend/Recorder/OBSService.cs"""
from __future__ import annotations
import re
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


# ===== SaveReplayBufferInternalGetPathAsync =====
must_replace(
    """        /// <summary>Waits for OBS replay save callback and returns the saved file path, or null on failure.</summary>
        private static async Task<string?> SaveReplayBufferInternalGetPathAsync(int slot)
        {
            var buffer = Pipeline(slot).BufferOutput;
            if (buffer == null)
                return null;

            Log.Information("Attempting to save replay buffer (slot {Slot})...", slot);
            _replaySavedBySlot[slot] = false;

            try
            {
                buffer.Save();
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to save replay buffer (slot {slot}): {ex.Message}");
                return null;
            }

            Log.Information("Waiting for replay buffer saved callback (slot {Slot})...", slot);
            int attempts = 0;
            while (!_replaySavedBySlot[slot] && attempts < 50)
            {
                await Task.Delay(100);
                attempts++;
            }

            if (!_replaySavedBySlot[slot])
            {
                Log.Warning("Replay buffer may not have saved correctly (slot {Slot})", slot);
                return null;
            }

            string? savedPath = buffer.GetLastReplayPath();

            for (int i = 0; i < 10 && string.IsNullOrEmpty(savedPath); i++)
            {
                savedPath = buffer.GetLastReplayPath();
                if (string.IsNullOrEmpty(savedPath))
                    await Task.Delay(100);
            }

            if (string.IsNullOrEmpty(savedPath))
            {
                Log.Error("Replay buffer path is null or empty (slot {Slot})", slot);
                return null;
            }

            return savedPath;
        }
""",
    """        /// <summary>Waits for OBS ReplayBuffer.Saved and returns the saved file path, or null on failure.</summary>
        private static async Task<string?> SaveReplayBufferInternalGetPathAsync(int slot)
        {
            var buffer = Pipeline(slot).BufferOutput;
            if (buffer == null)
                return null;

            var recording = AppState.Instance.GetRecording(slot);
            var request = new ReplaySaveRequest
            {
                Slot = slot,
                Game = recording?.Game ?? "Unknown",
                IgdbId = !string.IsNullOrEmpty(recording?.ExePath) ? GameUtils.GetIgdbIdFromExePath(recording!.ExePath) : null,
                AudioTrackNames = recording?.AudioTrackNames
            };

            lock (_replaySaveLock)
                _activeReplaySave = request;

            if (!await WaitForPriorSaveResolutionAsync(GetReplaySaveExpectedTimeout(slot)))
            {
                Log.Warning("Cannot save replay buffer: a previous save is still unresolved (slot {Slot}).", slot);
                lock (_replaySaveLock)
                    _activeReplaySave = null;
                return null;
            }

            Log.Information("Attempting to save replay buffer (slot {Slot})...", slot);
            try
            {
                buffer.Save();
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to save replay buffer (slot {slot}): {ex.Message}");
                lock (_replaySaveLock)
                    _activeReplaySave = null;
                return null;
            }

            string? savedPath = await WaitForReplaySavedAsync(request);
            lock (_replaySaveLock)
                _activeReplaySave = null;

            if (string.IsNullOrEmpty(savedPath))
            {
                Log.Error($"Replay buffer save failed (slot {slot}): {request.FailureReason ?? "unknown"}");
                return null;
            }

            return PathUtils.Normalize(savedPath);
        }

        private static async Task<string?> WaitForReplaySavedAsync(ReplaySaveRequest request)
        {
            TimeSpan expected = GetReplaySaveExpectedTimeout(request.Slot);
            TimeSpan backstop = TimeSpan.FromMinutes(15);
            Task<string?> signal = request.Signal.Task;

            try { return await signal.WaitAsync(expected); }
            catch (TimeoutException) { }

            Log.Warning($"Replay save not confirmed after {expected.TotalSeconds:F0}s (slot {request.Slot}); waiting up to {backstop.TotalMinutes:F0} minutes.");
            try { return await signal.WaitAsync(backstop - expected); }
            catch (TimeoutException) { }

            FailActiveReplaySave($"The replay was still being written after {backstop.TotalMinutes:F0} minutes.", obsSideStateUnknown: true);
            return await signal;
        }

        private static TimeSpan GetReplaySaveExpectedTimeout(int slot = 0)
        {
            int maxSizeMb = Pipeline(slot).EffectiveSettings?.ReplayBufferMaxSize
                ?? ActiveEffectiveSettings?.ReplayBufferMaxSize
                ?? Settings.Instance.ReplayBufferMaxSize;
            return TimeSpan.FromSeconds(Math.Clamp(maxSizeMb / 5.0, 60d, 600d));
        }

        private static void FailActiveReplaySave(string reason, bool obsSideStateUnknown = false)
        {
            lock (_replaySaveLock)
            {
                if (_activeReplaySave == null || _activeReplaySave.Signal.Task.IsCompleted)
                    return;

                _activeReplaySave.FailureReason = reason;
                _activeReplaySave.Signal.TrySetResult(null);

                if (obsSideStateUnknown)
                    _previousSaveIndeterminate = true;
            }
        }

        private static async Task<bool> WaitForPriorSaveResolutionAsync(TimeSpan limit)
        {
            long deadline = Environment.TickCount64 + (long)limit.TotalMilliseconds;
            while (true)
            {
                lock (_replaySaveLock)
                {
                    if (!_previousSaveIndeterminate)
                        return true;
                }

                if (Environment.TickCount64 >= deadline)
                    return false;

                await Task.Delay(500);
            }
        }

        private static void OnReplayMuxFailureLine(string logLine)
        {
            lock (_replaySaveLock)
            {
                if (_activeReplaySave != null && !_activeReplaySave.Signal.Task.IsCompleted)
                {
                    _activeReplaySave.FailureReason = $"OBS reported a write failure: {logLine}";
                    _activeReplaySave.Signal.TrySetResult(null);
                }
                else if (_previousSaveIndeterminate)
                {
                    Log.Warning("A previously timed-out replay save has now failed in OBS.");
                    _previousSaveIndeterminate = false;
                }
            }
        }

        private static async Task WaitForInFlightReplaySaveAsync(int slot)
        {
            Task<string?>? pending;
            lock (_replaySaveLock)
            {
                pending = (_activeReplaySave != null && _activeReplaySave.Slot == slot)
                    ? _activeReplaySave.Signal.Task
                    : null;
            }

            if (pending == null || pending.IsCompleted)
                return;

            TimeSpan limit = GetReplaySaveExpectedTimeout(slot);
            Log.Information($"Waiting up to {limit.TotalSeconds:F0}s for in-flight replay save (slot {slot})...");
            try { await pending.WaitAsync(limit); }
            catch (TimeoutException) { }
        }
""",
    "SaveReplayBufferInternalGetPathAsync",
)

# SaveReplayBuffer: frontend message + stop guard (tolerate leftover flag clears)
must_replace(
    """                await ContentService.CreateMetadataFile(savedPath, Content.ContentType.Buffer, game, igdbId: igdbId, audioTrackNames: recording?.AudioTrackNames);
                await ContentService.CreateThumbnail(savedPath, Content.ContentType.Buffer);
                _ = Task.Run(async () => await ContentService.CreateWaveformFile(savedPath, Content.ContentType.Buffer));

                await SettingsService.LoadContentFromFolderIntoState(true);

                Log.Information("Replay buffer save process completed successfully (slot {Slot})", slot);

                await ResetReplayBuffer(slot);
""",
    """                _ = MessageService.SendFrontendMessage("ReplayBufferSaved", new { });

                await ContentService.CreateMetadataFile(savedPath, Content.ContentType.Buffer, game, igdbId: igdbId, audioTrackNames: recording?.AudioTrackNames);
                await ContentService.CreateThumbnail(savedPath, Content.ContentType.Buffer);
                _ = Task.Run(async () => await ContentService.CreateWaveformFile(savedPath, Content.ContentType.Buffer));

                await SettingsService.LoadContentFromFolderIntoState(true);

                Log.Information("Replay buffer save process completed successfully (slot {Slot})", slot);

                if (!_isStoppingSlot[slot])
                    await ResetReplayBuffer(slot);
""",
    "SaveReplayBuffer complete path",
)

# Strip leftover _replaySavedBySlot lines
text = re.sub(r"^[ \t]*_replaySavedBySlot\[slot\] = false;\r?\n", "", text, flags=re.M)
text = re.sub(r"^[ \t]*_replaySavedBySlot\[slot\] = true;\r?\n", "", text, flags=re.M)
print(f"OK [strip _replaySavedBySlot refs, remaining={text.count('_replaySavedBySlot')}]")

# Log mux failure detection
must_replace(
    """                    Log.Information($"{(ObsLogLevel)level}: {formattedMessage}");

                    if (formattedMessage.Contains("capture window no longer exists, terminating capture"))
""",
    """                    Log.Information($"{(ObsLogLevel)level}: {formattedMessage}");

                    if (formattedMessage.Contains("replay_buffer", StringComparison.OrdinalIgnoreCase)
                        && (formattedMessage.Contains("Failed to open", StringComparison.OrdinalIgnoreCase)
                            || formattedMessage.Contains("Error opening", StringComparison.OrdinalIgnoreCase)
                            || formattedMessage.Contains("os_fopen failed", StringComparison.OrdinalIgnoreCase)
                            || formattedMessage.Contains("Failed to create file", StringComparison.OrdinalIgnoreCase)
                            || formattedMessage.Contains("No space left", StringComparison.OrdinalIgnoreCase)))
                    {
                        OnReplayMuxFailureLine(formattedMessage);
                    }

                    if (formattedMessage.Contains("capture window no longer exists, terminating capture"))
""",
    "log mux failure",
)

# ===== StartRecording: mode from eff, early disk check, store eff =====
must_replace(
            """            bool isReplayBufferMode = Settings.Instance.RecordingMode == RecordingMode.Buffer;
            bool isSessionMode = Settings.Instance.RecordingMode == RecordingMode.Session;
            bool isHybridMode = Settings.Instance.RecordingMode == RecordingMode.Hybrid;

            string fileName = Path.GetFileName(exePath);
""",
            """            EffectiveRecordingSettings eff = GameSettingsService.Resolve(exePath);

            bool isReplayBufferMode = eff.RecordingMode == RecordingMode.Buffer;
            bool isSessionMode = eff.RecordingMode == RecordingMode.Session;
            bool isHybridMode = eff.RecordingMode == RecordingMode.Hybrid;

            string fileName = Path.GetFileName(exePath);

            // Prevent starting if any of the system, recording or temp drives are almost full
            List<StorageService.FullDrive> fullDrives = StorageService.GetFullDrives();
            if (fullDrives.Count > 0)
            {
                string drivesText = string.Join(", ", fullDrives.Select(d => $"{d.Label} ({d.Root.TrimEnd('\\\\')}) is {d.UsedPercent:F1}% full"));
                Log.Error($"Cannot start recording, drive(s) over {StorageService.DriveFullThresholdPercent:F0}% full: {drivesText}");
                GameDetectionService.PreventRetryRecording = true;
                Task.Run(() => ShowModal("Not enough disk space", $"Recording cannot start because {drivesText}. Free up some space and try again.", "error"));
                Task.Run(() => PlaySound("error"));
                ClearAllPendingPreRecordings();
                return false;
            }
""",
    "StartRecording eff+disk",
)

# Fix TrimEnd escape - Python may have mangled it
text = text.replace(
    "d.Root.TrimEnd('\\\\\\\\')",
    "d.Root.TrimEnd('\\\\')"
)
# In C# we need TrimEnd('\\') which in file is TrimEnd('\\')
# After Python "TrimEnd('\\\\')" in a normal string becomes TrimEnd('\\') in output - good.
# But we used '\\\\' inside the replacement which became '\\' in text... let me check after write.

# Store eff on pipeline after slot allocated / _isStoppingSlot cleared
must_replace(
    """                _isStoppingSlot[slot] = false;

                var pl = Pipeline(slot);
                bool anotherSlotActive = IsAnotherSlotRecording(slot);
""",
    """                _isStoppingSlot[slot] = false;
                plUnexpected = 0; // placeholder removed below

                var pl = Pipeline(slot);
                pl.EffectiveSettings = eff;
                pl.UnexpectedStopHandled = 0;
                bool anotherSlotActive = IsAnotherSlotRecording(slot);
""",
    "store eff on pipeline",
)
text = text.replace("                plUnexpected = 0; // placeholder removed below\n\n", "")

# ResetVideoSettings with customResolution / frame rate from eff
must_replace(
    "                    ResetVideoSettings(out _, customFps: (uint)Settings.Instance.FrameRate);",
    "                    ResetVideoSettings(out _, customFps: (uint)eff.FrameRate, customResolution: eff.Resolution);",
    "ResetVideoSettings eff fps",
)

# HDR decision before CreateRecordingScene - insert after dedicated canvas block, before CreateRecordingScene
must_replace(
    """                CreateRecordingScene(slot, pl, dedicatedCanvasWidth, dedicatedCanvasHeight);

                // For manual recording, use display capture directly without game hooking
""",
    """                // Decide HDR up front when this start owns the global canvas (slot 0, no other active).
                if (slot == 0 && !anotherSlotActive)
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

                            if (HdrDetectionService.IsDisplayHdrActive(hdrTargetDeviceId))
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
                    ResetVideoSettings(out _, customFps: (uint)eff.FrameRate, customResolution: eff.Resolution);
                }

                CreateRecordingScene(slot, pl, dedicatedCanvasWidth, dedicatedCanvasHeight);

                // For manual recording, use display capture directly without game hooking
""",
    "HDR before CreateRecordingScene",
)

# We now may double-call ResetVideoSettings for slot 0 - remove the earlier one
must_replace(
    """                // OBS video settings are global; slot 1+ uses a dedicated canvas and must not touch global video.
                if (slot == 0 && !anotherSlotActive)
                    ResetVideoSettings(out _, customFps: (uint)eff.FrameRate, customResolution: eff.Resolution);
                else if (slot == 0 && anotherSlotActive)
                    Log.Information("Skipping global video settings change for slot {Slot}: another slot is already recording", slot);
                else
                    Log.Information("Skipping global video settings change for slot {Slot}: uses dedicated canvas", slot);
""",
    """                // OBS video settings are global; slot 1+ uses a dedicated canvas and must not touch global video.
                // Slot 0 HDR + ResetVideoSettings runs below once HDR is decided.
                if (slot == 0 && anotherSlotActive)
                    Log.Information("Skipping global video settings change for slot {Slot}: another slot is already recording", slot);
                else if (slot != 0)
                    Log.Information("Skipping global video settings change for slot {Slot}: uses dedicated canvas", slot);
""",
    "defer ResetVideoSettings",
)

# Game capture: volume, HDR color space
must_replace(
    """                        pl.GameCapture = new GameCapture($"gameplay_{slot}", GameCapture.CaptureMode.SpecificWindow);
                        pl.GameCapture.SetWindow($"*:*:{fileName}");

                        if (Settings.Instance.AudioOutputMode != AudioOutputMode.All)
                        {
                            pl.GameCapture.Update(s => s.Set("capture_audio", true));
                            Log.Information($"Game capture audio enabled (mode: {Settings.Instance.AudioOutputMode})");
                        }
""",
    """                        pl.GameCapture = new GameCapture($"gameplay_{slot}", GameCapture.CaptureMode.SpecificWindow);
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
""",
    "game capture volume/HDR",
)

# Window dims ResetVideoSettings with eff
must_replace(
    """                            ResetVideoSettings(
                                out bool is4by3,
                                customFps: (uint)Settings.Instance.FrameRate,
                                customOutputWidth: windowWidth,
                                customOutputHeight: windowHeight
                            );
""",
    """                            ResetVideoSettings(
                                out bool is4by3,
                                customFps: (uint)eff.FrameRate,
                                customOutputWidth: windowWidth,
                                customOutputHeight: windowHeight,
                                customResolution: eff.Resolution
                            );
""",
    "window dims reset eff",
)

# Encoder from eff + HDR + ApplyNvencBFrameLimit + VAAPI branch
must_replace(
    """                // Create video encoder
                string encoderId = Settings.Instance.Codec!.InternalEncoderId;
                Log.Information($"Using encoder: {Settings.Instance.Codec!.FriendlyName} ({encoderId})");

                using var videoEncoderSettings = new ObsKit.NET.Core.Settings();
                videoEncoderSettings.Set("preset", "Quality");
                videoEncoderSettings.Set("profile", "high");
                videoEncoderSettings.Set("use_bufsize", true);
                videoEncoderSettings.Set("rate_control", Settings.Instance.RateControl);
                videoEncoderSettings.Set("keyint_sec", 1);

                switch (Settings.Instance.RateControl)
                {
                    case "CBR":
                        int targetBitrateKbps = Settings.Instance.Bitrate * 1000;
                        videoEncoderSettings.Set("bitrate", targetBitrateKbps);
                        videoEncoderSettings.Set("max_bitrate", targetBitrateKbps);
                        videoEncoderSettings.Set("bufsize", targetBitrateKbps);
                        break;

                    case "VBR":
                        int minBitrateKbps = Settings.Instance.MinBitrate * 1000;
                        int maxBitrateKbps = Settings.Instance.MaxBitrate * 1000;
                        videoEncoderSettings.Set("bitrate", minBitrateKbps);
                        videoEncoderSettings.Set("max_bitrate", maxBitrateKbps);
                        videoEncoderSettings.Set("bufsize", maxBitrateKbps);
                        break;

                    case "CRF":
                        // Software x264 path mainly; no explicit bitrate
                        videoEncoderSettings.Set("crf", Settings.Instance.CrfValue);
                        break;

                    case "CQP":
                        // Hardware encoders (NVENC/QSV/AMF) often use cqp/cq; provide both cqp and qp for compatibility
                        videoEncoderSettings.Set("cqp", Settings.Instance.CqLevel);
                        videoEncoderSettings.Set("qp", Settings.Instance.CqLevel);
                        break;

                    case "CQVBR":
                        // OBS 31+ NVENC: Variable Bitrate with Target Quality (caps peak bitrate while targeting CQ)
                        if (!encoderId.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
                        {
                            ClearAllPendingPreRecordings();
                            throw new Exception("CQVBR is only supported with NVIDIA NVENC encoders (OBS 31+).");
                        }

                        int cqvbrMaxKbps = Settings.Instance.MaxBitrate * 1000;
                        videoEncoderSettings.Set("max_bitrate", cqvbrMaxKbps);
                        int maxTq = encoderId.Contains("av1", StringComparison.OrdinalIgnoreCase) ? 63 : 51;
                        int targetQ = Math.Clamp(Settings.Instance.CqLevel, 1, maxTq);
                        videoEncoderSettings.Set("target_quality", targetQ);
                        break;

                    default:
                        ClearAllPendingPreRecordings();
                        throw new Exception("Unsupported Rate Control method.");
                }

                // Disable HEVC b-frames on older NVIDIA GPUs (requires compute capability >= 7.0)
                if (encoderId.Equals("jim_hevc_nvenc", StringComparison.OrdinalIgnoreCase) &&
                    AppState.Instance.CudaComputeCapability != null &&
                    AppState.Instance.CudaComputeCapability < 7.0)
                {
                    videoEncoderSettings.Set("bf", 0);
                    Log.Information("NVENC b-frames disabled (CUDA compute capability < 7.0)");
                }

                pl.VideoEncoder = new VideoEncoder(encoderId, ObsName(slot, "Segra Recorder"), videoEncoderSettings);
""",
    """                string encoderId = eff.Codec!.InternalEncoderId;
                if (_isHdrRecording && _hdrEncoderId != null && slot == 0 && !anotherSlotActive)
                    encoderId = _hdrEncoderId;
                Log.Information($"Using encoder: {encoderId}{(_isHdrRecording && slot == 0 && !anotherSlotActive ? " (HDR)" : "")}");

                using var videoEncoderSettings = new ObsKit.NET.Core.Settings();
                videoEncoderSettings.Set("keyint_sec", 1);

                if (IsVaapiEncoder(encoderId))
                {
                    ConfigureVaapiVideoEncoder(videoEncoderSettings, encoderId, eff);
                }
                else
                {
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

                pl.VideoEncoder = new VideoEncoder(encoderId, ObsName(slot, "Segra Recorder"), videoEncoderSettings);
""",
    "encoder eff/VAAPI/NVENC",
)

OUT.write_text(text, encoding="utf-8", newline="\n")
print(f"Part B1 written ({len(text.splitlines())} lines)")
