using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ObsKit.NET;
using ObsKit.NET.Encoders;
using ObsKit.NET.Native.Types;
using ObsKit.NET.Outputs;
using ObsKit.NET.Scenes;
using ObsKit.NET.Sources;
using Segra.Backend.Core;
using Segra.Backend.Core.Models;
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

namespace Segra.Backend.Recorder
{
    public static partial class OBSService
    {
        // Constants
        private const uint OBS_SOURCE_FLAG_FORCE_MONO = 1u << 1; // from obs.h

        // Executables that OBS internally blacklists from game capture (cannot be hooked)
        // https://github.com/obsproject/obs-studio/blob/e448c0a963eda45f48515b2cb9a631daced9d503/plugins/win-capture/game-capture.c#L956
        private static readonly string[] ObsInternalBlacklist =
        [
            "explorer.exe",
            "steam.exe",
            "battle.net.exe",
            "galaxyclient.exe",
            "skype.exe",
            "uplay.exe",
            "origin.exe",
            "devenv.exe",
            "taskmgr.exe",
            "chrome.exe",
            "discord.exe",
            "firefox.exe",
            "systemsettings.exe",
            "applicationframehost.exe",
            "cmd.exe",
            "shellexperiencehost.exe",
            "winstore.app.exe",
            "searchui.exe",
            "lockapp.exe",
            "windowsinternal.composableshell.experiences.textinput.inputapp.exe"
        ];

        // Regex patterns for buffer parsing
        [GeneratedRegex(@"BufferDesc\.Width:\s*(\d+)")]
        private static partial Regex BufferDescWidthRegex();

        [GeneratedRegex(@"BufferDesc\.Height:\s*(\d+)")]
        private static partial Regex BufferDescHeightRegex();

        // Public properties
        public static bool IsInitialized { get; private set; }
        public static GpuVendor DetectedGpuVendor { get; private set; } = DetectGpuVendor();
        public static uint? CapturedWindowWidth { get; private set; } = null;
        public static uint? CapturedWindowHeight { get; private set; } = null;
        private static int _lastGameCaptureLogSlot = -1;
        public static string? InstalledOBSVersion { get; private set; } = null;

        // OBS context
        private static ObsContext? _obsContext;

        /// <summary>Holds scene, sources, encoders and outputs for one recording slot.</summary>
        private sealed class SessionPipeline
        {
            public Scene? MainScene;
            public SceneItem? GameCaptureItem;
            public SceneItem? DisplayItem;
            public RecordingOutput? SessionOutput;
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
            // Global audio settings as they were when this slot started; settings changed mid-recording apply to the next one
            public AudioOutputMode AudioOutputMode;
            public bool EnableSeparateAudioTracks;
            public uint VoiceChatMixerMask = 1u << 0;
            public int UnexpectedStopHandled;
        }

        private const int MaxSessionSlots = RecordingSlots.Max;
        private static readonly uint?[] _capturedWindowWidthBySlot = new uint?[MaxSessionSlots];
        private static readonly uint?[] _capturedWindowHeightBySlot = new uint?[MaxSessionSlots];
        private static readonly SessionPipeline[] _pipelines = [new SessionPipeline(), new SessionPipeline()];
        private static SessionPipeline Pipeline(int slot) => _pipelines[slot];

        /// <summary>OBS source/encoder names must be globally unique across dual slots.</summary>
        private static string ObsName(int slot, string name) => $"s{slot}_{name}";

        private static bool IsAnotherSlotRecording(int slot)
        {
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (i == slot) continue;
                if (AppState.Instance.GetRecording(i) != null)
                    return true;
                if (_pipelines[i].SessionOutput != null)
                    return true;
                if (_pipelines[i].BufferOutput != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Mic/desktop WASAPI and Discord captures must exist only once across dual slots;
        /// duplicating the same device feeds the global OBS mix twice.
        /// </summary>
        private static readonly object _sharedAudioLock = new();

        private static bool PipelineOwnsSharedAudio(SessionPipeline pl) =>
            pl.MicSources.Count > 0 || pl.DesktopSources.Count > 0 || pl.VoiceChatSources.Count > 0;

        private static SessionPipeline? FindSharedAudioOwner(int excludeSlot = -1)
        {
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (i == excludeSlot) continue;
                if (PipelineOwnsSharedAudio(_pipelines[i]))
                    return _pipelines[i];
            }
            return null;
        }

        private static int FindOtherActiveSlot(int slot)
        {
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (i == slot) continue;
                if (AppState.Instance.GetRecording(i) != null
                    || _pipelines[i].SessionOutput != null
                    || _pipelines[i].BufferOutput != null
                    || _pipelines[i].MainScene != null)
                    return i;
            }
            return -1;
        }

        private static IEnumerable<AudioOutputCapture> EnumerateDesktopSources()
        {
            foreach (var p in _pipelines)
            {
                foreach (var source in p.DesktopSources)
                    yield return source;
            }
        }

        private static IEnumerable<(string Name, string Window, Source Source)> EnumerateVoiceChatSources()
        {
            foreach (var p in _pipelines)
            {
                foreach (var v in p.VoiceChatSources)
                    yield return v;
            }
        }

