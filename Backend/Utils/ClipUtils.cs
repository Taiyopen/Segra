using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using Serilog;
using System.Globalization;
using Segra.Models;

namespace Segra.Backend.Utils
{
    public static class ClipUtils
    {
        public static async Task CreateClips(List<Selection> selections)
        {
            Settings.Instance.State.IsCreatingClip = true;
            string videoFolder = Settings.Instance.ContentFolder;

            if (selections == null || selections.Count == 0)
            {
                Log.Information("No selections provided.");
                Settings.Instance.State.IsCreatingClip = false;
                return;
            }

            string clipsOutputFolder = Path.Combine(videoFolder, "clips");
            if (!Directory.Exists(clipsOutputFolder))
            {
                Directory.CreateDirectory(clipsOutputFolder);
            }

            List<string> tempClipFiles = new List<string>();

            string ffmpegPath = "ffmpeg.exe";
            if (!File.Exists(ffmpegPath))
            {
                Log.Information($"FFmpeg executable not found at path: {ffmpegPath}");
                Settings.Instance.State.IsCreatingClip = false;
                return;
            }

            int clipNumber = 1;
            foreach (var selection in selections)
            {
                string inputFilePath = Path.Combine(videoFolder, selection.Type.ToLower() + "s", $"{selection.FileName}.mp4").Replace("\\", "/");

                if (!File.Exists(inputFilePath))
                {
                    Log.Information($"Input video file not found: {inputFilePath}");
                    continue;
                }

                string tempFileName = Path.Combine(Path.GetTempPath(), $"clip{clipNumber}_{Guid.NewGuid()}.mp4");

                await ExtractClip(inputFilePath, tempFileName, selection.StartTime, selection.EndTime, ffmpegPath);
                tempClipFiles.Add(tempFileName);

                clipNumber++;
            }

            if (tempClipFiles.Count == 0)
            {
                Log.Information("No valid clips were extracted.");
                Settings.Instance.State.IsCreatingClip = false;
                return;
            }

            string concatFilePath = Path.Combine(Path.GetTempPath(), $"concat_list_{Guid.NewGuid()}.txt");
            using (StreamWriter sw = new StreamWriter(concatFilePath, false, new System.Text.UTF8Encoding(false)))
            {
                foreach (var tempClip in tempClipFiles)
                {
                    // Escape backslashes and single quotes
                    string escapedPath = tempClip.Replace("\\", "\\\\").Replace("'", "\\'");
                    sw.WriteLine($"file '{escapedPath}'");
                }
            }

            string outputFileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";
            string outputFilePath = Path.Combine(clipsOutputFolder, outputFileName).Replace("\\", "/");

            string concatArguments = $"-y -f concat -safe 0 -i \"{concatFilePath}\" -c copy -movflags +faststart \"{outputFilePath}\"";
            Log.Information($"Executing FFmpeg command: {ffmpegPath} {concatArguments}");

            await RunFFmpegProcess(ffmpegPath, concatArguments);

            foreach (var tempClip in tempClipFiles)
            {
                try
                {
                    File.Delete(tempClip);
                }
                catch (Exception ex)
                {
                    Log.Information($"Error deleting temp clip file {tempClip}: {ex.Message}");
                }
            }

            try
            {
                File.Delete(concatFilePath);
            }
            catch (Exception ex)
            {
                Log.Information($"Error deleting concat list file {concatFilePath}: {ex.Message}");
            }

            string? game = selections.FirstOrDefault(s => !string.IsNullOrEmpty(s.Game))?.Game;
            ContentUtils.CreateMetadataFile(outputFilePath, Content.ContentType.Clip, game);
            ContentUtils.CreateThumbnail(outputFilePath, Content.ContentType.Clip);
            SettingsUtils.LoadContentFromFolderIntoState();
            Settings.Instance.State.IsCreatingClip = false;
            Log.Information("Clips merged successfully.");
        }

        private static async Task ExtractClip(string inputFilePath, string outputFilePath, double startTime, double endTime, string ffmpegPath)
        {
            // Use FFmpeg to extract the clip
            double duration = endTime - startTime;

            if (!File.Exists(ffmpegPath))
            {
                Log.Information($"FFmpeg executable not found at path: {ffmpegPath}");
                return;
            }

            // Re-encode the clip for precise timing and save as MP4
            string arguments = $"-y -ss {startTime.ToString(CultureInfo.InvariantCulture)} -t {duration.ToString(CultureInfo.InvariantCulture)} -i \"{inputFilePath}\" -c:v libx264 -preset veryfast -crf 23 -c:a aac -b:a 192k -movflags +faststart \"{outputFilePath}\"";

            Log.Information($"Executing FFmpeg command: {ffmpegPath} {arguments}");

            await RunFFmpegProcess(ffmpegPath, arguments);
        }

        private static async Task RunFFmpegProcess(string ffmpegPath, string arguments)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var process = new Process
            {
                StartInfo = processStartInfo,
            };

            process.Start();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Log.Information($"FFmpeg process exited with code {process.ExitCode}");
            }
        }
    }
}
