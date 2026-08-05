using Serilog;
using Segra.Backend.App;
using Segra.Backend.Core;
using Segra.Backend.Shared;
using Segra.Backend.Platform;
using System.Text.Json.Serialization;
using static Segra.Backend.Shared.GeneralUtils;

namespace Segra.Backend.Core.Models
{
    internal class AppState : IDisposable
    {
        private static AppState _instance = new();
        public static AppState Instance => _instance;

        private GpuVendor _gpuVendor = GpuVendor.Unknown;
        private double? _cudaComputeCapability = null;
        private PreRecording?[] _preRecordingsBySlot = new PreRecording?[RecordingSlots.Max];
        private Recording?[] _recordingsBySlot = new Recording?[RecordingSlots.Max];
        private bool _hasLoadedObs = false;
        private List<Content> _content = [];

        private List<AudioDevice> _inputDevices = [];
        private List<AudioDevice> _outputDevices = [];
        private List<Display> _displays = [];
        private List<Codec> _codecs = [];
        private List<OBSVersion> _availableOBSVersions = [];
        private bool _isCheckingForUpdates = false;
        private int _maxDisplayHeight = 1080;
        private double _currentFolderSizeGb = 0;
        private double? _recordingDriveUsedGb = null;
        private double? _recordingDriveFreeGb = null;

        private IPlatformWatcher? _deviceWatcher;
        private IPlatformWatcher? _displayWatcher;
        private System.Threading.Timer? _audioDeviceDebounceTimer;
        private System.Threading.Timer? _displayDebounceTimer;
        private const int DebounceDelayMs = 3000;

        public void Initialize()
        {
            _deviceWatcher = PlatformServices.Audio.CreateWatcher();
            _deviceWatcher.Changed += OnAudioDevicesChanged;

            _displayWatcher = PlatformServices.Display.CreateWatcher();
            _displayWatcher.Changed += OnDisplaysChanged;

            UpdateAudioDevices();
            UpdateDisplays();
        }

