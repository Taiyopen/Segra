using System.Diagnostics;
using Serilog;
using System.Globalization;
using System.Text.RegularExpressions;
using Segra.Backend.Models;

namespace Segra.Backend.Utils
{
    public static class ClipUtils
    {
        public static async Task CreateAiClipFromBookmarks(List<Bookmark> bookmarks, AiProgressMessage aiProgressMessage)
        {
            if (bookmarks == null || !bookmarks.Any())
            {
                Log.Information("No bookmarks provided to create clips.");
                return;
            }

            // Convert bookmarks to initial selections with buffer times
            List<Selection> initialSelections = new List<Selection>();

            foreach (var bookmark in bookmarks)
            {
                // Calculate start and end times (5 seconds before and after the bookmark time)
                // TODO (os): add so the AI calculates the added time before and after
                double startTime = Math.Max(0, bookmark.Time.TotalSeconds - 5); // Ensure not negative
                double endTime = bookmark.Time.TotalSeconds + 5;

                initialSelections.Add(new Selection
                {
                    Type = aiProgressMessage.Content.Type.ToString(),
                    StartTime = startTime,
                    EndTime = endTime,
                    FileName = aiProgressMessage.Content.FileName,
                    Game = aiProgressMessage.Content.Game
                });
            }

            // Merge overlapping selections
            List<Selection> mergedSelections = MergeOverlappingSelections(initialSelections);
            
            Log.Information($"Merged {initialSelections.Count} bookmarks into {mergedSelections.Count} clip sections");

            await CreateClips(mergedSelections, false, aiProgressMessage);
        }

        private static List<Selection> MergeOverlappingSelections(List<Selection> selections)
        {
            // Sort selections by start time
            var sortedSelections = selections.OrderBy(s => s.StartTime).ToList();
            List<Selection> mergedSelections = new List<Selection>();
            
            // Start with the first selection
            Selection current = sortedSelections[0];
            
            // Iterate through the sorted selections
            for (int i = 1; i < sortedSelections.Count; i++)
            {
                var next = sortedSelections[i];
                
                // Check if the current selection overlaps with the next one
                if (current.EndTime >= next.StartTime)
                {
                    // Merge by extending the end time if needed
                    current.EndTime = Math.Max(current.EndTime, next.EndTime);
                }
                else
                {
                    // No overlap, add current to result and move to next
                    mergedSelections.Add(current);
                    current = next;
                }
            }
            
            // Add the last merged selection
            mergedSelections.Add(current);
            
            return mergedSelections;
        }

