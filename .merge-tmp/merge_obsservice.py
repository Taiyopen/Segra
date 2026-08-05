#!/usr/bin/env python3
"""
Hybrid OBSService.cs merger.
Base: OURS (dual-slot + VRChat VVMW)
Ports: upstream ObsKit 1.5 / v1.7.0 path init, hotkeys, output events, EffectiveSettings,
       VoiceChat, HDR/NVENC/disk monitor hooks, ClearOutputSource, UpdateMonitorCapture, etc.
"""
from __future__ import annotations
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
text = (ROOT / ".merge-tmp" / "OBSService.ours.cs").read_text(encoding="utf-8")
OUT = ROOT / "Backend" / "Recorder" / "OBSService.cs"


def must_replace(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"FAIL [{label}]: expected 1 occurrence, found {count}")
    text = text.replace(old, new, 1)
    print(f"OK [{label}]")


def must_insert_after(anchor: str, insertion: str, label: str) -> None:
    global text
    count = text.count(anchor)
    if count != 1:
        raise SystemExit(f"FAIL [{label}]: anchor count={count}")
    text = text.replace(anchor, anchor + insertion, 1)
    print(f"OK [{label}]")


# ===== 1. Usings =====
must_replace(
    """using ObsKit.NET.Scenes;
using ObsKit.NET.Signals;
using ObsKit.NET.Sources;
using Segra.Backend.Core.Models;
using Segra.Backend.Services;
using Segra.Backend.Shared;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using static Segra.Backend.Shared.GeneralUtils;
using static Segra.Backend.App.MessageService;
using System.Net.Http.Json;
using Segra.Backend.Media;
using Segra.Backend.App;
using Segra.Backend.Windows.Display;
using Segra.Backend.Games;
using Segra.Backend.Games.VrChat;
using Segra.Backend.Windows.Input;
using Segra.Backend.Windows.Storage;
using System.Threading.Channels;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
""",
    """using ObsKit.NET.Scenes;
using ObsKit.NET.Sources;
using Segra.Backend.Core.Models;
using Segra.Backend.Services;
using Segra.Backend.Shared;
using Segra.Backend.Platform;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using static Segra.Backend.Shared.GeneralUtils;
using static Segra.Backend.App.MessageService;
using System.Net.Http.Json;
using Segra.Backend.Media;
using Segra.Backend.App;
using Segra.Backend.Games;
using Segra.Backend.Games.VrChat;
using Segra.Backend.Windows.Input;
using Segra.Backend.Windows.Storage;
using System.Threading.Channels;
#if WINDOWS
using Segra.Backend.Windows.Display;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
#endif
""",
    "usings",
)

# ===== 2. SessionPipeline =====
must_replace(
    """            public RecordingOutput? SessionOutput;
            public ReplayBuffer? BufferOutput;
            public SignalConnection? ReplaySavedConnection;
            public MonitorCapture? DisplaySource;
            public readonly List<AudioInputCapture> MicSources = new();
            public readonly List<AudioOutputCapture> DesktopSources = new();
            public Source? DiscordAudioSource;
            public VideoEncoder? VideoEncoder;
            public readonly List<AudioEncoder> AudioEncoders = new();
            public string? HookedExecutableFileName;
            public System.Threading.Timer? GameCaptureHookTimeoutTimer;
            public GameCapture? GameCapture;
            public Action<GameCapture>? HookedSubscription;
            public Action<GameCapture>? UnhookedSubscription;
            /// <summary>Secondary canvas for slot 1+; slot 0 uses the main canvas.</summary>
            public Canvas? RecordingCanvas;
        }
""",
    """            public RecordingOutput? SessionOutput;
            public ReplayBuffer? BufferOutput;
            public Source? DisplaySource;
            public readonly List<AudioInputCapture> MicSources = new();
            public readonly List<AudioOutputCapture> DesktopSources = new();
            public readonly List<(string Name, string Window, Source Source)> VoiceChatSources = new();
            public VideoEncoder? VideoEncoder;
            public readonly List<AudioEncoder> AudioEncoders = new();
            public string? HookedExecutableFileName;
            public System.Threading.Timer? GameCaptureHookTimeoutTimer;
            public GameCapture? GameCapture;
            public Action<GameCapture>? HookedSubscription;
            public Action<GameCapture>? UnhookedSubscription;
            public EventHandler<ReplaySavedEventArgs>? ReplaySavedHandler;
            public EventHandler<OutputStoppedEventArgs>? BufferStoppedHandler;
            public EventHandler<OutputStoppedEventArgs>? SessionStoppedHandler;
            /// <summary>Secondary canvas for slot 1+; slot 0 uses the main canvas.</summary>
            public Canvas? RecordingCanvas;
            public EffectiveRecordingSettings? EffectiveSettings;
            public uint VoiceChatMixerMask = 1u << 0;
            public int UnexpectedStopHandled;
        }
""",
    "SessionPipeline",
)

