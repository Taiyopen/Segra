using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Segra.Backend.App;
using Segra.Backend.Core.Models;
using Segra.Backend.Games;
using Segra.Backend.Services;
using Segra.Backend.Shared;
using Segra.Backend.Windows.Storage;
using Serilog;
using static Segra.Backend.Shared.GeneralUtils;

namespace Segra.Backend.Media
{
    public static class ClipService
    {
        // Dictionary to store active FFmpeg processes
        private static readonly Dictionary<int, List<Process>> ActiveFFmpegProcesses = new Dictionary<int, List<Process>>();
        // Clip IDs that were explicitly cancelled by the user, so their FFmpeg failures are not surfaced as errors
        private static readonly HashSet<int> CancelledClipIds = new HashSet<int>();
        // Lock for thread safety
        private static readonly object ProcessLock = new object();

        /// <param name="preferredOutputFileNameBase">Ignored for the output file name; clips are always saved as <c>{originalFileName}_clip_{i}.mp4</c> with a per-folder auto-incrementing <c>i</c>.</param>
        /// <param name="appendTimestampToPreferredFileName">Ignored; kept for call-site compatibility.</param>
        public static async Task CreateClips(
            List<Segment> segments,
            string? preferredOutputFileNameBase = null,
            bool appendTimestampToPreferredFileName = true,
            bool losslessOnly = false)
        {
            int id = Guid.NewGuid().GetHashCode();
            List<string> tempClipFiles = new List<string>();
            string? concatFilePath = null;
            string? outputFilePath = null;

            try
            {
                await MessageService.SendFrontendMessage("ClipProgress", new { id, progress = 0, segments });
                string videoFolder = Settings.Instance.ContentFolder;

                if (segments == null || !segments.Any())
                {
                    Log.Error("No segments provided.");
                    await MessageService.SendFrontendMessage("ClipProgress", new { id, progress = -1, segments, error = "No segments provided" });
                    return;
                }

                double totalDuration = segments.Sum(s => s.EndTime - s.StartTime);
                if (totalDuration <= 0)
                {
                    Log.Error("Total clip duration is zero or negative.");
                    await MessageService.SendFrontendMessage("ClipProgress", new { id, progress = -1, segments, error = "Invalid clip duration" });
                    return;
                }

                // Use game from first segment for the output folder
                var firstSegment = segments.FirstOrDefault();
                string outputGameFolder = StorageService.SanitizeGameNameForFolder(firstSegment?.Game ?? "Unknown");
                string outputFolder = PathUtils.Combine(videoFolder, FolderNames.Clips, outputGameFolder);
                Directory.CreateDirectory(outputFolder);

                if (!FFmpegService.FFmpegExists())
                {
                    Log.Error($"FFmpeg executable not found at path: {FFmpegService.GetFFmpegPath()}");
                    await MessageService.SendFrontendMessage("ClipProgress", new { id, progress = -1, segments, error = "FFmpeg not found" });
                    return;
                }

                // Lossless clips ignore track metadata / union layout — each segment is just -ss/-t/-c copy.
                var perSegmentTrackNames = new List<List<string>?>();
                List<string>? unionAudioLayout = null;
                if (!losslessOnly)
                {
                    bool anySegmentHasMutedTracks = segments.Any(s => s.MutedAudioTracks != null && s.MutedAudioTracks.Count > 0);
                    if (Settings.Instance.ClipKeepSeparateAudioTracks || anySegmentHasMutedTracks)
                    {
                        foreach (var seg in segments)
                            perSegmentTrackNames.Add(GetSourceAudioTrackNames(seg));
                    }
                    else
                    {
                        perSegmentTrackNames.AddRange(Enumerable.Repeat<List<string>?>(null, segments.Count));
                    }

                    unionAudioLayout = Settings.Instance.ClipKeepSeparateAudioTracks
                        ? BuildUnionAudioLayout(perSegmentTrackNames)
                        : null;
                }
                else
                {
                    perSegmentTrackNames.AddRange(Enumerable.Repeat<List<string>?>(null, segments.Count));
                }

                double processedDuration = 0;
                int segmentIndex = 0;
                foreach (var segment in segments)
                {
                    // Use the actual file path from metadata when available, fall back to reconstructed path
                    string inputFilePath;
                    if (!string.IsNullOrEmpty(segment.FilePath) && File.Exists(segment.FilePath))
                    {
                        inputFilePath = segment.FilePath;
                    }
                    else
                    {
                        string inputGameFolder = StorageService.SanitizeGameNameForFolder(segment.Game);
                        var segmentType = Enum.Parse<Content.ContentType>(segment.Type);
                        string inputFolderName = FolderNames.GetVideoFolderName(segmentType);
                        inputFilePath = PathUtils.Combine(videoFolder, inputFolderName, inputGameFolder, $"{segment.FileName}.mp4");
                    }
                    if (!File.Exists(inputFilePath))
                    {
                        Log.Information($"Input video file not found: {inputFilePath}");
                        segmentIndex++;
                        continue;
                    }

                    string tempFileName = PathUtils.Combine(Path.GetTempPath(), $"clip{Guid.NewGuid()}.mp4");
                    double clipDuration = segment.EndTime - segment.StartTime;

                    List<string>? segmentTrackNames = perSegmentTrackNames[segmentIndex];
                    List<string>? targetLayout = losslessOnly ? null : (Settings.Instance.ClipKeepSeparateAudioTracks ? unionAudioLayout : null);

                    await ExtractClip(id, inputFilePath, tempFileName, segment.StartTime, segment.EndTime, segmentTrackNames, segment.MutedAudioTracks, segment.AudioTrackVolumes, targetLayout, losslessOnly, progress =>
                    {
                        double clampedProgress = Math.Min(progress, 1.0);
                        double currentProgress = (processedDuration + (clampedProgress * clipDuration)) / totalDuration * 95;
                        _ = MessageService.SendFrontendMessage("ClipProgress", new { id, progress = currentProgress, segments });
                    });

                    // Verify the temp file was created successfully
                    if (!File.Exists(tempFileName))
                    {
                        Log.Error($"Failed to create temp clip file: {tempFileName}");
                        throw new Exception($"Failed to extract clip from {segment.FileName}");
                    }

                    processedDuration += clipDuration;
                    tempClipFiles.Add(tempFileName);
                    segmentIndex++;
                }

                if (!tempClipFiles.Any())
                {
                    Log.Error("No valid clips were extracted.");
                    await MessageService.SendFrontendMessage("ClipProgress", new { id, progress = -1, segments, error = "No valid clips were extracted" });
                    return;
                }

                _ = MessageService.SendFrontendMessage("ClipProgress", new { id, progress = 96, segments });

                string firstInputPath = ResolveSegmentInputPath(firstSegment!, videoFolder);
                string originalBase = GetSanitizedOriginalFileBase(firstSegment!, firstInputPath);
                int clipIndex = GetNextAvailableClipIndex(outputFolder, originalBase);
                string outputBaseName = $"{originalBase}_clip_{clipIndex}";
                string outputFileName = $"{outputBaseName}.mp4";
                outputFilePath = PathUtils.Combine(outputFolder, outputFileName);

                if (tempClipFiles.Count == 1)
                {
                    File.Move(tempClipFiles[0], outputFilePath);
                    tempClipFiles.Clear();
                }
                else
                {
                    concatFilePath = PathUtils.Combine(Path.GetTempPath(), $"concat_list_{Guid.NewGuid()}.txt");
                    await File.WriteAllLinesAsync(concatFilePath, tempClipFiles.Select(FFmpegService.BuildConcatListLine));

                    try
                    {
                        string concatArgs;
                        if (losslessOnly)
                        {
                            concatArgs = $"-y -f concat -safe 0 -i \"{concatFilePath}\" -map 0 -c copy \"{outputFilePath}\"";
                        }
                        else
                        {
                            string mapAllArg = unionAudioLayout != null ? "-map 0 " : "";
                            string concatMetadataArgs = "";
                            // When multi-track is active, re-encode audio during concat to fix
                            // DTS misalignment between streams (different seek offsets cause
                            // slightly different AAC frame counts per stream).
                            // Video is always stream-copied.
                            string codecArg = unionAudioLayout != null
                                ? $"-c:v copy -c:a aac -b:a {Settings.Instance.ClipAudioQuality} "
                                : "-c copy ";
                            if (unionAudioLayout != null)
                            {
                                concatMetadataArgs = string.Join(" ", unionAudioLayout.Select((name, i) => $"-metadata:s:a:{i} title=\"{name}\"")) + " ";
                            }
                            concatArgs = $"-y -f concat -safe 0 -i \"{concatFilePath}\" {mapAllArg}{codecArg}{concatMetadataArgs}-avoid_negative_ts make_zero -movflags +faststart \"{outputFilePath}\"";
                        }

                        await FFmpegService.RunWithProgress(id,
                            concatArgs,
                            totalDuration,
                            progress => { },
                            process =>
                            {
                                lock (ProcessLock)
                                {
                                    if (!ActiveFFmpegProcesses.ContainsKey(id))
                                    {
                                        ActiveFFmpegProcesses[id] = new List<Process>();
                                    }
                                    ActiveFFmpegProcesses[id].Add(process);
                                    Log.Information($"[Clip {id}] Tracking concatenation FFmpeg process (PID: {process.Id})");
                                }
                            }
                        );
                    }
                    finally
                    {
                        lock (ProcessLock)
                        {
                            ActiveFFmpegProcesses.Remove(id);
                        }
                    }
                }

                _ = MessageService.SendFrontendMessage("ClipProgress", new { id, progress = 97, segments });

                if (!File.Exists(outputFilePath))
                {
                    throw new Exception("Failed to create final clip file");
                }

                await EnsureFileReady(outputFilePath);

                _ = MessageService.SendFrontendMessage("ClipProgress", new { id, progress = 98, segments });

                await ContentService.CreateMetadataFile(outputFilePath, Content.ContentType.Clip, firstSegment?.Game!, null, firstSegment?.Title, igdbId: firstSegment?.IgdbId, audioTrackNames: unionAudioLayout);
                await ContentService.CreateThumbnail(outputFilePath, Content.ContentType.Clip);
                await ContentService.CreateWaveformFile(outputFilePath, Content.ContentType.Clip);

                _ = MessageService.SendFrontendMessage("ClipProgress", new { id, progress = 99, segments });

                // Load silently then await the state send before progress=100 removes the loading card,
                // so the clip is on screen first (avoids a skeleton-removed-before-content flicker).
                await SettingsService.LoadContentFromFolderIntoState(sendToFrontend: false);
                await MessageService.SendStateToFrontend("Clip created");
                await MessageService.SendFrontendMessage("ClipProgress", new { id, progress = 100, segments });
            }
            catch (Exception ex)
            {
                // If the user cancelled this clip, the FFmpeg process was killed on purpose.
                // Don't surface that as an error - CancelClip already cleared the UI.
                bool wasCancelled;
                lock (ProcessLock)
                {
                    wasCancelled = CancelledClipIds.Remove(id);
                }

                if (wasCancelled)
                {
                    Log.Information($"[Clip {id}] Clip creation stopped due to user cancellation");
                }
                else
                {
                    Log.Error($"[Clip {id}] Error creating clip: {ex.Message}");
                    Log.Error($"[Clip {id}] Stack trace: {ex.StackTrace}");
                }

                // Clean up any partially created output file
                if (!string.IsNullOrEmpty(outputFilePath))
                {
                    SafeDelete(outputFilePath);
                }

                if (!wasCancelled)
                {
                    string cardError = ex.Message;
                    if (ex is FFmpegException ffEx)
                    {
                        var (shortMessage, _) = FFmpegErrors.Describe(ffEx.ExitCode);
                        cardError = shortMessage;
                        _ = MessageService.ShowModal(
                            "Clip creation failed",
                            FFmpegErrors.DescribeForUser(ffEx.ExitCode),
                            "error");
                    }

                    // Notify frontend of failure
                    await MessageService.SendFrontendMessage("ClipProgress", new { id, progress = -1, segments, error = cardError });
                }
            }
            finally
            {
                // Always cleanup temp files
                tempClipFiles.ForEach(SafeDelete);
                if (!string.IsNullOrEmpty(concatFilePath))
                {
                    SafeDelete(concatFilePath);
                }

                // Drop any cancellation marker for this clip in case it completed before the kill landed
                lock (ProcessLock)
                {
                    CancelledClipIds.Remove(id);
                }
            }
        }

