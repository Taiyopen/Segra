using Serilog;
using System.Text.Json;
using Segra.Backend.App;
using Segra.Backend.Media;
using Segra.Backend.Shared;
using Segra.Backend.Recorder;
using Segra.Backend.Core.Models;
using Segra.Backend.Platform;
using Segra.Backend.Windows.Input;
using Segra.Backend.Windows.Storage;
using System.Text.Json.Serialization;
using System.Reflection;
#if WINDOWS
using Segra.Backend.Windows.GameMode;
#endif

namespace Segra.Backend.Core
{
    internal static class SettingsService
    {
        public static readonly string SettingsFilePath = PathUtils.Normalize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra", "settings.json"));

        public static void SaveSettings(bool force = false)
        {
            if (!force && !Program.hasLoadedInitialSettings)
            {
                Log.Error("Program has not loaded initial settings. Can't save!");
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(Settings.Instance, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(SettingsFilePath, json);
                Log.Information($"Settings saved to {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save settings: {ex.Message}");
            }
        }

        public static bool LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    Log.Information($"Settings file not found at {SettingsFilePath}. Using default settings.");
                    return false;
                }

                var json = File.ReadAllText(SettingsFilePath);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                Settings.Instance.BeginBulkUpdate();

                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;

                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        try
                        {
                            if (property.Value.ValueKind == JsonValueKind.Array)
                            {
                                var propertyName = char.ToUpperInvariant(property.Name[0]) + property.Name.Substring(1);
                                var targetProperty = typeof(Settings).GetProperty(
                                    propertyName,
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                                if (targetProperty != null && targetProperty.CanWrite)
                                {
                                    try
                                    {
                                        Type collectionType = targetProperty.PropertyType;

                                        Type elementType = collectionType.IsGenericType ?
                                            collectionType.GetGenericArguments()[0] : typeof(object);

                                        var listType = typeof(List<>).MakeGenericType(elementType);
                                        var validItems = Activator.CreateInstance(listType);

                                        var addMethod = listType.GetMethod("Add");

                                        foreach (JsonElement itemElement in property.Value.EnumerateArray())
                                        {
                                            try
                                            {
                                                var item = JsonSerializer.Deserialize(itemElement.GetRawText(), elementType, options);
                                                if (item != null)
                                                {
                                                    addMethod?.Invoke(validItems, new[] { item });
                                                }
                                            }
                                            catch (Exception itemEx)
                                            {
                                                Log.Warning($"Failed to deserialize an item in {property.Name}: {itemEx.Message}");
                                            }
                                        }

                                        targetProperty.SetValue(Settings.Instance, validItems);
                                    }
                                    catch (Exception collEx)
                                    {
                                        Log.Warning($"Failed to process collection property {property.Name}: {collEx.Message}");
                                    }
                                }
                            }
                            else
                            {
                                var propertyName = char.ToUpperInvariant(property.Name[0]) + property.Name.Substring(1);
                                var targetProperty = typeof(Settings).GetProperty(
                                    propertyName,
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                                if (targetProperty != null && targetProperty.CanWrite)
                                {
                                    try
                                    {
                                        var value = JsonSerializer.Deserialize(property.Value.GetRawText(), targetProperty.PropertyType, options);
                                        if (value != null)
                                        {
                                            targetProperty.SetValue(Settings.Instance, value);
                                        }
                                    }
                                    catch (Exception valEx)
                                    {
                                        Log.Warning($"Failed to deserialize property {property.Name}: {valEx.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception propEx)
                        {
                            Log.Warning($"Error processing property {property.Name}: {propEx.Message}");
                        }
                    }
                }

                Settings.Instance.RunOnStartup = PlatformServices.Startup.GetStartupStatus();
                AppState.Instance.GpuVendor = GeneralUtils.DetectGpuVendor();

                Log.Information("Settings loaded from {0}", SettingsFilePath);

                // The file has been read, so forcing this save is safe even though
                // Program.Main hasn't set hasLoadedInitialSettings yet.
                Settings.Instance.EndBulkUpdateAndSaveSettings(force: true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load settings: {ex.Message}");
                return false;
            }
        }

        public static async Task HandleUpdateSettings(JsonElement settingsElement)
        {
            try
            {
                var settings = Settings.Instance;

                // Begin bulk update to suppress multiple state updates
                settings.BeginBulkUpdate();

                bool hasChanges = await ApplySettingsPayload(settings, settingsElement);

                // Only save settings and send to frontend if changes were actually made
                if (hasChanges)
                {
                    Log.Information("Settings updated, saving changes");
                    settings.EndBulkUpdateAndSaveSettings();
                }
                else
                {
                    // End bulk update without saving if no changes were made
                    settings._isBulkUpdating = false;
                    Log.Information("No settings changes detected");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to update settings: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions PayloadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        // Owned by the backend: tokens come from AuthService and PendingOBSUpdate from OBSService.
        private static readonly HashSet<string> BackendOwnedSettings = [nameof(Settings.Auth), nameof(Settings.PendingOBSUpdate)];

        private static readonly Dictionary<string, PropertyInfo> SettingsByJsonName = typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && !BackendOwnedSettings.Contains(p.Name))
            .Select(p => (Property: p, Attribute: p.GetCustomAttribute<JsonPropertyNameAttribute>()))
            .Where(x => x.Attribute != null)
            .ToDictionary(x => x.Attribute!.Name, x => x.Property, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Applies every setting in a frontend payload whose value differs from <paramref name="settings"/>, then runs
        /// the follow-up actions of the ones that changed. Settings missing from the payload are left alone.
        /// </summary>
        internal static async Task<bool> ApplySettingsPayload(Settings settings, JsonElement payload)
        {
            var nullability = new NullabilityInfoContext();
            var changed = new List<string>();

            foreach (JsonProperty entry in payload.EnumerateObject())
            {
                if (!SettingsByJsonName.TryGetValue(entry.Name, out PropertyInfo? property))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = entry.Value.Deserialize(property.PropertyType, PayloadOptions);
                }
                catch (JsonException ex)
                {
                    Log.Warning($"Ignoring invalid value for setting {entry.Name}: {ex.Message}");
                    continue;
                }

                // Null only means something ("automatic", "none") for settings declared nullable.
                if (value == null && nullability.Create(property).WriteState != NullabilityState.Nullable)
                {
                    continue;
                }

                string oldJson = JsonSerializer.Serialize(property.GetValue(settings), PayloadOptions);
                string newJson = JsonSerializer.Serialize(value, PayloadOptions);
                if (oldJson == newJson || !await CanApplySetting(settings, property.Name, value))
                {
                    continue;
                }

                Log.Information("{Setting} changed from {Old} to {New}", property.Name, Shorten(oldJson), Shorten(newJson));
                property.SetValue(settings, value);
                changed.Add(property.Name);
            }

            // Run after every value is in, so the auto-selections below win over what the payload carried.
            foreach (string name in changed)
            {
                OnSettingChanged(settings, name);
            }

            return changed.Count > 0;
        }

        private static async Task<bool> CanApplySetting(Settings settings, string name, object? value)
        {
            switch (name)
            {
                case nameof(Settings.ContentFolder):
                    // If not proceeding, a warning modal was sent to the frontend
                    return await StorageWarningService.CheckContentFolderChange((string)value!);
                case nameof(Settings.Codec):
                    if (settings.Codec == null || value == null)
                    {
                        return false;
                    }
                    if (!OBSService.IsInitialized)
                    {
                        Log.Warning($"Codec change before OBS initialization, skipping");
                        return false;
                    }
                    return true;
                case nameof(Settings.DefaultMenuItem):
                    return !string.IsNullOrEmpty((string?)value);
                default:
                    return true;
            }
        }

        private static void OnSettingChanged(Settings settings, string name)
        {
            switch (name)
            {
                case nameof(Settings.ClipEncoder):
                    Log.Information($"Automatically changing ClipCodec to 'h264' due to ClipEncoder change");
                    settings.ClipCodec = "h264";
                    break;

                case nameof(Settings.Encoder):
                    // When encoder changes, automatically select an appropriate codec
                    var newCodec = OBSService.SelectDefaultCodec(settings.Encoder, AppState.Instance.Codecs);
                    if (newCodec != null && (settings.Codec == null || !settings.Codec.Equals(newCodec)))
                    {
                        Log.Information($"Automatically changing codec to '{newCodec.FriendlyName}' based on encoder change");
                        settings.Codec = newCodec;
                    }

                    // Ensure CRF is only used with CPU encoder; if user switches to GPU, switch to CQP
                    if (settings.Encoder == "gpu" && settings.RateControl == "CRF")
                    {
                        Log.Information($"Automatically changing RateControl from 'CRF' to 'CQP' because encoder is GPU");
                        settings.RateControl = "CQP";
                    }
                    else if (settings.Encoder == "cpu" && settings.RateControl == "CQP")
                    {
                        Log.Information($"Automatically changing RateControl from 'CQP' to 'CRF' because encoder is CPU");
                        settings.RateControl = "CRF";
                    }
                    break;

                case nameof(Settings.SoundEffectsVolume):
                    // Play the sound with the new volume to provide immediate feedback
                    _ = Task.Run(() => OBSService.PlaySound("start"));
                    break;

                case nameof(Settings.DisableWindowsGameMode):
                    // Enabling the option proactively disables Game Mode; disabling it leaves Game Mode untouched.
#if WINDOWS
                    if (settings.DisableWindowsGameMode)
                    {
                        GameModeService.EnforceDisabledIfEnabled();
                    }
#endif
                    break;

                case nameof(Settings.SelectedDisplay):
                    // Update the live display capture in place; only meaningful while recording without a game hook.
                    if (AppState.Instance.Recording != null && !AppState.Instance.Recording.IsUsingGameHook)
                    {
                        OBSService.UpdateMonitorCapture();
                    }
                    break;

                case nameof(Settings.ReceiveBetaUpdates):
                    _ = Task.Run(() => UpdateService.UpdateAppIfNecessary(forceCheck: true));
                    _ = Task.Run(() => UpdateService.GetReleaseNotes(forceCheck: true));
                    break;

                case nameof(Settings.SelectedOBSVersion):
                    // If we're changing OBS version, check if we need to download it
                    if (OBSService.IsInitialized)
                    {
                        _ = Task.Run(() => OBSService.CheckIfExistsOrDownloadAsync(true));
                    }
                    break;

                case nameof(Settings.Keybindings):
                    KeybindCaptureService.RefreshKeybindingsCache();
                    break;
            }
        }

        private static string Shorten(string json) => json.Length <= 200 ? json : json[..200] + "...";

        public static async Task LoadContentFromFolderIntoState(bool sendToFrontend = true)
        {
            var contentTypes = Enum.GetValues(typeof(Content.ContentType)).Cast<Content.ContentType>().ToArray();
            var content = new List<Content>();

            try
            {
                foreach (var contentType in contentTypes)
                {
                    string metadataPath = FolderNames.GetMetadataFolderPath(contentType);

                    if (!Directory.Exists(metadataPath))
                    {
                        continue;
                    }

                    var metadataFiles = Directory.EnumerateFiles(metadataPath, "*.json", SearchOption.TopDirectoryOnly)
                                                 .Where(file => IsMetadataFile(file));

                    foreach (var metadataFilePath in metadataFiles)
                    {
                        var serializedMetadataFilePath = PathUtils.Normalize(metadataFilePath);
                        try
                        {
                            var metadataContent = File.ReadAllText(serializedMetadataFilePath);
                            var metadata = JsonSerializer.Deserialize<Content>(metadataContent);

                            if (metadata == null || !File.Exists(metadata.FilePath))
                            {
                                Log.Warning($"Invalid or missing metadata for file: {serializedMetadataFilePath}");
                                continue;
                            }

                            // Update FileSizeKb if it is 0 (migration, remove this in the future)
                            if (metadata.FileSizeKb == 0)
                            {
                                Log.Information($"[MIGRATION] Adding FileSizeKb to {metadata.FilePath}");
                                var updatedMetadata = await ContentService.UpdateMetadataFile(metadataFilePath, c =>
                                {
                                    c.FileSizeKb = ContentService.GetFileSize(c.FilePath).sizeKb;
                                });

                                if (updatedMetadata != null)
                                {
                                    metadata = updatedMetadata;
                                }
                            }

                            content.Add(new Content
                            {
                                Type = metadata.Type,
                                Title = metadata.Title,
                                Game = metadata.Game,
                                Bookmarks = metadata.Bookmarks,
                                FileName = metadata.FileName,
                                FilePath = metadata.FilePath,
                                FileSize = metadata.FileSize,
                                FileSizeKb = metadata.FileSizeKb,
                                Duration = metadata.Duration,
                                CreatedAt = metadata.CreatedAt,
                                UploadId = metadata.UploadId,
                                IgdbId = metadata.IgdbId,
                                AudioTrackNames = metadata.AudioTrackNames,
                                IsImported = metadata.IsImported,
                                PendingEditSourceType = metadata.PendingEditSourceType,
                                ReadyToDeleteSourceType = metadata.ReadyToDeleteSourceType
                            });
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Error processing metadata file '{serializedMetadataFilePath}': {ex.Message}");
                        }
                    }
                }

                content = content.OrderByDescending(v => v.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                Log.Error($"Error reading videos: {ex.Message}");
            }

            await ContentService.ReconcileGameNamesByIgdb(content);

            AppState.Instance.SetContent(content, sendToFrontend);

            // Honor sendToFrontend so a silent reload doesn't leak a state send via the folder size.
            Windows.Storage.StorageService.UpdateFolderSizeInState(sendToFrontend);
        }

        public static void GetPrimaryMonitorResolution(out uint boundsWidth, out uint boundsHeight)
        {
            if (PlatformServices.Display.GetPrimaryMonitorPhysicalResolution(out boundsWidth, out boundsHeight))
            {
                Log.Information($"Primary monitor resolution: {boundsWidth}x{boundsHeight}");
                return;
            }

            boundsWidth = 1920;
            boundsHeight = 1080;
            Log.Warning("Could not query primary monitor resolution, defaulting to 1920x1080");
        }

        public static void GetResolution(string resolution, out uint width, out uint height)
        {
            switch (resolution)
            {
                case "720p":
                    width = 1280;
                    height = 720;
                    break;
                case "1080p":
                    width = 1920;
                    height = 1080;
                    break;
                case "1440p":
                    width = 2560;
                    height = 1440;
                    break;
                case "4K":
                    width = 3840;
                    height = 2160;
                    break;
                default:
                    width = 1920;
                    height = 1080;
                    break;
            }
        }

        public static void SetAvailableOBSVersions(List<Core.Models.OBSVersion> versions)
        {
            if (versions == null || versions.Count == 0)
            {
                Log.Warning("Received empty OBS versions list");
                return;
            }

            Log.Information($"Setting {versions.Count} available OBS versions");
            AppState.Instance.AvailableOBSVersions = versions;

            // If the selected version is not in the list anymore, reset it to null (automatic)
            if (!string.IsNullOrEmpty(Settings.Instance.SelectedOBSVersion) &&
                !versions.Any(v => v.Version == Settings.Instance.SelectedOBSVersion))
            {
                Log.Warning($"Selected OBS version {Settings.Instance.SelectedOBSVersion} is no longer available, resetting to automatic");
                Settings.Instance.SelectedOBSVersion = null;
            }
        }

        /// <summary>
        /// Reconciles selected device settings with currently available devices.
        /// If a selected device's ID no longer exists but a device with a matching name is found,
        /// the selected device's ID is updated to the new ID. This handles cases where Windows
        /// assigns new IDs to devices after updates or hardware changes.
        /// </summary>
        public static void ReconcileDeviceSettings(List<DeviceSetting> selectedDevices, List<AudioDevice> availableDevices, string deviceType)
        {
            bool hasChanges = false;

            foreach (DeviceSetting selectedDevice in selectedDevices)
            {
                // "default" is a virtual device that always resolves at capture time — skip reconciliation
                if (selectedDevice.Id == "default")
                {
                    continue;
                }

                AudioDevice? currentById = availableDevices.FirstOrDefault(d => d.Id == selectedDevice.Id);
                if (currentById != null)
                {
                    // Keep stored display names in sync (e.g. after naming format improvements)
                    if (!string.Equals(selectedDevice.Name, currentById.Name, StringComparison.Ordinal))
                    {
                        Log.Information($"Updating {deviceType} device display name: '{selectedDevice.Name}' → '{currentById.Name}'");
                        selectedDevice.Name = currentById.Name;
                        hasChanges = true;
                    }
                    continue;
                }

                string savedNameNormalized = NormalizeDeviceName(selectedDevice.Name);

                // Prevent matching the same device multiple times if they have the same name
                HashSet<string> alreadyUsedIds = selectedDevices.Select(d => d.Id).ToHashSet();

                AudioDevice? matchingDevice = availableDevices
                    .Where(d => !alreadyUsedIds.Contains(d.Id) &&
                        NormalizeDeviceName(d.Name).Equals(savedNameNormalized, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.IsDefault)
                    .FirstOrDefault();

                if (matchingDevice != null)
                {
                    Log.Information($"Reconciling {deviceType} device: '{selectedDevice.Name}' ID changed from '{selectedDevice.Id}' to '{matchingDevice.Id}'");
                    selectedDevice.Id = matchingDevice.Id;
                    selectedDevice.Name = matchingDevice.Name;
                    hasChanges = true;
                }
                else
                {
                    Log.Warning($"Saved {deviceType} device '{selectedDevice.Name}' (ID: {selectedDevice.Id}) not found in available devices");
                }
            }

            if (hasChanges)
            {
                SaveSettings();
            }
        }

        public static void SelectDefaultDevices()
        {
            Settings.Instance.BeginBulkUpdate();
            Settings.Instance.InputDevices.Add(new DeviceSetting
            {
                Id = "default",
                Name = "Default Device",
                Volume = 1.0f
            });
            Settings.Instance.OutputDevices.Add(new DeviceSetting
            {
                Id = "default",
                Name = "Default Device",
                Volume = 1.0f
            });
            Settings.Instance.EndBulkUpdateAndSaveSettings();
            Log.Information("Auto-selected default input and output devices");
        }

        /// <summary>
        /// Migrates cache contents (metadata, thumbnails, waveforms) from the old folder to the new folder.
        /// </summary>
        public static async Task MigrateCacheFolder(string oldCacheFolder, string newCacheFolder)
        {
            if (string.IsNullOrEmpty(oldCacheFolder) || string.IsNullOrEmpty(newCacheFolder))
            {
                Log.Warning("Cannot migrate cache: old or new folder path is empty");
                return;
            }

            if (string.Equals(oldCacheFolder.Replace("/", "\\"), newCacheFolder.Replace("/", "\\"), StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("Cache folder unchanged, no migration needed");
                return;
            }

            var foldersToMigrate = new[] { FolderNames.Metadata, FolderNames.Thumbnails, FolderNames.Waveforms };

            foreach (var folderName in foldersToMigrate)
            {
                string sourcePath = Path.Combine(oldCacheFolder.Replace("/", "\\"), folderName);
                string destPath = Path.Combine(newCacheFolder.Replace("/", "\\"), folderName);

                if (!Directory.Exists(sourcePath))
                {
                    Log.Information($"Source folder does not exist, skipping: {sourcePath}");
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(destPath);

                    foreach (var dir in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        string targetDir = dir.Replace(sourcePath, destPath);
                        Directory.CreateDirectory(targetDir);
                    }

                    foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        string targetFile = file.Replace(sourcePath, destPath);
                        if (File.Exists(targetFile))
                        {
                            File.Delete(targetFile);
                        }
                        File.Move(file, targetFile);
                    }

                    Directory.Delete(sourcePath, true);
                    Log.Information($"Successfully migrated {folderName} from {sourcePath} to {destPath}");
                }
                catch (Exception ex)
                {
                    Log.Error($"Error migrating {folderName}: {ex.Message}");
                }
            }

            await LoadContentFromFolderIntoState();
            Log.Information("Cache migration completed");
        }

        private static bool IsMetadataFile(string filePath)
        {
            return Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                   !Path.GetFileName(filePath).StartsWith(".");
        }

        private static string NormalizeDeviceName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            const string defaultSuffix = " (Default)";
            if (name.EndsWith(defaultSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^defaultSuffix.Length];
            }

            return name;
        }
    }
}
