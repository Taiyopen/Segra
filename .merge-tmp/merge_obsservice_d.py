#!/usr/bin/env python3
"""Part D: helper methods, encoders, Linux install, preview guards."""
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


HELPERS = r'''
        private static Source? TryAddVoiceChatSource(SessionPipeline pl, (string Name, string Window) app, bool muted)
        {
            if (pl.MainScene == null)
                return null;
            if (pl.VoiceChatSources.Any(v => v.Window == app.Window))
                return null;

            try
            {
                var voiceSource = new Source("wasapi_process_output_capture", ObsName(Array.IndexOf(_pipelines, pl) >= 0 ? Array.IndexOf(_pipelines, pl) : 0, $"{app.Name} Audio"));
                // Prefer stable slot name via owning pipeline index:
                int ownerSlot = 0;
                for (int i = 0; i < MaxSessionSlots; i++)
                {
                    if (ReferenceEquals(_pipelines[i], pl))
                    {
                        ownerSlot = i;
                        break;
                    }
                }
                voiceSource = new Source("wasapi_process_output_capture", ObsName(ownerSlot, $"{app.Name} Audio"));
                voiceSource.Update(s =>
                {
                    s.Set("window", app.Window);
                    s.Set("priority", 2);
                });
                voiceSource.IsMuted = muted;
                try { voiceSource.AudioMixers = pl.VoiceChatMixerMask; }
                catch (Exception ex) { Log.Warning($"Failed to set mixer for {app.Name} source: {ex.Message}"); }
                pl.MainScene.AddSource(voiceSource);
                pl.VoiceChatSources.Add((app.Name, app.Window, voiceSource));
                Log.Information($"Added {app.Name} application audio capture source{(muted ? " (muted until game hooks)" : "")}");
                return voiceSource;
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to create {app.Name} audio capture source: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Called by GameDetectionService's process watcher. Starts capturing a voice chat app
        /// that launches while a GameAndDiscord-mode recording is active.
        /// </summary>
        public static void OnVoiceChatAppStarted(string exePath)
        {
            try
            {
                if (Settings.Instance.AudioOutputMode != AudioOutputMode.GameAndDiscord) return;

                SessionPipeline? owner = null;
                GameCapture? capture = null;
                for (int i = 0; i < MaxSessionSlots; i++)
                {
                    var pl = Pipeline(i);
                    if (pl.MainScene == null || _isStoppingSlot[i]) continue;
                    if (owner == null && PipelineOwnsSharedAudio(pl))
                        owner = pl;
                    if (capture == null && pl.GameCapture != null)
                        capture = pl.GameCapture;
                }
                if (owner == null || capture == null) return;

                string fileName = Path.GetFileName(exePath);
                foreach (var app in VoiceChatApps)
                {
                    string appExe = app.Window.Split(':')[^1];
                    if (!string.Equals(fileName, appExe, StringComparison.OrdinalIgnoreCase)) continue;
                    if (owner.VoiceChatSources.Any(v => v.Window == app.Window)) return;
                    TryAddVoiceChatSource(owner, app, muted: !capture.IsHooked);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to handle voice chat app start for {exePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Repoints active game capture sources at a newly launched game executable.
        /// </summary>
        public static void UpdateGameCaptureWindow(string exePath)
        {
            try
            {
                string fileName = Path.GetFileName(exePath);
                for (int i = 0; i < MaxSessionSlots; i++)
                {
                    if (_isStoppingSlot[i]) continue;
                    var source = Pipeline(i).GameCapture;
                    if (source == null) continue;
                    source.Update(s => s.Set("window", $"*:*:{fileName}"));
                    Log.Information($"Updated game capture source to: {fileName} (slot {i})");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to update game capture window for {exePath}: {ex.Message}");
            }
        }

#if WINDOWS
        private static string? GetCaptureTargetDeviceId()
        {
            var displays = AppState.Instance.Displays;
            if (displays == null || displays.Count == 0)
                return null;

            if (Settings.Instance.SelectedDisplay != null)
            {
                var match = displays.FirstOrDefault(d => d.DeviceId == Settings.Instance.SelectedDisplay!.DeviceId);
                if (match != null)
                    return match.DeviceId;
            }

            return displays[0].DeviceId;
        }

        private static string? ResolveGameHdrTargetDeviceId()
        {
            string? fallbackDeviceId = GetCaptureTargetDeviceId();
            bool needWindow = DisplaysDisagreeOnHdr(fallbackDeviceId);
            int attempts = needWindow ? HdrWindowWaitAttempts : 3;
            int delayMs = needWindow ? HdrWindowWaitDelayMs : 100;

            if (needWindow)
                Log.Information("HDR detection: displays disagree on HDR; waiting up to {TimeoutMs}ms for the game window to determine its monitor.", attempts * delayMs);

            string? windowDeviceId = DisplayService.GetDeviceIdForWindow(
                WindowUtils.TryGetPreRecordingWindowHandle(maxAttempts: attempts, delayMs: delayMs));

            if (windowDeviceId != null)
                return windowDeviceId;

            if (needWindow)
                Log.Information("HDR detection: game window not found within wait budget; using fallback display for the HDR decision.");
            return fallbackDeviceId;
        }

        private static bool DisplaysDisagreeOnHdr(string? fallbackDeviceId)
        {
            var displays = AppState.Instance.Displays;
            if (displays == null || displays.Count < 2)
                return false;

            bool fallbackHdr = HdrDetectionService.IsDisplayHdrActive(fallbackDeviceId);
            return displays.Any(d => HdrDetectionService.IsDisplayHdrActive(d.DeviceId) != fallbackHdr);
        }
#endif

        private static void ApplyNvencBFrameLimit(ObsKit.NET.Core.Settings videoEncoderSettings, string encoderId)
        {
#if WINDOWS
            int? maxBFrames = NvencCapsService.GetMaxBFrames(encoderId);
            if (maxBFrames == null)
                return;

            int bf = Math.Min(2, maxBFrames.Value);
            videoEncoderSettings.Set("bf", bf);
            if (bf < 2)
                Log.Information($"NVENC b-frames limited to {bf} ({encoderId} supports max {maxBFrames} on this GPU)");
#endif
        }

        private static bool IsVaapiEncoder(string encoderId) =>
            encoderId.Contains("vaapi", StringComparison.OrdinalIgnoreCase);

        private static void ConfigureVaapiVideoEncoder(ObsKit.NET.Core.Settings s, string encoderId, EffectiveRecordingSettings eff)
        {
            const int H264_HIGH = 100, HEVC_MAIN = 1, HEVC_MAIN_10 = 2, AV1_MAIN = 0;
            string codec = EncoderInfo.Get(encoderId)?.Codec ?? "h264";
            int profile = codec switch
            {
                "hevc" => _isHdrRecording ? HEVC_MAIN_10 : HEVC_MAIN,
                "av1" => AV1_MAIN,
                _ => H264_HIGH,
            };
            s.Set("profile", profile);

            string rc = eff.RateControl == "CRF" ? "CQP" : eff.RateControl;
            s.Set("rate_control", rc);

            switch (rc)
            {
                case "CBR":
                    s.Set("bitrate", eff.Bitrate * 1000);
                    break;
                case "VBR":
                    s.Set("bitrate", eff.MinBitrate * 1000);
                    s.Set("maxrate", eff.MaxBitrate * 1000);
                    break;
                case "CQP":
                    s.Set("qp", eff.RateControl == "CRF" ? eff.CrfValue : eff.CqLevel);
                    break;
            }
        }

        private static void StartDiskSpaceMonitor()
        {
            StopDiskSpaceMonitor();

            _diskSpaceMonitorTimer = new System.Threading.Timer(
                OnDiskSpaceCheck,
                null,
                DiskSpaceCheckIntervalMs,
                DiskSpaceCheckIntervalMs
            );

            Log.Information($"Started disk space monitor (every {DiskSpaceCheckIntervalMs / 1000}s, stop below {GetRecordingFreeSpaceThresholdBytes() / (1024 * 1024)} MB free)");
        }

        private static long EstimateRecordingBytesPerSecond()
        {
            string rateControl = ActiveEffectiveSettings?.RateControl ?? Settings.Instance.RateControl;
            int bitrate = ActiveEffectiveSettings?.Bitrate ?? Settings.Instance.Bitrate;
            int maxBitrate = ActiveEffectiveSettings?.MaxBitrate ?? Settings.Instance.MaxBitrate;

            int videoMbps = rateControl switch
            {
                "CBR" => bitrate,
                "VBR" => maxBitrate,
                _ => Math.Max(maxBitrate, QualityModeAssumedMbps)
            };

            long bitsPerSecond = (videoMbps + 1L) * 1_000_000L;
            return bitsPerSecond / 8L;
        }

        private static long GetRecordingFreeSpaceThresholdBytes()
        {
            long intervalSeconds = DiskSpaceCheckIntervalMs / 1000;
            long perIntervalWithMargin = (long)(EstimateRecordingBytesPerSecond() * intervalSeconds * 1.5);
            const long finalizationBufferBytes = 128L * 1024 * 1024;
            long threshold = perIntervalWithMargin + finalizationBufferBytes;
            return Math.Max(StorageService.MinimumRecordingFreeSpaceBytes, threshold);
        }

        private static void StopDiskSpaceMonitor()
        {
            if (_diskSpaceMonitorTimer != null)
            {
                _diskSpaceMonitorTimer.Dispose();
                _diskSpaceMonitorTimer = null;
                Log.Information("Stopped disk space monitor");
            }
        }

        private static void OnDiskSpaceCheck(object? state)
        {
            try
            {
                if (!AppState.Instance.HasAnyRecording())
                    return;

                var driveSpace = StorageService.GetContentDriveSpaceGb();
                AppState.Instance.SetRecordingDriveSpaceGb(
                    driveSpace?.UsedGb,
                    driveSpace?.FreeGb,
                    sendToFrontend: true
                );

                long? freeBytes = StorageService.GetContentDriveFreeBytes();
                if (freeBytes == null || freeBytes.Value >= GetRecordingFreeSpaceThresholdBytes())
                    return;

                StopDiskSpaceMonitor();
                GameDetectionService.PreventRetryRecording = true;

                double freeMb = freeBytes.Value / (1024.0 * 1024.0);
                long thresholdMb = GetRecordingFreeSpaceThresholdBytes() / (1024 * 1024);
                Log.Warning($"Recording drive low on space ({freeMb:F0} MB free, threshold {thresholdMb} MB). Stopping recording to finalize the file safely.");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ShowModal("Recording stopped: running low on disk space",
                            $"The recording drive is running low on space ({freeMb:F0} MB free), so recording was stopped to save the file safely. Free up some space before recording again.",
                            "error");
                        _ = Task.Run(() => PlaySound("error"));
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error notifying frontend of low disk space stop: {ex.Message}");
                    }
                    finally
                    {
                        await StopRecording();
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Warning($"Disk space monitor check failed: {ex.Message}");
            }
        }

'''