        /// <summary>
        /// Extracts a time range from a session recording into the Clips folder (FFmpeg). Does not start or stop OBS outputs.
        /// Output file name is always <c>{sessionFileBase}_clip_{i}.mp4</c> (see <see cref="CreateClips"/>).
        /// </summary>
        internal static async Task CreateClipFromSessionTimeRange(
            Recording recording,
            double startSeconds,
            double endSeconds,
            string clipTitle,
            string? preferredOutputFileNameBase = null,
            int? clipIgdbId = null,
            bool appendTimestampToPreferredFileName = true)
        {
            if (string.IsNullOrEmpty(recording.FilePath) || !File.Exists(recording.FilePath))
            {
                Log.Warning("Session clip: source file missing or path empty");
                return;
            }

            if (endSeconds <= startSeconds + 0.25)
            {
                Log.Warning("Session clip: invalid time range");
                return;
            }

            await EnsureFileReady(recording.FilePath);

            string baseName = Path.GetFileNameWithoutExtension(recording.FilePath);
            int? igdbId = clipIgdbId;
            if (igdbId == null && !string.IsNullOrEmpty(recording.ExePath))
                igdbId = GameUtils.GetIgdbIdFromExePath(recording.ExePath);

            var segment = new Segment
            {
                Id = Random.Shared.NextInt64(),
                Type = Content.ContentType.Session.ToString(),
                StartTime = startSeconds,
                EndTime = endSeconds,
                FileName = baseName,
                FilePath = recording.FilePath,
                Game = recording.Game,
                Title = clipTitle,
                IgdbId = igdbId
            };

            await CreateClips(new List<Segment> { segment }, preferredOutputFileNameBase, appendTimestampToPreferredFileName);
        }