        [JsonPropertyName("gpuVendor")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GpuVendor GpuVendor
        {
            get => _gpuVendor;
            set
            {
                if (_gpuVendor != value)
                {
                    _gpuVendor = value;
                }
            }
        }

        [JsonPropertyName("cudaComputeCapability")]
        public double? CudaComputeCapability
        {
            get => _cudaComputeCapability;
            set
            {
                if (_cudaComputeCapability != value)
                {
                    _cudaComputeCapability = value;
                }
            }
        }

        [JsonPropertyName("preRecordings")]
        public List<PreRecording> PreRecordings
        {
            get
            {
                var list = new List<PreRecording>();
                for (int i = 0; i < RecordingSlots.Max; i++)
                {
                    if (_preRecordingsBySlot[i] != null)
                        list.Add(_preRecordingsBySlot[i]!);
                }
                return list;
            }
        }

        [JsonPropertyName("recordings")]
        public List<Recording> Recordings
        {
            get
            {
                var list = new List<Recording>();
                for (int i = 0; i < RecordingSlots.Max; i++)
                {
                    if (_recordingsBySlot[i] != null)
                        list.Add(_recordingsBySlot[i]!);
                }
                return list;
            }
        }

        /// <summary>First active pre-recording (slot 0, then slot 1). Backward compat for single-recording UI.</summary>
        [JsonPropertyName("preRecording")]
        public PreRecording? PreRecording
        {
            get => GetPreRecording(0) ?? GetPreRecording(1);
            set
            {
                if (value == null)
                {
                    ClearAllPreRecordings();
                    return;
                }
                SetPreRecording(value.Slot, value);
            }
        }

        /// <summary>First active recording (slot 0, then slot 1). Backward compat for single-recording UI.</summary>
        [JsonPropertyName("recording")]
        public Recording? Recording
        {
            get => GetRecording(0) ?? GetRecording(1);
            set
            {
                if (value == null)
                {
                    ClearAllRecordings();
                    return;
                }
                SetRecording(value.Slot, value);
            }
        }

        public PreRecording? GetPreRecording(int slot)
        {
            if (slot < 0 || slot >= RecordingSlots.Max) return null;
            return _preRecordingsBySlot[slot];
        }

        public void SetPreRecording(int slot, PreRecording? value)
        {
            if (slot < 0 || slot >= RecordingSlots.Max) return;
            if (_preRecordingsBySlot[slot] == value) return;
            _preRecordingsBySlot[slot] = value;
            SendToFrontend("State update: PreRecording");
        }

        public Recording? GetRecording(int slot)
        {
            if (slot < 0 || slot >= RecordingSlots.Max) return null;
            return _recordingsBySlot[slot];
        }

        public void SetRecording(int slot, Recording? value)
        {
            if (slot < 0 || slot >= RecordingSlots.Max) return;
            if (_recordingsBySlot[slot] == value) return;
            _recordingsBySlot[slot] = value;
            SendToFrontend("State update: Recording");
        }

        public bool HasAnyRecording()
        {
            for (int i = 0; i < RecordingSlots.Max; i++)
            {
                if (_recordingsBySlot[i] != null) return true;
            }
            return false;
        }

        public bool HasAnyPreRecording()
        {
            for (int i = 0; i < RecordingSlots.Max; i++)
            {
                if (_preRecordingsBySlot[i] != null) return true;
            }
            return false;
        }

        public bool IsSlotOccupied(int slot) =>
            GetRecording(slot) != null || GetPreRecording(slot) != null;

        public int? GetFreeSlot()
        {
            for (int i = 0; i < RecordingSlots.Max; i++)
            {
                if (!IsSlotOccupied(i)) return i;
            }
            return null;
        }

        public Recording? GetRecordingByPid(int pid)
        {
            for (int i = 0; i < RecordingSlots.Max; i++)
            {
                var rec = _recordingsBySlot[i];
                if (rec?.Pid == pid) return rec;
            }
            return null;
        }

        public PreRecording? GetPreRecordingByPid(int pid)
        {
            for (int i = 0; i < RecordingSlots.Max; i++)
            {
                var pre = _preRecordingsBySlot[i];
                if (pre?.Pid == pid) return pre;
            }
            return null;
        }

        public bool IsPidBeingRecorded(int pid) =>
            GetRecordingByPid(pid) != null || GetPreRecordingByPid(pid) != null;

        public void ClearRecording(int slot)
        {
            if (slot < 0 || slot >= RecordingSlots.Max) return;
            if (_recordingsBySlot[slot] == null) return;
            _recordingsBySlot[slot] = null;
            SendToFrontend("State update: Recording");
        }

        public void ClearPreRecording(int slot)
        {
            if (slot < 0 || slot >= RecordingSlots.Max) return;
            if (_preRecordingsBySlot[slot] == null) return;
            _preRecordingsBySlot[slot] = null;
            SendToFrontend("State update: PreRecording");
        }

        public void ClearAllRecordings()
        {
            bool changed = false;
            for (int i = 0; i < RecordingSlots.Max; i++)
            {
                if (_recordingsBySlot[i] != null)
                {
                    _recordingsBySlot[i] = null;
                    changed = true;
                }
            }
            if (changed)
                SendToFrontend("State update: Recording");
        }

        public void ClearAllPreRecordings()
        {
            bool changed = false;
            for (int i = 0; i < RecordingSlots.Max; i++)
            {
                if (_preRecordingsBySlot[i] != null)
                {
                    _preRecordingsBySlot[i] = null;
                    changed = true;
                }
            }
            if (changed)
                SendToFrontend("State update: PreRecording");
        }

        [JsonPropertyName("hasLoadedObs")]
        public bool HasLoadedObs
        {
            get => _hasLoadedObs;
            set
            {
                if (_hasLoadedObs != value)
                {
                    _hasLoadedObs = value;
                    SendToFrontend("State update: HasLoadedObs");
                }
            }
        }

        [JsonPropertyName("content")]
        public List<Content> Content
        {
            get => _content;
            private set
            {
                if (_content != value)
                {
                    _content = value;
                    SendToFrontend("State update: Content");
                }
            }
        }

        [JsonPropertyName("inputDevices")]
        public List<AudioDevice> InputDevices
        {
            get => _inputDevices;
            set
            {
                if (_inputDevices != value)
                {
                    _inputDevices = value;
                    SendToFrontend("State update: InputDevices");
                }
            }
        }

        [JsonPropertyName("outputDevices")]
        public List<AudioDevice> OutputDevices
        {
            get => _outputDevices;
            set
            {
                if (_outputDevices != value)
                {
                    _outputDevices = value;
                    SendToFrontend("State update: OutputDevices");
                }
            }
        }

        [JsonPropertyName("displays")]
        public List<Display> Displays
        {
            get => _displays;
            set
            {
                if (_displays != value)
                {
                    _displays = value;
                    SendToFrontend("State update: Displays");
                }
            }
        }

        [JsonPropertyName("maxDisplayHeight")]
        public int MaxDisplayHeight
        {
            get => _maxDisplayHeight;
            set
            {
                if (_maxDisplayHeight != value)
                {
                    _maxDisplayHeight = value;
                    SendToFrontend("State update: MaxDisplayHeight");
                }
            }
        }

        [JsonPropertyName("codecs")]
        public List<Codec> Codecs
        {
            get => _codecs;
            set
            {
                if (_codecs != value)
                {
                    _codecs = value;
                    SendToFrontend("State update: Codecs");
                }
            }
        }

        [JsonPropertyName("availableOBSVersions")]
        public List<OBSVersion> AvailableOBSVersions
        {
            get => _availableOBSVersions;
            set
            {
                if (_availableOBSVersions != value)
                {
                    _availableOBSVersions = value;
                    SendToFrontend("State update: AvailableOBSVersions");
                }
            }
        }

        [JsonPropertyName("isCheckingForUpdates")]
        public bool IsCheckingForUpdates
        {
            get => _isCheckingForUpdates;
            set
            {
                if (_isCheckingForUpdates != value)
                {
                    _isCheckingForUpdates = value;
                    SendToFrontend("State update: IsCheckingForUpdates");
                }
            }
        }

        [JsonPropertyName("currentFolderSizeGb")]
        public double CurrentFolderSizeGb
        {
            get => _currentFolderSizeGb;
            set => SetCurrentFolderSizeGb(value, sendToFrontend: true);
        }

        // sendToFrontend: false lets a silent content reload stay silent, so the new content and the
        // cleared recording arrive in one state message instead of a stray send causing a flicker.
        public void SetCurrentFolderSizeGb(double value, bool sendToFrontend)
        {
            if (Math.Abs(_currentFolderSizeGb - value) > 0.001)
            {
                _currentFolderSizeGb = value;
                if (sendToFrontend)
                {
                    SendToFrontend("State update: CurrentFolderSizeGb");
                }
            }
        }

        [JsonPropertyName("recordingDriveUsedGb")]
        public double? RecordingDriveUsedGb => _recordingDriveUsedGb;

        [JsonPropertyName("recordingDriveFreeGb")]
        public double? RecordingDriveFreeGb => _recordingDriveFreeGb;

        public void SetRecordingDriveSpaceGb(double? usedGb, double? freeGb, bool sendToFrontend)
        {
            bool changed = _recordingDriveUsedGb != usedGb || _recordingDriveFreeGb != freeGb;
            if (!changed)
            {
                return;
            }

            _recordingDriveUsedGb = usedGb;
            _recordingDriveFreeGb = freeGb;
            if (sendToFrontend)
            {
                SendToFrontend("State update: Recording drive space");
            }
        }

        // Cache folder path for metadata, thumbnails, waveforms (read-only, exposed to frontend)
        [JsonPropertyName("cacheFolder")]
        public string CacheFolder => FolderNames.CacheFolder.Replace("\\", "/");

        public void UpdateAudioDevices()
        {
            List<AudioDevice> inputDevices = PlatformServices.Audio.GetInputDevices();
            if (!Enumerable.SequenceEqual(_inputDevices, inputDevices))
            {
                _inputDevices = inputDevices;
            }

            List<AudioDevice> outputDevices = PlatformServices.Audio.GetOutputDevices();
            if (!Enumerable.SequenceEqual(_outputDevices, outputDevices))
            {
                _outputDevices = outputDevices;
            }

            Log.Information("Audio devices");
            Log.Information("-------------");
            foreach (AudioDevice device in InputDevices)
            {
                Log.Information($"Input device: {device.Name} {device.Id}");
            }

            foreach (AudioDevice device in OutputDevices)
            {
                Log.Information($"Output device: {device.Name} {device.Id}");
            }
            Log.Information("-------------");

            // Reconcile selected device settings with available devices
            // This handles cases where device IDs change (e.g., after Windows/driver updates)
            SettingsService.ReconcileDeviceSettings(Settings.Instance.InputDevices, inputDevices, "input");
            SettingsService.ReconcileDeviceSettings(Settings.Instance.OutputDevices, outputDevices, "output");

            _ = MessageService.SendStateToFrontend("Updated audio devices");
            _ = MessageService.SendSettingsToFrontend("Updated audio devices (device reconciliation may have changed selections)");
        }

        public void UpdateRecordingEndTime(DateTime endTime, int? slot = null)
        {
            if (slot.HasValue)
            {
                var rec = GetRecording(slot.Value);
                if (rec != null)
                {
                    rec.EndTime = endTime;
                    SendToFrontend("State update: Recording end time");
                }
                return;
            }

            bool changed = false;
            for (int i = 0; i < RecordingSlots.Max; i++)
            {
                var rec = _recordingsBySlot[i];
                if (rec != null)
                {
                    rec.EndTime = endTime;
                    changed = true;
                }
            }
            if (changed)
                SendToFrontend("State update: Recording end time");
        }

        public void NotifyRecordingUpdated()
        {
            SendToFrontend("State update: Recording updated");
        }

        public void NotifyContentUpdated()
        {
            SendToFrontend("State update: Content updated");
        }

        public void SetContent(List<Content> contents, bool sendToFrontend)
        {
            _content = contents;
            if (sendToFrontend)
            {
                SendToFrontend("State update: Content");
            }
        }

        public void Dispose()
        {
            if (_deviceWatcher != null)
            {
                _deviceWatcher.Changed -= OnAudioDevicesChanged;
                _deviceWatcher.Dispose();
                _deviceWatcher = null;
            }

            if (_displayWatcher != null)
            {
                _displayWatcher.Changed -= OnDisplaysChanged;
                _displayWatcher.Dispose();
                _displayWatcher = null;
            }

            _audioDeviceDebounceTimer?.Dispose();
            _displayDebounceTimer?.Dispose();
        }

        private static void SendToFrontend(string cause)
        {
            if (Settings.Instance != null && !Settings.Instance._isBulkUpdating)
            {
                _ = MessageService.SendStateToFrontend(cause);
            }
        }

        private void OnAudioDevicesChanged()
        {
            _audioDeviceDebounceTimer?.Dispose();
            _audioDeviceDebounceTimer = new System.Threading.Timer(
                _ => UpdateAudioDevices(),
                null,
                DebounceDelayMs,
                Timeout.Infinite
            );
        }

        private void OnDisplaysChanged()
        {
            _displayDebounceTimer?.Dispose();
            _displayDebounceTimer = new System.Threading.Timer(
                _ => UpdateDisplays(),
                null,
                DebounceDelayMs,
                Timeout.Infinite
            );
        }

        private static void UpdateDisplays()
        {
            bool hasChanged = PlatformServices.Display.LoadAvailableMonitorsIntoState();
            if (hasChanged)
            {
                SendToFrontend("Display change detected");
            }
        }
    }
}