        public static async Task CreateClips(List<Selection> selections, bool updateFrontend = true, AiProgressMessage? aiProgressMessage = null)
        {
            int id = Guid.NewGuid().GetHashCode();
            if (updateFrontend)
            {
                await MessageUtils.SendFrontendMessage("ClipProgress", new { id, progress = 0 });
            }
            string videoFolder = Settings.Instance.ContentFolder;

            if (selections == null || !selections.Any())
            {
                Log.Information("No selections provided.");
                return;
            }

            double totalDuration = selections.Sum(s => s.EndTime - s.StartTime);
            if (totalDuration <= 0)
            {
                Log.Information("Total clip duration is zero or negative.");
                return;
            }

            string outputFolder = Path.Combine(videoFolder, aiProgressMessage != null ? "highlights" : "clips");
            Directory.CreateDirectory(outputFolder);

            List<string> tempClipFiles = new List<string>();
            string ffmpegPath = "ffmpeg.exe";

            if (!File.Exists(ffmpegPath))
            {
                Log.Information($"FFmpeg executable not found at path: {ffmpegPath}");
                return;
            }

            double processedDuration = 0;
            foreach (var selection in selections)
            {
                string inputFilePath = Path.Combine(videoFolder, selection.Type.ToLower() + "s", $"{selection.FileName}.mp4").Replace("\\", "/");
                if (!File.Exists(inputFilePath))
                {
                    Log.Information($"Input video file not found: {inputFilePath}");
                    continue;
                }

                string tempFileName = Path.Combine(Path.GetTempPath(), $"clip{Guid.NewGuid()}.mp4");
                double clipDuration = selection.EndTime - selection.StartTime;

                await ExtractClip(inputFilePath, tempFileName, selection.StartTime, selection.EndTime, ffmpegPath, progress =>
                {
                    double currentProgress = (processedDuration + (progress * clipDuration)) / totalDuration * 100;
                    if (updateFrontend)
                    {
                        MessageUtils.SendFrontendMessage("ClipProgress", new { id, progress = currentProgress });
                    }

                    if (aiProgressMessage != null && !string.IsNullOrEmpty(aiProgressMessage.Id))
                    {
                        // Update from 80% to 98%
                        double fraction = (processedDuration + (progress * clipDuration)) / totalDuration;
                        double aiProgress = 80 + fraction * 18;
                        if (aiProgress > 98) aiProgress = 98;

                        aiProgressMessage.Progress = (int)Math.Floor(aiProgress);
                        aiProgressMessage.Message = $"Rendering clips...";
                        MessageUtils.SendFrontendMessage("AiProgress", aiProgressMessage);
                    }
                });

                processedDuration += clipDuration;
                tempClipFiles.Add(tempFileName);
                if (updateFrontend)
                {
                    await MessageUtils.SendFrontendMessage("ClipProgress", new { id, progress = (processedDuration / totalDuration) * 100 });
                }
            }

            if (!tempClipFiles.Any())
            {
                Log.Information("No valid clips were extracted.");
                return;
            }

            // Concatenation phase (progress completes to 100% when done)
            string concatFilePath = Path.Combine(Path.GetTempPath(), $"concat_list_{Guid.NewGuid()}.txt");
            await File.WriteAllLinesAsync(concatFilePath, tempClipFiles.Select(f => $"file '{f.Replace("\\", "\\\\").Replace("'", "\\'")}"));

            string outputFileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";
            string outputFilePath = Path.Combine(outputFolder, outputFileName).Replace("\\", "/");

            if (aiProgressMessage != null)
            {
                aiProgressMessage.Progress = 99;
                aiProgressMessage.Message = "Rendering final clip...";
                MessageUtils.SendFrontendMessage("AiProgress", aiProgressMessage);
            }

            await RunFFmpegProcess(ffmpegPath,
                $"-y -f concat -safe 0 -i \"{concatFilePath}\" -c copy -movflags +faststart \"{outputFilePath}\"",
                totalDuration,
                progress =>
                {
                    if (updateFrontend)
                    {
                        MessageUtils.SendFrontendMessage("ClipProgress", new { id, progress = 100 });
                    }
                }
                );

            // Cleanup
            tempClipFiles.ForEach(f => SafeDelete(f));
            SafeDelete(concatFilePath);

            // Finalization
            ContentUtils.CreateMetadataFile(outputFilePath, aiProgressMessage != null ? Content.ContentType.Highlight : Content.ContentType.Clip, selections.FirstOrDefault()?.Game);
            ContentUtils.CreateThumbnail(outputFilePath, aiProgressMessage != null ? Content.ContentType.Highlight : Content.ContentType.Clip);
            SettingsUtils.LoadContentFromFolderIntoState();
            if (updateFrontend)
            {
                await MessageUtils.SendFrontendMessage("ClipProgress", new { id, progress = 100 });
            }
        }

        public static async Task<string> CreateAiClipToAnalyzeFromBookmark(Bookmark bookmark, Content content)
        {
            // Calculate start and end times (10 seconds before and 10 seconds after the bookmark time) for the Ai to analyze
            Log.Information("Creating AI clip to analyze for " + bookmark);
            double startTime = Math.Max(0, bookmark.Time.TotalSeconds - 10); // Ensure not negative
            double endTime = bookmark.Time.TotalSeconds + 10; // TODO (os): what happens if this is longer than the clip if it's right at the end?

            string videoFolder = Settings.Instance.ContentFolder;

            // Create .ai directory if it doesn't exist
            string aiOutputFolder = Path.Combine(videoFolder, ".ai").Replace("\\", "/");
            Directory.CreateDirectory(aiOutputFolder);

            string inputFilePath = Path.Combine(videoFolder, content.Type.ToString().ToLower() + "s", $"{content.FileName}.mp4").Replace("\\", "/");
            if (!File.Exists(inputFilePath))
            {
                Log.Error($"Input video file not found: {inputFilePath}");
                return null;
            }

            string outputFileName = $"{content.FileName}_{bookmark.Type}_{bookmark.Id}.mp4";
            string outputFilePath = Path.Combine(aiOutputFolder, outputFileName).Replace("\\", "/");

            string ffmpegPath = "ffmpeg.exe";
            if (!File.Exists(ffmpegPath))
            {
                Log.Error($"FFmpeg executable not found at path: {ffmpegPath}");
                return null;
            }

            double clipDuration = endTime - startTime;

            // 1) Extract WITHOUT re-encoding to a temp file (fastest way)
            string tempFilePath = Path.GetTempFileName().Replace(".tmp", ".mp4");
            string copyArguments =
                $"-y -ss {startTime.ToString(CultureInfo.InvariantCulture)} " +
                $"-t {clipDuration.ToString(CultureInfo.InvariantCulture)} " +
                $"-i \"{inputFilePath}\" " +
                $"-c copy -movflags +faststart \"{tempFilePath}\"";

            await RunFFmpegProcessSimple(ffmpegPath, copyArguments);

            var fileInfo = new FileInfo(tempFilePath);
            const long oneGB = 1L << 30; // 1GB in bytes

            // The file limit is 1GB, so if it's larger, we need to compress it (this takes longer time)
            if (fileInfo.Length > oneGB)
            {
                Log.Information($"Temp file is larger than 1GB ({fileInfo.Length} bytes). Re-encoding...");

                // Now re-encode from the temp file to the final output path
                string reencodeArguments =
                    $"-y -i \"{tempFilePath}\" " +
                    $"-c:v libx264 -preset ultrafast -crf 23 -c:a aac -b:a 192k -movflags +faststart \"{outputFilePath}\"";

                await RunFFmpegProcessSimple(ffmpegPath, reencodeArguments);

                // After re-encode is done, we no longer need the temp file and the compressed file is already at the final output path
                SafeDelete(tempFilePath);
            }
            else
            {
                // If the file is <= 1GB, just move it to the final output path
                SafeDelete(outputFilePath);
                File.Move(tempFilePath, outputFilePath);
            }

            // Return the generated filepath so it can be tracked
            return outputFilePath;
        }