        private static string ClipMbpsToBv(int mbps)
        {
            int m = Math.Clamp(mbps <= 0 ? 35 : mbps, 10, 100);
            return $"{m}M";
        }

        private static int ClipVbrMidpointMbps(Settings s)
        {
            int mn = Math.Min(s.ClipMinBitrate, s.ClipMaxBitrate);
            int mx = Math.Max(s.ClipMinBitrate, s.ClipMaxBitrate);
            int mid = (mn + mx) / 2;
            if (mid <= 0)
                mid = s.ClipBitrate;
            return Math.Clamp(mid, 10, 100);
        }

        private static int ClipVbrPeakMbps(Settings s) =>
            Math.Clamp(Math.Max(s.ClipMinBitrate, s.ClipMaxBitrate), 10, 100);

        private static string NormalizeClipRateControl(Settings settings)
        {
            string rc = (settings.ClipRateControl ?? "CRF").Trim().ToUpperInvariant();
            bool cpuClip = settings.ClipEncoder.Equals("cpu", StringComparison.OrdinalIgnoreCase);
            var gpu = DetectGpuVendor();

            if (cpuClip && (rc == "CQP" || rc == "CQVBR"))
                return rc == "CQVBR" ? "VBR" : "CRF";
            if (!cpuClip && rc == "CRF")
                return "CQP";
            if (rc == "CQVBR" && (cpuClip || gpu != GpuVendor.Nvidia))
                return "CQP";

            return rc switch
            {
                "CBR" or "VBR" or "CRF" or "CQP" or "CQVBR" => rc,
                _ => cpuClip ? "CRF" : "CQP"
            };
        }

