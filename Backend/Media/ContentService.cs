using Segra.Backend.App;
using Segra.Backend.Core.Models;
using Segra.Backend.Games;
using Segra.Backend.Services;
using Segra.Backend.Shared;
using Segra.Backend.Windows.Storage;
using Serilog;
using System.Text.Json;

namespace Segra.Backend.Media
{
    internal class ContentService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static async Task CreateMetadataFile(string filePath, Content.ContentType type, string game, List<Bookmark>? bookmarks = null, string? title = null, DateTime? createdAt = null, int? igdbId = null, bool isImported = false, List<string>? audioTrackNames = null)
        {
            bookmarks ??= [];

            try
            {
                // Ensure the video file exists
                if (!File.Exists(filePath))
                {
                    Log.Information($"Video file not found: {filePath}");
                    return;
                }

                // Get the directory and file name
                string contentFileName = Path.GetFileNameWithoutExtension(filePath);

                // Ensure the metadata folder exists
                string metadataFolderPath = FolderNames.GetMetadataFolderPath(type);
                if (!Directory.Exists(metadataFolderPath))
                {
                    Directory.CreateDirectory(metadataFolderPath);
                }

                // Create the metadata file
                string metadataFilePath = Path.Combine(metadataFolderPath, $"{contentFileName}.json");
                var (displaySize, sizeKb) = GetFileSize(filePath);

                var duration = await GetVideoDurationAsync(filePath);
                var metadataContent = new Content
                {
                    Type = type,
                    Title = title ?? string.Empty,
                    Game = game,
                    Bookmarks = bookmarks,
                    FileName = contentFileName,
                    FilePath = filePath,
                    FileSize = displaySize,
                    FileSizeKb = sizeKb,
                    CreatedAt = createdAt ?? DateTime.Now,
                    Duration = duration,
                    AudioTrackNames = audioTrackNames,
                    IgdbId = igdbId,
                    IsImported = isImported
                };

                string metadataJson = JsonSerializer.Serialize(metadataContent, _jsonOptions);

                await File.WriteAllTextAsync(metadataFilePath, metadataJson);
                Log.Information($"Metadata file created at: {metadataFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error creating metadata file: {ex.Message}");
            }
        }

        public static async Task<Content?> UpdateMetadataFile(string metadataFilePath, Action<Content> updateAction)
        {
            try
            {
                if (!File.Exists(metadataFilePath))
                {
                    Log.Error($"Metadata file not found: {metadataFilePath}");
                    return null;
                }

                // Read and deserialize
                string metadataJson = await File.ReadAllTextAsync(metadataFilePath);
                var content = System.Text.Json.JsonSerializer.Deserialize<Content>(metadataJson);

                if (content == null)
                {
                    Log.Error($"Failed to deserialize metadata: {metadataFilePath}");
                    return null;
                }

                // Apply the update
                updateAction(content);

                // Serialize and write back
                string updatedJson = JsonSerializer.Serialize(content, _jsonOptions);

                await File.WriteAllTextAsync(metadataFilePath, updatedJson);

                return content;
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating metadata file {metadataFilePath}: {ex.Message}");
                return null;
            }
        }

        public static async Task SyncContentGameNamesByIgdb()
        {
            var list = AppState.Instance.Content;
            if (list == null || list.Count == 0) return;

            int changed = await ReconcileGameNamesByIgdb(list);
            if (changed > 0)
            {
                AppState.Instance.SetContent(list, sendToFrontend: true);
            }
        }