# Fix TryAddVoiceChatSource - I accidentally double-created. Clean version:
HELPERS = '''
        private static Source? TryAddVoiceChatSource(SessionPipeline pl, (string Name, string Window) app, bool muted)
        {
            if (pl.MainScene == null)
                return null;
            if (pl.VoiceChatSources.Any(v => v.Window == app.Window))
                return null;

            int ownerSlot = 0;
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (ReferenceEquals(_pipelines[i], pl))
                {
                    ownerSlot = i;
                    break;
                }
            }

            try
            {
                var voiceSource = new Source("wasapi_process_output_capture", ObsName(ownerSlot, $"{app.Name} Audio"));
                voiceSource.Update(s =>
                {
                    s.Set("window", app.Window);
                    s.Set("priority", 2);
                });
                voiceSource.IsMuted = muted;
                try { voiceSource.AudioMixers = pl.VoiceChatMixerMask; }
                catch (Exception ex) { Log.Warning($"Failed to set mixer for {app.Name} source: {ex.Message}"); }
                pl.MainScene.AddSource(voiceSource);
                pl.VoiceChatSources.Add((app.Name, app.Window, voiceSource));
                Log.Information($"Added {app.Name} application audio capture source{(muted ? " (muted until game hooks)" : "")}");
                return voiceSource;
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to create {app.Name} audio capture source: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Called by GameDetectionService's process watcher. Starts capturing a voice chat app
        /// that launches while a GameAndDiscord-mode recording is active.
        /// </summary>
        public static void OnVoiceChatAppStarted(string exePath)
        {
            try
            {
                if (Settings.Instance.AudioOutputMode != AudioOutputMode.GameAndDiscord) return;

                SessionPipeline? owner = null;
                GameCapture? capture = null;
                for (int i = 0; i < MaxSessionSlots; i++)
                {
                    var pl = Pipeline(i);
                    if (pl.MainScene == null || _isStoppingSlot[i]) continue;
                    if (owner == null && PipelineOwnsSharedAudio(pl))
                        owner = pl;
                    if (capture == null && pl.GameCapture != null)
                        capture = pl.GameCapture;
                }
                // Prefer a pipeline that already owns shared audio; otherwise any active scene.
                if (owner == null)
                {
                    for (int i = 0; i < MaxSessionSlots; i++)
                    {
                        if (Pipeline(i).MainScene != null && !_isStoppingSlot[i])
                        {
                            owner = Pipeline(i);
                            break;
                        }
                    }
                }
                if (owner == null || capture == null) return;

                string fileName = Path.GetFileName(exePath);
                foreach (var app in VoiceChatApps)
                {
                    string appExe = app.Window.Split(':')[^1];
                    if (!string.Equals(fileName, appExe, StringComparison.OrdinalIgnoreCase)) continue;
                    if (owner.VoiceChatSources.Any(v => v.Window == app.Window)) return;
                    TryAddVoiceChatSource(owner, app, muted: !capture.IsHooked);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to handle voice chat app start for {exePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Repoints active game capture sources at a newly launched game executable.
        /// </summary>
        public static void UpdateGameCaptureWindow(string exePath)
        {
            try
            {
                string fileName = Path.GetFileName(exePath);
                for (int i = 0; i < MaxSessionSlots; i++)
                {
                    if (_isStoppingSlot[i]) continue;
                    var source = Pipeline(i).GameCapture;
                    if (source == null) continue;
                    source.Update(s => s.Set("window", $"*:*:{fileName}"));
                    Log.Information($"Updated game capture source to: {fileName} (slot {i})");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to update game capture window for {exePath}: {ex.Message}");
            }
        }

#if WINDOWS
        private static string? GetCaptureTargetDeviceId()
        {
            var displays = AppState.Instance.Displays;
            if (displays == null || displays.Count == 0)
                return null;

            if (Settings.Instance.SelectedDisplay != null)
            {
                var match = displays.FirstOrDefault(d => d.DeviceId == Settings.Instance.SelectedDisplay!.DeviceId);
                if (match != null)
                    return match.DeviceId;
            }

            return displays[0].DeviceId;
        }

        private static string? ResolveGameHdrTargetDeviceId()
        {
            string? fallbackDeviceId = GetCaptureTargetDeviceId();
            bool needWindow = DisplaysDisagreeOnHdr(fallbackDeviceId);
            int attempts = needWindow ? HdrWindowWaitAttempts : 3;
            int delayMs = needWindow ? HdrWindowWaitDelayMs : 100;

            if (needWindow)
                Log.Information("HDR detection: displays disagree on HDR; waiting up to {TimeoutMs}ms for the game window to determine its monitor.", attempts * delayMs);

            string? windowDeviceId = DisplayService.GetDeviceIdForWindow(
                WindowUtils.TryGetPreRecordingWindowHandle(maxAttempts: attempts, delayMs: delayMs));

            if (windowDeviceId != null)
                return windowDeviceId;

            if (needWindow)
                Log.Information("HDR detection: game window not found within wait budget; using fallback display for the HDR decision.");
            return fallbackDeviceId;
        }

        private static bool DisplaysDisagreeOnHdr(string? fallbackDeviceId)
        {
            var displays = AppState.Instance.Displays;
            if (displays == null || displays.Count < 2)
                return false;

            bool fallbackHdr = HdrDetectionService.IsDisplayHdrActive(fallbackDeviceId);
            return displays.Any(d => HdrDetectionService.IsDisplayHdrActive(d.DeviceId) != fallbackHdr);
        }
#endif

        private static void ApplyNvencBFrameLimit(ObsKit.NET.Core.Settings videoEncoderSettings, string encoderId)
        {
#if WINDOWS
            int? maxBFrames = NvencCapsService.GetMaxBFrames(encoderId);
            if (maxBFrames == null)
                return;

            int bf = Math.Min(2, maxBFrames.Value);
            videoEncoderSettings.Set("bf", bf);
            if (bf < 2)
                Log.Information($"NVENC b-frames limited to {bf} ({encoderId} supports max {maxBFrames} on this GPU)");
#endif
        }

        private static bool IsVaapiEncoder(string encoderId) =>
            encoderId.Contains("vaapi", StringComparison.OrdinalIgnoreCase);

        private static void ConfigureVaapiVideoEncoder(ObsKit.NET.Core.Settings s, string encoderId, EffectiveRecordingSettings eff)
        {
            const int H264_HIGH = 100, HEVC_MAIN = 1, HEVC_MAIN_10 = 2, AV1_MAIN = 0;
            string codec = EncoderInfo.Get(encoderId)?.Codec ?? "h264";
            int profile = codec switch
            {
                "hevc" => _isHdrRecording ? HEVC_MAIN_10 : HEVC_MAIN,
                "av1" => AV1_MAIN,
                _ => H264_HIGH,
            };
            s.Set("profile", profile);

            string rc = eff.RateControl == "CRF" ? "CQP" : eff.RateControl;
            s.Set("rate_control", rc);

            switch (rc)
            {
                case "CBR":
                    s.Set("bitrate", eff.Bitrate * 1000);
                    break;
                case "VBR":
                    s.Set("bitrate", eff.MinBitrate * 1000);
                    s.Set("maxrate", eff.MaxBitrate * 1000);
                    break;
                case "CQP":
                    s.Set("qp", eff.RateControl == "CRF" ? eff.CrfValue : eff.CqLevel);
                    break;
            }
        }

        private static void StartDiskSpaceMonitor()
        {
            StopDiskSpaceMonitor();

            _diskSpaceMonitorTimer = new System.Threading.Timer(
                OnDiskSpaceCheck,
                null,
                DiskSpaceCheckIntervalMs,
                DiskSpaceCheckIntervalMs
            );

            Log.Information($"Started disk space monitor (every {DiskSpaceCheckIntervalMs / 1000}s, stop below {GetRecordingFreeSpaceThresholdBytes() / (1024 * 1024)} MB free)");
        }

        private static long EstimateRecordingBytesPerSecond()
        {
            string rateControl = ActiveEffectiveSettings?.RateControl ?? Settings.Instance.RateControl;
            int bitrate = ActiveEffectiveSettings?.Bitrate ?? Settings.Instance.Bitrate;
            int maxBitrate = ActiveEffectiveSettings?.MaxBitrate ?? Settings.Instance.MaxBitrate;

            int videoMbps = rateControl switch
            {
                "CBR" => bitrate,
                "VBR" => maxBitrate,
                _ => Math.Max(maxBitrate, QualityModeAssumedMbps)
            };

            long bitsPerSecond = (videoMbps + 1L) * 1_000_000L;
            return bitsPerSecond / 8L;
        }

        private static long GetRecordingFreeSpaceThresholdBytes()
        {
            long intervalSeconds = DiskSpaceCheckIntervalMs / 1000;
            long perIntervalWithMargin = (long)(EstimateRecordingBytesPerSecond() * intervalSeconds * 1.5);
            const long finalizationBufferBytes = 128L * 1024 * 1024;
            long threshold = perIntervalWithMargin + finalizationBufferBytes;
            return Math.Max(StorageService.MinimumRecordingFreeSpaceBytes, threshold);
        }

        private static void StopDiskSpaceMonitor()
        {
            if (_diskSpaceMonitorTimer != null)
            {
                _diskSpaceMonitorTimer.Dispose();
                _diskSpaceMonitorTimer = null;
                Log.Information("Stopped disk space monitor");
            }
        }

        private static void OnDiskSpaceCheck(object? state)
        {
            try
            {
                if (!AppState.Instance.HasAnyRecording())
                    return;

                var driveSpace = StorageService.GetContentDriveSpaceGb();
                AppState.Instance.SetRecordingDriveSpaceGb(
                    driveSpace?.UsedGb,
                    driveSpace?.FreeGb,
                    sendToFrontend: true
                );

                long? freeBytes = StorageService.GetContentDriveFreeBytes();
                if (freeBytes == null || freeBytes.Value >= GetRecordingFreeSpaceThresholdBytes())
                    return;

                StopDiskSpaceMonitor();
                GameDetectionService.PreventRetryRecording = true;

                double freeMb = freeBytes.Value / (1024.0 * 1024.0);
                long thresholdMb = GetRecordingFreeSpaceThresholdBytes() / (1024 * 1024);
                Log.Warning($"Recording drive low on space ({freeMb:F0} MB free, threshold {thresholdMb} MB). Stopping recording to finalize the file safely.");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ShowModal("Recording stopped: running low on disk space",
                            $"The recording drive is running low on space ({freeMb:F0} MB free), so recording was stopped to save the file safely. Free up some space before recording again.",
                            "error");
                        _ = Task.Run(() => PlaySound("error"));
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error notifying frontend of low disk space stop: {ex.Message}");
                    }
                    finally
                    {
                        await StopRecording();
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Warning($"Disk space monitor check failed: {ex.Message}");
            }
        }

'''

