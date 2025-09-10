using Segra.Backend.Models;
using Serilog;
using System.Diagnostics;
using System.Globalization;

namespace Segra.Backend.Utils
{
    internal class ContentUtils
    {
        public static void CreateMetadataFile(string filePath, Content.ContentType type, string game, List<Bookmark>? bookmarks = null, string? title = null, DateTime? createdAt = null)
        {
            bookmarks ??= new List<Bookmark>();

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

                // Ensure the .metadata folder exists
                string metadataFolderPath = Path.Combine(Settings.Instance.ContentFolder, ".metadata", type.ToString().ToLower() + "s");
                if (!Directory.Exists(metadataFolderPath))
                {
                    DirectoryInfo dir = Directory.CreateDirectory(metadataFolderPath);
                    dir.Attributes |= FileAttributes.Hidden;
                }

                // Create the metadata file
                string metadataFilePath = Path.Combine(metadataFolderPath, $"{contentFileName}.json");
                var (displaySize, sizeKb) = GetFileSize(filePath);
                // Build audio track names: Track 1 is Full Mix, then one per audio source (inputs then outputs), up to OBS's 6 total tracks
                var trackNames = new List<string>
                {
                    "Full Mix"
                };
                try
                {
                    if (Settings.Instance.EnableSeparateAudioTracks)
                    {
                        var perSourceNames = new List<string>();
                        if (Settings.Instance.InputDevices != null)
                            perSourceNames.AddRange(Settings.Instance.InputDevices.Select(d => d.Name));
                        if (Settings.Instance.OutputDevices != null)
                            perSourceNames.AddRange(Settings.Instance.OutputDevices.Select(d => d.Name));

                        // OBS supports 6 tracks total; we already used 1 for the mix
                        foreach (var name in perSourceNames.Take(Math.Max(0, 5)))
                        {
                            trackNames.Add(name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to build audio track names for metadata: {ex.Message}");
                }

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
                    Duration = GetVideoDuration(filePath),
                    AudioTrackNames = trackNames
                };

                string metadataJson = System.Text.Json.JsonSerializer.Serialize(metadataContent, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(metadataFilePath, metadataJson);
                Log.Information($"Metadata file created at: {metadataFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error creating metadata file: {ex.Message}");
            }
        }

        public static void CreateThumbnail(string filePath, Content.ContentType type)
        {
            try
            {
                // Get the directory and file name
                string contentFileName = Path.GetFileNameWithoutExtension(filePath);

                // Ensure the .thumbnails folder exists
                string thumbnailsFolderPath = Path.Combine(Settings.Instance.ContentFolder, ".thumbnails", type.ToString().ToLower() + "s");
                if (!Directory.Exists(thumbnailsFolderPath))
                {
                    DirectoryInfo dir = Directory.CreateDirectory(thumbnailsFolderPath);
                    dir.Attributes |= FileAttributes.Hidden;
                }

                // Define the output thumbnail file path
                string thumbnailFilePath = Path.Combine(thumbnailsFolderPath, $"{contentFileName}.jpeg");

                // Locate the bundled FFmpeg executable
                string ffmpegPath = "ffmpeg.exe";
                if (!File.Exists(ffmpegPath))
                {
                    Log.Information("FFmpeg binary not found in Resources/FFmpeg!");
                    return;
                }

                // Ensure the duration is available
                TimeSpan duration = GetVideoDuration(filePath);

                if (duration == TimeSpan.Zero)
                {
                    // Handle the case where the duration is not available
                    throw new Exception("Video duration is not available.");
                }

                // Calculate the midpoint
                TimeSpan midpoint = TimeSpan.FromTicks(duration.Ticks / 2);

                string midpointTime = midpoint.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

                // FFmpeg arguments to extract a thumbnail at 5 seconds
                string ffmpegArgs = $"-ss {midpointTime} -i \"{filePath}\" -vf \"scale=720:-1\" -qscale:v 9 -vframes 1 \"{thumbnailFilePath}\"";

                // Run FFmpeg as a process
                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = ffmpegArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = new Process { StartInfo = processInfo })
                {
                    var errorBuilder = new System.Text.StringBuilder();
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                    // Continuously drain stdout to avoid deadlocks
                    var outputReadTask = new Task(() =>
                    {
                        using (var reader = process.StandardOutput)
                        {
                            while (!reader.EndOfStream)
                            {
                                reader.ReadLine();
                            }
                        }
                    });

                    process.Start();
                    process.BeginErrorReadLine();
                    outputReadTask.Start();

                    process.WaitForExit();
                    outputReadTask.Wait(1000);

                    if (process.ExitCode != 0)
                    {
                        Log.Error($"FFmpeg error: {errorBuilder}");
                        Log.Error("Thumbnail generation failed.");
                    }
                    else
                    {
                        Log.Information($"Thumbnail successfully created at: {thumbnailFilePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error creating thumbnail: {ex.Message}");
            }
        }

        public static void CreateWaveformFile(string videoFilePath, Content.ContentType type)
        {
            string ffmpegPath = "ffmpeg.exe";

            try
            {
                if (!File.Exists(ffmpegPath))
                {
                    Log.Error($"FFmpeg executable not found at: {ffmpegPath}");
                    return;
                }
                if (!File.Exists(videoFilePath))
                {
                    Log.Error($"Video file not found at: {videoFilePath}");
                    return;
                }

                string contentFileName = Path.GetFileNameWithoutExtension(videoFilePath);

                // Ensure the .waveforms folder exists and is hidden
                string waveformFolderPath = Path.Combine(Settings.Instance.ContentFolder, ".waveforms", type.ToString().ToLower() + "s");
                if (!Directory.Exists(waveformFolderPath))
                {
                    DirectoryInfo dir = Directory.CreateDirectory(waveformFolderPath);
                    dir.Attributes |= FileAttributes.Hidden;
                }

                string tempPcmPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pcm");
                string waveformJsonPathTemp = Path.Combine(waveformFolderPath, $"{contentFileName}.peaks.temp.json");
                string waveformJsonPath = Path.Combine(waveformFolderPath, $"{contentFileName}.peaks.json");

                // Decode audio to raw mono 16-bit PCM at a modest sample rate for efficiency
                int sampleRate = 11025;
                string ffmpegArgs = $"-i \"{videoFilePath}\" -vn -ac 1 -ar {sampleRate} -f s16le -acodec pcm_s16le \"{tempPcmPath}\"";

                var processInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = ffmpegArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = processInfo })
                {
                    var errorBuilder = new System.Text.StringBuilder();
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                    // Simple task to continuously read stdout to prevent buffer filling
                    var outputReadTask = new Task(() =>
                    {
                        using (var reader = process.StandardOutput)
                        {
                            while (!reader.EndOfStream)
                            {
                                reader.ReadLine();
                            }
                        }
                    });

                    process.Start();
                    process.BeginErrorReadLine();
                    outputReadTask.Start();

                    process.WaitForExit();

                    // Give the output task a moment to finish
                    outputReadTask.Wait(1000);

                    if (process.ExitCode != 0)
                    {
                        Log.Error($"FFmpeg error while extracting PCM: {errorBuilder}");
                        return;
                    }
                }

                if (!File.Exists(tempPcmPath))
                {
                    Log.Error("PCM extraction did not produce output file.");
                    return;
                }

                // Read PCM and compute min/max pairs as 8-bit integers similar to audiowaveform output
                byte[] pcmBytes = File.ReadAllBytes(tempPcmPath);
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
                    File.WriteAllText(waveformJsonPathTemp, System.Text.Json.JsonSerializer.Serialize(emptyJson));
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
                    int byteIndex = i * 2;
                    for (int s = i; s < end; s++, byteIndex += 2)
                    {
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
                    data = data
                };
                // Serialize JSON
                var json = System.Text.Json.JsonSerializer.Serialize(wrapper);
                File.WriteAllText(waveformJsonPathTemp, json);
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

        public static TimeSpan GetVideoDuration(string videoFilePath)
        {
            string ffmpegPath = "ffmpeg.exe";

            try
            {
                if (!File.Exists(ffmpegPath))
                    throw new FileNotFoundException($"FFmpeg executable not found at: {ffmpegPath}");
                if (!File.Exists(videoFilePath))
                    throw new FileNotFoundException($"Video file not found at: {videoFilePath}");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{videoFilePath}\"",
                    RedirectStandardError = true, // FFmpeg outputs metadata to standard error
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = processStartInfo })
                {
                    process.Start();
                    string output = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    // Extract duration from FFmpeg output
                    string? durationLine = ExtractDuration(output);
                    if (TimeSpan.TryParse(durationLine, out var duration))
                    {
                        return duration;
                    }

                    throw new Exception("Unable to parse duration from FFmpeg output.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting video duration: {ex.Message}");
                return TimeSpan.Zero; // Return zero duration in case of error
            }
        }

        public static void DeleteContent(string filePath, Content.ContentType type)
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
                if (File.Exists(normalizedFilePath))
                {
                    File.Delete(normalizedFilePath);
                    Log.Information($"Video file deleted: {normalizedFilePath}");
                }
                else
                {
                    Log.Warning($"Video file not found (already deleted?): {normalizedFilePath}");
                }

                // Extract the content file name without extension
                string contentFileName = Path.GetFileNameWithoutExtension(normalizedFilePath);

                // Construct the metadata file path
                string metadataFolderPath = Path.Combine(Settings.Instance.ContentFolder, ".metadata", type.ToString().ToLower() + "s");
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
                string thumbnailsFolderPath = Path.Combine(Settings.Instance.ContentFolder, ".thumbnails", type.ToString().ToLower() + "s");
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
                string waveformFolderPath = Path.Combine(Settings.Instance.ContentFolder, ".waveforms", type.ToString().ToLower() + "s");
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
                SettingsUtils.LoadContentFromFolderIntoState();
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

        private static string? ExtractDuration(string ffmpegOutput)
        {
            // Look for a line like: "Duration: 00:02:34.56, start: 0.000000, bitrate: 128 kb/s"
            const string durationKeyword = "Duration: ";
            int startIndex = ffmpegOutput.IndexOf(durationKeyword);
            if (startIndex != -1)
            {
                startIndex += durationKeyword.Length;
                int endIndex = ffmpegOutput.IndexOf(",", startIndex);
                if (endIndex != -1)
                {
                    return ffmpegOutput.Substring(startIndex, endIndex - startIndex).Trim();
                }
            }
            return null;
        }
    }
}