        public static async Task<int> ReconcileGameNamesByIgdb(List<Content> contents)
        {
            if (contents == null || contents.Count == 0) return 0;

            var idToName = GameUtils.GetIgdbIdToNameMap();
            if (idToName.Count == 0) return 0;

            int changedCount = 0;
            int errorCount = 0;

            foreach (var content in contents)
            {
                try
                {
                    if (content.IgdbId is not int igdbId) continue;
                    if (!idToName.TryGetValue(igdbId, out string? canonicalName) || string.IsNullOrEmpty(canonicalName)) continue;
                    if (string.Equals(content.Game, canonicalName, StringComparison.Ordinal)) continue;

                    string newSanitized = StorageService.SanitizeGameNameForFolder(canonicalName);

                    string? newFilePath = null;
                    bool moved = false;

                    if (!string.IsNullOrEmpty(content.FilePath) && File.Exists(content.FilePath))
                    {
                        string? currentDir = Path.GetDirectoryName(content.FilePath);
                        string? typeFolder = currentDir != null ? Path.GetDirectoryName(currentDir) : null;

                        if (!string.IsNullOrEmpty(typeFolder))
                        {
                            string newDir = Path.Combine(typeFolder, newSanitized);
                            string candidatePath = Path.Combine(newDir, Path.GetFileName(content.FilePath));

                            bool sameAsCurrent = string.Equals(
                                Path.GetFullPath(candidatePath),
                                Path.GetFullPath(content.FilePath),
                                StringComparison.OrdinalIgnoreCase);

                            if (!sameAsCurrent)
                            {
                                if (File.Exists(candidatePath))
                                {
                                    Log.Warning("Skipping move for {File}: destination already exists at {Dest}", content.FilePath, candidatePath);
                                }
                                else
                                {
                                    Directory.CreateDirectory(newDir);
                                    File.Move(content.FilePath, candidatePath);
                                    newFilePath = candidatePath;
                                    moved = true;
                                    Log.Information("Moved content for IGDB {Id}: {Old} -> {New}", igdbId, content.FilePath, candidatePath);
                                }
                            }
                        }
                    }

                    string sidecar = Path.Combine(FolderNames.GetMetadataFolderPath(content.Type), content.FileName + ".json");
                    await UpdateMetadataFile(sidecar, c =>
                    {
                        c.Game = canonicalName;
                        if (moved && newFilePath != null) c.FilePath = newFilePath;
                    });

                    content.Game = canonicalName;
                    if (moved && newFilePath != null) content.FilePath = newFilePath;

                    changedCount++;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Log.Error(ex, "Failed reconciling game name for content {File}", content?.FilePath ?? "(unknown)");
                }
            }

            if (changedCount > 0 || errorCount > 0)
            {
                Log.Information("ReconcileGameNamesByIgdb: changed={Changed}, errors={Errors}", changedCount, errorCount);
            }

            return changedCount;
        }

        public static async Task CreateThumbnail(string filePath, Content.ContentType type)
        {
            try
            {
                // Get the directory and file name
                string contentFileName = Path.GetFileNameWithoutExtension(filePath);

                // Ensure the thumbnails folder exists
                string thumbnailsFolderPath = FolderNames.GetThumbnailsFolderPath(type);
                if (!Directory.Exists(thumbnailsFolderPath))
                {
                    Directory.CreateDirectory(thumbnailsFolderPath);
                }

                // Define the output thumbnail file path
                string thumbnailFilePath = Path.Combine(thumbnailsFolderPath, $"{contentFileName}.jpeg");

                if (!FFmpegService.FFmpegExists())
                {
                    Log.Information("FFmpeg binary not found!");
                    return;
                }

                await FFmpegService.CreateThumbnailFile(filePath, thumbnailFilePath);
                Log.Information($"Thumbnail successfully created at: {thumbnailFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error creating thumbnail: {ex.Message}");
            }
        }