# ===== 3. Shared audio Discord -> VoiceChat =====
must_replace(
    "pl.MicSources.Count > 0 || pl.DesktopSources.Count > 0 || pl.DiscordAudioSource != null;",
    "pl.MicSources.Count > 0 || pl.DesktopSources.Count > 0 || pl.VoiceChatSources.Count > 0;",
    "PipelineOwnsSharedAudio",
)

must_replace(
    """        private static Source? FindDiscordAudioSource()
        {
            foreach (var p in _pipelines)
            {
                if (p.DiscordAudioSource != null)
                    return p.DiscordAudioSource;
            }
            return null;
        }
""",
    """        private static IEnumerable<(string Name, string Window, Source Source)> EnumerateVoiceChatSources()
        {
            foreach (var p in _pipelines)
            {
                foreach (var v in p.VoiceChatSources)
                    yield return v;
            }
        }
""",
    "EnumerateVoiceChatSources",
)

must_replace(
    """            if (from.DiscordAudioSource != null)
            {
                to.DiscordAudioSource = from.DiscordAudioSource;
                from.DiscordAudioSource = null;
            }

            Log.Information("Transferred shared WASAPI/Discord audio ownership to remaining recording slot");
""",
    """            to.VoiceChatSources.AddRange(from.VoiceChatSources);
            from.VoiceChatSources.Clear();
            to.VoiceChatMixerMask = from.VoiceChatMixerMask;

            Log.Information("Transferred shared WASAPI/voice-chat audio ownership to remaining recording slot");
""",
    "TransferSharedAudioOwnership",
)

must_replace(
    """            if (pl.DiscordAudioSource != null)
            {
                try
                {
                    pl.DiscordAudioSource.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose Discord audio source: {ex.Message}");
                }
                pl.DiscordAudioSource = null;
            }
        }
""",
    """            foreach (var (_, _, voiceSource) in pl.VoiceChatSources)
            {
                try
                {
                    voiceSource.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose voice chat audio source: {ex.Message}");
                }
            }
            pl.VoiceChatSources.Clear();
        }
""",
    "DisposeOwnedSharedAudioSources",
)

# ===== 4. Global fields after ReplayBufferSaveLock =====
must_insert_after(
    """        /// <summary>Serializes replay buffer save, manual F10 save, and VRChat VVMW tail clips.</summary>
        private static readonly SemaphoreSlim ReplayBufferSaveLock = new(1, 1);

""",
    """        private static readonly (string Name, string Window)[] VoiceChatApps =
        [
            ("Discord", "Discord:Chrome_WidgetWin_1:Discord.exe"),
            ("TeamSpeak", "TeamSpeak:Chrome_WidgetWin_1:TeamSpeak.exe"),
            ("TeamSpeak 3", "TeamSpeak 3:Qt5152QWindowIcon:ts3client_win64.exe"),
            ("TeamSpeak 3", "TeamSpeak 3:Qt5152QWindowIcon:ts3client_win32.exe"),
        ];

        private static System.Threading.Timer? _diskSpaceMonitorTimer;
        private const int DiskSpaceCheckIntervalMs = 60000;
        private const int QualityModeAssumedMbps = 150;

        private static bool _isHdrRecording;
        private static string? _hdrEncoderId;
#if WINDOWS
        private const int HdrWindowWaitAttempts = 120;
        private const int HdrWindowWaitDelayMs = 500;
#endif

        private static ReplaySaveRequest? _activeReplaySave;
        private static readonly object _replaySaveLock = new();
        private static bool _previousSaveIndeterminate;

        private sealed class ReplaySaveRequest
        {
            public required int Slot { get; init; }
            public required string Game { get; init; }
            public int? IgdbId { get; init; }
            public List<string>? AudioTrackNames { get; init; }
            public string? FailureReason;
            public readonly TaskCompletionSource<string?> Signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static EffectiveRecordingSettings? ActiveEffectiveSettings
        {
            get
            {
                for (int i = 0; i < MaxSessionSlots; i++)
                {
                    if (_pipelines[i].EffectiveSettings != null && AppState.Instance.GetRecording(i) != null)
                        return _pipelines[i].EffectiveSettings;
                }
                for (int i = 0; i < MaxSessionSlots; i++)
                {
                    if (_pipelines[i].EffectiveSettings != null)
                        return _pipelines[i].EffectiveSettings;
                }
                return null;
            }
        }

""",
    "global fields",
)

