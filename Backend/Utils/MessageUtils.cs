using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Text.Json;
using Serilog;
using System.Net.Http.Headers;
using System.Diagnostics;
using Segra.Backend.Services;
using Segra.Backend.Models;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Globalization;

namespace Segra.Backend.Utils
{
    public class Selection
    {
        public long Id { get; set; }
        // TODO (os): make this of type ContentType
        public required string Type { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public required string FileName { get; set; }
        public required string Game { get; set; }
    }

    public static class MessageUtils
    {
        private static WebSocket? activeWebSocket;
        private static readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static async Task HandleMessage(string message)
        {
            Log.Information("Websocket message received: " + RedactSensitiveInfo(message));
            if (string.IsNullOrEmpty(message))
            {
                Log.Information("Received empty message.");
                return;
            }

            try
            {
                var jsonDoc = JsonDocument.Parse(message);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("Method", out JsonElement methodElement))
                {
                    string? method = methodElement.GetString();

                    if (method == null)
                    {
                        Log.Warning("Received message with null method.");
                        return;
                    }

                    switch (method)
                    {
                        case "ToggleFullscreen":
                            if (root.TryGetProperty("Parameters", out var fsParams) &&
                                fsParams.TryGetProperty("enabled", out var enabledEl))
                            {
                                bool enabled = enabledEl.GetBoolean();
                                try
                                {
                                    Program.SetFullscreen(enabled);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "Failed to toggle fullscreen");
                                }
                            }
                            break;
                        case "Login":
                            root.TryGetProperty("Parameters", out JsonElement loginParameterElement);
                            string accessToken = loginParameterElement.GetProperty("accessToken").GetString()!;
                            string refreshToken = loginParameterElement.GetProperty("refreshToken").GetString()!;
                            await AuthService.Login(accessToken, refreshToken);
                            break;
                        case "Logout":
                            await AuthService.Logout();
                            break;
                        case "CancelClip":
                            if (root.TryGetProperty("Parameters", out var cancelClipParams) &&
                                cancelClipParams.TryGetProperty("id", out var clipId))
                            {
                                ClipUtils.CancelClip(clipId.GetInt32());
                            }
                            break;
                        case "CreateClip":
                            root.TryGetProperty("Parameters", out JsonElement clipParameterElement);
                            await HandleCreateClip(clipParameterElement);
                            break;
                        case "CreateAiClip":
                            root.TryGetProperty("Parameters", out JsonElement aiClipParameterElement);
                            await HandleCreateAiClip(aiClipParameterElement);
                            break;
                        case "ApplyUpdate":
                            UpdateUtils.ApplyUpdate();
                            break;
                        case "CheckForUpdates":
                            Log.Information("CheckForUpdates command received.");
                            _ = Task.Run(UpdateUtils.UpdateAppIfNecessary);
                            break;
                        case "AddToWhitelist":
                            root.TryGetProperty("Parameters", out JsonElement addWhitelistParameterElement);
                            await HandleAddToWhitelist(addWhitelistParameterElement);
                            break;
                        case "RemoveFromWhitelist":
                            root.TryGetProperty("Parameters", out JsonElement removeWhitelistParameterElement);
                            await HandleRemoveFromWhitelist(removeWhitelistParameterElement);
                            break;
                        case "AddToBlacklist":
                            root.TryGetProperty("Parameters", out JsonElement addBlacklistParameterElement);
                            await HandleAddToBlacklist(addBlacklistParameterElement);
                            break;
                        case "RemoveFromBlacklist":
                            root.TryGetProperty("Parameters", out JsonElement removeBlacklistParameterElement);
                            await HandleRemoveFromBlacklist(removeBlacklistParameterElement);
                            break;
                        case "DeleteContent":
                            root.TryGetProperty("Parameters", out JsonElement deleteContentParameterElement);
                            await HandleDeleteContent(deleteContentParameterElement);
                            break;
                        case "UploadContent":
                            root.TryGetProperty("Parameters", out JsonElement uploadContentParameterElement);
                            await HandleUploadContent(uploadContentParameterElement);
                            break;
                        case "OpenFileLocation":
                            root.TryGetProperty("Parameters", out JsonElement openFileLocationParameterElement);
                            openFileLocationParameterElement.TryGetProperty("FilePath", out JsonElement filePathElement);
                            Process.Start("explorer.exe", $"/select,\"{filePathElement.ToString().Replace("/", "\\")}\"");
                            break;
                        case "OpenInBrowser":
                            root.TryGetProperty("Parameters", out JsonElement openInBrowserParameterElement);
                            if (openInBrowserParameterElement.TryGetProperty("Url", out JsonElement urlElement))
                            {
                                string url = urlElement.GetString()!;
                                Log.Information($"Opening URL in browser: {url}");
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = url,
                                    UseShellExecute = true
                                });
                            }
                            else
                            {
                                Log.Error("URL parameter not found in OpenInBrowser message");
                            }
                            break;
                        case "OpenLogsLocation":
                            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra");
                            string? logFilePath = Directory.GetFiles(logDir, "*.log").FirstOrDefault();
                            if (!string.IsNullOrEmpty(logFilePath))
                            {
                                Process.Start("explorer.exe", $"/select,\"{logFilePath}\"");
                            }
                            else
                            {
                                Log.Warning("No log files found in the Segra directory");
                            }
                            break;
                        case "SelectGameExecutable":
                            await HandleSelectGameExecutable();
                            break;
                        case "StartRecording":
                            if (Settings.Instance.State.Recording != null || Settings.Instance.State.PreRecording != null)
                            {
                                Log.Information("Recording already in progress. Skipping...");
                                return;
                            }

