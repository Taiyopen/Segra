using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using Serilog;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Linq;
using Segra.Models;

namespace Segra.Backend.Utils
{
    public static class ClipUtils
    {
        public static async Task CreateClips(List<Selection> selections)
        {
            int id = Guid.NewGuid().GetHashCode();
            await MessageUtils.SendFrontendMessage("ClipProgress", new { id, progress = 0 });
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

            string clipsOutputFolder = Path.Combine(videoFolder, "clips");
            Directory.CreateDirectory(clipsOutputFolder);

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
                    MessageUtils.SendFrontendMessage("ClipProgress", new { id, progress = currentProgress });
                });

                processedDuration += clipDuration;
                tempClipFiles.Add(tempFileName);
                await MessageUtils.SendFrontendMessage("ClipProgress", new { id, progress = (processedDuration / totalDuration) * 100 });
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
            string outputFilePath = Path.Combine(clipsOutputFolder, outputFileName).Replace("\\", "/");

            await RunFFmpegProcess(ffmpegPath,
                $"-y -f concat -safe 0 -i \"{concatFilePath}\" -c copy -movflags +faststart \"{outputFilePath}\"",
                totalDuration,
                progress => MessageUtils.SendFrontendMessage("ClipProgress", new { id, progress = 100 }));

            // Cleanup
            tempClipFiles.ForEach(f => SafeDelete(f));
            SafeDelete(concatFilePath);

            // Finalization
            ContentUtils.CreateMetadataFile(outputFilePath, Content.ContentType.Clip, selections.FirstOrDefault()?.Game);
            ContentUtils.CreateThumbnail(outputFilePath, Content.ContentType.Clip);
            SettingsUtils.LoadContentFromFolderIntoState();
            await MessageUtils.SendFrontendMessage("ClipProgress", new { id, progress = 100 });
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

                    var timeMatch = Regex.Match(e.Data, @"time=(\d+:\d+:\d+\.\d+)");
                    if (timeMatch.Success && totalDuration.HasValue)
                    {
                        var ts = TimeSpan.Parse(timeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                        progressCallback?.Invoke(ts.TotalSeconds / totalDuration.Value);
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    Log.Information($"FFmpeg process exited with code {process.ExitCode}");
            }
        }

        private static void SafeDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) { Log.Information($"Error deleting file {path}: {ex.Message}"); }
        }
    }
}