# Remove obsolete bool array for replay flags (we'll replace save internals)
must_replace(
    "        private static readonly bool[] _replaySavedBySlot = new bool[MaxSessionSlots];\n",
    "",
    "remove _replaySavedBySlot decl",
)

# ===== 5. InitializeAsync =====
must_replace(
    """            try
            {
                await CheckIfExistsOrDownloadAsync();
            }
            catch (Exception ex)
            {
                Log.Error($"OBS installation failed: {ex.Message}");
                await MessageService.ShowModal(
                    "Recorder Error",
                    "The recorder installation failed. Please check your internet connection and try again. If you have any games running, please close them and restart Segra.",
                    "error",
                    "Could not install recorder"
                );
                AppState.Instance.HasLoadedObs = true;
                return;
            }

            if (Obs.IsInitialized)
                throw new Exception("Error: OBS is already initialized.");

            // Start the log queue processor before setting the log handler
            _ = Task.Run(ProcessLogQueueAsync);

            try
            {
                // Initialize OBS using ObsKit.NET fluent API
                _obsContext = Obs.Initialize(config =>
                {
                    config
                        .WithLocale("en-US")
                        .WithDataPath("./data/libobs/")
                        .WithModulePath("./obs-plugins/64bit/", "./data/obs-plugins/%module%/")
                        .WithVideo(v => v
                            .Resolution(1920, 1080)
                            .Fps(60))
                        .WithAudio(a => a
                            .WithSampleRate(44100)
                            .WithSpeakers(SpeakerLayout.Stereo))
                        .WithLogging((level, message) =>
                        {
                            try
                            {
                                // Queue the message for async processing - this is non-blocking
                                _logChannel.Writer.TryWrite(((int)level, message));
                            }
                            catch
                            {
                                // Silently ignore marshaling errors to never block OBS
                            }
                        });
                });

                // Disable auto-dispose for manual resource management
                Obs.AutoDispose = false;

                InstalledOBSVersion = Obs.Version;
                Log.Information("OBS version: " + InstalledOBSVersion);

                // Set available encoders in state
                SetAvailableEncodersInState();

                IsInitialized = true;
                AppState.Instance.HasLoadedObs = true;
                Log.Information("OBS initialized successfully!");

                _ = Task.Run(RecoveryService.CheckForOrphanedFilesAsync);
                GameDetectionService.StartAsync();
                GameDetectionService.ForegroundHook.Start();
            }
""",
    """            try
            {
                await CheckIfExistsOrDownloadAsync();
            }
            catch (Exception ex)
            {
                Log.Error($"OBS installation failed: {ex.Message}");
#if WINDOWS
                await MessageService.ShowModal(
                    "Recorder Error",
                    "The recorder installation failed. Please check your internet connection and try again. If you have any games running, please close them and restart Segra.",
                    "error",
                    "Could not install recorder"
                );
#else
                await MessageService.ShowModal(
                    "Recorder not found",
                    "Segra's Linux recorder needs OBS Studio's libraries (libobs). Install OBS with your package manager, for example:\\n\\n    sudo apt install obs-studio\\n\\nThen restart Segra.",
                    "error",
                    "OBS Studio not found"
                );
#endif
                AppState.Instance.HasLoadedObs = true;
                return;
            }

#if WINDOWS
            NvencCapsService.StartProbe();
#endif

            if (Obs.IsInitialized)
                throw new Exception("Error: OBS is already initialized.");

            _ = Task.Run(ProcessLogQueueAsync);

            try
            {
#if WINDOWS
                string baseDir = AppContext.BaseDirectory;
                string obsModulePath = Path.Combine(baseDir, "obs-plugins", "64bit");
                string obsModuleDataPath = Path.Combine(baseDir, "data", "obs-plugins", "%module%");
                string obsDataPath = Path.Combine(baseDir, "data", "libobs");
                Log.Information($"OBS runtime paths: data='{obsDataPath}', modules='{obsModulePath}'");
#else
                string obsModulePath = Environment.GetEnvironmentVariable("SEGRA_OBS_MODULE_PATH") ?? "./obs-plugins/";
                string obsModuleDataPath = Environment.GetEnvironmentVariable("SEGRA_OBS_MODULE_DATA_PATH") ?? "./data/obs-plugins/%module%/";
                string obsDataPath = Environment.GetEnvironmentVariable("SEGRA_OBS_DATA_PATH") ?? "./data/libobs/";
                Log.Information($"Linux OBS runtime: data='{obsDataPath}', modules='{obsModulePath}'");
#endif
                _obsContext = Obs.Initialize(config =>
                {
                    config
                        .WithLocale("en-US")
                        .WithDataPath(obsDataPath)
                        .WithModulePath(obsModulePath, obsModuleDataPath)
#if !WINDOWS
                        .ForHeadlessOperation()
#endif
                        .WithVideo(v => v
                            .Resolution(1920, 1080)
                            .Fps(60))
                        .WithAudio(a => a
                            .WithSampleRate(44100)
                            .WithSpeakers(SpeakerLayout.Stereo))
                        .WithLogging((level, message) =>
                        {
                            try { _logChannel.Writer.TryWrite(((int)level, message)); }
                            catch { }
                        });
                });

                Obs.AutoDispose = false;

                InstalledOBSVersion = Obs.Version;
                Log.Information("OBS version: " + InstalledOBSVersion);

                SetAvailableEncodersInState();

                IsInitialized = true;
                AppState.Instance.HasLoadedObs = true;
                Log.Information("OBS initialized successfully!");

                try { KeybindCaptureService.Start(); }
                catch (Exception ex) { Log.Error(ex, "Failed to register keybind hotkeys"); }

                _ = Task.Run(RecoveryService.CheckForOrphanedFilesAsync);
                _ = GameDetectionService.StartAsync();
                GameDetectionService.ForegroundHook.Start();
            }
""",
    "InitializeAsync",
)