        private static string BuildCpuClipRateArgs(Settings settings)
        {
            string rc = NormalizeClipRateControl(settings);
            bool hevc = settings.ClipCodec.Equals("h265", StringComparison.OrdinalIgnoreCase);

            return rc switch
            {
                "CBR" =>
                    hevc
                        ? $"-b:v {ClipMbpsToBv(settings.ClipBitrate)}"
                        : $"-b:v {ClipMbpsToBv(settings.ClipBitrate)} -minrate {ClipMbpsToBv(settings.ClipBitrate)} -maxrate {ClipMbpsToBv(settings.ClipBitrate)} -bufsize {ClipMbpsToBv(settings.ClipBitrate * 2)}",
                "VBR" =>
                    hevc
                        ? $"-b:v {ClipMbpsToBv(ClipVbrMidpointMbps(settings))} -maxrate {ClipMbpsToBv(ClipVbrPeakMbps(settings))} -bufsize {ClipMbpsToBv(ClipVbrPeakMbps(settings) * 2)}"
                        : $"-b:v {ClipMbpsToBv(ClipVbrMidpointMbps(settings))} -maxrate {ClipMbpsToBv(ClipVbrPeakMbps(settings))} -bufsize {ClipMbpsToBv(ClipVbrPeakMbps(settings) * 2)}",
                _ => $"-crf {settings.ClipQualityCpu}"
            };
        }

        private static string BuildNvidiaClipRateArgs(Settings settings)
        {
            string rc = NormalizeClipRateControl(settings);
            return rc switch
            {
                "CBR" =>
                    $"-rc:v cbr -b:v {ClipMbpsToBv(settings.ClipBitrate)} -bufsize {ClipMbpsToBv(settings.ClipBitrate * 2)}",
                "VBR" =>
                    $"-rc:v vbr -b:v {ClipMbpsToBv(ClipVbrMidpointMbps(settings))} -maxrate {ClipMbpsToBv(ClipVbrPeakMbps(settings))} -bufsize {ClipMbpsToBv(ClipVbrPeakMbps(settings) * 2)}",
                "CQVBR" =>
                    $"-rc:v vbr -cq {settings.ClipQualityGpu} -maxrate {ClipMbpsToBv(ClipVbrPeakMbps(settings))} -bufsize {ClipMbpsToBv(ClipVbrPeakMbps(settings) * 2)}",
                _ =>
                    $"-rc:v vbr -cq {settings.ClipQualityGpu}"
            };
        }

        private static string BuildAmdClipRateArgs(Settings settings)
        {
            string rc = NormalizeClipRateControl(settings);
            return rc switch
            {
                "CBR" =>
                    $"-rc cbr -b:v {ClipMbpsToBv(settings.ClipBitrate)}",
                "VBR" =>
                    $"-rc vbr_peak -b:v {ClipMbpsToBv(ClipVbrMidpointMbps(settings))} -maxrate {ClipMbpsToBv(ClipVbrPeakMbps(settings))}",
                _ =>
                    $"-rc cqp -qp_i {settings.ClipQualityGpu} -qp_p {settings.ClipQualityGpu}"
            };
        }

        private static string BuildIntelClipRateArgs(Settings settings)
        {
            string rc = NormalizeClipRateControl(settings);
            return rc switch
            {
                "CBR" =>
                    $"-b:v {ClipMbpsToBv(settings.ClipBitrate)} -maxrate {ClipMbpsToBv(settings.ClipBitrate)}",
                "VBR" =>
                    $"-b:v {ClipMbpsToBv(ClipVbrMidpointMbps(settings))} -maxrate {ClipMbpsToBv(ClipVbrPeakMbps(settings))}",
                _ =>
                    $"-global_quality {settings.ClipQualityGpu}"
            };
        }