must_replace(
    "        public static void DisposeSources(int slot)\n",
    HELPERS + "        public static void DisposeSources(int slot)\n",
    "insert helpers",
)

# ===== Encoder friendly names + SetAvailableEncoders =====
must_replace(
    """                ["obs_qsv11_av1"] = "Intel QSV AV1",

                // ???? CPU / software paths ??????????????????????????????????????????????????????
                ["obs_x264"] = "Software x264",
                ["ffmpeg_openh264"] = "Software OpenH264",
            };
""",
    """                ["obs_qsv11_av1"] = "Intel QSV AV1",

                // VAAPI (Linux hardware)
                ["ffmpeg_vaapi_tex"] = "VAAPI H.264",
                ["hevc_ffmpeg_vaapi_tex"] = "VAAPI H.265",
                ["av1_ffmpeg_vaapi_tex"] = "VAAPI AV1",

                // CPU / software paths
                ["obs_x264"] = "Software x264",
                ["ffmpeg_openh264"] = "Software OpenH264",
            };
""",
    "VAAPI encoder names",
)

must_replace(
    """            // Enumerate all encoder types using ObsKit.NET
            var encoderTypes = Obs.EnumerateEncoderTypes().ToList();
            int idx = 0;

            foreach (var encoderId in encoderTypes)
            {
                EncoderFriendlyNames.TryGetValue(encoderId, out var name);
                string friendlyName = name ?? encoderId;
                bool isHardware = encoderId.Contains("nvenc", StringComparison.OrdinalIgnoreCase) ||
                                  encoderId.Contains("amf", StringComparison.OrdinalIgnoreCase) ||
                                  encoderId.Contains("qsv", StringComparison.OrdinalIgnoreCase);

                Log.Information($"{idx} - {friendlyName} | {encoderId} | {(isHardware ? "Hardware" : "Software")}");
                if (name != null)
                {
                    AppState.Instance.Codecs.Add(new Codec { InternalEncoderId = encoderId, FriendlyName = friendlyName, IsHardwareEncoder = isHardware });
                }
                idx++;
            }
""",
    """            var encoderTypes = Obs.EnumerateEncoderTypes().ToList();
            int idx = 0;

            var encoderInfoById = EncoderInfo.GetAll(includeInternal: true)
                .ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var encoderId in encoderTypes)
            {
                EncoderFriendlyNames.TryGetValue(encoderId, out var name);
                string friendlyName = name ?? encoderId;
                bool isHardware = encoderInfoById.TryGetValue(encoderId, out var info) && info.IsHardware;

                Log.Information($"{idx} - {friendlyName} | {encoderId} | {(isHardware ? "Hardware" : "Software")}");
                if (name != null)
                {
                    AppState.Instance.Codecs.Add(new Codec { InternalEncoderId = encoderId, FriendlyName = friendlyName, IsHardwareEncoder = isHardware });
                }
                idx++;
            }
""",
    "SetAvailableEncoders EncoderInfo",
)