        /// <summary>Every audio source feeding a recording, in mixer order and labelled for the PiP mixer.</summary>
        internal static List<(string Id, string Name, string Kind, Source Source)> GetMixerSources()
        {
            var result = new List<(string Id, string Name, string Kind, Source Source)>();
            try
            {
                static List<string> DeviceLabels(List<DeviceSetting>? devices, string defaultLabel) =>
                    (devices ?? []).Where(d => !string.IsNullOrEmpty(d.Id))
                        .Select(d => d.Id == "default" ? defaultLabel : d.Name)
                        .ToList();

                var micLabels = DeviceLabels(Settings.Instance.InputDevices, "預設麥克風");
                var desktopLabels = DeviceLabels(Settings.Instance.OutputDevices, "預設輸出");

                // Shared mic/desktop/voice sources live in exactly one pipeline, so indexes don't collide
                foreach (var pl in _pipelines)
                {
                    var mics = pl.MicSources.ToArray();
                    for (int i = 0; i < mics.Length; i++)
                        if (mics[i] != null)
                            result.Add(($"mic{i}", i < micLabels.Count ? micLabels[i] : $"麥克風 {i + 1}", "mic", mics[i]));

                    var desktops = pl.DesktopSources.ToArray();
                    for (int i = 0; i < desktops.Length; i++)
                        if (desktops[i] != null)
                            result.Add(($"desktop{i}", i < desktopLabels.Count ? desktopLabels[i] : $"桌面聲音 {i + 1}", "desktop", desktops[i]));
                }

                // Game capture only carries audio outside the "All" mode
                for (int slot = 0; slot < MaxSessionSlots; slot++)
                {
                    var pl = _pipelines[slot];
                    var game = pl.GameCapture;
                    if (game != null && pl.AudioOutputMode != AudioOutputMode.All)
                        result.Add(($"game{slot}", AppState.Instance.GetRecording(slot)?.Game ?? "遊戲聲音", "game", game));
                }

                foreach (var pl in _pipelines)
                {
                    var voices = pl.VoiceChatSources.ToArray();
                    for (int i = 0; i < voices.Length; i++)
                        if (voices[i].Source != null)
                            result.Add(($"voice{i}", voices[i].Name, "voice", voices[i].Source));
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to list mixer sources");
            }
            return result;
        }

        /// <summary>
        /// Attach an existing shared audio source to another slot's scene so it stays active
        /// on that canvas without opening a second WASAPI/Discord capture.
        /// </summary>
        private static void AttachSharedAudioSourceToScene(Scene scene, Source source, string label)
        {
            try
            {
                scene.AddSource(source);
                Log.Information("Attached shared {Label} audio source to secondary slot scene", label);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to attach shared {Label} audio source to scene: {Message}", label, ex.Message);
            }
        }

        private static void TransferSharedAudioOwnership(SessionPipeline from, SessionPipeline to)
        {
            if (!PipelineOwnsSharedAudio(from))
                return;

            to.MicSources.AddRange(from.MicSources);
            from.MicSources.Clear();

            to.DesktopSources.AddRange(from.DesktopSources);
            from.DesktopSources.Clear();

            to.VoiceChatSources.AddRange(from.VoiceChatSources);
            from.VoiceChatSources.Clear();
            to.VoiceChatMixerMask = from.VoiceChatMixerMask;

            Log.Information("Transferred shared WASAPI/voice-chat audio ownership to remaining recording slot");
        }

        private static void DisposeOwnedSharedAudioSources(SessionPipeline pl)
        {
            foreach (var micSource in pl.MicSources)
            {
                try
                {
                    AudioMixerService.DisposeSource(micSource);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose mic source: {ex.Message}");
                }
            }
            pl.MicSources.Clear();

            foreach (var desktopSource in pl.DesktopSources)
            {
                try
                {
                    AudioMixerService.DisposeSource(desktopSource);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose desktop source: {ex.Message}");
                }
            }
            pl.DesktopSources.Clear();

            foreach (var (_, _, voiceSource) in pl.VoiceChatSources)
            {
                try
                {
                    AudioMixerService.DisposeSource(voiceSource);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose voice chat audio source: {ex.Message}");
                }
            }
            pl.VoiceChatSources.Clear();
        }

        public static GameCapture? GetGameCaptureSource(int slot) => Pipeline(slot).GameCapture;

        public static bool IsSlotCaptureHooked(int slot) => Pipeline(slot).GameCapture?.IsHooked ?? false;

        public static string? GetTrackedExecutableFileName(int slot) =>
            Pipeline(slot).HookedExecutableFileName
            ?? AppState.Instance.GetRecording(slot)?.FileName;

        private static int? TryResolveHookedProcessId(string hookedExecutable, int slot)
        {
            string hookedFileName = Path.GetFileName(hookedExecutable);
            if (string.IsNullOrEmpty(hookedFileName))
                return null;

            var otherPids = new HashSet<int>();
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (i == slot) continue;
                if (AppState.Instance.GetRecording(i)?.Pid is int otherPid)
                    otherPids.Add(otherPid);
            }

            int? bestPid = null;
            long bestMemory = -1;

            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(hookedFileName)))
            {
                try
                {
                    if (process.HasExited || otherPids.Contains(process.Id))
                        continue;

                    string? path = null;
                    try
                    {
                        path = process.MainModule?.FileName;
                    }
                    catch
                    {
                        // Access denied for some processes
                    }

                    if (path != null
                        && !path.EndsWith(hookedFileName, StringComparison.OrdinalIgnoreCase)
                        && !hookedFileName.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))
                        continue;

                    long mem = process.WorkingSet64;
                    if (mem > bestMemory)
                    {
                        bestMemory = mem;
                        bestPid = process.Id;
                    }
                }
                catch
                {
                    // Ignore processes we cannot inspect
                }
                finally
                {
                    process.Dispose();
                }
            }

            return bestPid;
        }

        /// <summary>First hooked game capture (slot 0, then slot 1), otherwise first available.</summary>
        public static GameCapture? GameCaptureSource =>
            GetGameCaptureSource(0) is { IsHooked: true } gc0 ? gc0
            : GetGameCaptureSource(1) is { IsHooked: true } gc1 ? gc1
            : GetGameCaptureSource(0) ?? GetGameCaptureSource(1);

        /// <summary>Used by OBS log processing to avoid false-positive stop when capture blips.</summary>
        private static readonly bool[] _isStillHookedAfterUnhookBySlot = new bool[MaxSessionSlots];

        // Recording/output state
        private static readonly bool[] _isStoppingSlot = new bool[MaxSessionSlots];
        private static readonly SemaphoreSlim[] _slotLifecycleLocks =
            Enumerable.Range(0, MaxSessionSlots).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
        private static uint _currentBaseWidth;
        private static uint _currentBaseHeight;

        private static (uint width, uint height) EnsureEvenDimensions(uint width, uint height)
        {
            if (width % 2 != 0)
                width--;
            if (height % 2 != 0)
                height--;
            return (Math.Max(2, width), Math.Max(2, height));
        }

        public static bool TryGetCapturedWindowDimensions(int slot, out uint width, out uint height)
        {
            if (slot >= 0
                && slot < MaxSessionSlots
                && _capturedWindowWidthBySlot[slot].HasValue
                && _capturedWindowHeightBySlot[slot].HasValue)
            {
                width = _capturedWindowWidthBySlot[slot]!.Value;
                height = _capturedWindowHeightBySlot[slot]!.Value;
                return true;
            }

            width = 0;
            height = 0;
            return false;
        }

        private static void ClearCapturedWindowDimensions(int slot)
        {
            if (slot < 0 || slot >= MaxSessionSlots)
                return;

            _capturedWindowWidthBySlot[slot] = null;
            _capturedWindowHeightBySlot[slot] = null;

            if (slot == 0)
            {
                CapturedWindowWidth = null;
                CapturedWindowHeight = null;
            }
        }

        private static void ClearAllCapturedWindowDimensions()
        {
            for (int i = 0; i < MaxSessionSlots; i++)
                ClearCapturedWindowDimensions(i);
            _lastGameCaptureLogSlot = -1;
        }

        // Replay buffer save callbacks (one per slot)

        /// <summary>
        /// Parses settings recording audio bitrate (e.g. "128k") to kbps for AAC encoders.
        /// </summary>
        private static int GetRecordingAudioBitrateKbps()
        {
            var raw = Settings.Instance.RecordingAudioBitrate?.Trim();
            if (string.IsNullOrEmpty(raw))
                return 128;
            if (raw.EndsWith("k", StringComparison.OrdinalIgnoreCase))
                raw = raw[..^1];
            if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var kbps))
                return Math.Clamp(kbps, 96, 320);
            return 128;
        }

        private static uint SanitizeAudioTrackMask(uint mask) => mask & 0x3Fu;

        private static void ApplyAudioTrackMask(Source source, uint mask, string label)
        {
            try
            {
                source.AudioMixers = mask;
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to set mixers for {label}: {ex.Message}");
            }
        }

        private static void ApplyAudioTrackMask(IReadOnlyList<Source> sources, uint mask, string labelPrefix)
        {
            for (int i = 0; i < sources.Count; i++)
                ApplyAudioTrackMask(sources[i], mask, $"{labelPrefix} {i + 1}");
        }

        /// <summary>
        /// Gets whether the game capture is currently hooked.
        /// Uses the built-in IsHooked property from OBSKit.NET.
        /// </summary>
        private static bool IsGameCaptureHooked => GameCaptureSource?.IsHooked ?? false;

        // Threading primitives
        private static readonly SemaphoreSlim _stopRecordingSemaphore = new SemaphoreSlim(1, 1);
        /// <summary>Serializes replay buffer save, manual F10 save, and VRChat VVMW tail clips.</summary>
        private static readonly SemaphoreSlim ReplayBufferSaveLock = new(1, 1);

        private static readonly (string Name, string Window)[] VoiceChatApps =
        [
            ("Discord", "Discord:Chrome_WidgetWin_1:Discord.exe"),
            ("Discord Canary", "DiscordCanary:Chrome_WidgetWin_1:DiscordCanary.exe"),
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

        // Log processing queue - prevents OBS thread from blocking on log operations
        private static readonly Channel<(int level, string message)> _logChannel =
            Channel.CreateUnbounded<(int, string)>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        private static DateTime _lastSuppressedAudioTsLogAtUtc = DateTime.MinValue;
        private static int _suppressedAudioTsLogCount = 0;

        /// <summary>The recording mode a slot started with (per-game override aware), for hotkeys acting on that slot.</summary>
        public static RecordingMode GetSlotRecordingMode(int slot) =>
            Pipeline(slot).EffectiveSettings?.RecordingMode ?? Settings.Instance.RecordingMode;

        public static async Task<bool> SaveReplayBuffer(int slot = 0)
        {
            var pl = Pipeline(slot);
            if (pl.BufferOutput == null || !pl.BufferOutput.IsActive)
            {
                Log.Warning("Cannot save replay buffer: buffer is not active for slot {Slot}", slot);
                return false;
            }

            await ReplayBufferSaveLock.WaitAsync();
            try
            {
                string? savedPath = await SaveReplayBufferInternalGetPathAsync(slot);
                if (string.IsNullOrEmpty(savedPath))
                    return false;

                Log.Information($"Replay buffer saved to: {savedPath}");
                var recording = AppState.Instance.GetRecording(slot);
                // With no recording in the slot, a save comes from the always-on display buffer
                var alwaysOn = recording == null && slot == AlwaysOnBufferSlot ? _alwaysOnBuffer : null;
                string game = recording?.Game ?? (alwaysOn != null ? AlwaysOnBufferName : "Unknown");
                string? exePath = recording?.ExePath;
                List<string>? audioTrackNames = recording?.AudioTrackNames ?? alwaysOn?.AudioTrackNames;
                int? igdbId = !string.IsNullOrEmpty(exePath) ? GameUtils.GetIgdbIdFromExePath(exePath) : null;

                await EnsureFileReady(savedPath);

                TimeSpan savedDuration;
                try
                {
                    savedDuration = await FFmpegService.GetVideoDuration(savedPath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not read replay buffer save duration");
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    return false;
                }

                if (savedDuration.TotalSeconds < 0.5)
                {
                    Log.Warning(
                        "Replay buffer save too short ({Seconds:0.###}s), discarding duplicate/empty save",
                        savedDuration.TotalSeconds);
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    return false;
                }

                _ = MessageService.SendFrontendMessage("ReplayBufferSaved", new { });

                await ContentService.CreateMetadataFile(savedPath, Content.ContentType.Buffer, game, igdbId: igdbId, audioTrackNames: audioTrackNames);
                await ContentService.CreateThumbnail(savedPath, Content.ContentType.Buffer);
                _ = Task.Run(async () => await ContentService.CreateWaveformFile(savedPath, Content.ContentType.Buffer));

                await SettingsService.LoadContentFromFolderIntoState(true);

                Log.Information("Replay buffer save process completed successfully (slot {Slot})", slot);

                if (!_isStoppingSlot[slot])
                    await ResetReplayBuffer(slot);


                return true;
            }
            finally
            {
                ReplayBufferSaveLock.Release();
            }
        }

        /// <summary>
        /// VRChat VVMW: triggers a replay buffer save, then keeps only the last <paramref name="tailDurationSeconds"/>
        /// (PlaybackEnded ??PlaybackStart wall time) into Clips via FFmpeg <c>-sseof</c>. Does not register a full Replay Buffer item.
        /// Requires Hybrid or Replay Buffer mode with an active buffer. Set replay buffer duration ??longest expected playback.
        /// </summary>
        public static async Task<bool> TrySaveReplayBufferTailAsClipAsync(
            int slot,
            double tailDurationSeconds,
            string clipTitle,
            string preferredOutputFileNameBase,
            int clipIgdbId)
        {
            var buffer = Pipeline(slot).BufferOutput;
            if (buffer == null || !buffer.IsActive)
                return false;
            // The always-on display buffer is not the VRChat recording's buffer
            if (slot == AlwaysOnBufferSlot && _alwaysOnBuffer != null)
                return false;
            if (tailDurationSeconds < VrChatVvmwIntegration.MinWallClipSeconds)
                return false;
            if (!FFmpegService.FFmpegExists())
            {
                Log.Warning("[VRChat VVMW] FFmpeg not found; cannot create clip from replay buffer");
                return false;
            }

            await ReplayBufferSaveLock.WaitAsync();
            try
            {
                string? savedPath = await SaveReplayBufferInternalGetPathAsync(slot);
                if (string.IsNullOrEmpty(savedPath))
                    return false;

                await EnsureFileReady(savedPath);

                var recording = AppState.Instance.GetRecording(slot);
                if (recording == null)
                {
                    Log.Warning("[VRChat VVMW] No active recording state for slot {Slot}; discarding replay save", slot);
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    return false;
                }

                TimeSpan fullDur;
                try
                {
                    fullDur = await FFmpegService.GetVideoDuration(savedPath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[VRChat VVMW] Could not read replay save duration");
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    return false;
                }

                double fileSec = fullDur.TotalSeconds;
                double tail = Math.Min(tailDurationSeconds, fileSec);

                if (tailDurationSeconds > fileSec + 0.25)
                {
                    Log.Warning(
                        "[VRChat VVMW] Playback duration {Playback}s exceeds replay file length {File}s; clip will be shorter. Increase Replay Buffer duration in Video settings if needed.",
                        tailDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                        fileSec.ToString("0.###", CultureInfo.InvariantCulture));
                }

                int cfgSec = Settings.Instance.ReplayBufferDuration;
                if (tailDurationSeconds > cfgSec + 0.5)
                {
                    Log.Warning(
                        "[VRChat VVMW] Playback duration {Playback}s exceeds configured replay buffer ring size ({Config}s); older footage may be lost. Increase replay buffer duration.",
                        tailDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                        cfgSec);
                }

                if (tail <= 0.25)
                {
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    return false;
                }

                string game = recording.Game ?? "Unknown";
                string sanitizedGame = StorageService.SanitizeGameNameForFolder(game);
                string clipsRoot = Path.Combine(Settings.Instance.ContentFolder, FolderNames.Clips, sanitizedGame);
                Directory.CreateDirectory(clipsRoot);

                string baseName = StorageService.SanitizeGameNameForFolder((preferredOutputFileNameBase ?? "clip").Trim());
                string outPath = GetNextAvailableVvmwClipPath(clipsRoot, baseName);
                string tempOut = Path.Combine(Path.GetTempPath(), $"vvmw_tail_{Guid.NewGuid():N}.mp4");

                string tailArg = tail.ToString("0.###", CultureInfo.InvariantCulture);
                try
                {
                    await FFmpegService.RunSimple(new[]
                    {
                        // Keep all streams (video + every audio track) when trimming replay buffer tails.
                        // Default mapping may keep only a single audio stream.
                        "-y", "-sseof", "-" + tailArg, "-i", savedPath, "-map", "0", "-c", "copy", "-movflags", "+faststart", tempOut,
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[VRChat VVMW] FFmpeg failed to trim replay buffer save");
                    TryDeleteReplayTempFile(tempOut);
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    return false;
                }

                TryDeleteReplayTempFile(savedPath);

                if (!File.Exists(tempOut))
                {
                    await ResetReplayBuffer(slot);
                    return false;
                }

                try
                {
                    File.Move(tempOut, outPath, overwrite: false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[VRChat VVMW] Failed to move trimmed clip");
                    TryDeleteReplayTempFile(tempOut);
                    await ResetReplayBuffer(slot);
                    return false;
                }

                await EnsureFileReady(outPath);

                await ContentService.CreateMetadataFile(outPath, Content.ContentType.Clip, game, null, clipTitle, igdbId: clipIgdbId, audioTrackNames: recording.AudioTrackNames);
                await ContentService.CreateThumbnail(outPath, Content.ContentType.Clip);
                await ContentService.CreateWaveformFile(outPath, Content.ContentType.Clip);
                await SettingsService.LoadContentFromFolderIntoState(true);

                Log.Information("[VRChat VVMW] Saved replay tail clip: {Path}", outPath);

                await ResetReplayBuffer(slot);
                return true;
            }
            finally
            {
                ReplayBufferSaveLock.Release();
            }
        }

        /// <summary>
        /// Uses <c>{baseName}.mp4</c>, or <c>{baseName}_2.mp4</c>, <c>{baseName}_3.mp4</c>, ??when the base name is already taken (same pattern intent as manual clips avoiding collisions).
        /// </summary>
        private static string GetNextAvailableVvmwClipPath(string clipsRoot, string baseName)
        {
            string outPath = Path.Combine(clipsRoot, $"{baseName}.mp4");
            int n = 2;
            while (File.Exists(outPath))
            {
                outPath = Path.Combine(clipsRoot, $"{baseName}_{n}.mp4");
                n++;
            }

            return outPath;
        }

        private static void TryDeleteReplayTempFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TryDeleteReplayTempFile {Path}", path);
            }
        }

        /// <summary>Waits for OBS ReplayBuffer.Saved and returns the saved file path, or null on failure.</summary>
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

        /// <summary>
        /// Stops and restarts the replay buffer so that subsequent saves
        /// only contain footage recorded after the last save.
        /// </summary>
        private static async Task ResetReplayBuffer(int slot)
        {
            var buffer = Pipeline(slot).BufferOutput;
            if (buffer == null)
                return;

            Log.Information("Resetting replay buffer (slot {Slot})...", slot);

            bool stopped = buffer.Stop(waitForCompletion: true, timeoutMs: 30000);

            if (!stopped)
            {
                Log.Warning("Replay buffer did not stop within timeout for reset (slot {Slot}). Forcing stop.", slot);
                buffer.ForceStop();
                await Task.Delay(500);
            }

            bool started = buffer.Start();

            if (!started)
            {
                string error = buffer.LastError ?? "Unknown error";
                Log.Error($"Failed to restart replay buffer after reset (slot {slot}): {error}");
                if (slot == AlwaysOnBufferSlot && _alwaysOnBuffer != null)
                    OnAlwaysOnBufferFailed();
            }
            else
            {
                Log.Information("Replay buffer restarted successfully (slot {Slot})", slot);
            }
        }

        /// <summary>
        /// Processes OBS log messages from the queue asynchronously.
        /// This runs on a background thread to prevent blocking OBS's internal logging thread.
        /// </summary>
        private static async Task ProcessLogQueueAsync()
        {
            await foreach (var (level, formattedMessage) in _logChannel.Reader.ReadAllAsync())
            {
                try
                {
                    // OBS can spam TS smoothing logs at very high frequency, which can flood
                    // the unbounded queue and eventually exhaust memory.
                    if (formattedMessage.Contains("Audio timestamp for '", StringComparison.Ordinal)
                        && formattedMessage.Contains("TS_SMOOTHING_THRESHOLD", StringComparison.Ordinal))
                    {
                        _suppressedAudioTsLogCount++;
                        DateTime now = DateTime.UtcNow;
                        if ((now - _lastSuppressedAudioTsLogAtUtc).TotalSeconds >= 5)
                        {
                            Log.Debug("Suppressed {Count} noisy OBS audio timestamp smoothing logs in the last interval.", _suppressedAudioTsLogCount);
                            _suppressedAudioTsLogCount = 0;
                            _lastSuppressedAudioTsLogAtUtc = now;
                        }
                        continue;
                    }

                    Log.Information($"{(ObsLogLevel)level}: {formattedMessage}");

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
                    {
                        int? affectedSlot = null;
                        for (int i = 0; i < MaxSessionSlots; i++)
                        {
                            if (formattedMessage.Contains($"gameplay_{i}", StringComparison.Ordinal))
                            {
                                affectedSlot = i;
                                break;
                            }
                        }

                        // Drop the flag from the initial "existing hook found". This pump cannot
                        // see lines that arrive during the wait, so a stale true would skip a real stop.
                        if (affectedSlot is int slotToReset)
                            _isStillHookedAfterUnhookBySlot[slotToReset] = false;
                        else
                        {
                            for (int i = 0; i < MaxSessionSlots; i++)
                                _isStillHookedAfterUnhookBySlot[i] = false;
                        }

                        Log.Information("Capture window no longer exists, waiting a second to make sure it's not a false positive.");
                        await Task.Delay(1000);

                        if (affectedSlot is int slot)
                        {
                            Log.Information("Checking if hook is still active for slot {Slot}: {StillHooked}", slot, _isStillHookedAfterUnhookBySlot[slot]);
                            if (AppState.Instance.GetRecording(slot) != null
                                && Pipeline(slot).GameCapture != null
                                && !IsSlotCaptureHooked(slot)
                                && !_isStillHookedAfterUnhookBySlot[slot])
                            {
                                Log.Information("Capture stopped for slot {Slot}. Stopping recording.", slot);
                                ScheduleStopRecordingSlot(slot);
                            }
                            _isStillHookedAfterUnhookBySlot[slot] = false;
                        }
                        else
                        {
                            for (int i = 0; i < MaxSessionSlots; i++)
                            {
                                Log.Information("Checking if hook is still active for slot {Slot}: {StillHooked}", i, _isStillHookedAfterUnhookBySlot[i]);
                                if (AppState.Instance.GetRecording(i) != null
                                    && Pipeline(i).GameCapture != null
                                    && !IsSlotCaptureHooked(i)
                                    && !_isStillHookedAfterUnhookBySlot[i])
                                {
                                    Log.Information("Capture stopped for slot {Slot}. Stopping recording.", i);
                                    ScheduleStopRecordingSlot(i);
                                }
                                _isStillHookedAfterUnhookBySlot[i] = false;
                            }
                        }
                    }

                    if (formattedMessage.Contains("existing hook found"))
                    {
                        for (int i = 0; i < MaxSessionSlots; i++)
                        {
                            if (formattedMessage.Contains($"gameplay_{i}", StringComparison.Ordinal))
                                _isStillHookedAfterUnhookBySlot[i] = true;
                        }
                    }

                    if (formattedMessage.Contains("[game-capture: 'gameplay_", StringComparison.Ordinal))
                    {
                        for (int i = 0; i < MaxSessionSlots; i++)
                        {
                            if (formattedMessage.Contains($"gameplay_{i}", StringComparison.Ordinal))
                            {
                                _lastGameCaptureLogSlot = i;
                                break;
                            }
                        }
                    }

                    // Parse window dimensions from OBS game capture logs
                    if (formattedMessage.Contains("BufferDesc.Width:"))
                    {
                        var match = BufferDescWidthRegex().Match(formattedMessage);
                        if (match.Success && uint.TryParse(match.Groups[1].Value, out uint width))
                        {
                            int slot = _lastGameCaptureLogSlot >= 0 ? _lastGameCaptureLogSlot : 0;
                            _capturedWindowWidthBySlot[slot] = width;
                            if (slot == 0)
                                CapturedWindowWidth = width;
                            Log.Information($"Captured window width for slot {slot}: {width}");
                        }
                    }

                    if (formattedMessage.Contains("BufferDesc.Height:"))
                    {
                        var match = BufferDescHeightRegex().Match(formattedMessage);
                        if (match.Success && uint.TryParse(match.Groups[1].Value, out uint height))
                        {
                            int slot = _lastGameCaptureLogSlot >= 0 ? _lastGameCaptureLogSlot : 0;
                            _capturedWindowHeightBySlot[slot] = height;
                            if (slot == 0)
                                CapturedWindowHeight = height;
                            Log.Information($"Captured window height for slot {slot}: {height}");
                        }
                    }

                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                    if (e.StackTrace != null)
                    {
                        Log.Error(e.StackTrace);
                    }
                }
            }
        }

        private static bool AnySessionOutputActive()
        {
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (Pipeline(i).SessionOutput != null)
                    return true;
            }
            return false;
        }

        private static int AllocateSessionSlotIndex(bool startManually)
        {
            _ = startManually;
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (AppState.Instance.IsSlotOccupied(i))
                    continue;
                if (Pipeline(i).SessionOutput != null)
                    continue;
                if (Pipeline(i).BufferOutput != null)
                    continue;
                return i;
            }
            return -1;
        }

        private static void ClearPendingPreRecordingForSlot(int slot) =>
            AppState.Instance.ClearPreRecording(slot);

        private static void ClearAllPendingPreRecordings() =>
            AppState.Instance.ClearAllPreRecordings();

        private static bool SlotHasPipelineResources(int slot)
        {
            var pl = Pipeline(slot);
            return pl.MainScene != null
                || pl.SessionOutput != null
                || pl.BufferOutput != null
                || pl.GameCapture != null
                || pl.VideoEncoder != null;
        }

        private static bool ShouldStopSlot(int slot)
        {
            if (AppState.Instance.GetRecording(slot) != null)
                return true;
            // The always-on buffer is not a recording; stopping recordings leaves it running
            if (slot == AlwaysOnBufferSlot && _alwaysOnBuffer != null)
                return false;
            if (SlotHasPipelineResources(slot))
                return true;
            return false;
        }

        /// <summary>
        /// Tears down a slot that never reached a stable recording state (e.g. start cancelled or failed mid-setup).
        /// </summary>
        private static void CleanupPartialSlot(int slot, bool clearPreRecording = true)
        {
            if (slot < 0 || slot >= MaxSessionSlots)
                return;

            StopGameCaptureHookTimeoutTimer(slot);

            var pl = Pipeline(slot);
            try
            {
                if (pl.BufferOutput != null)
                {
                    if (!pl.BufferOutput.Stop(waitForCompletion: false))
                        pl.BufferOutput.ForceStop();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to stop replay buffer during partial cleanup (slot {Slot}): {Message}", slot, ex.Message);
            }

            try
            {
                if (pl.SessionOutput != null)
                {
                    if (!pl.SessionOutput.Stop(waitForCompletion: false))
                        pl.SessionOutput.ForceStop();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to stop session output during partial cleanup (slot {Slot}): {Message}", slot, ex.Message);
            }

            DisposeOutput(slot);
            DisposeSources(slot);
            DisposeEncoders(slot);

            if (slot == 0 && !IsAnotherSlotRecording(slot))
                Obs.ClearOutputSource(0);

            pl.HookedExecutableFileName = null;
            if (clearPreRecording)
                ClearPendingPreRecordingForSlot(slot);
            ClearCapturedWindowDimensions(slot);
        }

        public static async Task InitializeAsync()
        {
            // Detect GPU vendor early in initialization
            DetectGpuVendor();

            if (IsInitialized)
                return;

            try
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
                    "Segra's Linux recorder needs OBS Studio's libraries (libobs). Install OBS with your package manager, for example:\n\n    sudo apt install obs-studio\n\nThen restart Segra.",
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
                SyncAlwaysOnBuffer();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize OBS: {ex.Message}");
                await MessageService.ShowModal(
                    "Recorder Error",
                    "Failed to initialize the recorder. Please check the logs for more details.",
                    "error",
                    "Could not initialize recorder"
                );
                AppState.Instance.HasLoadedObs = true;
            }
        }

        public static void Shutdown()
        {
            if (!IsInitialized)
            {
                Log.Information("OBS is not initialized, skipping shutdown");
                return;
            }

            StopAlwaysOnBufferForExit();

            try
            {
                Log.Information("Shutting down OBS...");

                try { KeybindCaptureService.Stop(); }
                catch (Exception ex) { Log.Debug(ex, "KeybindCaptureService.Stop during shutdown"); }

                AudioMixerService.DisposeAll();
                _obsContext?.Dispose();
                _obsContext = null;

                IsInitialized = false;
                Log.Information("OBS shutdown completed successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during OBS shutdown");
            }
        }

        /// <summary>
        /// Configures OBS video settings based on the provided dimensions.
        /// </summary>
        /// <param name="is4by3">True if the content was detected as 4:3 and stretched to 16:9.</param>
        private static void ResetVideoSettings(out bool is4by3, uint? customFps = null, uint? customOutputWidth = null, uint? customOutputHeight = null, string? customResolution = null)
        {
            SettingsService.GetPrimaryMonitorResolution(out uint baseWidth, out uint baseHeight);

            // Use custom values if provided, otherwise use defaults
            baseWidth = customOutputWidth ?? baseWidth;
            baseHeight = customOutputHeight ?? baseHeight;

            // Get the maximum height from resolution setting (per-game override may substitute)
            SettingsService.GetResolution(customResolution ?? Settings.Instance.Resolution, out uint maxWidth, out uint maxHeight);

            // Calculate output dimensions respecting the max height cap while preserving aspect ratio
            uint outputWidth = baseWidth;
            uint outputHeight = baseHeight;

            // Check if the input aspect ratio is close to 4:3 (1.33)
            double aspectRatio = (double)baseWidth / baseHeight;
            is4by3 = Math.Abs(aspectRatio - 4.0 / 3.0) < 0.1 && Settings.Instance.Stretch4By3;

            // If the content is 4:3 and stretching is enabled, stretch it to 16:9 while preserving height
            // Only modify output dimensions, not base dimensions (base = actual capture size)
            if (is4by3)
            {
                // Calculate 16:9 width based on the current height for output only
                outputWidth = (uint)(baseHeight * (16.0 / 9.0));
                Log.Information($"Stretching 4:3 content to 16:9: {baseWidth}x{baseHeight} -> {outputWidth}x{outputHeight}");
            }

            // If content height exceeds max height setting, downscale proportionally
            if (outputHeight > maxHeight)
            {
                double scale = (double)maxHeight / outputHeight;
                outputWidth = (uint)(outputWidth * scale);
                outputHeight = maxHeight;

                // Round to nearest multiple of 4 (required by video encoders)
                // Example: 1279 ??1280 instead of OBS rounding down to 1276
                outputWidth = (uint)(Math.Round(outputWidth / 4.0) * 4);
                outputHeight = (uint)(Math.Round(outputHeight / 4.0) * 4);

                Log.Information($"Downscaling from {baseWidth}x{baseHeight} to {outputWidth}x{outputHeight} (max height: {maxHeight})");
            }

            (baseWidth, baseHeight) = EnsureEvenDimensions(baseWidth, baseHeight);

            _currentBaseWidth = baseWidth;
            _currentBaseHeight = baseHeight;

            Obs.SetVideo(v =>
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

        /// <summary>Re-creates monitor capture (e.g. after display settings change while not game-hooked).</summary>
        public static void RefreshMonitorCaptureForSlot(int slotIndex)
        {
            DisposeDisplaySource(slotIndex);
            AddMonitorCapture(slotIndex);
        }

        public static void AddMonitorCapture(int slot, bool warnIfNotFound = true)
        {
            var pl = Pipeline(slot);
            if (pl.MainScene == null)
            {
                Log.Warning("Cannot add monitor capture: scene not created");
                return;
            }

            int monitorIndex = ResolveSelectedMonitorIndex(warnIfNotFound);

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

        private static async Task FinalizeSessionFileAsync(Recording? rec)
        {
            if (rec == null || rec.FilePath == null)
                return;

            bool hasManualBookmarks = rec.Bookmarks.Any(b => b.Type == BookmarkType.Manual);
            bool discardWithoutBookmarks = Pipeline(rec.Slot).EffectiveSettings?.DiscardSessionsWithoutBookmarks
                ?? ActiveEffectiveSettings?.DiscardSessionsWithoutBookmarks
                ?? Settings.Instance.DiscardSessionsWithoutBookmarks;
            if (discardWithoutBookmarks && !hasManualBookmarks)
            {
                Log.Information("Discarding session recording without manual bookmarks");
                try
                {
                    if (File.Exists(rec.FilePath))
                    {
                        File.Delete(rec.FilePath);
                        Log.Information($"Deleted video file: {rec.FilePath}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to delete discarded session file: {ex.Message}");
                }
                return;
            }

            await EnsureFileReady(rec.FilePath!);

            int? igdbId = !string.IsNullOrEmpty(rec.ExePath)
                ? GameUtils.GetIgdbIdFromExePath(rec.ExePath)
                : null;
            await ContentService.CreateMetadataFile(rec.FilePath!, Content.ContentType.Session, rec.Game, rec.Bookmarks, igdbId: igdbId, audioTrackNames: rec.AudioTrackNames);
            await ContentService.CreateThumbnail(rec.FilePath!, Content.ContentType.Session);
            string sessionFilePath = rec.FilePath!;
            _ = Task.Run(async () => await ContentService.CreateWaveformFile(sessionFilePath, Content.ContentType.Session));

            Log.Information("Recording details:");
            Log.Information($"Start Time: {rec.StartTime}");
            Log.Information($"End Time: {rec.EndTime}");
            Log.Information($"Duration: {rec.Duration}");
            Log.Information($"File Path: {rec.FilePath}");
        }

        private static async Task StopRecordingSlotCore(int slot)
        {
            if (slot < 0 || slot >= MaxSessionSlots)
                return;

            if (_isStoppingSlot[slot])
            {
                Log.Information("StopRecordingSlot({Slot}) called but slot is already stopping.", slot);
                return;
            }

            _isStoppingSlot[slot] = true;

            try
            {
                var pl = Pipeline(slot);
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
                    bool successfullyStopped = pl.BufferOutput.Stop(waitForCompletion: true, timeoutMs: 30000);

                    if (successfullyStopped)
                    {
                        Log.Information(isHybridMode ? "Hybrid: Replay buffer stopped (slot {Slot})." : "Replay buffer stopped (slot {Slot}).", slot);
                        Thread.Sleep(200);
                    }
                    else
                    {
                        Log.Warning(isHybridMode ? "Hybrid: Replay buffer did not stop within timeout (slot {Slot}). Forcing stop." : "Replay buffer did not stop within timeout (slot {Slot}). Forcing stop.", slot);
                        pl.BufferOutput.ForceStop();
                        Thread.Sleep(500);
                    }
                }

                if (pl.SessionOutput != null)
                {
                    Log.Information(isHybridMode ? "Hybrid: Stopping recording..." : "Stopping recording...");
                    bool successfullyStopped = pl.SessionOutput.Stop(waitForCompletion: true, timeoutMs: 30000);

                    if (successfullyStopped)
                    {
                        Log.Information(isHybridMode ? "Hybrid: Recording stopped." : "Recording stopped.");
                        Thread.Sleep(200);
                    }
                    else
                    {
                        Log.Warning(isHybridMode ? "Hybrid: Recording did not stop within timeout. Forcing stop." : "Recording did not stop within timeout. Forcing stop.");
                        pl.SessionOutput.ForceStop();
                        Thread.Sleep(500);
                    }
                }

                DisposeOutput(slot);
                DisposeSources(slot);
                DisposeEncoders(slot);

                if (slot == 0)
                    Obs.ClearOutputSource(0);
                DisposeRecordingCanvas(slot);
                pl.HookedExecutableFileName = null;
                pl.EffectiveSettings = null;

                if (isSessionMode || isHybridMode)
                {
                    await FinalizeSessionFileAsync(recording);

                    // Load session metadata into AppState before highlight creation (CreateHighlight reads AppState.Content).
                    await SettingsService.LoadContentFromFolderIntoState(true);

                    if (Settings.Instance.EnableAi && Settings.Instance.AutoGenerateHighlights
                        && recording?.FilePath != null
                        && recording.Bookmarks.Any(b => b.Type.IncludeInHighlight()))
                    {
                        await Segra.Backend.Media.AiService.CreateHighlight(Path.GetFileNameWithoutExtension(recording.FilePath));
                    }
                }

                AppState.Instance.ClearRecording(slot);
                ClearPendingPreRecordingForSlot(slot);

                _ = GameIntegrationService.Shutdown(slot);

                RecordingPreviewService.OnRecordingStopped(slot);
                ClearCapturedWindowDimensions(slot);

                if (!AppState.Instance.HasAnyRecording())
                {
                    StopDiskSpaceMonitor();
                    GeneralUtils.SetProcessPriority(ProcessPriorityClass.Normal);
                    Segra.Backend.App.NotifyIconService.SetNotifyIconStatus(Segra.Backend.App.NotifyIconState.Idle);
                    CapturedWindowWidth = null;
                    CapturedWindowHeight = null;
                    ClearAllCapturedWindowDimensions();
                    _isHdrRecording = false;
                    _hdrEncoderId = null;
                    await VrChatVvmwIntegration.FlushDeferredClipsAsync();
                }
            }
            finally
            {
                _isStoppingSlot[slot] = false;
            }
        }

        /// <summary>
        /// Schedules an async slot stop. Uses a method parameter so the slot is captured by value,
        /// avoiding C# for-loop closure bugs where all lambdas see the final loop index.
        /// </summary>
        private static void ScheduleStopRecordingSlot(int slot)
        {
            _ = Task.Run(() => StopRecordingSlot(slot));
        }

        public static async Task StopRecordingSlot(int slot)
        {
            if (slot < 0 || slot >= MaxSessionSlots)
                return;

            Log.Information("StopRecordingSlot({Slot}) requested.", slot);

            // Unblock StartRecording window-wait loops waiting on this pre-recording entry.
            ClearPendingPreRecordingForSlot(slot);

            await _slotLifecycleLocks[slot].WaitAsync();
            _slotLifecycleLocks[slot].Release();

            await _stopRecordingSemaphore.WaitAsync();
            try
            {
                if (!ShouldStopSlot(slot))
                {
                    Log.Information("StopRecordingSlot({Slot}) called but slot is not active.", slot);
                    return;
                }

                await StopRecordingSlotCore(slot);
                await StorageService.EnsureStorageBelowLimit();
            }
            finally
            {
                _stopRecordingSemaphore.Release();
                SyncAlwaysOnBuffer();
            }
        }

        public static async Task StopRecording()
        {
            var slotsToStop = new List<int>();
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (AppState.Instance.GetPreRecording(i) != null || ShouldStopSlot(i))
                    slotsToStop.Add(i);
            }

            if (slotsToStop.Count == 0)
            {
                Log.Information("StopRecording called but no active slots.");
                return;
            }

            foreach (int slot in slotsToStop)
                await StopRecordingSlot(slot);
        }

        /// <summary>
        /// Event handler for GameCapture.Hooked event.
        /// </summary>
        private static void OnGameCaptureHookedEvent(GameCapture capture, int slotIndex)
        {
            try
            {
                var pl = Pipeline(slotIndex);
                string? title = capture.HookedWindowTitle?.Trim();
                string? windowClass = capture.HookedWindowClass?.Trim();
                string? executable = capture.HookedExecutable?.Trim();

                Log.Information($"Game hooked: Title='{title}', Class='{windowClass}', Executable='{executable}'");

                StopGameCaptureHookTimeoutTimer(slotIndex);

                DisposeDisplaySource(slotIndex);

                var audioOutputMode = pl.AudioOutputMode;
                if (audioOutputMode != AudioOutputMode.All)
                {
                    bool preserveDesktopForHybridTracks = pl.EnableSeparateAudioTracks;

                    if (!preserveDesktopForHybridTracks)
                    {
                        foreach (var desktopSource in EnumerateDesktopSources())
                        {
                            try { desktopSource.IsMuted = true; }
                            catch (Exception ex) { Log.Warning($"Failed to mute desktop source: {ex.Message}"); }
                        }
                        Log.Information("Muted desktop audio sources (game hooked, using capture_audio)");
                    }
                    else
                    {
                        Log.Information("Keeping desktop audio sources active (separate audio tracks: do not mute output capture on hook).");
                    }

                    if (audioOutputMode == AudioOutputMode.GameAndDiscord)
                    {
                        foreach (var (voiceName, _, voiceSource) in EnumerateVoiceChatSources())
                        {
                            try { voiceSource.IsMuted = false; Log.Information($"Unmuted {voiceName} audio source (game hooked)"); }
                            catch (Exception ex) { Log.Warning($"Failed to unmute {voiceName} source: {ex.Message}"); }
                        }
                    }
                }

                var recording = AppState.Instance.GetRecording(slotIndex);
                if (recording != null)
                {
                    recording.IsUsingGameHook = true;

                    if (!string.IsNullOrEmpty(executable))
                    {
                        pl.HookedExecutableFileName = Path.GetFileName(executable);
                        recording.FileName = pl.HookedExecutableFileName;

                        int? hookedPid = TryResolveHookedProcessId(executable, slotIndex);
                        if (hookedPid.HasValue)
                        {
                            recording.Pid = hookedPid;
                            Log.Information("Updated recording slot {Slot} tracked PID to {Pid} ({Exe})", slotIndex, hookedPid, pl.HookedExecutableFileName);
                        }
                    }

                    _ = SendSettingsToFrontend("Updated game hook");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing OnGameCaptureHookedEvent");
            }
        }


        /// <summary>
        /// Event handler for GameCapture.Unhooked event.
        /// </summary>
        private static void OnGameCaptureUnhookedEvent(GameCapture capture, int slotIndex)
        {
            _ = capture;
            Log.Information("Game unhooked.");

            var audioOutputMode = Pipeline(slotIndex).AudioOutputMode;
            if (audioOutputMode != AudioOutputMode.All)
            {
                // Only restore desktop/Discord fallback when no other slot still has a hooked game.
                bool anotherSlotStillHooked = false;
                for (int i = 0; i < MaxSessionSlots; i++)
                {
                    if (i == slotIndex) continue;
                    if (IsSlotCaptureHooked(i))
                    {
                        anotherSlotStillHooked = true;
                        break;
                    }
                }

                if (!anotherSlotStillHooked)
                {
                    foreach (var desktopSource in EnumerateDesktopSources())
                    {
                        try { desktopSource.IsMuted = false; }
                        catch (Exception ex) { Log.Warning($"Failed to unmute desktop source: {ex.Message}"); }
                    }
                    Log.Information("Unmuted desktop audio sources (game unhooked, falling back to desktop audio)");

                    if (audioOutputMode == AudioOutputMode.GameAndDiscord)
                    {
                        foreach (var (voiceName, _, voiceSource) in EnumerateVoiceChatSources())
                        {
                            try { voiceSource.IsMuted = true; Log.Information($"Muted {voiceName} audio source (game unhooked)"); }
                            catch (Exception ex) { Log.Warning($"Failed to mute {voiceName} source: {ex.Message}"); }
                        }
                    }
                }
                else
                {
                    Log.Information(
                        "Keeping shared desktop/Discord mute state (another slot is still hooked)");
                }
            }

            var recording = AppState.Instance.GetRecording(slotIndex);
            if (recording != null)
                recording.IsUsingGameHook = false;
        }

        private static void OnReplaySaved(int slot, object? sender, ReplaySavedEventArgs e)
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

            // The always-on buffer fails quietly: no modal, it retries or stays off (see OnAlwaysOnBufferFailed)
            if (slot == AlwaysOnBufferSlot && _alwaysOnBuffer != null)
            {
                if (!_isStoppingSlot[slot])
                {
                    Log.Error($"Always-on replay buffer stopped unexpectedly (code {e.Code}); last error: {e.LastError ?? "(none)"}");
                    OnAlwaysOnBufferFailed();
                }
                return;
            }

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
                        $"OBS stopped the recording because the disk is full. Free up space and try again.\n\nDetails: {lastError}");
                }

                return ("Recording stopped unexpectedly",
                    $"OBS stopped the recording.\n\nDetails: {lastError}");
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

        private static void SetForceMono(Source source, bool forceMono)
        {
            try
            {
                uint flags = source.Flags;
                bool currentlyMono = (flags & OBS_SOURCE_FLAG_FORCE_MONO) != 0;
                if (forceMono && !currentlyMono)
                {
                    source.Flags = flags | OBS_SOURCE_FLAG_FORCE_MONO;
                }
                else if (!forceMono && currentlyMono)
                {
                    source.Flags = flags & ~OBS_SOURCE_FLAG_FORCE_MONO;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to set force mono on source: {ex.Message}");
            }
        }

        private static void CreateRecordingScene(int slot, SessionPipeline pl, uint? canvasWidth = null, uint? canvasHeight = null)
        {
            if (slot == 0)
            {
                pl.MainScene = new Scene($"Recording Scene {slot}");
                Log.Information("Created recording scene on main canvas (slot {Slot})", slot);
                return;
            }

            var (width, height) = EnsureEvenDimensions(
                canvasWidth ?? _currentBaseWidth,
                canvasHeight ?? _currentBaseHeight);

            pl.RecordingCanvas = Canvas.Create(
                ObsName(slot, "Recording Canvas"),
                width,
                height);
            pl.MainScene = pl.RecordingCanvas.CreateScene($"Recording Scene {slot}");
            pl.RecordingCanvas.SetScene(pl.MainScene);
            Log.Information("Created recording scene on dedicated canvas (slot {Slot}, {Width}x{Height})", slot, width, height);
        }

        private static void DisposeRecordingCanvas(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.RecordingCanvas == null)
                return;

            try
            {
                pl.RecordingCanvas.Dispose();
                Log.Information("Recording canvas disposed (slot {Slot})", slot);
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to dispose recording canvas (slot {slot}): {ex.Message}");
            }

            pl.RecordingCanvas = null;
        }


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
                // The mode the owning recording started with, not whatever the setting says now
                if (owner.AudioOutputMode != AudioOutputMode.GameAndDiscord) return;

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
        /// Repoints one slot's game capture source at a newly launched game executable.
        /// </summary>
        public static void UpdateGameCaptureWindow(string exePath, int slot)
        {
            try
            {
                if (slot < 0 || slot >= MaxSessionSlots) return;
                if (_isStoppingSlot[slot]) return;

                var source = Pipeline(slot).GameCapture;
                if (source == null) return;

                string fileName = Path.GetFileName(exePath);
                source.Update(s => s.Set("window", $"*:*:{fileName}"));
                Log.Information("Updated game capture source to: {FileName} (slot {Slot})", fileName, slot);
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

            bool fallbackHdr = DisplayConfigService.IsDisplayHdrActive(fallbackDeviceId);
            return displays.Any(d => DisplayConfigService.IsDisplayHdrActive(d.DeviceId) != fallbackHdr);
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

        public static void DisposeSources(int slot)
        {
            var pl = Pipeline(slot);

            lock (_sharedAudioLock)
            {
                // If this slot owns shared WASAPI/Discord and another slot is still active,
                // transfer ownership so the remaining recording keeps mic/desktop/Discord.
                if (PipelineOwnsSharedAudio(pl) && IsAnotherSlotRecording(slot))
                {
                    int otherSlot = FindOtherActiveSlot(slot);
                    if (otherSlot >= 0)
                        TransferSharedAudioOwnership(pl, Pipeline(otherSlot));
                }
            }

            if (pl.MainScene != null)
            {
                try
                {
                    pl.MainScene.Dispose();
                    Log.Information("Scene disposed");
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose scene: {ex.Message}");
                }
                pl.MainScene = null;
            }

            pl.GameCaptureItem = null;
            pl.DisplayItem = null;

            DisposeDisplaySource(slot);
            DisposeGameCaptureSource(slot);

            lock (_sharedAudioLock)
            {
                // Only dispose shared audio when no other slot still needs it.
                if (PipelineOwnsSharedAudio(pl) && !IsAnotherSlotRecording(slot))
                    DisposeOwnedSharedAudioSources(pl);
                else if (PipelineOwnsSharedAudio(pl))
                {
                    // Safety: another slot became active between checks ??transfer instead of disposing.
                    int otherSlot = FindOtherActiveSlot(slot);
                    if (otherSlot >= 0)
                        TransferSharedAudioOwnership(pl, Pipeline(otherSlot));
                    else
                        DisposeOwnedSharedAudioSources(pl);
                }
            }

            DisposeRecordingCanvas(slot);
        }

        public static void DisposeGameCaptureSource(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.GameCaptureItem != null)
            {
                try
                {
                    pl.GameCaptureItem.Remove();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to remove game capture scene item: {ex.Message}");
                }
                pl.GameCaptureItem = null;
            }

            if (pl.GameCapture != null)
            {
                try
                {
                    if (pl.HookedSubscription != null)
                        pl.GameCapture.Hooked -= pl.HookedSubscription;
                    if (pl.UnhookedSubscription != null)
                        pl.GameCapture.Unhooked -= pl.UnhookedSubscription;
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to unsubscribe from game capture events: {ex.Message}");
                }

                pl.HookedSubscription = null;
                pl.UnhookedSubscription = null;

                try
                {
                    AudioMixerService.DisposeSource(pl.GameCapture);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose game capture source: {ex.Message}");
                }
                pl.GameCapture = null;
            }

            StopGameCaptureHookTimeoutTimer(slot);
        }

        private static void StartGameCaptureHookTimeoutTimer(int slotIndex)
        {
            StopGameCaptureHookTimeoutTimer(slotIndex);

            var pl = Pipeline(slotIndex);
            pl.GameCaptureHookTimeoutTimer = new System.Threading.Timer(
                CheckGameCaptureHookStatus,
                slotIndex,
                90000,
                Timeout.Infinite
            );

            Log.Information("Started game capture hook timer (90 seconds) for slot {Slot}", slotIndex);
        }

        private static void StopGameCaptureHookTimeoutTimer(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.GameCaptureHookTimeoutTimer != null)
            {
                pl.GameCaptureHookTimeoutTimer.Dispose();
                pl.GameCaptureHookTimeoutTimer = null;
                Log.Information("Stopped game capture hook timer for slot {Slot}", slot);
            }
        }

        private static void CheckGameCaptureHookStatus(object? state)
        {
            if (state is not int slot)
                return;

            var pl = Pipeline(slot);
            bool hooked = pl.GameCapture?.IsHooked ?? false;
            if (!hooked)
            {
                Log.Warning("Game capture did not hook within 90 seconds for slot {Slot}. Removing game capture source.", slot);
                DisposeGameCaptureSource(slot);
            }
            else
            {
                Log.Information("Game capture hook check completed for slot {Slot}. Hook status: {Status}", slot, hooked ? "Hooked" : "Not hooked");
                StopGameCaptureHookTimeoutTimer(slot);
            }
        }

        public static void DisposeDisplaySource(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.DisplayItem != null)
            {
                try
                {
                    Log.Information("Removing display scene item from scene");
                    pl.DisplayItem.Remove();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to remove display scene item: {ex.Message}");
                }
                pl.DisplayItem = null;
            }

            if (pl.DisplaySource != null)
            {
                try
                {
                    Log.Information("Disposing display source (expect OBS 'source destroyed' log to confirm WGC cleanup)");
                    pl.DisplaySource.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose display source: {ex.Message}");
                }
                pl.DisplaySource = null;
            }
        }

        public static void DisposeEncoders(int slot)
        {
            var pl = Pipeline(slot);
            try { pl.VideoEncoder?.Dispose(); }
            catch (Exception ex) { Log.Warning($"Error disposing video encoder (slot {slot}): {ex.Message}"); }
            pl.VideoEncoder = null;

            foreach (var audioEncoder in pl.AudioEncoders)
            {
                try { audioEncoder.Dispose(); }
                catch (Exception ex) { Log.Warning($"Error disposing audio encoder (slot {slot}): {ex.Message}"); }
            }
            pl.AudioEncoders.Clear();
        }

        public static void DisposeOutput(int slot)
        {
            var pl = Pipeline(slot);

            // Fail any in-flight save tied to this slot before tearing the buffer down.
            // Disposing the output also joins any in-flight mux thread (AutoDispose is false).
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

            try { pl.SessionOutput?.Dispose(); }
            catch (Exception ex) { Log.Warning($"Error disposing session output (slot {slot}): {ex.Message}"); }
            pl.SessionOutput = null;

            try { pl.BufferOutput?.Dispose(); }
            catch (Exception ex) { Log.Warning($"Error disposing buffer output (slot {slot}): {ex.Message}"); }
            pl.BufferOutput = null;
        }

        public static async Task AvailableOBSVersionsAsync()
        {
            try
            {
                string url = "https://segra.tv/api/obs/versions";
                List<Core.Models.OBSVersion>? response = null;
                using (HttpClient client = new())
                {
                    try
                    {
                        response = await client.GetFromJsonAsync<List<Core.Models.OBSVersion>>(url);
                        if (response != null)
                        {
                            Log.Information($"Available OBS versions: {string.Join(", ", response.Select(v => v.Version))}");
                        }
                        else
                        {
                            Log.Warning("Received null OBS versions list from API");
                            response = new List<Core.Models.OBSVersion>();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error parsing OBS versions from API: {ex.Message}");
                        response = new List<Core.Models.OBSVersion>();
                    }
                }

                // Filter versions based on current Segra version compatibility
                if (response != null && response.Count > 0)
                {
                    // Get the current Segra version
                    NuGet.Versioning.SemanticVersion currentVersion;
                    if (UpdateService.UpdateManager.CurrentVersion != null)
                    {
                        currentVersion = NuGet.Versioning.SemanticVersion.Parse(UpdateService.UpdateManager.CurrentVersion.ToString());
                    }
                    else
                    {
                        // Running in local development, use a high version to ensure we get the latest stable version
                        currentVersion = NuGet.Versioning.SemanticVersion.Parse("9.9.9");
                        Log.Warning("Could not get current version from UpdateManager, using default version for OBS compatibility check");
                    }

                    // Filter to only compatible versions
                    List<Core.Models.OBSVersion> compatibleVersions = response.Where(v =>
                    {
                        // SupportsFrom: null or empty means no lower limit
                        bool supportsFrom = string.IsNullOrEmpty(v.SupportsFrom) ||
                                          (NuGet.Versioning.SemanticVersion.TryParse(v.SupportsFrom, out var minVersion) &&
                                           currentVersion >= minVersion);

                        // SupportsTo: null or empty means no upper limit
                        bool supportsTo = v.SupportsTo == null ||
                                        string.IsNullOrEmpty(v.SupportsTo) ||
                                        (NuGet.Versioning.SemanticVersion.TryParse(v.SupportsTo, out var maxVersion) &&
                                         currentVersion <= maxVersion);

                        return supportsFrom && supportsTo;
                    }).ToList();

                    Log.Information($"Compatible OBS versions for Segra {currentVersion}: {string.Join(", ", compatibleVersions.Select(v => v.Version))}");
                    response = compatibleVersions;
                }

                SettingsService.SetAvailableOBSVersions(response ?? new List<Core.Models.OBSVersion>());
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to get available OBS versions: {ex.Message}");
            }
        }

        public static bool IsOBSInstalled()
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

        public static async Task CheckIfExistsOrDownloadAsync(bool isUpdate = false)
        {
            Log.Information("Checking if OBS is installed");

            // Ensure we have the latest available versions
            await AvailableOBSVersionsAsync();

            if (isUpdate)
            {
                // We need to reinstall the Segra app to apply the update, because all OBS resources are placed in the app directory
                Settings.Instance.PendingOBSUpdate = true;
                SettingsService.SaveSettings();
                await UpdateService.ForceReinstallCurrentVersionAsync();
                await ShowModal("OBS Update", "Please restart Segra to apply the update.");
                return;
            }

            if (IsOBSInstalled() && !isUpdate && !Settings.Instance.PendingOBSUpdate)
            {
                Log.Information("OBS is installed");
                return;
            }

            string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;

            // Store obs.zip and hash in AppData to preserve them across updates
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra");
            Directory.CreateDirectory(appDataDir); // Ensure directory exists

            string zipPath = Path.Combine(appDataDir, "obs.zip");
            string localHashPath = Path.Combine(appDataDir, "obs.hash");
            bool needsDownload = true;

            // Determine which version to download
            string? selectedVersion = Settings.Instance.SelectedOBSVersion;
            Core.Models.OBSVersion? versionToDownload = null;

            // If a specific version is selected, try to find it
            if (!string.IsNullOrEmpty(selectedVersion))
            {
                versionToDownload = AppState.Instance.AvailableOBSVersions
                    .FirstOrDefault(v => v.Version == selectedVersion);

                if (versionToDownload == null)
                {
                    Log.Warning($"Selected OBS version {selectedVersion} not found in available versions. Using latest stable version.");
                }
            }

            // If no specific version was selected or found, use the latest non-beta version
            if (versionToDownload == null)
            {
                versionToDownload = AppState.Instance.AvailableOBSVersions
                    .Where(v => !v.IsBeta)
                    .OrderByDescending(v => v.Version)
                    .FirstOrDefault();

                Log.Information($"Using latest stable OBS version: {versionToDownload?.Version}");
            }

            // Download the selected or latest version
            if (versionToDownload != null)
            {
                Log.Information($"Using OBS version: {versionToDownload.Version}");
                string metadataUrl = versionToDownload.Url; // This is the GitHub metadata URL

                using (var httpClient = new HttpClient())
                {
                    // First, fetch the metadata from GitHub
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Segra");
                    httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3.json");

                    Log.Information($"Fetching metadata for OBS version {versionToDownload.Version} from {metadataUrl}");
                    var response = await httpClient.GetAsync(metadataUrl);

                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Error($"Failed to fetch metadata from {metadataUrl}. Status: {response.StatusCode}");
                        throw new Exception($"Failed to fetch file metadata: {response.ReasonPhrase}");
                    }

                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var metadata = System.Text.Json.JsonSerializer.Deserialize<GitHubFileMetadata>(jsonResponse);

                    if (metadata?.DownloadUrl == null)
                    {
                        Log.Error("Download URL not found in the API response.");
                        throw new Exception("Invalid API response: Missing download URL.");
                    }

                    string remoteHash = metadata.Sha;
                    string actualDownloadUrl = metadata.DownloadUrl;

                    // Check if we already have the file with the correct hash
                    if (!isUpdate && File.Exists(zipPath) && File.Exists(localHashPath))
                    {
                        string localHash = await File.ReadAllTextAsync(localHashPath);
                        if (localHash == remoteHash)
                        {
                            Log.Information("Found existing obs.zip with matching hash. Skipping download.");
                            needsDownload = false;
                        }
                        else
                        {
                            Log.Information("Found existing obs.zip but hash doesn't match. Downloading new version.");
                            needsDownload = true;
                        }
                    }

                    // If this is an update or we need to download, proceed with download
                    if (needsDownload)
                    {
                        Log.Information($"Downloading OBS version {versionToDownload.Version}");

                        httpClient.DefaultRequestHeaders.Clear();

                        // Download with progress reporting
                        using var downloadResponse = await httpClient.GetAsync(actualDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        downloadResponse.EnsureSuccessStatusCode();

                        var totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1L;
                        using var contentStream = await downloadResponse.Content.ReadAsStreamAsync();
                        using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                        var buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;
                        int lastReportedProgress = -1;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                int progress = (int)((totalBytesRead * 100) / totalBytes);
                                // Only send update if progress changed (avoid flooding)
                                if (progress != lastReportedProgress)
                                {
                                    lastReportedProgress = progress;
                                    await SendFrontendMessage("ObsDownloadProgress", new { progress, status = "downloading" });
                                }
                            }
                        }

                        // Save the hash for future reference
                        await File.WriteAllTextAsync(localHashPath, remoteHash);

                        Log.Information("Download complete");
                    }
                }

                // This should already be deleted on reinstall, but just in case
                if (Settings.Instance.PendingOBSUpdate)
                {
                    string dataPath = Path.Combine(currentDirectory, "data");
                    if (Directory.Exists(dataPath))
                    {
                        Directory.Delete(dataPath, true);
                    }

                    string obsPluginsPath = Path.Combine(currentDirectory, "obs-plugins");
                    if (Directory.Exists(obsPluginsPath))
                    {
                        Directory.Delete(obsPluginsPath, true);
                    }
                }

                try
                {
                    ZipFile.ExtractToDirectory(zipPath, currentDirectory, true);

                    if (Settings.Instance.PendingOBSUpdate)
                    {
                        await ShowModal("OBS Update", $"OBS update to {versionToDownload.Version} applied successfully.");
                        Settings.Instance.PendingOBSUpdate = false;
                        SettingsService.SaveSettings();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to extract OBS: {ex.Message}");
                    await ShowModal("OBS Update", "Failed to apply OBS update. Please try again.", "error");
                    throw;
                }

                Log.Information("OBS setup complete");
                return;
            }

            // If we somehow got here without a version to download, log an error
            Log.Error("No OBS versions available from API. This should not happen.");
        }

        private class GitHubFileMetadata
        {
            [System.Text.Json.Serialization.JsonPropertyName("sha")]
            public required string Sha { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("download_url")]
            public required string DownloadUrl { get; set; }
        }

        public static void PlaySound(string resourceName, int delay = 0)
        {
            Thread.Sleep(delay);
            using var stream = Properties.Resources.ResourceManager.GetStream(resourceName);
            if (stream == null)
                throw new ArgumentException($"Resource '{resourceName}' not found or not a stream.");

            using var reader = new WaveFileReader(stream);
            var sampleProvider = reader.ToSampleProvider();
            var volumeProvider = new VolumeSampleProvider(sampleProvider)
            {
                Volume = Settings.Instance.SoundEffectsVolume
            };

            using var waveOut = new WaveOutEvent { DesiredLatency = 50 };
            waveOut.Init(volumeProvider);
            waveOut.Play();

            while (waveOut.PlaybackState == PlaybackState.Playing)
                Thread.Sleep(10);
        }


        private static readonly Dictionary<string, string> EncoderFriendlyNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // ???? NVIDIA NVENC ????????????????????????????????????????????????????????????????????????
                ["jim_nvenc"] = "NVIDIA NVENC H.264",
                ["jim_hevc_nvenc"] = "NVIDIA NVENC H.265",
                ["jim_av1_nvenc"] = "NVIDIA NVENC AV1",

                // ???? AMD AMF ????????????????????????????????????????????????????????????????????????????????
                ["h264_texture_amf"] = "AMD AMF H.264",
                ["h265_texture_amf"] = "AMD AMF H.265",
                ["av1_texture_amf"] = "AMD AMF AV1",

                // ???? Intel Quick Sync ??????????????????????????????????????????????????????????????
                ["obs_qsv11_v2"] = "Intel QSV H.264",
                ["obs_qsv11_hevc"] = "Intel QSV H.265",
                ["obs_qsv11_av1"] = "Intel QSV AV1",

                // VAAPI (Linux hardware)
                ["ffmpeg_vaapi_tex"] = "VAAPI H.264",
                ["hevc_ffmpeg_vaapi_tex"] = "VAAPI H.265",
                ["av1_ffmpeg_vaapi_tex"] = "VAAPI AV1",

                // CPU / software paths
                ["obs_x264"] = "Software x264",
                ["ffmpeg_openh264"] = "Software OpenH264",
            };

        private static void SetAvailableEncodersInState()
        {
            Log.Information("Available encoders:");

            var encoderTypes = Obs.EnumerateEncoderTypes().ToList();
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

            Log.Information($"Total encoders found: {idx}");

            if (Settings.Instance.Codec == null)
            {
                Settings.Instance.Codec = SelectDefaultCodec(Settings.Instance.Encoder, AppState.Instance.Codecs);
            }
        }

        public static Codec? SelectDefaultCodec(string encoderType, List<Codec> availableCodecs)
        {
            if (availableCodecs == null || availableCodecs.Count == 0)
            {
                return null;
            }

            Codec? selectedCodec = null;

            if (encoderType == "cpu")
            {
                // Prefer obs_x264 if available
                selectedCodec = availableCodecs.FirstOrDefault(
                    c => c.InternalEncoderId.Equals(
                        "obs_x264",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                // If not found, fallback to first software (CPU) encoder
                if (selectedCodec == null)
                {
                    selectedCodec = availableCodecs.FirstOrDefault(
                        c => !c.IsHardwareEncoder
                    );
                }
            }
            else if (encoderType == "gpu")
            {
                // Prefer NVIDIA NVENC (jim_nvenc)
                selectedCodec = availableCodecs.FirstOrDefault(
                    c => c.InternalEncoderId.Equals(
                        "jim_nvenc",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                // If not found, try AMD AMF H.264
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
                if (selectedCodec == null)
                {
                    selectedCodec = availableCodecs.FirstOrDefault(
                        c => c.IsHardwareEncoder
                    );
                }
            }

            // Ultimate fallback: First available encoder if no match or no selection
            if (selectedCodec == null)
            {
                selectedCodec = availableCodecs.FirstOrDefault();
            }

            return selectedCodec;
        }

        public static bool SupportsHybridMp4()
        {
            string? versionToCheck = Settings.Instance.SelectedOBSVersion ?? InstalledOBSVersion;

            if (string.IsNullOrEmpty(versionToCheck))
                return true;

            string cleanVersion = versionToCheck.Split('-')[0].Trim();
            if (Version.TryParse(cleanVersion, out Version? version))
                return version >= new Version(30, 2);

            return true;
        }

        /// <summary>
        /// Returns a downscaled JPEG of the current capture (game capture when hooked, otherwise display capture).
        /// </summary>
#if WINDOWS
        public static byte[]? TryGetRecordingPreviewJpeg(int maxEdgePixels = 220)
        {
            for (int slot = 0; slot < MaxSessionSlots; slot++)
            {
                var jpeg = TryGetRecordingPreviewJpeg(slot, maxEdgePixels);
                if (jpeg != null)
                    return jpeg;
            }

            return null;
        }

        public static byte[]? TryGetRecordingPreviewJpeg(int slot, int maxEdgePixels = 220)
        {
            if (!IsInitialized || slot < 0 || slot >= MaxSessionSlots)
                return null;

            var pl = Pipeline(slot);
            if (pl.MainScene == null)
                return null;

            try
            {
                var gcap = pl.GameCapture;
                if (gcap is { IsHooked: true })
                {
                    uint sw = gcap.Width;
                    uint sh = gcap.Height;
                    if (sw > 0 && sh > 0)
                    {
                        var shot = gcap.TakeScreenshot(0, 0, sw, sh);
                        if (shot != null && shot.Pixels.Length > 0)
                            return EncodeBgraScreenshotToJpeg(shot.Pixels, shot.Width, shot.Height, maxEdgePixels);
                    }
                }

                var disp = pl.DisplaySource;
                if (disp != null)
                {
                    uint sw = disp.Width;
                    uint sh = disp.Height;
                    if (sw > 0 && sh > 0)
                    {
                        var shot = disp.TakeScreenshot(0, 0, sw, sh);
                        if (shot != null && shot.Pixels.Length > 0)
                            return EncodeBgraScreenshotToJpeg(shot.Pixels, shot.Width, shot.Height, maxEdgePixels);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TryGetRecordingPreviewJpeg failed for slot {Slot}", slot);
            }

            return null;
        }

        private static byte[] EncodeBgraScreenshotToJpeg(byte[] pixels, uint width, uint height, int maxEdgePixels)
        {
            int w = (int)width;
            int h = (int)height;

            using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var bmpData = bitmap.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                int srcStride = w * 4;
                int dstStride = bmpData.Stride;
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        pixels,
                        y * srcStride,
                        bmpData.Scan0 + y * dstStride,
                        srcStride);
                }
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }

            int maxDim = Math.Max(w, h);
            if (maxDim > maxEdgePixels)
            {
                double scale = (double)maxEdgePixels / maxDim;
                int nw = Math.Max(1, (int)Math.Round(w * scale));
                int nh = Math.Max(1, (int)Math.Round(h * scale));

                using var scaled = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(bitmap, 0, 0, nw, nh);
                }

                return BitmapToJpegBytes(scaled);
            }

            return BitmapToJpegBytes(bitmap);
        }

        private static byte[] BitmapToJpegBytes(Bitmap bmp)
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