        private static async Task ExtractClip(int clipId, string inputFilePath, string outputFilePath, double startTime, double endTime,
                            List<string>? audioTrackNames, List<int>? mutedAudioTracks, Dictionary<int, double>? audioTrackVolumes, List<string>? targetAudioLayout, bool losslessOnly, Action<double> progressCallback)
        {
            double duration = endTime - startTime;

            if (losslessOnly)
            {
                string ss = startTime.ToString(CultureInfo.InvariantCulture);
                string durStr = duration.ToString(CultureInfo.InvariantCulture);
                // -map 0 keeps every stream (all audio tracks, video, subtitles) — default mapping would often drop extra audio.
                string losslessArgs = $"-y -ss {ss} -i \"{inputFilePath}\" -t {durStr} -map 0 -c copy \"{outputFilePath}\"";
                Log.Information("Extracting clip (lossless remux: -ss, -i, -t, -map 0, -c copy)");
                Log.Information($"FFmpeg arguments: {losslessArgs}");

                try
                {
                    await FFmpegService.RunWithProgress(clipId, losslessArgs, duration, progressCallback, process =>
                    {
                        lock (ProcessLock)
                        {
                            if (!ActiveFFmpegProcesses.ContainsKey(clipId))
                            {
                                ActiveFFmpegProcesses[clipId] = new List<Process>();
                            }
                            ActiveFFmpegProcesses[clipId].Add(process);
                            Log.Information($"[Clip {clipId}] Tracking FFmpeg process (PID: {process.Id})");
                        }
                    });
                }
                finally
                {
                    lock (ProcessLock)
                    {
                        ActiveFFmpegProcesses.Remove(clipId);
                        Log.Information($"[Clip {clipId}] Removed from active processes");
                    }
                }

                return;
            }

            var settingsForCodec = Settings.Instance;

            bool wantCpuEncoder = settingsForCodec.ClipEncoder.Equals("cpu", StringComparison.OrdinalIgnoreCase);
            GpuVendor clipGpuVendor = wantCpuEncoder ? GpuVendor.Unknown : DetectGpuVendor();
            if (!wantCpuEncoder && clipGpuVendor == GpuVendor.Unknown)
            {
                Log.Warning("Unknown GPU vendor detected, falling back to CPU encoding");
                wantCpuEncoder = true;
            }

            string videoCodec;
            string qualityArgs;
            string presetArgs;
            if (wantCpuEncoder)
            {
                if (settingsForCodec.ClipCodec.Equals("h265", StringComparison.OrdinalIgnoreCase))
                    videoCodec = "libx265";
                else
                    videoCodec = "libx264";

                qualityArgs = BuildCpuClipRateArgs(settingsForCodec);
                presetArgs = $"-preset {settingsForCodec.ClipPreset}";
            }
            else
            {
                switch (clipGpuVendor)
                {
                    case GpuVendor.Nvidia:
                        if (settingsForCodec.ClipCodec.Equals("h265", StringComparison.OrdinalIgnoreCase))
                            videoCodec = "hevc_nvenc";
                        else if (settingsForCodec.ClipCodec.Equals("av1", StringComparison.OrdinalIgnoreCase))
                            videoCodec = "av1_nvenc";
                        else
                            videoCodec = "h264_nvenc";

                        qualityArgs = BuildNvidiaClipRateArgs(settingsForCodec);
                        presetArgs = $"-preset {settingsForCodec.ClipPreset}";
                        break;

                    case GpuVendor.AMD:
                        if (settingsForCodec.ClipCodec.Equals("h265", StringComparison.OrdinalIgnoreCase))
                            videoCodec = "hevc_amf";
                        else if (settingsForCodec.ClipCodec.Equals("av1", StringComparison.OrdinalIgnoreCase))
                            videoCodec = "av1_amf";
                        else
                            videoCodec = "h264_amf";

                        qualityArgs = BuildAmdClipRateArgs(settingsForCodec);
                        presetArgs = $"-usage {settingsForCodec.ClipPreset}";
                        break;

                    case GpuVendor.Intel:
                        if (settingsForCodec.ClipCodec.Equals("h265", StringComparison.OrdinalIgnoreCase))
                            videoCodec = "hevc_qsv";
                        else if (settingsForCodec.ClipCodec.Equals("av1", StringComparison.OrdinalIgnoreCase))
                            videoCodec = "av1_qsv";
                        else
                            videoCodec = "h264_qsv";

                        qualityArgs = BuildIntelClipRateArgs(settingsForCodec);
                        presetArgs = $"-preset {settingsForCodec.ClipPreset}";
                        break;

                    default:
                        if (settingsForCodec.ClipCodec.Equals("h265", StringComparison.OrdinalIgnoreCase))
                            videoCodec = "libx265";
                        else
                            videoCodec = "libx264";

                        qualityArgs = BuildCpuClipRateArgs(settingsForCodec);
                        presetArgs = $"-preset {settingsForCodec.ClipPreset}";
                        break;
                }
            }

            string fpsArg = settingsForCodec.ClipFps > 0 ? $"-r {settingsForCodec.ClipFps}" : "";

            var settings = settingsForCodec;

            // Build audio mapping, filter, and metadata based on per-segment muted tracks
            string mapArgs = "";
            string metadataArgs = "";
            string filterArgs = "";
            string extraInputArgs = "";

            if (targetAudioLayout != null && targetAudioLayout.Count > 0)
            {
                // Normalise output to the union layout so every temp clip has identical stream layout.
                // Silent (muted/missing) tracks use separate -f lavfi inputs so they are never mixed
                // with real decoded streams inside filter_complex -- mixing synthetic sources and real
                // streams in the same filtergraph causes a scheduler deadlock in FFmpeg.
                var filterParts = new List<string>();
                var mapParts = new List<string> { "-map 0:v:0" };
                var metaParts = new List<string>();
                var extraInputParts = new List<string>();
                int silenceInputIdx = 1; // lavfi inputs start at 1 (0 is the main file)

                bool sourceHasIndividualTracks = audioTrackNames != null && audioTrackNames.Count > 1;

                // Enabled individual source tracks (index > 0, not muted)
                var enabledSourceTracks = new List<int>();
                if (sourceHasIndividualTracks)
                {
                    for (int i = 1; i < audioTrackNames!.Count; i++)
                    {
                        if (mutedAudioTracks == null || !mutedAudioTracks.Contains(i))
                            enabledSourceTracks.Add(i);
                    }
                }

                // Pre-compute which individual track positions map to an enabled source stream
                var indivSourceMap = new Dictionary<int, int>(); // layout position j -> source audio index
                if (sourceHasIndividualTracks)
                {
                    for (int j = 1; j < targetAudioLayout.Count; j++)
                    {
                        int srcIdx = audioTrackNames!.FindIndex(n => string.Equals(n, targetAudioLayout[j], StringComparison.OrdinalIgnoreCase));
                        if (srcIdx > 0 && (mutedAudioTracks == null || !mutedAudioTracks.Contains(srcIdx)))
                            indivSourceMap[j] = srcIdx;
                    }
                }

                // Count how many times each source audio stream index is referenced
                var refCount = new Dictionary<int, int>();
                foreach (int i in enabledSourceTracks)
                {
                    refCount.TryGetValue(i, out int c);
                    refCount[i] = c + 1;
                }
                foreach (int srcIdx in indivSourceMap.Values)
                {
                    refCount.TryGetValue(srcIdx, out int c);
                    refCount[srcIdx] = c + 1;
                }

                // Build asplit filters for streams referenced more than once
                var available = new Dictionary<int, Queue<string>>();
                foreach (var (srcIdx, count) in refCount)
                {
                    if (count <= 1)
                    {
                        available[srcIdx] = new Queue<string>(new[] { $"[0:a:{srcIdx}]" });
                    }
                    else
                    {
                        var labels = Enumerable.Range(0, count).Select(k => $"[split_{srcIdx}_{k}]").ToList();
                        filterParts.Add($"[0:a:{srcIdx}]asplit={count}{string.Join("", labels)}");
                        available[srcIdx] = new Queue<string>(labels);
                    }
                }

                string durationStr = duration.ToString(CultureInfo.InvariantCulture);
                string silenceInput = $"-f lavfi -t {durationStr} -i \"anullsrc=cl=stereo:r=48000\"";
                string atrim = $"atrim=end={durationStr},asetpts=PTS-STARTPTS";

                // Position 0: Full Mix
                if (!sourceHasIndividualTracks)
                {
                    // No individual track metadata -- pass through the source's default audio
                    filterParts.Add($"[0:a:0]{atrim}[out_a0]");
                    mapParts.Add("-map \"[out_a0]\"");
                }
                else if (enabledSourceTracks.Count == 0)
                {
                    extraInputParts.Add(silenceInput);
                    filterParts.Add($"[{silenceInputIdx}:a:0]{atrim}[out_a0]");
                    mapParts.Add("-map \"[out_a0]\"");
                    silenceInputIdx++;
                }
                else if (enabledSourceTracks.Count == 1)
                {
                    int trackIdx = enabledSourceTracks[0];
                    string volFilter = WithAtrim(GetVolumeFilter(audioTrackVolumes, trackIdx), durationStr);
                    filterParts.Add($"{available[trackIdx].Dequeue()}{volFilter}[out_a0]");
                    mapParts.Add("-map \"[out_a0]\"");
                }
                else
                {
                    // Apply per-track volume before mixing
                    var mixInputLabels = new List<string>();
                    foreach (int i in enabledSourceTracks)
                    {
                        double vol = GetTrackVolume(audioTrackVolumes, i);
                        string srcLabel = available[i].Dequeue();
                        if (Math.Abs(vol - 1.0) > 0.001)
                        {
                            string volLabel = $"[vol_mix_{i}]";
                            filterParts.Add($"{srcLabel}volume={vol.ToString(CultureInfo.InvariantCulture)}{volLabel}");
                            mixInputLabels.Add(volLabel);
                        }
                        else
                        {
                            mixInputLabels.Add(srcLabel);
                        }
                    }
                    string inputs = string.Join("", mixInputLabels);
                    // amix defaults to normalize=1, which attenuates each input (~1/sqrt(N)) and makes clips sound quieter than the source mix.
                    filterParts.Add($"{inputs}amix=inputs={enabledSourceTracks.Count}:duration=longest:normalize=0,{atrim}[out_a0]");
                    mapParts.Add("-map \"[out_a0]\"");
                }
                metaParts.Add($"-metadata:s:a:0 title=\"{targetAudioLayout[0]}\"");

                // Positions 1+: individual tracks aligned by name
                for (int j = 1; j < targetAudioLayout.Count; j++)
                {
                    if (indivSourceMap.TryGetValue(j, out int srcIdx))
                    {
                        string volFilter = WithAtrim(GetVolumeFilter(audioTrackVolumes, srcIdx), durationStr);
                        filterParts.Add($"{available[srcIdx].Dequeue()}{volFilter}[out_a{j}]");
                        mapParts.Add($"-map \"[out_a{j}]\"");
                    }
                    else
                    {
                        extraInputParts.Add(silenceInput);
                        filterParts.Add($"[{silenceInputIdx}:a:0]{atrim}[out_a{j}]");
                        mapParts.Add($"-map \"[out_a{j}]\"");
                        silenceInputIdx++;
                    }
                    metaParts.Add($"-metadata:s:a:{j} title=\"{targetAudioLayout[j]}\"");
                }

                filterArgs = filterParts.Count > 0 ? $"-filter_complex \"{string.Join(";", filterParts)}\" " : "";
                extraInputArgs = extraInputParts.Count > 0 ? string.Join(" ", extraInputParts) + " " : "";
                mapArgs = string.Join(" ", mapParts) + " ";
                metadataArgs = string.Join(" ", metaParts) + " ";
            }
            else
            {
                // Legacy paths (keepSeparate=false or no track metadata)
                bool hasMutedTracks = mutedAudioTracks != null && mutedAudioTracks.Count > 0 && audioTrackNames != null && audioTrackNames.Count > 1;
                bool hasVolumeChanges = audioTrackVolumes != null && audioTrackVolumes.Any(kv => Math.Abs(kv.Value - 1.0) > 0.001);
                bool needsAudioProcessing = hasMutedTracks || (hasVolumeChanges && audioTrackNames != null && audioTrackNames.Count > 1);

                if (needsAudioProcessing && audioTrackNames != null)
                {
                    var enabledTracks = new List<int>();
                    for (int i = 1; i < audioTrackNames.Count; i++)
                    {
                        if (mutedAudioTracks == null || !mutedAudioTracks.Contains(i))
                            enabledTracks.Add(i);
                    }

                    if (enabledTracks.Count > 0)
                    {
                        bool anyVolChange = enabledTracks.Any(i => Math.Abs(GetTrackVolume(audioTrackVolumes, i) - 1.0) > 0.001);

                        if (enabledTracks.Count == 1 && !anyVolChange)
                        {
                            mapArgs = $"-map 0:v:0 -map 0:a:{enabledTracks[0]} ";
                        }
                        else
                        {
                            var filterPartsList = new List<string>();
                            var mixInputLabels = new List<string>();

                            foreach (int i in enabledTracks)
                            {
                                double vol = GetTrackVolume(audioTrackVolumes, i);
                                if (Math.Abs(vol - 1.0) > 0.001)
                                {
                                    filterPartsList.Add($"[0:a:{i}]volume={vol.ToString(CultureInfo.InvariantCulture)}[vol_{i}]");
                                    mixInputLabels.Add($"[vol_{i}]");
                                }
                                else
                                {
                                    mixInputLabels.Add($"[0:a:{i}]");
                                }
                            }

                            if (enabledTracks.Count == 1)
                            {
                                // Single track with volume change
                                filterArgs = $"-filter_complex \"{string.Join(";", filterPartsList)}\" ";
                                mapArgs = $"-map 0:v:0 -map \"{mixInputLabels[0]}\" ";
                            }
                            else
                            {
                                string allInputs = string.Join("", mixInputLabels);
                                filterPartsList.Add($"{allInputs}amix=inputs={enabledTracks.Count}:duration=longest:normalize=0[mix]");
                                filterArgs = $"-filter_complex \"{string.Join(";", filterPartsList)}\" ";
                                mapArgs = "-map 0:v:0 -map \"[mix]\" ";
                            }
                        }

                        metadataArgs = "-metadata:s:a:0 title=\"Full Mix\" ";
                    }
                }
                else if (settings.ClipKeepSeparateAudioTracks)
                {
                    // Keep all source audio streams even when metadata does not expose track names
                    // (e.g. some VRChat recordings), so FFmpeg won't collapse output to one track.
                    mapArgs = "-map 0:v:0 -map 0:a ";
                    if (audioTrackNames != null)
                    {
                        for (int i = 0; i < audioTrackNames.Count; i++)
                            metadataArgs += $"-metadata:s:a:{i} title=\"{audioTrackNames[i]}\" ";
                    }
                }
            }

            // When using the union layout, force a consistent sample rate on every output audio stream.
            // Source tracks may be 44.1 kHz while the anullsrc silence inputs are 48 kHz, so without this
            // each temp clip's per-slot sample rate depends on whether the slot got real audio or silence.
            // The concat demuxer then uses the first clip's stream params for subsequent clips, playing
            // mismatched samples at the wrong rate (the reported "shrunken audio").
            string audioRateArg = targetAudioLayout != null ? "-ar 48000 " : "";
            string verboseFlag = filterArgs.Length > 0 || extraInputArgs.Length > 0 ? "-v verbose " : "";
            string clipStartStr = startTime.ToString(CultureInfo.InvariantCulture);
            string clipDurationStr = duration.ToString(CultureInfo.InvariantCulture);

            string arguments = $"-y {verboseFlag}-ss {clipStartStr} -t {clipDurationStr} " +
                             $"-i \"{inputFilePath}\" {extraInputArgs}{filterArgs}{mapArgs}-c:v {videoCodec} {presetArgs} {qualityArgs} {fpsArg} " +
                             $"-c:a aac -b:a {settings.ClipAudioQuality} {audioRateArg}{metadataArgs}-t {clipDurationStr} -movflags +faststart \"{outputFilePath}\"";
            Log.Information("Extracting clip (re-encode)");
            Log.Information($"FFmpeg arguments: {arguments}");

            try
            {
                await FFmpegService.RunWithProgress(clipId, arguments, duration, progressCallback, process =>
                {
                    // Track the process so it can be cancelled
                    lock (ProcessLock)
                    {
                        if (!ActiveFFmpegProcesses.ContainsKey(clipId))
                        {
                            ActiveFFmpegProcesses[clipId] = new List<Process>();
                        }
                        ActiveFFmpegProcesses[clipId].Add(process);
                        Log.Information($"[Clip {clipId}] Tracking FFmpeg process (PID: {process.Id})");
                    }
                });
            }
            finally
            {
                // Clean up the process from tracking after completion or error
                lock (ProcessLock)
                {
                    ActiveFFmpegProcesses.Remove(clipId);
                    Log.Information($"[Clip {clipId}] Removed from active processes");
                }
            }
        }