        private static async Task ExtractClip(string inputFilePath, string outputFilePath, double startTime, double endTime,
                                            string ffmpegPath, Action<double> progressCallback)
        {
            double duration = endTime - startTime;
            string arguments = $"-y -ss {startTime.ToString(CultureInfo.InvariantCulture)} -t {duration.ToString(CultureInfo.InvariantCulture)} " +
                             $"-i \"{inputFilePath}\" -c:v libx264 -preset veryfast -crf 23 -c:a aac -b:a 192k -movflags +faststart \"{outputFilePath}\"";

            await RunFFmpegProcess(ffmpegPath, arguments, duration, progressCallback);
        }

        private static async Task RunFFmpegProcess(string ffmpegPath, string arguments, double? totalDuration, Action<double> progressCallback)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    try
                    {
                        // Only try to parse time if we have total duration
                        if (totalDuration.HasValue)
                        {
                            var timeMatch = Regex.Match(e.Data, @"time=(\d+:\d+:\d+\.\d+)");
                            if (timeMatch.Success)
                            {
                                var ts = TimeSpan.Parse(timeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                                progressCallback?.Invoke(ts.TotalSeconds / totalDuration.Value);
                            }
                        }
                    }
                    catch
                    {
                        // Silently ignore any parsing errors to prevent exceptions
                    }
                };

                try
                {
                    process.Start();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    Log.Error($"Error in FFmpeg process: {ex.Message}");
                }
            }

            // Always make sure we report completion
            progressCallback?.Invoke(1.0);
        }

        private static async Task RunFFmpegProcessSimple(string ffmpegPath, string arguments)
        {
            Log.Information("Running simple ffmpeg with arguments: " + arguments);
            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                try
                {
                    Log.Information("Starting process...");
                    process.Start();

                    var readErrorTask = Task.Run(async () =>
                    {
                        while (!process.StandardError.EndOfStream)
                        {
                            await process.StandardError.ReadLineAsync();
                        }
                    });

                    var readOutputTask = Task.Run(async () =>
                    {
                        while (!process.StandardOutput.EndOfStream)
                        {
                            await process.StandardOutput.ReadLineAsync();
                        }
                    });

                    Log.Information("Waiting for process to complete...");

                    // Wait for the process to exit
                    await process.WaitForExitAsync();

                    // Now that the process has exited, we can safely wait for the read tasks to complete
                    // Use a timeout to prevent hanging if there's an issue
                    await Task.WhenAll(
                        Task.WhenAny(readErrorTask, Task.Delay(1000)),
                        Task.WhenAny(readOutputTask, Task.Delay(1000))
                    );

                    Log.Information("Process completed with exit code: " + process.ExitCode);

                    if (process.ExitCode != 0)
                    {
                        Log.Error($"FFmpeg process exited with non-zero exit code: {process.ExitCode}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error in FFmpeg process: {ex.Message}");
                }
            }
        }

        private static void SafeDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) { Log.Information($"Error deleting file {path}: {ex.Message}"); }
        }
    }
}