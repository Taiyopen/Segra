using Segra.Backend.Audio;
using Segra.Backend.Utils;
using Serilog;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Segra.Models
{
    internal class Settings
    {
        private static Settings _instance = new Settings();
        public static Settings Instance => _instance;
        public bool _isBulkUpdating = false;

        private string _contentFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Segra").Replace("\\", "/");
        private string _theme = "segra";
        private string _resolution;
        private int _frameRate = 60;
        private int _bitrate = 40;
        private string _rateControl = "VBR";
        private int _crfValue = 23;
        private int _cqLevel = 20;
        private string _encoder = "gpu";
        private string _codec = "h264";
        private int _storageLimit = 100;
        private List<string> _inputDevices = new List<string>();
        private List<string> _outputDevices = new List<string>();
        private bool _enableDisplayRecording = false;
        private bool _enableAi = true;
        private bool _runOnStartup = false;
        private bool _receiveBetaUpdates = false;
        private RecordingMode _recordingMode = RecordingMode.Session;
        private int _replayBufferDuration = 30;
        private int _replayBufferMaxSize = 500;
        private List<Keybind> _keybindings;
        private State _state = new State();
        private Auth _auth = new Auth();

        // Returns the default keybindings
        private static List<Keybind> GetDefaultKeybindings()
        {
            return new List<Keybind>
            { 
                new Keybind(new List<int> { 119 }, KeybindAction.CreateBookmark, true), // 119 is F8
                new Keybind(new List<int> { 121 }, KeybindAction.SaveReplayBuffer, true) // 121 is F10
            };
        }

        public Settings()
        {
            SetDefaultResolution();
            _keybindings = GetDefaultKeybindings();
        }

        // Begin bulk update suppression
        public void BeginBulkUpdate()
        {
            _isBulkUpdating = true;
        }

        public void EndBulkUpdateAndSaveSettings()
        {
            _isBulkUpdating = false;
            SendToFrontend();
            SettingsUtils.SaveSettings();
        }

        private void SendToFrontend()
        {
            if (!_isBulkUpdating)
            {
                MessageUtils.SendSettingsToFrontend();
            }
        }

        private void SetDefaultResolution()
        {
            int screenHeight = 1080; // Fallback value
            var primaryScreen = Screen.PrimaryScreen;

            if (primaryScreen != null)
            {
                screenHeight = primaryScreen.Bounds.Height;
            }

            // Determine resolution based on height
            if (screenHeight >= 2160)
            {
                _resolution = "4K";
            }
            else if (screenHeight >= 1440)
            {
                _resolution = "1440p";
            }
            else
            {
                _resolution = "1080p";
            }
        }

        [JsonPropertyName("contentFolder")]
        public string ContentFolder
        {
            get => _contentFolder;
            set
            {
                _contentFolder = value.Replace("\\", "/");
                SendToFrontend();
                SettingsUtils.LoadContentFromFolderIntoState();
            }
        }

        [JsonPropertyName("theme")]
        public string Theme
        {
            get => _theme;
            set
            {
                _theme = value;
            }
        }

        [JsonPropertyName("resolution")]
        public string Resolution
        {
            get => _resolution;
            set
            {
                _resolution = value;
            }
        }

        [JsonPropertyName("frameRate")]
        public int FrameRate
        {
            get => _frameRate;
            set
            {
                _frameRate = value;
            }
        }

        [JsonPropertyName("rateControl")]
        public string RateControl
        {
            get => _rateControl;
            set
            {
                _rateControl = value;
            }
        }

        [JsonPropertyName("crfValue")]
        public int CrfValue
        {
            get => _crfValue;
            set
            {
                _crfValue = value;
            }
        }

        [JsonPropertyName("cqLevel")]
        public int CqLevel
        {
            get => _cqLevel;
            set
            {
                _cqLevel = value;
            }
        }

        [JsonPropertyName("bitrate")]
        public int Bitrate
        {
            get => _bitrate;
            set
            {
                _bitrate = value;
            }
        }

        [JsonPropertyName("encoder")]
        public string Encoder
        {
            get => _encoder;
            set
            {
                _encoder = value;
            }
        }

        [JsonPropertyName("codec")]
        public string Codec
        {
            get => _codec;
            set
            {
                _codec = value;
            }
        }

        [JsonPropertyName("storageLimit")]
        public int StorageLimit
        {
            get => _storageLimit;
            set
            {
                _storageLimit = value;
            }
        }

        [JsonPropertyName("inputDevices")]
        public List<string> InputDevices
        {
            get => _inputDevices;
            set
            {
                _inputDevices = value;
            }
        }

        [JsonPropertyName("outputDevices")]
        public List<string> OutputDevices
        {
            get => _outputDevices;
            set
            {
                _outputDevices = value;
            }
        }

        [JsonPropertyName("enableDisplayRecording")]
        public bool EnableDisplayRecording
        {
            get => _enableDisplayRecording;
            set
            {
                _enableDisplayRecording = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("enableAi")]
        public bool EnableAi
        {
            get => _enableAi;
            set
            {
                _enableAi = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("runOnStartup")]
        public bool RunOnStartup
        {
            get => _runOnStartup;
            set
            {
                _runOnStartup = value;
                StartupUtils.SetStartupStatus(value);
                SendToFrontend();
            }
        }

        [JsonPropertyName("receiveBetaUpdates")]
        public bool ReceiveBetaUpdates
        {
            get => _receiveBetaUpdates;
            set
            {
                _receiveBetaUpdates = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("recordingMode")]
        public RecordingMode RecordingMode
        {
            get => _recordingMode;
            set
            {
                _recordingMode = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("replayBufferDuration")]
        public int ReplayBufferDuration
        {
            get => _replayBufferDuration;
            set
            {
                _replayBufferDuration = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("replayBufferMaxSize")]
        public int ReplayBufferMaxSize
        {
            get => _replayBufferMaxSize;
            set
            {
                _replayBufferMaxSize = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("state")]
        public State State
        {
            get => _state;
        }

        [JsonPropertyName("auth")]
        public Auth Auth
        {
            get => _auth;
            set
            {
                _auth = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("keybindings")]
        public List<Keybind> Keybindings
        {
            get => _keybindings;
            set
            {
                _keybindings = value ?? new List<Keybind>();

                // Check for each default keybind action and add it if missing
                foreach (var defaultKeybind in GetDefaultKeybindings())
                {
                    if (!_keybindings.Any(k => k.Action == defaultKeybind.Action))
                    {
                        _keybindings.Add(defaultKeybind);
                        Log.Information($"Added missing keybind for action {defaultKeybind.Action}");
                    }
                }

                SendToFrontend();
            }
        }
    }

    // Enum definitions with JSON converters
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ThemeType
    {
        [EnumMember(Value = "night")]
        Night,
        [EnumMember(Value = "dark")]
        Dark
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EncoderType
    {
        [EnumMember(Value = "gpu")]
        GPU,
        [EnumMember(Value = "cpu")]
        CPU
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CodecType
    {
        [EnumMember(Value = "h264")]
        H264,
        [EnumMember(Value = "h265")]
        H265
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PresetType
    {
        [EnumMember(Value = "fast")]
        Fast,
        [EnumMember(Value = "medium")]
        Medium,
        [EnumMember(Value = "slow")]
        Slow
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProfileType
    {
        [EnumMember(Value = "baseline")]
        Baseline,
        [EnumMember(Value = "main")]
        Main,
        [EnumMember(Value = "high")]
        High
    }

    // State class
    internal class State : IDisposable
    {
        private Recording _recording = null;
        private bool _hasLoadedObs = false;
        private List<Content> _content = new List<Content>();

        private List<AudioDevice> _inputDevices = new List<AudioDevice>();
        private List<AudioDevice> _outputDevices = new List<AudioDevice>();
        private bool _isCheckingForUpdates = false;

        private AudioDeviceWatcher _deviceWatcher;

        public void Initialize()
        {
            _deviceWatcher = new AudioDeviceWatcher();
            _deviceWatcher.DevicesChanged += UpdateAudioDevices;

            UpdateAudioDevices();

            bool hasNoSelectedAudioDevices = Settings.Instance.InputDevices.Count == 0 && Settings.Instance.OutputDevices.Count == 0;
            Log.Information($"Has no selected audio devices: {hasNoSelectedAudioDevices}");
            if (hasNoSelectedAudioDevices)
            {
                SelectDefaultDevices();
            }
        }

        private void SendToFrontend()
        {
            if (Settings.Instance != null && !Settings.Instance._isBulkUpdating)
            {
                MessageUtils.SendSettingsToFrontend();
            }
        }

        [JsonPropertyName("recording")]
        public Recording Recording
        {
            get => _recording;
            set
            {
                _recording = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("hasLoadedObs")]
        public bool HasLoadedObs
        {
            get => _hasLoadedObs;
            set
            {
                _hasLoadedObs = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("content")]
        public List<Content> Content
        {
            get => _content;
            private set
            {
                _content = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("inputDevices")]
        public List<AudioDevice> InputDevices
        {
            get => _inputDevices;
            set
            {
                _inputDevices = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("outputDevices")]
        public List<AudioDevice> OutputDevices
        {
            get => _outputDevices;
            set
            {
                _outputDevices = value;
                SendToFrontend();
            }
        }

        [JsonPropertyName("isCheckingForUpdates")]
        public bool IsCheckingForUpdates
        {
            get => _isCheckingForUpdates;
            set
            {
                _isCheckingForUpdates = value;
                SendToFrontend();
            }
        }

        public void UpdateAudioDevices()
        {
            // Get the list of input devices
            var inputDevices = AudioDeviceUtils.GetInputDevices();
            if (!Enumerable.SequenceEqual(_inputDevices, inputDevices))
            {
                _inputDevices = inputDevices;
            }

            // Get the list of output devices
            var outputDevices = AudioDeviceUtils.GetOutputDevices();
            if (!Enumerable.SequenceEqual(_outputDevices, outputDevices))
            {
                _outputDevices = outputDevices;
            }

            Log.Information("Audio devices");
            Log.Information("-------------");
            foreach (var device in InputDevices)
            {
                Log.Information($"Input device: {device.Name} {device.Id}");
            }

            foreach (var device in OutputDevices)
            {
                Log.Information($"Output device: {device.Name} {device.Id}");
            }
            Log.Information("-------------");
            MessageUtils.SendSettingsToFrontend();
        }

        private void SelectDefaultDevices()
        {
            var defaultInputDevice = _inputDevices.FirstOrDefault(d => d.IsDefault);
            if (defaultInputDevice != null)
            {
                Settings.Instance.BeginBulkUpdate();
                Settings.Instance.InputDevices.Add(defaultInputDevice.Id);
                Settings.Instance.EndBulkUpdateAndSaveSettings();
                Log.Information($"Auto-selected default input device: {defaultInputDevice.Name}");
            }

            var defaultOutputDevice = _outputDevices.FirstOrDefault(d => d.IsDefault);
            if (defaultOutputDevice != null)
            {
                Settings.Instance.BeginBulkUpdate();
                Settings.Instance.OutputDevices.Add(defaultOutputDevice.Id);
                Settings.Instance.EndBulkUpdateAndSaveSettings();
                Log.Information($"Auto-selected default output device: {defaultOutputDevice.Name}");
            }
        }

        public void UpdateRecordingEndTime(DateTime endTime)
        {
            if (_recording != null)
            {
                _recording.EndTime = endTime;
                SendToFrontend();
            }
        }

        public void SetContent(List<Content> contents, bool sendToFrontend)
        {
            _content = contents;
            if (sendToFrontend)
            {
                SendToFrontend();
            }
        }

        public void Dispose()
        {
            if (_deviceWatcher != null)
            {
                _deviceWatcher.DevicesChanged -= UpdateAudioDevices;
                _deviceWatcher.Dispose();
                _deviceWatcher = null;
            }
        }
    }

    // Recording class
    internal class Recording
    {
        [JsonPropertyName("startTime")]
        public DateTime StartTime { get; set; }

        [JsonPropertyName("endTime")]
        public DateTime? EndTime { get; set; } // Nullable in case recording is ongoing

        [JsonPropertyName("filePath")]
        public string FilePath { get; set; }

        [JsonPropertyName("game")]
        public string Game { get; set; }

        [JsonPropertyName("isUsingGameHook")]
        public bool IsUsingGameHook { get; set; }

        [JsonPropertyName("bookmarks")]
        public List<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();

        [JsonPropertyName("duration")]
        public TimeSpan? Duration
        {
            get
            {
                if (EndTime.HasValue)
                {
                    return EndTime.Value - StartTime;
                }
                else
                {
                    return null;
                }
            }
        }
    }

    // Content class
    public class Content
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum ContentType
        {
            Session,
            Buffer,
            Clip,
            Highlight
        }

        public ContentType Type { get; set; } = ContentType.Session;

        public string Title { get; set; } = string.Empty;

        public string Game { get; set; } = string.Empty;
        public List<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();

        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        public string FileSize { get; set; } = string.Empty;

        public TimeSpan Duration { get; set; }

        public DateTime CreatedAt { get; set; }

        public AiAnalysis? AiAnalysis { get; set; }
    }

    public class AiAnalysis
    {
        public string id;
    }

    internal class AudioDevice : IEquatable<AudioDevice>
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsDefault { get; set; }

        public bool Equals(AudioDevice other)
        {
            if (other == null)
                return false;
            return this.Id == other.Id && this.Name == other.Name;
        }

        public override int GetHashCode()
        {
            return (Id + Name).GetHashCode();
        }
    }

    // Auth class for storing authentication tokens
    internal class Auth
    {
        private string _jwt = string.Empty;
        private string _refreshToken = string.Empty;

        [JsonPropertyName("jwt")]
        public string Jwt
        {
            get => _jwt;
            set
            {
                _jwt = value;
                if (Settings.Instance != null && !Settings.Instance._isBulkUpdating)
                {
                    SettingsUtils.SaveSettings();
                }
            }
        }

        [JsonPropertyName("refreshToken")]
        public string RefreshToken
        {
            get => _refreshToken;
            set
            {
                _refreshToken = value;
                if (Settings.Instance != null && !Settings.Instance._isBulkUpdating)
                {
                    SettingsUtils.SaveSettings();
                }
            }
        }

        public bool HasCredentials()
        {
            return !string.IsNullOrEmpty(_jwt) && !string.IsNullOrEmpty(_refreshToken);
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecordingMode
    {
        Session,
        Buffer
    }
}