        public static async void CancelClip(int clipId)
        {
            Log.Information($"[Clip {clipId}] Cancel requested");

            bool wasCancelled = false;

            lock (ProcessLock)
            {
                if (ActiveFFmpegProcesses.TryGetValue(clipId, out var processes))
                {
                    // Mark as cancelled so the resulting FFmpeg failure isn't surfaced as an error
                    CancelledClipIds.Add(clipId);
                    Log.Information($"[Clip {clipId}] Found {processes.Count} active process(es) to cancel");

                    foreach (var process in processes.ToList())
                    {
                        try
                        {
                            int processId = process.Id; // Capture ID before killing

                            if (!process.HasExited)
                            {
                                Log.Information($"[Clip {clipId}] Killing FFmpeg process (PID: {processId})");
                                process.Kill(true); // Force kill the process and child processes
                                Log.Information($"[Clip {clipId}] Successfully killed process (PID: {processId})");
                            }
                            else
                            {
                                Log.Information($"[Clip {clipId}] Process (PID: {processId}) already exited");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[Clip {clipId}] Error killing FFmpeg process: {ex.Message}");
                        }
                    }

                    ActiveFFmpegProcesses.Remove(clipId);
                    Log.Information($"[Clip {clipId}] Removed from active processes after cancellation");
                    wasCancelled = true;
                }
                else
                {
                    Log.Warning($"[Clip {clipId}] No active processes found to cancel (may have already completed)");
                }
            }

            if (wasCancelled)
            {
                await MessageService.SendFrontendMessage("ClipProgress", new { id = clipId, progress = 100, segments = new List<Segment>() });
            }
        }

        private static string ResolveSegmentInputPath(Segment segment, string videoFolder)
        {
            if (!string.IsNullOrEmpty(segment.FilePath) && File.Exists(segment.FilePath))
                return segment.FilePath;

            string inputGameFolder = StorageService.SanitizeGameNameForFolder(segment.Game);
            var segmentType = Enum.Parse<Content.ContentType>(segment.Type);
            string inputFolderName = FolderNames.GetVideoFolderName(segmentType);
            return Path.Combine(videoFolder, inputFolderName, inputGameFolder, $"{segment.FileName}.mp4");
        }

        private static string GetSanitizedOriginalFileBase(Segment segment, string resolvedInputPath)
        {
            string baseName = !string.IsNullOrEmpty(segment.FileName)
                ? Path.GetFileNameWithoutExtension(segment.FileName)
                : Path.GetFileNameWithoutExtension(resolvedInputPath);
            return StorageService.SanitizeGameNameForFolder(baseName);
        }

        /// <summary>First available index i so that <c>{originalBase}_clip_{i}.mp4</c> does not exist in the folder.</summary>
        private static int GetNextAvailableClipIndex(string outputFolder, string sanitizedOriginalBase)
        {
            int i = 1;
            while (File.Exists(Path.Combine(outputFolder, $"{sanitizedOriginalBase}_clip_{i}.mp4")))
                i++;
            return i;
        }

        private static List<string>? BuildUnionAudioLayout(List<List<string>?> perSegmentTrackNames)
        {
            var union = new List<string> { "Full Mix" };
            foreach (var trackNames in perSegmentTrackNames)
            {
                if (trackNames == null) continue;
                foreach (var name in trackNames.Skip(1))
                {
                    if (!union.Any(u => string.Equals(u, name, StringComparison.OrdinalIgnoreCase)))
                        union.Add(name);
                }
            }
            return union.Count > 1 ? union : null;
        }

        private static List<string>? GetSourceAudioTrackNames(Segment segment)
        {
            try
            {
                var contentType = Enum.Parse<Content.ContentType>(segment.Type);
                string metadataFolderPath = FolderNames.GetMetadataFolderPath(contentType);
                string metadataFilePath = PathUtils.Combine(metadataFolderPath, $"{segment.FileName}.json");

                if (File.Exists(metadataFilePath))
                {
                    var metadataContent = File.ReadAllText(metadataFilePath);
                    var metadata = JsonSerializer.Deserialize<Content>(metadataContent);
                    if (metadata?.AudioTrackNames != null && metadata.AudioTrackNames.Count > 1)
                    {
                        return metadata.AudioTrackNames;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to read source audio track names: {ex.Message}");
            }

            return null;
        }

        private static void SafeDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) { Log.Information($"Error deleting file {path}: {ex.Message}"); }
        }

        private static string WithAtrim(string filterChain, string durationStr)
        {
            if (filterChain == "acopy")
                return $"atrim=end={durationStr},asetpts=PTS-STARTPTS";
            return $"{filterChain},atrim=end={durationStr},asetpts=PTS-STARTPTS";
        }

        private static double GetTrackVolume(Dictionary<int, double>? audioTrackVolumes, int trackIndex)
        {
            if (audioTrackVolumes != null && audioTrackVolumes.TryGetValue(trackIndex, out double vol))
                return Math.Max(0, Math.Min(1, vol));
            return 1.0;
        }

        private static string GetVolumeFilter(Dictionary<int, double>? audioTrackVolumes, int trackIndex)
        {
            double vol = GetTrackVolume(audioTrackVolumes, trackIndex);
            if (Math.Abs(vol - 1.0) > 0.001)
                return $"volume={vol.ToString(CultureInfo.InvariantCulture)}";
            return "acopy";
        }
    }
}
