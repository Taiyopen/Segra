using Segra.Backend.Audio;
using Segra.Backend.Utils;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.IO;
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
        private string _inputDevice = string.Empty;
        private string _outputDevice = string.Empty;
        private bool _enableDisplayRecording = false;
        private State _state = new State();

        public Settings()
        {
            SetDefaultResolution();
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

        [JsonPropertyName("inputDevice")]
        public string InputDevice
        {
            get => _inputDevice;
            set
            {
                _inputDevice = value;
            }
        }

        [JsonPropertyName("outputDevice")]
        public string OutputDevice
        {
            get => _outputDevice;
            set
            {
                _outputDevice = value;
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

        [JsonPropertyName("state")]
        public State State
        {
            get => _state;
            /*set
            {
                _state = value;
                SendToFrontend();
            }*/
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

        private AudioDeviceWatcher _deviceWatcher;

        public void Initialize()
        {
            _deviceWatcher = new AudioDeviceWatcher();
            _deviceWatcher.DevicesChanged += UpdateAudioDevices;

            UpdateAudioDevices();
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
    internal class Content
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
    }

    internal class AudioDevice : IEquatable<AudioDevice>
    {
        public string Id { get; set; }
        public string Name { get; set; }

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
}