# Fix double-escaped newlines in the Linux modal string inside the Python source
text = text.replace(
    "for example:\\\\n\\\\n    sudo apt install obs-studio\\\\n\\\\nThen restart Segra.",
    "for example:\\n\\n    sudo apt install obs-studio\\n\\nThen restart Segra.",
)

# ===== 6. Shutdown =====
must_replace(
    """                Log.Information("Shutting down OBS...");

                // Dispose the OBS context to properly clean up OBS resources
                _obsContext?.Dispose();
""",
    """                Log.Information("Shutting down OBS...");

                try { KeybindCaptureService.Stop(); }
                catch (Exception ex) { Log.Debug(ex, "KeybindCaptureService.Stop during shutdown"); }

                _obsContext?.Dispose();
""",
    "Shutdown",
)

# ===== 7. ResetVideoSettings =====
must_replace(
    "private static void ResetVideoSettings(out bool is4by3, uint? customFps = null, uint? customOutputWidth = null, uint? customOutputHeight = null)",
    "private static void ResetVideoSettings(out bool is4by3, uint? customFps = null, uint? customOutputWidth = null, uint? customOutputHeight = null, string? customResolution = null)",
    "ResetVideoSettings sig",
)
must_replace(
    "            // Get the maximum height from resolution setting\n            SettingsService.GetResolution(Settings.Instance.Resolution, out uint maxWidth, out uint maxHeight);",
    "            // Get the maximum height from resolution setting (per-game override may substitute)\n            SettingsService.GetResolution(customResolution ?? Settings.Instance.Resolution, out uint maxWidth, out uint maxHeight);",
    "ResetVideoSettings resolution",
)
must_replace(
    """            Obs.SetVideo(v => v
                .BaseResolution(baseWidth, baseHeight)
                .OutputResolution(outputWidth, outputHeight)
                .Fps(customFps ?? 60));
        }
""",
    """            Obs.SetVideo(v =>
            {
                v.BaseResolution(baseWidth, baseHeight)
                 .OutputResolution(outputWidth, outputHeight)
                 .Fps(customFps ?? 60);

                if (_isHdrRecording)
                    v.Hdr();
                else
                    v.Sdr();
            });
        }
""",
    "ResetVideoSettings HDR",
)

OUT.write_text(text, encoding="utf-8", newline="\n")
print(f"Part A written ({len(text.splitlines())} lines)")
print("PART_A_OK")