        public static async Task CreateWaveformFile(string videoFilePath, Content.ContentType type)
        {
            try
            {
                if (!FFmpegService.FFmpegExists())
                {
                    Log.Error($"FFmpeg executable not found at: {FFmpegService.GetFFmpegPath()}");
                    return;
                }
                if (!File.Exists(videoFilePath))
                {
                    Log.Error($"Video file not found at: {videoFilePath}");
                    return;
                }

                string contentFileName = Path.GetFileNameWithoutExtension(videoFilePath);

                // Ensure the waveforms folder exists
                string waveformFolderPath = FolderNames.GetWaveformsFolderPath(type);
                if (!Directory.Exists(waveformFolderPath))
                {
                    Directory.CreateDirectory(waveformFolderPath);
                }

                string tempPcmPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pcm");
                string waveformJsonPathTemp = Path.Combine(waveformFolderPath, $"{contentFileName}.peaks.temp.json");
                string waveformJsonPath = Path.Combine(waveformFolderPath, $"{contentFileName}.peaks.json");

                // Decode audio to raw mono 16-bit PCM at a modest sample rate for efficiency.
                // Probe the file for its audio track count so multi-track recordings can be
                // mixed together rather than ffmpeg silently picking just one stream.
                int sampleRate = 11025;
                var audioTrackNames = await Mp4BoxReader.ReadAudioTrackNamesAsync(videoFilePath);
                int audioStreamCount = audioTrackNames?.Count ?? 1;
                await FFmpegService.ExtractPcmAudio(videoFilePath, tempPcmPath, sampleRate, audioStreamCount);

                if (!File.Exists(tempPcmPath))
                {
                    Log.Error("PCM extraction did not produce output file.");
                    return;
                }

                // Read PCM and compute min/max pairs as 8-bit integers similar to audiowaveform output
                byte[] pcmBytes = await File.ReadAllBytesAsync(tempPcmPath);
                int totalSamples = pcmBytes.Length / 2; // 16-bit mono
                if (totalSamples == 0)
                {
                    Log.Warning("No audio samples found when generating waveform peaks.");
                    var emptyJson = new
                    {
                        version = 2,
                        channels = 1,
                        sample_rate = sampleRate,
                        samples_per_pixel = 1,
                        bits = 8,
                        length = 0,
                        data = Array.Empty<int>()
                    };
                    await File.WriteAllTextAsync(waveformJsonPathTemp, JsonSerializer.Serialize(emptyJson));
                    File.Move(waveformJsonPathTemp, waveformJsonPath, true);
                    return;
                }

                // Aim for ~50 pixel columns per second; each column contributes two values (min,max)
                double columnsPerSecond = 50.0;
                int columns = Math.Max(1, (int)Math.Round((totalSamples / (double)sampleRate) * columnsPerSecond));
                int samplesPerPixel = Math.Max(1, (int)Math.Ceiling(totalSamples / (double)columns));

                var data = new List<int>(columns * 2);

                for (int i = 0; i < totalSamples; i += samplesPerPixel)
                {
                    int end = Math.Min(totalSamples, i + samplesPerPixel);
                    short min16 = short.MaxValue;
                    short max16 = short.MinValue;
                    for (int s = i; s < end; s++)
                    {
                        int byteIndex = s * 2;
                        short sample = BitConverter.ToInt16(pcmBytes, byteIndex);
                        if (sample < min16) min16 = sample;
                        if (sample > max16) max16 = sample;
                    }
                    // Scale 16-bit PCM to 8-bit range approximately -128..127
                    int min8 = (int)Math.Round(min16 / 256.0);
                    int max8 = (int)Math.Round(max16 / 256.0);
                    // Clamp to [-128,127]
                    min8 = Math.Max(-128, Math.Min(127, min8));
                    max8 = Math.Max(-128, Math.Min(127, max8));
                    data.Add(min8);
                    data.Add(max8);
                }

                var wrapper = new
                {
                    version = 2,
                    channels = 1,
                    sample_rate = sampleRate,
                    samples_per_pixel = samplesPerPixel,
                    bits = 8,
                    length = data.Count,
                    data
                };
                // Serialize JSON
                var json = JsonSerializer.Serialize(wrapper);
                await File.WriteAllTextAsync(waveformJsonPathTemp, json);
                File.Move(waveformJsonPathTemp, waveformJsonPath, true);
                Log.Information($"Waveform JSON successfully created at: {waveformJsonPath}");

                // Cleanup
                try { File.Delete(tempPcmPath); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                Log.Error($"Error creating waveform JSON: {ex.Message}");
            }
        }

        public static async Task<TimeSpan> GetVideoDurationAsync(string videoFilePath)
        {
            try
            {
                return await FFmpegService.GetVideoDuration(videoFilePath);
            }
            catch (Exception ex)
            {
                Log.Error($"Error getting video duration: {ex.Message}");
                return TimeSpan.Zero; // Return zero duration in case of error
            }
        }

        public static async Task DeleteContent(string filePath, Content.ContentType type, bool sendToFrontend = true)
        {
            try
            {
                // Validate the file path
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    Log.Warning("DeleteClip called with an invalid file path.");
                    return;
                }

                // Normalize the file path
                string normalizedFilePath = Path.GetFullPath(filePath);

                // Ensure the video file exists before attempting deletion
                string? videoDirectory = Path.GetDirectoryName(normalizedFilePath);
                if (File.Exists(normalizedFilePath))
                {
                    int maxRetries = 3;
                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            File.Delete(normalizedFilePath);
                            Log.Information($"Video file deleted: {normalizedFilePath}");
                            break;
                        }
                        catch (IOException)
                        {
                            if (i == maxRetries - 1) throw; // Re-throw on last attempt
                            Log.Warning($"File is locked, retrying deletion in 500ms... (Attempt {i + 1}/{maxRetries})");
                            await Task.Delay(500);
                        }
                    }

                    // Clean up empty game folder if it exists
                    if (!string.IsNullOrEmpty(videoDirectory) && Directory.Exists(videoDirectory))
                    {
                        try
                        {
                            // Only delete if the folder is empty and is a game subfolder (not the root video type folder)
                            string contentRoot = Settings.Instance.ContentFolder;
                            string[] rootFolders =
                            {
                                FolderNames.Sessions,
                                FolderNames.Buffers,
                                FolderNames.Clips,
                                FolderNames.Highlights,
                                FolderNames.PendingEdit
                            };
                            bool isGameSubfolder = rootFolders.Any(rf =>
                                videoDirectory.StartsWith(Path.Combine(contentRoot, rf), StringComparison.OrdinalIgnoreCase) &&
                                !videoDirectory.Equals(Path.Combine(contentRoot, rf), StringComparison.OrdinalIgnoreCase));

                            if (isGameSubfolder && !Directory.EnumerateFileSystemEntries(videoDirectory).Any())
                            {
                                Directory.Delete(videoDirectory);
                                Log.Information($"Deleted empty game folder: {videoDirectory}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"Failed to clean up empty game folder: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Log.Warning($"Video file not found (already deleted?): {normalizedFilePath}");
                }

                // Extract the content file name without extension
                string contentFileName = Path.GetFileNameWithoutExtension(normalizedFilePath);

                // Construct the metadata file path
                string metadataFolderPath = FolderNames.GetMetadataFolderPath(type);
                string metadataFilePath = Path.Combine(metadataFolderPath, $"{contentFileName}.json");

                // Delete the metadata file if it exists
                if (File.Exists(metadataFilePath))
                {
                    File.Delete(metadataFilePath);
                    Log.Information($"Metadata file deleted: {metadataFilePath}");
                }
                else
                {
                    Log.Warning($"Metadata file not found: {metadataFilePath}");
                }

                // Construct the thumbnail file path
                string thumbnailsFolderPath = FolderNames.GetThumbnailsFolderPath(type);
                string thumbnailFilePath = Path.Combine(thumbnailsFolderPath, $"{contentFileName}.jpeg");

                // Delete the thumbnail file if it exists
                if (File.Exists(thumbnailFilePath))
                {
                    File.Delete(thumbnailFilePath);
                    Log.Information($"Thumbnail file deleted: {thumbnailFilePath}");
                }
                else
                {
                    Log.Warning($"Thumbnail file not found: {thumbnailFilePath}");
                }

                // Construct the waveform JSON path
                string waveformFolderPath = FolderNames.GetWaveformsFolderPath(type);
                string waveformFilePath = Path.Combine(waveformFolderPath, $"{contentFileName}.peaks.json");

                // Delete the waveform file if it exists
                if (File.Exists(waveformFilePath))
                {
                    File.Delete(waveformFilePath);
                    Log.Information($"Waveform file deleted: {waveformFilePath}");
                }
                else
                {
                    Log.Warning($"Waveform file not found: {waveformFilePath}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error($"Access denied while deleting files: {ex.Message}");
            }
            catch (IOException ex)
            {
                Log.Error($"I/O error while deleting files: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Error($"Unexpected error while deleting clip: {ex.Message}");
            }
            finally
            {
                await SettingsService.LoadContentFromFolderIntoState(sendToFrontend);
            }
        }

        public static (string displaySize, long sizeKb) GetFileSize(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                long fileSizeInKb = fileInfo.Length / 1024;
                double fileSizeInMb = fileInfo.Length / (1024.0 * 1024.0);

                if (fileSizeInMb > 1000)
                {
                    double fileSizeInGb = fileSizeInMb / 1024.0;
                    return ($"{fileSizeInGb:F2} GB", fileSizeInKb);
                }
                else
                {
                    return ($"{fileSizeInMb:F2} MB", fileSizeInKb);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error getting file size: {ex.Message}");
                return ("Unknown", 0);
            }
        }

        private static void TryMoveSidecarFile(string sourcePath, string destPath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                    return;
                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                if (File.Exists(destPath))
                    File.Delete(destPath);
                File.Move(sourcePath, destPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TryMoveSidecarFile failed {Src} -> {Dest}", sourcePath, destPath);
            }
        }

        private static async Task MoveSingleContentToPendingEdit(Content content, Content.ContentType sourceType)
        {
            if (sourceType != Content.ContentType.Session && sourceType != Content.ContentType.Buffer)
            {
                Log.Warning("MoveToPendingEdit ignored non-session/buffer type {Type} for {File}", sourceType, content.FileName);
                return;
            }

            string oldVideoPath = Path.GetFullPath(content.FilePath);
            if (!File.Exists(oldVideoPath))
            {
                Log.Warning("MoveToPendingEdit: video missing {Path}", oldVideoPath);
                return;
            }

            string contentFolder = Path.GetFullPath(Settings.Instance.ContentFolder);
            string gameFolder = StorageService.SanitizeGameNameForFolder(content.Game ?? "Unknown");
            string fileBase = Path.GetFileName(oldVideoPath);
            string destDir = Path.Combine(contentFolder, FolderNames.PendingEdit, gameFolder);
            Directory.CreateDirectory(destDir);
            string newVideoPath = Path.GetFullPath(Path.Combine(destDir, fileBase));

            if (string.Equals(newVideoPath, oldVideoPath, StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("MoveToPendingEdit: already at destination {Path}", oldVideoPath);
                return;
            }

            if (File.Exists(newVideoPath))
            {
                Log.Warning("MoveToPendingEdit: destination already exists {Path}", newVideoPath);
                return;
            }

            try
            {
                File.Move(oldVideoPath, newVideoPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MoveToPendingEdit: failed moving video {Old} -> {New}", oldVideoPath, newVideoPath);
                return;
            }

            string newMetaDir = FolderNames.GetMetadataFolderPath(Content.ContentType.PendingEdit);
            Directory.CreateDirectory(newMetaDir);
            string newMetaPath = Path.Combine(newMetaDir, $"{content.FileName}.json");
            string oldMetaPath = Path.Combine(FolderNames.GetMetadataFolderPath(sourceType), $"{content.FileName}.json");

            try
            {
                if (File.Exists(newMetaPath))
                    throw new IOException($"Metadata already exists at {newMetaPath}");

                if (File.Exists(oldMetaPath))
                {
                    string json = await File.ReadAllTextAsync(oldMetaPath);
                    var meta = JsonSerializer.Deserialize<Content>(json);
                    if (meta == null)
                        throw new InvalidOperationException("Failed to deserialize metadata");

                    meta.Type = Content.ContentType.PendingEdit;
                    meta.FilePath = newVideoPath;
                    await File.WriteAllTextAsync(newMetaPath, JsonSerializer.Serialize(meta, _jsonOptions));
                    File.Delete(oldMetaPath);
                }
                else
                {
                    Log.Warning("MoveToPendingEdit: no metadata at {Old}, recreating", oldMetaPath);
                    await CreateMetadataFile(
                        newVideoPath,
                        Content.ContentType.PendingEdit,
                        content.Game ?? "Unknown",
                        content.Bookmarks,
                        string.IsNullOrEmpty(content.Title) ? null : content.Title,
                        content.CreatedAt,
                        content.IgdbId,
                        content.IsImported,
                        content.AudioTrackNames);
                }

                TryMoveSidecarFile(
                    Path.Combine(FolderNames.GetThumbnailsFolderPath(sourceType), $"{content.FileName}.jpeg"),
                    Path.Combine(FolderNames.GetThumbnailsFolderPath(Content.ContentType.PendingEdit), $"{content.FileName}.jpeg"));

                TryMoveSidecarFile(
                    Path.Combine(FolderNames.GetWaveformsFolderPath(sourceType), $"{content.FileName}.peaks.json"),
                    Path.Combine(FolderNames.GetWaveformsFolderPath(Content.ContentType.PendingEdit), $"{content.FileName}.peaks.json"));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MoveToPendingEdit: metadata/sidecar failed after video move; reverting file");
                try
                {
                    if (File.Exists(newVideoPath) && !File.Exists(oldVideoPath))
                        File.Move(newVideoPath, oldVideoPath);
                }
                catch (Exception revertEx)
                {
                    Log.Error(revertEx, "MoveToPendingEdit: revert failed; manual fix may be needed");
                }

                if (File.Exists(newMetaPath))
                {
                    try { File.Delete(newMetaPath); } catch { /* ignore */ }
                }
            }
        }

        public static async Task HandleMoveToPendingEdit(JsonElement message)
        {
            try
            {
                var snapshots = new List<(Content content, Content.ContentType sourceType)>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void TryAddItem(string? fileName, string? contentTypeStr)
                {
                    if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(contentTypeStr))
                        return;
                    if (!Enum.TryParse(contentTypeStr, out Content.ContentType sourceType))
                        return;
                    if (sourceType != Content.ContentType.Session && sourceType != Content.ContentType.Buffer)
                        return;
                    string key = $"{sourceType}:{fileName}";
                    if (!seen.Add(key))
                        return;

                    Content? c = AppState.Instance.Content.FirstOrDefault(x =>
                        x.FileName == fileName && x.Type == sourceType);
                    if (c != null)
                        snapshots.Add((c, sourceType));
                    else
                        Log.Warning("MoveToPendingEdit: content not in state {File} {Type}", fileName, sourceType);
                }

                if (message.TryGetProperty("Items", out JsonElement itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in itemsEl.EnumerateArray())
                    {
                        if (!item.TryGetProperty("FileName", out JsonElement fnEl) ||
                            !item.TryGetProperty("ContentType", out JsonElement ctEl))
                            continue;
                        TryAddItem(fnEl.GetString(), ctEl.GetString());
                    }
                }
                else if (message.TryGetProperty("FileName", out JsonElement fnSingle) &&
                         message.TryGetProperty("ContentType", out JsonElement ctSingle))
                {
                    TryAddItem(fnSingle.GetString(), ctSingle.GetString());
                }

                if (snapshots.Count == 0)
                {
                    Log.Warning("MoveToPendingEdit: no valid items");
                    return;
                }

                foreach (var pair in snapshots)
                    await MoveSingleContentToPendingEdit(pair.content, pair.sourceType);

                await SettingsService.LoadContentFromFolderIntoState(true);
                await MessageService.SendSettingsToFrontend("Moved to pending edit");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HandleMoveToPendingEdit failed");
            }
        }

        public static async Task HandleAddBookmark(JsonElement message)
        {
            try
            {
                // Get required properties from the message
                if (message.TryGetProperty("FilePath", out JsonElement filePathElement) &&
                    message.TryGetProperty("Type", out JsonElement typeElement) &&
                    message.TryGetProperty("Time", out JsonElement timeElement) &&
                    message.TryGetProperty("ContentType", out JsonElement contentTypeElement) &&
                    message.TryGetProperty("Id", out JsonElement idElement))
                {
                    string? filePath = filePathElement.GetString();
                    string? bookmarkTypeStr = typeElement.GetString();
                    string? timeString = timeElement.GetString();
                    string? contentTypeStr = contentTypeElement.GetString();
                    int bookmarkId = idElement.GetInt32();

                    if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(timeString) || string.IsNullOrEmpty(contentTypeStr))
                    {
                        Log.Error("Required parameters are null or empty in AddBookmark message");
                        return;
                    }

                    // Parse bookmark type, default to Manual if not valid
                    BookmarkType bookmarkType = BookmarkType.Manual;
                    if (!string.IsNullOrEmpty(bookmarkTypeStr) && Enum.TryParse<BookmarkType>(bookmarkTypeStr, out var parsedType))
                    {
                        bookmarkType = parsedType;
                    }

                    // Determine content type from the provided value
                    if (!Enum.TryParse<Content.ContentType>(contentTypeStr, out Content.ContentType contentType))
                    {
                        Log.Error($"Invalid content type: {contentTypeStr}");
                        return;
                    }

                    // Get metadata file path
                    string contentFileName = Path.GetFileNameWithoutExtension(filePath);
                    string metadataFolderPath = FolderNames.GetMetadataFolderPath(contentType);
                    string metadataFilePath = Path.Combine(metadataFolderPath, $"{contentFileName}.json");

                    // Create a new bookmark
                    var bookmark = new Bookmark
                    {
                        Id = bookmarkId,
                        Type = bookmarkType,
                        Time = TimeSpan.Parse(timeString)
                    };

                    // Update the metadata file
                    var content = await UpdateMetadataFile(metadataFilePath, c =>
                    {
                        if (c.Bookmarks == null)
                        {
                            c.Bookmarks = [];
                        }
                        c.Bookmarks.Add(bookmark);
                    });

                    if (content == null)
                    {
                        return;
                    }

                    // Update the bookmark in the in-memory content collection
                    var contentItem = AppState.Instance.Content.FirstOrDefault(c =>
                        c.FilePath == filePath &&
                        c.Type.ToString() == contentTypeStr);

                    if (contentItem == null)
                    {
                        Log.Error($"Content item not found for {filePath} and {contentTypeStr}");
                        return;
                    }

                    contentItem.Bookmarks.Add(bookmark);

                    await MessageService.SendSettingsToFrontend("Added bookmark");
                    Log.Information($"Added bookmark of type {bookmarkType} at {timeString} to {metadataFilePath}");
                }
                else
                {
                    Log.Error("Required properties missing in AddBookmark message.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling AddBookmark: {ex.Message}");
            }
        }

        public static async Task HandleDeleteBookmark(JsonElement message)
        {
            try
            {
                // Get required properties from the message
                if (message.TryGetProperty("FilePath", out JsonElement filePathElement) &&
                    message.TryGetProperty("ContentType", out JsonElement contentTypeElement) &&
                    message.TryGetProperty("Id", out JsonElement idElement))
                {
                    string? filePath = filePathElement.GetString();
                    string? contentTypeStr = contentTypeElement.GetString();
                    int bookmarkId = idElement.GetInt32();

                    if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(contentTypeStr))
                    {
                        Log.Error("Required parameters are null or empty in DeleteBookmark message");
                        return;
                    }

                    // Determine content type from the provided value
                    if (!Enum.TryParse<Content.ContentType>(contentTypeStr, out Content.ContentType contentType))
                    {
                        Log.Error($"Invalid content type: {contentTypeStr}");
                        return;
                    }

                    // Get metadata file path
                    string contentFileName = Path.GetFileNameWithoutExtension(filePath);
                    string metadataFolderPath = FolderNames.GetMetadataFolderPath(contentType);
                    string metadataFilePath = Path.Combine(metadataFolderPath, $"{contentFileName}.json");

                    // Update the metadata file
                    var content = await UpdateMetadataFile(metadataFilePath, c =>
                    {
                        if (c.Bookmarks != null)
                        {
                            c.Bookmarks = c.Bookmarks.Where(b => b.Id != bookmarkId).ToList();
                        }
                    });

                    if (content == null)
                    {
                        return;
                    }

                    // Update the bookmark in the in-memory content collection
                    var contentItem = AppState.Instance.Content.FirstOrDefault(c =>
                        c.FilePath == filePath &&
                        c.Type.ToString() == contentTypeStr);

                    if (contentItem != null && contentItem.Bookmarks != null)
                    {
                        contentItem.Bookmarks = contentItem.Bookmarks.Where(b => b.Id != bookmarkId).ToList();
                    }

                    await MessageService.SendSettingsToFrontend("Deleted bookmark");
                    Log.Information($"Deleted bookmark with id {bookmarkId} from {metadataFilePath}");
                }
                else
                {
                    Log.Error("Required properties missing in DeleteBookmark message.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling DeleteBookmark: {ex.Message}");
            }
        }

        public static async Task HandleRenameContent(JsonElement message)
        {
            try
            {
                Log.Information($"Handling RenameContent with message: {message}");

                if (message.TryGetProperty("FileName", out JsonElement fileNameElement) &&
                    message.TryGetProperty("ContentType", out JsonElement contentTypeElement) &&
                    message.TryGetProperty("Title", out JsonElement titleElement))
                {
                    string fileName = fileNameElement.GetString()!;
                    string contentTypeStr = contentTypeElement.GetString()!;
                    string newTitle = titleElement.GetString() ?? string.Empty;

                    if (!Enum.TryParse(contentTypeStr, true, out Content.ContentType contentType))
                    {
                        Log.Error($"Invalid ContentType provided: {contentTypeStr}");
                        return;
                    }

                    string metadataFolderPath = FolderNames.GetMetadataFolderPath(contentType);
                    string metadataFilePath = Path.Combine(metadataFolderPath, $"{fileName}.json");

                    if (!File.Exists(metadataFilePath))
                    {
                        Log.Error($"Metadata file not found for rename: {metadataFilePath}");
                        return;
                    }

                    string metadataJson = await File.ReadAllTextAsync(metadataFilePath);
                    var existingContent = JsonSerializer.Deserialize<Content>(metadataJson);
                    if (existingContent == null)
                    {
                        Log.Error($"Failed to deserialize metadata for rename: {metadataFilePath}");
                        return;
                    }

                    string sanitizedNewFileName = StorageService.SanitizeFileName(newTitle.Trim());
                    if (string.IsNullOrEmpty(sanitizedNewFileName))
                    {
                        Log.Warning("RenameContent rejected: title is empty or invalid after sanitization");
                        return;
                    }

                    bool fileNameChanged = !string.Equals(sanitizedNewFileName, fileName, StringComparison.OrdinalIgnoreCase);

                    if (fileNameChanged)
                    {
                        string oldVideoPath = Path.GetFullPath(existingContent.FilePath);
                        if (!File.Exists(oldVideoPath))
                        {
                            Log.Error($"Video file not found for rename: {oldVideoPath}");
                            return;
                        }

                        string extension = Path.GetExtension(oldVideoPath);
                        if (string.IsNullOrEmpty(extension))
                            extension = ".mp4";

                        string? videoDirectory = Path.GetDirectoryName(oldVideoPath);
                        if (string.IsNullOrEmpty(videoDirectory))
                        {
                            Log.Error($"Could not determine directory for rename: {oldVideoPath}");
                            return;
                        }

                        string newVideoPath = Path.GetFullPath(Path.Combine(videoDirectory, $"{sanitizedNewFileName}{extension}"));
                        if (File.Exists(newVideoPath))
                        {
                            Log.Warning("RenameContent rejected: destination video already exists at {Path}", newVideoPath);
                            return;
                        }

                        string newMetadataFilePath = Path.Combine(metadataFolderPath, $"{sanitizedNewFileName}.json");
                        if (File.Exists(newMetadataFilePath))
                        {
                            Log.Warning("RenameContent rejected: destination metadata already exists at {Path}", newMetadataFilePath);
                            return;
                        }

                        File.Move(oldVideoPath, newVideoPath);

                        TryMoveSidecarFile(
                            Path.Combine(FolderNames.GetThumbnailsFolderPath(contentType), $"{fileName}.jpeg"),
                            Path.Combine(FolderNames.GetThumbnailsFolderPath(contentType), $"{sanitizedNewFileName}.jpeg"));

                        TryMoveSidecarFile(
                            Path.Combine(FolderNames.GetWaveformsFolderPath(contentType), $"{fileName}.peaks.json"),
                            Path.Combine(FolderNames.GetWaveformsFolderPath(contentType), $"{sanitizedNewFileName}.peaks.json"));

                        TryMoveSidecarFile(
                            Path.Combine(FolderNames.GetWaveformsFolderPath(contentType), $"{fileName}.peaks.temp.json"),
                            Path.Combine(FolderNames.GetWaveformsFolderPath(contentType), $"{sanitizedNewFileName}.peaks.temp.json"));

                        File.Move(metadataFilePath, newMetadataFilePath);
                        metadataFilePath = newMetadataFilePath;

                        await UpdateMetadataFile(metadataFilePath, c =>
                        {
                            c.Title = newTitle.Trim();
                            c.FileName = sanitizedNewFileName;
                            c.FilePath = newVideoPath;
                        });

                        Log.Information(
                            "Renamed content {OldFile} -> {NewFile} ({Type})",
                            fileName,
                            sanitizedNewFileName,
                            contentType);
                    }
                    else
                    {
                        await UpdateMetadataFile(metadataFilePath, c =>
                        {
                            c.Title = newTitle.Trim();
                        });

                        Log.Information($"Updated title for {fileName} to '{newTitle.Trim()}' in metadata file: {metadataFilePath}");
                    }

                    await SettingsService.LoadContentFromFolderIntoState(true);

                    var updatedContent = AppState.Instance.Content.FirstOrDefault(c =>
                        c.FileName.Equals(sanitizedNewFileName, StringComparison.OrdinalIgnoreCase) &&
                        c.Type == contentType);

                    await MessageService.SendFrontendMessage("ContentRenamed", new
                    {
                        OldFileName = fileName,
                        NewFileName = sanitizedNewFileName,
                        ContentType = contentTypeStr,
                        Content = updatedContent,
                    });

                    await MessageService.SendSettingsToFrontend("Renamed content");
                }
                else
                {
                    Log.Error("FileName, ContentType, or Title property not found in RenameContent message.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling RenameContent: {ex.Message}");
            }
        }
    }
}