must_replace(
    """                // If not found, try AMD AMF H.264
                if (selectedCodec == null)
                {
                    selectedCodec = availableCodecs.FirstOrDefault(
                        c => c.InternalEncoderId.Equals(
                            "h264_texture_amf",
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                }

                // If still not found, fallback to first hardware encoder
""",
    """                // If not found, try AMD AMF H.264
                if (selectedCodec == null)
                {
                    selectedCodec = availableCodecs.FirstOrDefault(
                        c => c.InternalEncoderId.Equals(
                            "h264_texture_amf",
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                }

                // If not found, try VAAPI H.264 (Linux hardware)
                if (selectedCodec == null)
                {
                    selectedCodec = availableCodecs.FirstOrDefault(
                        c => c.InternalEncoderId.Equals(
                            "ffmpeg_vaapi_tex",
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                }

                // If still not found, fallback to first hardware encoder
""",
    "SelectDefaultCodec VAAPI",
)

# ===== IsOBSInstalled =====
must_replace(
    """        public static bool IsOBSInstalled()
        {
            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "obs.dll");
            return File.Exists(dllPath);
        }
""",
    """        public static bool IsOBSInstalled()
        {
#if WINDOWS
            string dllPath = Path.Combine(AppContext.BaseDirectory, "obs.dll");
            return File.Exists(dllPath);
#else
            string baseDir = AppContext.BaseDirectory;
            string? dataPath = Environment.GetEnvironmentVariable("SEGRA_OBS_DATA_PATH");
            if (!string.IsNullOrEmpty(dataPath) && Directory.Exists(dataPath))
                return true;

            return File.Exists(Path.Combine(Platform.Linux.LinuxObsRuntime.DownloadedBundleDir(), "lib", "libobs.so.0"))
                || File.Exists(Path.Combine(baseDir, "lib", "libobs.so.0"))
                || File.Exists(Path.Combine(baseDir, "libobs.so.0"))
                || LinuxSystemLibObsPath() != null;
#endif
        }

#if !WINDOWS
        private static string? LinuxSystemLibObsPath()
        {
            string[] dirs =
            [
                "/usr/lib/x86_64-linux-gnu",
                "/usr/lib64",
                "/usr/lib",
                "/lib/x86_64-linux-gnu",
                "/lib64",
                "/lib",
            ];
            foreach (var d in dirs)
            {
                var p = Path.Combine(d, "libobs.so.0");
                if (File.Exists(p)) return p;
            }
            return null;
        }
#endif
""",
    "IsOBSInstalled",
)