                            await Task.Run(() => OBSUtils.StartRecording(startManually: true));
                            break;
                        case "StopRecording":
                            await Task.Run(OBSUtils.StopRecording);
                            break;
                        case "NewConnection":
                            Log.Information("NewConnection command received.");
                            await SendSettingsToFrontend("New connection");

                            // Get current version
                            if (UpdateUtils.UpdateManager.CurrentVersion != null)
                            {
                                string appVersion = UpdateUtils.UpdateManager.CurrentVersion.ToString();

                                // Send version to frontend to prevent mismatch
                                await SendFrontendMessage("AppVersion", new
                                {
                                    version = appVersion
                                });
                            }

                            _ = Task.Run(UpdateUtils.GetReleaseNotes);
                            break;
                        case "SetVideoLocation":
                            await SetVideoLocationAsync();
                            Log.Information("SetVideoLocation command received.");
                            break;
                        case "UpdateSettings":
                            root.TryGetProperty("Parameters", out JsonElement settingsParameterElement);
                            SettingsUtils.HandleUpdateSettings(settingsParameterElement);
                            Log.Information("UpdateSettings command received.");
                            break;
                        case "AddBookmark":
                            root.TryGetProperty("Parameters", out JsonElement bookmarkParameterElement);
                            await HandleAddBookmark(bookmarkParameterElement);
                            Log.Information("AddBookmark command received.");
                            break;
                        case "DeleteBookmark":
                            root.TryGetProperty("Parameters", out JsonElement deleteBookmarkParameterElement);
                            await HandleDeleteBookmark(deleteBookmarkParameterElement);
                            Log.Information("DeleteBookmark command received.");
                            break;
                        case "ImportFile":
                            root.TryGetProperty("Parameters", out JsonElement importParameterElement);
                            await HandleImportFile(importParameterElement);
                            Log.Information("ImportFile command received.");
                            break;
                        default:
                            Log.Information($"Unknown method: {method}");
                            break;
                    }
                }
                else
                {
                    Log.Information("Method property not found in message.");
                }
            }
            catch (JsonException ex)
            {
                Log.Error($"Failed to parse message as JSON: {ex.Message}");
            }
        }
        private static async Task HandleCreateAiClip(JsonElement message)
        {
            Log.Information($"{message}");
            message.TryGetProperty("FileName", out JsonElement fileNameElement);
            await AiService.AnalyzeVideo(fileNameElement.GetString()!);
        }

        private static async Task HandleCreateClip(JsonElement message)
        {
            Log.Information($"{message}");

            if (message.TryGetProperty("Selections", out JsonElement selectionsElement))
            {
                var selections = new List<Selection>();
                foreach (var selectionElement in selectionsElement.EnumerateArray())
                {
                    if (selectionElement.TryGetProperty("id", out JsonElement idElement) &&
                        selectionElement.TryGetProperty("startTime", out JsonElement startTimeElement) &&
                        selectionElement.TryGetProperty("endTime", out JsonElement endTimeElement) &&
                        selectionElement.TryGetProperty("fileName", out JsonElement fileNameElement) &&
                        selectionElement.TryGetProperty("type", out JsonElement videoTypeElement) &&
                        selectionElement.TryGetProperty("game", out JsonElement gameElement))
                    {
                        long id = idElement.GetInt64();
                        double startTime = startTimeElement.GetDouble();
                        double endTime = endTimeElement.GetDouble();
                        string fileName = fileNameElement.GetString()!;
                        string type = videoTypeElement.GetString()!;
                        string game = gameElement.GetString()!;

                        // Create a new Selection instance with all required properties.
                        selections.Add(new Selection
                        {
                            Id = id,
                            Type = type,
                            StartTime = startTime,
                            EndTime = endTime,
                            FileName = fileName,
                            Game = game
                        });
                    }
                }

                await ClipUtils.CreateClips(selections);
            }
            else
            {
                Log.Information("Selections property not found in CreateClip message.");
            }
        }

        private static string SmartFormatGameName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Unknown";

            string result = input.Trim();

            // Handle Desktop special case
            if (result.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
                return "Desktop Recording";

            // First, identify and preserve potential acronyms (2-6 consecutive uppercase letters)
            var acronymMatches = System.Text.RegularExpressions.Regex.Matches(result, @"\b[A-Z]{2,6}\b");
            var acronymPlaceholders = new Dictionary<string, string>();
            int placeholderIndex = 0;

            foreach (System.Text.RegularExpressions.Match match in acronymMatches)
            {
                string placeholder = $"__ACRONYM_{placeholderIndex}__";
                acronymPlaceholders[placeholder] = match.Value;
                result = result.Replace(match.Value, placeholder);
                placeholderIndex++;
            }

            // Add spaces before capital letters (CamelCase handling)
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<!^)(?=[A-Z][a-z])|(?<=[a-z])(?=[A-Z])", " ");

            // Add spaces before numbers that follow letters
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<=[a-zA-Z])(?=\d)", " ");

            // Add spaces after numbers that are followed by letters
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<=\d)(?=[a-zA-Z])", " ");

            // Clean up multiple spaces
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();

            // Restore acronyms
            foreach (var placeholder in acronymPlaceholders)
            {
                result = result.Replace(placeholder.Key, placeholder.Value);
            }

            // Split into words for processing
            var words = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var processedWords = new List<string>();

            foreach (string word in words)
            {
                string processedWord = word;

                // Keep acronyms as they are (2-6 uppercase letters)
                if (word.Length >= 2 && word.Length <= 6 && word.All(char.IsUpper) && word.All(char.IsLetter))
                {
                    processedWord = word;
                }
                // Handle connecting words as lowercase (except if first word)
                else if (processedWords.Count > 0 &&
                         (word.Equals("of", StringComparison.OrdinalIgnoreCase) ||
                          word.Equals("the", StringComparison.OrdinalIgnoreCase) ||
                          word.Equals("and", StringComparison.OrdinalIgnoreCase) ||
                          word.Equals("in", StringComparison.OrdinalIgnoreCase) ||
                          word.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                          word.Equals("at", StringComparison.OrdinalIgnoreCase) ||
                          word.Equals("to", StringComparison.OrdinalIgnoreCase) ||
                          word.Equals("for", StringComparison.OrdinalIgnoreCase)))
                {
                    processedWord = word.ToLower();
                }
                // Detect Roman numerals (proper patterns only)
                else if (System.Text.RegularExpressions.Regex.IsMatch(word, @"^(I|II|III|IV|V|VI|VII|VIII|IX|X|XI|XII|XIII|XIV|XV|XVI|XVII|XVIII|XIX|XX)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    processedWord = word.ToUpper();
                }
                // Regular title case for other words
                else
                {
                    processedWord = char.ToUpper(word[0]) + (word.Length > 1 ? word.Substring(1).ToLower() : "");
                }

                processedWords.Add(processedWord);
            }

            return string.Join(" ", processedWords);
        }

        private static (string gameName, DateTime? extractedDate) ParseFileNameInfo(string fileName)
        {
            string gameName = fileName; // Fallback to original filename
            DateTime? extractedDate = null;

            // Remove common prefixes and suffixes
            string cleanName = fileName
                .Replace("Highlight_", "")
                .Replace("Clip_", "")
                .Replace("Replay_", "")
                .Replace("Desktop_", "")
                .Replace("DesktopRecording", "Desktop")
                .Replace("Recording_", "");

            // Date patterns to try
            var datePatterns = new[]
            {
                @"(\d{4}-\d{2}-\d{2})",           // YYYY-MM-DD
                @"(\d{2}-\d{2}-\d{4})",           // MM-DD-YYYY or DD-MM-YYYY
                @"(\d{4}\.\d{2}\.\d{2})",         // YYYY.MM.DD
                @"(\d{2}\.\d{2}\.\d{4})",         // MM.DD.YYYY or DD.MM.YYYY
                @"(\d{4}_\d{2}_\d{2})",           // YYYY_MM_DD
                @"(\d{2}_\d{2}_\d{4})"            // MM_DD_YYYY or DD_MM_YYYY
            };

            // Try to extract date
            foreach (string pattern in datePatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(cleanName, pattern);
                if (match.Success)
                {
                    string dateStr = match.Groups[1].Value;

                    // Try different date formats
                    string[] formats = {
                        "yyyy-MM-dd", "MM-dd-yyyy", "dd-MM-yyyy",
                        "yyyy.MM.dd", "MM.dd.yyyy", "dd.MM.yyyy",
                        "yyyy_MM_dd", "MM_dd_yyyy", "dd_MM_yyyy"
                    };

                    foreach (string format in formats)
                    {
                        if (DateTime.TryParseExact(dateStr, format, null, DateTimeStyles.None, out DateTime parsedDate))
                        {
                            extractedDate = parsedDate;
                            break;
                        }
                    }

                    if (extractedDate.HasValue)
                    {
                        // Remove the date part from the name for game detection
                        cleanName = cleanName.Replace(dateStr, "").Trim('_', '-', '.', ' ');
                        break;
                    }
                }
            }

            // Remove time patterns
            var timePatterns = new[]
            {
                @"_\d{2}-\d{2}-\d{2}",    // _HH-MM-SS
                @"_\d{2}\.\d{2}\.\d{2}",  // _HH.MM.SS
                @"_\d{2}:\d{2}:\d{2}",    // _HH:MM:SS
                @"\s\d{2}-\d{2}-\d{2}",   // HH-MM-SS
                @"\s\d{2}\.\d{2}\.\d{2}", // HH.MM.SS
                @"\s\d{2}:\d{2}:\d{2}"    // HH:MM:SS
            };

            foreach (string pattern in timePatterns)
            {
                cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, pattern, "");
            }

            // Clean up any remaining separators and common words
            cleanName = cleanName
                .Replace("_", " ")
                .Replace("-", " ")
                .Replace(".", " ")
                .Trim();

            // Remove common action words at the end
            var actionWords = new[] { "Ace", "Win", "Victory", "Goal", "Clutch", "Kill", "DoubleKill", "TripleKill",
                                    "BossFight", "Mission", "Match", "Explore", "Sabotage", "FullMatch", "Highlight",
                                    "Clip", "Replay", "Play", "Heist", "Recording", "Build", "SoloWin" };

            foreach (string action in actionWords)
            {
                if (cleanName.EndsWith(" " + action, StringComparison.OrdinalIgnoreCase))
                {
                    cleanName = cleanName.Substring(0, cleanName.Length - action.Length - 1).Trim();
                    break;
                }
            }

            // Smart game name parsing - use cleanName if meaningful, otherwise fallback to original fileName
            string nameToFormat = (!string.IsNullOrWhiteSpace(cleanName) &&
                                  !System.Text.RegularExpressions.Regex.IsMatch(cleanName, @"^[\d\s_\-\.]+$"))
                                  ? cleanName : fileName;
            gameName = SmartFormatGameName(nameToFormat);

            return (gameName, extractedDate);
        }

        private static async Task HandleImportFile(JsonElement parameters)
        {
            int importId = Guid.NewGuid().GetHashCode();

            try
            {
                // Extract sectionId and determine content type
                if (!parameters.TryGetProperty("sectionId", out JsonElement sectionIdElement))
                {
                    Log.Error("sectionId not found in ImportFile parameters");
                    ShowModal("Import Error", "Missing section ID parameter", "error");
                    return;
                }

                string sectionId = sectionIdElement.GetString()!;
                Content.ContentType contentType;

                switch (sectionId)
                {
                    case "sessions":
                        contentType = Content.ContentType.Session;
                        break;
                    case "replayBuffer":
                        contentType = Content.ContentType.Buffer;
                        break;
                    default:
                        Log.Error($"Invalid sectionId: {sectionId}");
                        ShowModal("Import Error", $"Invalid section ID: {sectionId}", "error");
                        return;
                }

                // Open file dialog
                using var openFileDialog = new OpenFileDialog
                {
                    Filter = "MP4 Video Files (*.mp4)|*.mp4",
                    Title = "Import MP4 Video Files",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = true
                };

                if (openFileDialog.ShowDialog() != DialogResult.OK)
                {
                    Log.Information("Import cancelled by user");
                    return;
                }

                string[] selectedFiles = openFileDialog.FileNames;
                if (selectedFiles.Length == 0)
                {
                    Log.Information("No files selected for import");
                    return;
                }

                Log.Information($"Starting import of {selectedFiles.Length} file(s) to {contentType}");

                // Create target directory
                string contentFolder = Settings.Instance.ContentFolder;
                string targetFolder = Path.Combine(contentFolder, contentType.ToString().ToLower() + "s").Replace("\\", "/");
                Directory.CreateDirectory(targetFolder);

                int importedCount = 0;
                int failedCount = 0;

                for (int i = 0; i < selectedFiles.Length; i++)
                {
                    string sourceFile = selectedFiles[i];
                    string originalFileName = Path.GetFileNameWithoutExtension(sourceFile);
                    string fileExtension = Path.GetExtension(sourceFile);

                    // Validate file extension is MP4
                    if (!fileExtension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                    {
                        failedCount++;
                        Log.Error($"Skipping {originalFileName}: Only MP4 files are allowed");
                        continue;
                    }

                    try
                    {
                        // Send initial progress for current file
                        double progressPercent = (double)i / selectedFiles.Length * 100;
                        try
                        {
                            await SendFrontendMessage("ImportProgress", new
                            {
                                id = importId,
                                progress = progressPercent,
                                fileName = originalFileName,
                                totalFiles = selectedFiles.Length,
                                currentFileIndex = i + 1,
                                status = "importing"
                            });
                        }
                        catch (Exception msgEx)
                        {
                            Log.Warning($"Failed to send import progress message: {msgEx.Message}");
                        }

                        // Generate unique filename with timestamp
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string targetFileName = $"{timestamp}_{originalFileName}{fileExtension}";
                        string targetFilePath = Path.Combine(targetFolder, targetFileName).Replace("\\", "/");

                        // Ensure unique filename if file already exists
                        int counter = 1;
                        while (File.Exists(targetFilePath))
                        {
                            targetFileName = $"{timestamp}_{originalFileName}_{counter}{fileExtension}";
                            targetFilePath = Path.Combine(targetFolder, targetFileName).Replace("\\", "/");
                            counter++;
                        }

                        Log.Information($"Importing {originalFileName} to {targetFilePath}");

                        // Copy the video file to target location
                        File.Copy(sourceFile, targetFilePath);

                        // Send progress after file copy
                        try
                        {
                            await SendFrontendMessage("ImportProgress", new
                            {
                                id = importId,
                                progress = progressPercent + (25.0 / selectedFiles.Length),
                                fileName = originalFileName,
                                totalFiles = selectedFiles.Length,
                                currentFileIndex = i + 1,
                                status = "importing"
                            });
                        }
                        catch (Exception msgEx)
                        {
                            Log.Warning($"Failed to send import progress message: {msgEx.Message}");
                        }

                        // Parse game name and date from filename
                        var (detectedGame, detectedDate) = ParseFileNameInfo(originalFileName);

                        // Log detected information
                        Log.Information($"Detected game: '{detectedGame}' from filename: '{originalFileName}'");
                        if (detectedDate.HasValue)
                        {
                            Log.Information($"Detected date: {detectedDate.Value:yyyy-MM-dd} from filename: '{originalFileName}'");
                        }

                        // Create metadata file with detected game name and date
                        ContentUtils.CreateMetadataFile(targetFilePath, contentType, detectedGame, null, detectedDate);

                        // Send progress after metadata creation
                        try
                        {
                            await SendFrontendMessage("ImportProgress", new
                            {
                                id = importId,
                                progress = progressPercent + (50.0 / selectedFiles.Length),
                                fileName = originalFileName,
                                totalFiles = selectedFiles.Length,
                                currentFileIndex = i + 1,
                                status = "importing"
                            });
                        }
                        catch (Exception msgEx)
                        {
                            Log.Warning($"Failed to send import progress message: {msgEx.Message}");
                        }

                        // Create thumbnail image
                        ContentUtils.CreateThumbnail(targetFilePath, contentType);

                        // Send progress after thumbnail creation
                        try
                        {
                            await SendFrontendMessage("ImportProgress", new
                            {
                                id = importId,
                                progress = progressPercent + (75.0 / selectedFiles.Length),
                                fileName = originalFileName,
                                totalFiles = selectedFiles.Length,
                                currentFileIndex = i + 1,
                                status = "importing"
                            });
                        }
                        catch (Exception msgEx)
                        {
                            Log.Warning($"Failed to send import progress message: {msgEx.Message}");
                        }

                        // Create waveform data asynchronously
                        _ = Task.Run(() => ContentUtils.CreateWaveformFile(targetFilePath, contentType));

                        importedCount++;
                        Log.Information($"Successfully imported {originalFileName}");
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        Log.Error($"Failed to import {originalFileName}: {ex.Message}");

                        // Send error progress update
                        double progressPercent = (double)i / selectedFiles.Length * 100;
                        try
                        {
                            await SendFrontendMessage("ImportProgress", new
                            {
                                id = importId,
                                progress = progressPercent + (100.0 / selectedFiles.Length),
                                fileName = originalFileName,
                                totalFiles = selectedFiles.Length,
                                currentFileIndex = i + 1,
                                status = "error",
                                message = $"Failed to import: {ex.Message}"
                            });
                        }
                        catch (Exception msgEx)
                        {
                            Log.Warning($"Failed to send import error progress message: {msgEx.Message}");
                        }
                    }
                }

                // Send final progress update
                try
                {
                    await SendFrontendMessage("ImportProgress", new
                    {
                        id = importId,
                        progress = 100,
                        fileName = importedCount > 0 ? "Finished" : "Failed",
                        totalFiles = selectedFiles.Length,
                        currentFileIndex = selectedFiles.Length,
                        status = importedCount > 0 ? "done" : "error",
                        message = $"Completed: {importedCount} successful, {failedCount} failed"
                    });
                }
                catch (Exception msgEx)
                {
                    Log.Warning($"Failed to send final import progress message: {msgEx.Message}");
                }

                // Reload content list to include newly imported files
                SettingsUtils.LoadContentFromFolderIntoState();

                // No need for completion modal since progress cards show completion status
                Log.Information($"Import process completed: {importedCount} successful, {failedCount} failed");
            }
            catch (Exception ex)
            {
                Log.Error($"Error during import process: {ex.Message}");

                // Send error progress update for the entire import process
                try
                {
                    await SendFrontendMessage("ImportProgress", new
                    {
                        id = importId,
                        progress = 0,
                        fileName = "Import Failed",
                        totalFiles = 0,
                        currentFileIndex = 0,
                        status = "error",
                        message = $"Import process failed: {ex.Message}"
                    });
                }
                catch (Exception msgEx)
                {
                    Log.Warning($"Failed to send import error message: {msgEx.Message}");
                }

                ShowModal("Import Error", $"An error occurred during import: {ex.Message}", "error");
            }
        }

        private static async Task HandleUploadContent(JsonElement message)
        {
            try
            {
                string filePath = message.GetProperty("FilePath").GetString()!;
                string fileName = Path.GetFileName(filePath);
                string title = message.GetProperty("Title").GetString()!;

                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                using var httpClient = new HttpClient();
                using var formData = new MultipartFormDataContent();

                void ProgressHandler(long sent, long total)
                {
                    int progress = (int)((sent / (double)total) * 100);
                    _ = SendFrontendMessage("UploadProgress", new
                    {
                        title,
                        fileName,
                        progress,
                        status = "uploading",
                        message = $"Uploading... {progress}%"
                    });
                }

                var fileContent = new ProgressableStreamContent(fileBytes, "application/octet-stream", ProgressHandler);
                formData.Add(fileContent, "file", fileName);

                AddOptionalContent(formData, message, "Game");
                AddOptionalContent(formData, message, "Title");
                AddOptionalContent(formData, message, "Description");

                await SendFrontendMessage("UploadProgress", new
                {
                    title,
                    fileName,
                    progress = 0,
                    status = "uploading",
                    message = "Starting upload..."
                });

                var request = new HttpRequestMessage(HttpMethod.Post, "https://processing.segra.tv/upload")
                {
                    Content = formData
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AuthService.GetJwtAsync());

                var response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                await SendFrontendMessage("UploadProgress", new
                {
                    title,
                    fileName,
                    progress = 100,
                    status = "done",
                    message = "Upload completed successfully"
                });

                var responseContent = await response.Content.ReadAsStringAsync();
                Log.Information($"Upload success: {responseContent}");

                // Parse the response to extract the URL and update the content with uploadId
                if (!string.IsNullOrEmpty(responseContent))
                {
                    try
                    {
                        var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                        if (responseJson.TryGetProperty("success", out var successElement) &&
                            successElement.GetBoolean() &&
                            responseJson.TryGetProperty("url", out var urlElement))
                        {
                            string url = urlElement.GetString()!;
                            if (!string.IsNullOrEmpty(url))
                            {
                                // Extract uploadId from the URL (after the last slash)
                                string uploadId = url.Split('/').Last();
                                Log.Information($"Extracted upload ID: {uploadId}");

                                // Update the content with the uploadId
                                var contentList = Settings.Instance.State.Content.ToList();
                                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                                Log.Information($"File name: {fileName}, without extension: {fileNameWithoutExtension}");

                                var contentToUpdate = contentList.FirstOrDefault(c =>
                                    Path.GetFileNameWithoutExtension(c.FileName) == fileNameWithoutExtension);
                                Log.Information($"Content to update: {contentToUpdate?.FileName ?? "not found"}");

                                if (contentToUpdate != null)
                                {
                                    contentToUpdate.UploadId = uploadId;

                                    // Also update the metadata file
                                    string contentTypeStr = contentToUpdate.Type.ToString().ToLower() + "s";
                                    string metadataFolderPath = Path.Combine(Settings.Instance.ContentFolder, ".metadata", contentTypeStr);
                                    string metadataFilePath = Path.Combine(metadataFolderPath, $"{fileNameWithoutExtension}.json");

                                    if (File.Exists(metadataFilePath))
                                    {
                                        try
                                        {
                                            string metadataJson = File.ReadAllText(metadataFilePath);
                                            var content = JsonSerializer.Deserialize<Content>(metadataJson);
                                            if (content != null)
                                            {
                                                content.UploadId = uploadId;
                                                string updatedMetadataJson = JsonSerializer.Serialize(content, new JsonSerializerOptions
                                                {
                                                    WriteIndented = true
                                                });
                                                File.WriteAllText(metadataFilePath, updatedMetadataJson);
                                                Log.Information($"Updated metadata file with upload ID: {metadataFilePath}");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Error($"Error updating metadata file: {ex.Message}");
                                        }
                                    }

                                    Log.Information($"Updated content with upload ID: {uploadId}");
                                    SettingsUtils.LoadContentFromFolderIntoState(true);
                                }

                                // Open browser if setting is enabled
                                if (Settings.Instance.ClipShowInBrowserAfterUpload)
                                {
                                    Log.Information($"Opening URL in browser: {url}");
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = url,
                                        UseShellExecute = true
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to parse upload response or update content: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Upload failed: {ex.Message}");
                string errorFileName = message.GetProperty("FilePath").GetString()!;
                string errorTitle = message.GetProperty("Title").GetString()!;

                await ShowModal(
                    "Upload Error",
                    "The upload failed.\n" + ex.Message,
                    "error",
                    "Could not upload clip"
                );

                await SendFrontendMessage("UploadProgress", new
                {
                    title = errorTitle,
                    fileName = Path.GetFileName(errorFileName),
                    progress = 0,
                    status = "error",
                    message = ex.Message
                });
            }
        }

        private static void AddOptionalContent(MultipartFormDataContent formData, JsonElement message, string field)
        {
            if (message.TryGetProperty(field, out JsonElement element))
            {
                formData.Add(new StringContent(element.GetString()!), field.ToLower());
            }
        }

        private static async Task HandleDeleteContent(JsonElement message)
        {
            Log.Information($"Handling DeleteContent with message: {message}");

            // Extract FileName and ContentType
            if (message.TryGetProperty("FileName", out JsonElement fileNameElement) &&
                message.TryGetProperty("ContentType", out JsonElement contentTypeElement))
            {
                string fileName = fileNameElement.GetString()!;
                string contentTypeStr = contentTypeElement.GetString()!;

                if (Enum.TryParse(contentTypeStr, true, out Content.ContentType contentType))
                {
                    // Construct the full file path based on the file name and content type
                    string videoFolder = Settings.Instance.ContentFolder;
                    string contentTypeFolder = Path.Combine(videoFolder, contentType.ToString().ToLower() + "s");
                    string filePath = Path.Combine(contentTypeFolder, $"{fileName}.mp4"); // Assuming .mp4 extension

                    // Invoke the deletion asynchronously
                    await Task.Run(() => ContentUtils.DeleteContent(filePath, contentType));
                }
                else
                {
                    Log.Error($"Invalid ContentType provided: {contentTypeStr}");
                }
            }
            else
            {
                Log.Information("FileName or ContentType property not found in DeleteContent message.");
            }
        }

        private static async Task HandleAddBookmark(JsonElement message)
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
                    Content.ContentType contentType;
                    if (!Enum.TryParse<Content.ContentType>(contentTypeStr, out contentType))
                    {
                        Log.Error($"Invalid content type: {contentTypeStr}");
                        return;
                    }

                    // Get metadata file path
                    string contentFileName = Path.GetFileNameWithoutExtension(filePath);
                    string metadataFolderPath = Path.Combine(Settings.Instance.ContentFolder, ".metadata", contentType.ToString().ToLower() + "s");
                    string metadataFilePath = Path.Combine(metadataFolderPath, $"{contentFileName}.json");

                    if (!File.Exists(metadataFilePath))
                    {
                        Log.Error($"Metadata file not found: {metadataFilePath}");
                        return;
                    }

                    // Read existing metadata
                    string metadataJson = await File.ReadAllTextAsync(metadataFilePath);
                    var content = JsonSerializer.Deserialize<Content>(metadataJson);

                    if (content == null)
                    {
                        Log.Error($"Failed to deserialize metadata: {metadataFilePath}");
                        return;
                    }

                    // Create a new bookmark
                    var bookmark = new Bookmark
                    {
                        Id = bookmarkId,
                        Type = bookmarkType,
                        Time = TimeSpan.Parse(timeString)
                    };

                    // Add the bookmark to the content
                    if (content.Bookmarks == null)
                    {
                        content.Bookmarks = new List<Bookmark>();
                    }
                    content.Bookmarks.Add(bookmark);

                    // Save the updated metadata
                    string updatedMetadataJson = JsonSerializer.Serialize(content, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    await File.WriteAllTextAsync(metadataFilePath, updatedMetadataJson);

                    // Update the bookmark in the in-memory content collection
                    var contentItem = Settings.Instance?.State.Content.FirstOrDefault(c =>
                        c.FilePath == filePath &&
                        c.Type.ToString() == contentTypeStr);

                    if (contentItem == null)
                    {
                        Log.Error($"Content item not found for {filePath} and {contentTypeStr}");
                        return;
                    }

                    contentItem.Bookmarks.Add(bookmark);

                    await SendSettingsToFrontend("Added bookmark");
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

        private static async Task HandleDeleteBookmark(JsonElement message)
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
                    Content.ContentType contentType;
                    if (!Enum.TryParse<Content.ContentType>(contentTypeStr, out contentType))
                    {
                        Log.Error($"Invalid content type: {contentTypeStr}");
                        return;
                    }

                    // Get metadata file path
                    string contentFileName = Path.GetFileNameWithoutExtension(filePath);
                    string metadataFolderPath = Path.Combine(Settings.Instance.ContentFolder, ".metadata", contentType.ToString().ToLower() + "s");
                    string metadataFilePath = Path.Combine(metadataFolderPath, $"{contentFileName}.json");

                    if (!File.Exists(metadataFilePath))
                    {
                        Log.Error($"Metadata file not found: {metadataFilePath}");
                        return;
                    }

                    // Read existing metadata
                    string metadataJson = await File.ReadAllTextAsync(metadataFilePath);
                    var content = JsonSerializer.Deserialize<Content>(metadataJson);

                    if (content == null)
                    {
                        Log.Error($"Failed to deserialize metadata: {metadataFilePath}");
                        return;
                    }

                    // Remove the bookmark from the content
                    if (content.Bookmarks != null)
                    {
                        content.Bookmarks = content.Bookmarks.Where(b => b.Id != bookmarkId).ToList();
                    }

                    // Save the updated metadata
                    string updatedMetadataJson = JsonSerializer.Serialize(content, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    await File.WriteAllTextAsync(metadataFilePath, updatedMetadataJson);

                    // Update the bookmark in the in-memory content collection
                    var contentItem = Settings.Instance?.State.Content.FirstOrDefault(c =>
                        c.FilePath == filePath &&
                        c.Type.ToString() == contentTypeStr);

                    if (contentItem != null && contentItem.Bookmarks != null)
                    {
                        contentItem.Bookmarks = contentItem.Bookmarks.Where(b => b.Id != bookmarkId).ToList();
                    }

                    await SendSettingsToFrontend("Deleted bookmark");
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

        private static Task SetVideoLocationAsync()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                // Set an initial description or instruction for the dialog
                fbd.Description = "Select a folder to set as the video location.";

                // Optionally, set the root folder for the dialog (e.g., My Computer or Desktop)
                fbd.RootFolder = Environment.SpecialFolder.Desktop;

                // Show the dialog and check if the user selected a folder
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    // Get the selected folder path
                    string selectedPath = fbd.SelectedPath;
                    Log.Information($"Selected Folder: {selectedPath}");

                    // Update settings with the selected folder path
                    Settings.Instance.ContentFolder = selectedPath;
                }
                else
                {
                    Log.Information("Folder selection was canceled.");
                }
            }

            return Task.CompletedTask;
        }

        public static async Task StartWebsocket()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5000/");
            listener.Start();
            Log.Information("WebSocket server started at ws://localhost:5000/");

            try
            {
                while (true)
                {
                    HttpListenerContext context = await listener.GetContextAsync();

                    if (context.Request.IsWebSocketRequest)
                    {
                        Log.Information("Received WebSocket connection request");

                        // Close the current WebSocket if already active
                        if (activeWebSocket != null && activeWebSocket.State == WebSocketState.Open)
                        {
                            await activeWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "New connection", CancellationToken.None);
                            Log.Information("Closed previous WebSocket connection.");
                        }

                        HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(null);
                        activeWebSocket = wsContext.WebSocket;

                        Log.Information("WebSocket connection established");
                        await HandleWebSocketAsync(activeWebSocket);
                    }
                    else
                    {
                        Log.Information("Invalid request: Not a WebSocket request");
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"Exception in StartWebsocket: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    Log.Information(ex.StackTrace);
                }
            }
        }

        private static async Task HandleWebSocketAsync(WebSocket webSocket)
        {
            byte[] buffer = new byte[4096];
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Log.Information("Client initiated WebSocket closure.");
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client initiated closure", CancellationToken.None);
                    }
                    else
                    {
                        string receivedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Log.Information($"Received message: {receivedMessage}");
                        await HandleMessage(receivedMessage);
                    }
                }
            }
            catch (WebSocketException wsEx)
            {
                Log.Information($"WebSocketException in HandleWebSocketAsync: {wsEx.Message}");
                Log.Information($"WebSocket state at exception: {webSocket.State}");
                if (wsEx.InnerException != null)
                {
                    Log.Information($"Inner exception: {wsEx.InnerException.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Information($"General exception in HandleWebSocketAsync: {ex.Message}");
            }
            finally
            {
                if (webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Server-side error", CancellationToken.None);
                }
                Log.Information("WebSocket connection closed.");
            }
        }

        public static async Task SendFrontendMessage(string method, object content)
        {
            await sendLock.WaitAsync();
            try
            {
                // Wait for up to 10 seconds for the websocket to be open
                int maxWaitTimeMs = 10000;
                int waitIntervalMs = 100;
                int elapsedTime = 0;

                while ((activeWebSocket == null || activeWebSocket.State != WebSocketState.Open)
                    && elapsedTime < maxWaitTimeMs)
                {
                    await Task.Delay(waitIntervalMs);
                    elapsedTime += waitIntervalMs;
                }

                if (activeWebSocket?.State == WebSocketState.Open)
                {
                    var message = new { method, content };
                    byte[] buffer = JsonSerializer.SerializeToUtf8Bytes(message, jsonOptions);
                    await activeWebSocket.SendAsync(
                        buffer,
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken: CancellationToken.None
                    );
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending message: {ex.Message}");
            }
            finally
            {
                sendLock.Release();
            }
        }

        public static async Task ShowModal(string title, string description, string type = "info", string? subtitle = null)
        {
            // Validate the modal type
            if (type != "info" && type != "warning" && type != "error")
            {
                Log.Warning($"Invalid modal type '{type}'. Defaulting to 'info'.");
                type = "info";
            }

            var modalContent = new
            {
                title,
                subtitle,
                description,
                type
            };

            await SendFrontendMessage("ShowModal", modalContent);
            Log.Information($"Sent modal to frontend: {title} ({type})");
        }

        public static async Task SendSettingsToFrontend(string cause)
        {
            if (!Program.hasLoadedInitialSettings || Settings.Instance._isBulkUpdating)
                return;

            Log.Information("Sending settings to frontend ({Cause})", cause);
            await SendFrontendMessage("Settings", Settings.Instance);
        }

        private static async Task HandleAddToWhitelist(JsonElement parameters)
        {
            try
            {
                if (parameters.TryGetProperty("game", out JsonElement gameElement))
                {
                    var game = JsonSerializer.Deserialize<Game>(gameElement.GetRawText());
                    if (game != null && !string.IsNullOrEmpty(game.Name) && !string.IsNullOrEmpty(game.Path))
                    {
                        var comparer = new GameEqualityComparer();
                        bool exists = Settings.Instance.Whitelist.Any(g => comparer.Equals(g, game));

                        if (!exists)
                        {
                            var whitelist = new List<Game>(Settings.Instance.Whitelist);
                            whitelist.Add(game);
                            Settings.Instance.Whitelist = whitelist;
                            Log.Information($"Added game {game.Name} to whitelist");
                        }
                        else
                        {
                            Log.Information($"Game {game.Name} already exists in whitelist");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error adding to whitelist: {ex.Message}");
                await ShowModal("Error", $"Failed to add game to whitelist: {ex.Message}", "error");
            }
        }

        private static async Task HandleRemoveFromWhitelist(JsonElement parameters)
        {
            try
            {
                if (parameters.TryGetProperty("game", out JsonElement gameElement))
                {
                    var game = JsonSerializer.Deserialize<Game>(gameElement.GetRawText());
                    if (game != null && !string.IsNullOrEmpty(game.Name) && !string.IsNullOrEmpty(game.Path))
                    {
                        var comparer = new GameEqualityComparer();
                        var existingGame = Settings.Instance.Whitelist.FirstOrDefault(g => comparer.Equals(g, game));

                        if (existingGame != null)
                        {
                            var whitelist = new List<Game>(Settings.Instance.Whitelist);
                            whitelist.Remove(existingGame);
                            Settings.Instance.Whitelist = whitelist;
                            Log.Information($"Removed game {game.Name} from whitelist");
                        }
                        else
                        {
                            Log.Information($"Game {game.Name} does not exist in whitelist");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing from whitelist: {ex.Message}");
                await ShowModal("Error", $"Failed to remove game from whitelist: {ex.Message}", "error");
            }
        }

        private static async Task HandleAddToBlacklist(JsonElement parameters)
        {
            try
            {
                if (parameters.TryGetProperty("game", out JsonElement gameElement))
                {
                    var game = JsonSerializer.Deserialize<Game>(gameElement.GetRawText());
                    if (game != null && !string.IsNullOrEmpty(game.Name) && !string.IsNullOrEmpty(game.Path))
                    {
                        var comparer = new GameEqualityComparer();
                        bool exists = Settings.Instance.Blacklist.Any(g => comparer.Equals(g, game));

                        if (!exists)
                        {
                            var blacklist = new List<Game>(Settings.Instance.Blacklist);
                            blacklist.Add(game);
                            Settings.Instance.Blacklist = blacklist;
                            Log.Information($"Added game {game.Name} to blacklist");
                        }
                        else
                        {
                            Log.Information($"Game {game.Name} already exists in blacklist");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error adding to blacklist: {ex.Message}");
                await ShowModal("Error", $"Failed to add game to blacklist: {ex.Message}", "error");
            }
        }

        private static async Task HandleRemoveFromBlacklist(JsonElement parameters)
        {
            try
            {
                if (parameters.TryGetProperty("game", out JsonElement gameElement))
                {
                    var game = JsonSerializer.Deserialize<Game>(gameElement.GetRawText());
                    if (game != null && !string.IsNullOrEmpty(game.Name) && !string.IsNullOrEmpty(game.Path))
                    {
                        var comparer = new GameEqualityComparer();
                        var existingGame = Settings.Instance.Blacklist.FirstOrDefault(g => comparer.Equals(g, game));

                        if (existingGame != null)
                        {
                            var blacklist = new List<Game>(Settings.Instance.Blacklist);
                            blacklist.Remove(existingGame);
                            Settings.Instance.Blacklist = blacklist;
                            Log.Information($"Removed game {game.Name} from blacklist");
                        }
                        else
                        {
                            Log.Information($"Game {game.Name} does not exist in blacklist");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing from blacklist: {ex.Message}");
                await ShowModal("Error", $"Failed to remove game from blacklist: {ex.Message}", "error");
            }
        }

        private static async Task HandleSelectGameExecutable()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Executable Files (*.exe)|*.exe",
                    Title = "Select Game Executable",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;
                    string fileName = Path.GetFileNameWithoutExtension(filePath);

                    // Create a game object with lowercase property names to match frontend expectations
                    var gameObject = new
                    {
                        name = fileName,
                        path = filePath
                    };

                    // Send the selected game back to the frontend
                    await SendFrontendMessage("SelectedGameExecutable", gameObject);
                    Log.Information($"Selected game executable: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error selecting game executable: {ex.Message}");
                await ShowModal("Error", $"Failed to select game executable: {ex.Message}", "error");
            }
        }

        private static readonly string[] SensitiveProperties =
        [
            "accesstoken",
            "refreshtoken",
            "jwt"
        ];

        private static string RedactSensitiveInfo(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            foreach (var prop in SensitiveProperties)
            {
                var pattern = $"\"{prop}\":\"([^\"]+)\"";
                message = Regex.Replace(message, pattern, $"\"{prop}\":\"-REDACTED-\"", RegexOptions.IgnoreCase);
            }

            return message;
        }
    }
}