# ===== Preview JPEG: wrap System.Drawing usage =====
# Check if Already has #if - if methods use Bitmap, guard them
if "using System.Drawing;" not in text.split("namespace")[0] or "#if WINDOWS" in text.split("namespace")[0]:
    # Drawing usings are inside #if WINDOWS - need to guard preview methods
    must_replace(
        "        public static byte[]? TryGetRecordingPreviewJpeg(int maxEdgePixels = 220)\n",
        """#if WINDOWS
        public static byte[]? TryGetRecordingPreviewJpeg(int maxEdgePixels = 220)
""",
        "preview jpeg if start",
    )
    # Close before end of class - find BitmapToJpegBytes end
    must_replace(
        """        private static byte[] BitmapToJpegBytes(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            var jpegCodec = ImageCodecInfo.GetImageEncoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(Encoder.Quality, 72L);
            bmp.Save(ms, jpegCodec, encParams);
            return ms.ToArray();
        }

    }
}
""",
        """        private static byte[] BitmapToJpegBytes(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            var jpegCodec = ImageCodecInfo.GetImageEncoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(Encoder.Quality, 72L);
            bmp.Save(ms, jpegCodec, encParams);
            return ms.ToArray();
        }
#else
        public static byte[]? TryGetRecordingPreviewJpeg(int maxEdgePixels = 220) => null;
        public static byte[]? TryGetRecordingPreviewJpeg(int slot, int maxEdgePixels = 220) => null;
#endif

    }
}
""",
        "preview jpeg endif",
    )

# Check DisplayService.GetDeviceIdForWindow and WindowUtils.TryGetPreRecordingWindowHandle exist
# Verify MinimumRecordingFreeSpaceBytes / SetRecordingDriveSpaceGb / GetContentDriveSpaceGb exist

OUT.write_text(text, encoding="utf-8", newline="\n")
print(f"Part D written ({len(text.splitlines())} lines)")
print("conflict markers:", text.count("<<<<<<<"), text.count("======="), text.count(">>>>>>>"))
