using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Segra.Backend.Core.Models;
using Segra.Backend.Media;
using Segra.Backend.Recorder;
using Segra.Backend.Windows.Storage;
using Serilog;
using static Segra.Backend.Shared.GeneralUtils;

namespace Segra.Backend.Games.VrChat
{
    /// <summary>
    /// Tails the latest VRChat Unity output log. Uses <c>[VVMW] PlaybackStart</c> / <c>PlaybackEnded</c>
    /// lines (pipe-separated Url, Performer, …). <c>PlaybackEnded</c> must include <c>Url</c> and it must match
    /// <c>PlaybackStart</c> after normalization (same video). Pending start/Url is cleared when <c>PlaybackEnded</c>
    /// is processed (or discarded), so a later <c>PlaybackStart</c> never reuses a previous play. Duplicate
    /// <c>PlaybackStart</c> lines for the same Url before an end keep the <b>first</b> start time. Clip metadata title:
    /// <c>表演者 - 影片標題</c> (oEmbed, or HTTP
    /// <c>Content-Disposition</c> filename for direct/API download URLs, then URL fallback).
    /// Output file: <c>yyyy-MM-dd_{Performer}_{title}</c>. Clips prefer replay buffer tail; falls back to session file.
    /// </summary>
    internal sealed class VrChatVvmwIntegration : Integration
    {
        /// <summary>IGDB id for VRChat (used for clip metadata when exe is not in the games list).</summary>
        internal const int IgdbId = 33615;

        /// <summary>PlaybackStart→PlaybackEnded wall time under this (seconds) is ignored (no clip).</summary>
        internal const double MinWallClipSeconds = 5;
        internal const double ClipLeadPaddingSeconds = 2;
        internal const double ClipTailPaddingSeconds = 5;

        private static readonly object DeferredLock = new();
        private static readonly List<DeferredVvmwClip> Deferred = new();

        private static readonly HttpClient OembedHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12),
        };

        private CancellationTokenSource? _cts;
        private Task? _loop;

        private readonly object _stateLock = new();
        /// <summary>Log time from the pending play’s first <c>PlaybackStart</c> (duplicates with same Url ignored).</summary>
        private DateTime? _playbackStartLogLocal;
        /// <summary><c>Url=</c> from that first <c>PlaybackStart</c>; cleared when <c>PlaybackEnded</c> completes or aborts the pair.</summary>
        private string? _playbackUrlFromStart;

        private string _partialLineBuffer = "";
        private string? _currentLogPath;
        private long _filePosition;

        private static string VrChatLogDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "VRChat", "VRChat");

        public override Task Start()
        {
            _cts = new CancellationTokenSource();
            Log.Information("[VRChat VVMW] Starting output log monitor");
            _loop = Task.Run(() => PollLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        public override Task Shutdown()
        {
            Log.Information("[VRChat VVMW] Stopping output log monitor");
            try
            {
                _cts?.Cancel();
                _loop?.Wait(TimeSpan.FromSeconds(8));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[VRChat VVMW] Shutdown wait");
            }

            _cts?.Dispose();
            _cts = null;
            _loop = null;
            return Task.CompletedTask;
        }

        /// <summary>Retry deferred clips after the session file is finalized (e.g. OBS released the file).</summary>
        public static async Task FlushDeferredClipsAsync()
        {
            List<DeferredVvmwClip> batch;
            lock (DeferredLock)
            {
                if (Deferred.Count == 0)
                    return;
                batch = new List<DeferredVvmwClip>(Deferred);
                Deferred.Clear();
            }

            foreach (var d in batch)
            {
                try
                {
                    if (string.IsNullOrEmpty(d.SessionFilePath) || !File.Exists(d.SessionFilePath))
                        continue;

                    await EnsureFileReady(d.SessionFilePath);
                    var rec = new Recording
                    {
                        Game = d.Game,
                        FileName = Path.GetFileName(d.SessionFilePath) ?? "session.mp4",
                        FilePath = d.SessionFilePath,
                        ExePath = "VRChat.exe",
                        StartTime = DateTime.Now,
                    };
                    await ClipService.CreateClipFromSessionTimeRange(
                        rec,
                        d.StartSec,
                        d.EndSec,
                        d.Title,
                        d.FileNameBase,
                        clipIgdbId: IgdbId,
                        appendTimestampToPreferredFileName: false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[VRChat VVMW] Deferred clip still failed");
                }
            }
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!Directory.Exists(VrChatLogDirectory))
                    {
                        await Task.Delay(1000, ct);
                        continue;
                    }

                    string? latest = Directory.GetFiles(VrChatLogDirectory, "output_log_*.txt")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault()?.FullName;

                    if (latest == null)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }

                    if (!string.Equals(_currentLogPath, latest, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentLogPath = latest;
                        _filePosition = 0;
                        _partialLineBuffer = "";
                        Log.Information($"[VRChat VVMW] Tailing {_currentLogPath}");
                    }

                    await ReadAndProcessNewBytes(latest, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[VRChat VVMW] Poll loop error");
                    await Task.Delay(1000, ct);
                }
            }
        }

        private async Task ReadAndProcessNewBytes(string filePath, CancellationToken ct)
        {
            long len;
            try
            {
                len = new FileInfo(filePath).Length;
            }
            catch
            {
                await Task.Delay(250, ct);
                return;
            }

            if (len < _filePosition)
            {
                _filePosition = 0;
                _partialLineBuffer = "";
            }

            if (_filePosition >= len)
            {
                await Task.Delay(250, ct);
                return;
            }

            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(_filePosition, SeekOrigin.Begin);
            int toRead = (int)(len - _filePosition);
            var buf = new byte[toRead];
            int n = await fs.ReadAsync(buf.AsMemory(0, toRead), ct);
            _filePosition = len;

            _partialLineBuffer += Encoding.UTF8.GetString(buf, 0, n);

            while (true)
            {
                int nl = _partialLineBuffer.IndexOf('\n');
                if (nl < 0)
                    break;

                string line = _partialLineBuffer.Substring(0, nl).TrimEnd('\r');
                _partialLineBuffer = _partialLineBuffer.Substring(nl + 1);
                ProcessLine(line);
            }
        }

        private void ProcessLine(string line)
        {
            if (!line.Contains("[VVMW]", StringComparison.Ordinal))
                return;

            if (!TryParseLeadingTimestamp(line, out DateTime logLocal))
                return;

            if (line.Contains("PlaybackStart", StringComparison.Ordinal))
            {
                if (!TryParseVvmwPipeFields(line, "PlaybackStart", out var fields))
                    return;
                if (!fields.TryGetValue("Url", out string? url) || string.IsNullOrWhiteSpace(url))
                {
                    Log.Debug("[VRChat VVMW] PlaybackStart without Url — ignored");
                    return;
                }

                url = url.Trim();
                lock (_stateLock)
                {
                    if (_playbackStartLogLocal != null &&
                        _playbackUrlFromStart != null &&
                        string.Equals(NormalizeUrlForCompare(url), NormalizeUrlForCompare(_playbackUrlFromStart), StringComparison.Ordinal))
                    {
                        Log.Debug(
                            "[VRChat VVMW] Duplicate PlaybackStart (same normalized Url) ignored — keeping first start time at {Time:yyyy-MM-dd HH:mm:ss}",
                            _playbackStartLogLocal);
                        return;
                    }

                    _playbackStartLogLocal = logLocal;
                    _playbackUrlFromStart = url;
                }

                Log.Debug($"[VRChat VVMW] PlaybackStart at {logLocal:yyyy-MM-dd HH:mm:ss}: {url}");
                return;
            }

            if (line.Contains("PlaybackEnded", StringComparison.Ordinal))
            {
                if (!TryParseVvmwPipeFields(line, "PlaybackEnded", out var fields))
                    return;
                fields.TryGetValue("Url", out string? endUrl);
                fields.TryGetValue("Performer", out string? performer);
                endUrl = string.IsNullOrWhiteSpace(endUrl) ? null : endUrl.Trim();
                performer = string.IsNullOrWhiteSpace(performer) ? null : performer.Trim();

                DateTime? startAt;
                string? startUrl;
                lock (_stateLock)
                {
                    if (_playbackStartLogLocal == null || string.IsNullOrEmpty(_playbackUrlFromStart))
                    {
                        Log.Debug("[VRChat VVMW] PlaybackEnded without matching PlaybackStart — ignored");
                        return;
                    }

                    if (string.IsNullOrEmpty(endUrl))
                    {
                        Log.Warning(
                            "[VRChat VVMW] PlaybackEnded without Url — PlaybackStart/PlaybackEnded must both carry a matching Url; discarding pending start");
                        _playbackStartLogLocal = null;
                        _playbackUrlFromStart = null;
                        return;
                    }

                    if (!string.Equals(
                            NormalizeUrlForCompare(endUrl),
                            NormalizeUrlForCompare(_playbackUrlFromStart),
                            StringComparison.Ordinal))
                    {
                        Log.Warning(
                            "[VRChat VVMW] PlaybackEnded Url does not match PlaybackStart (end={End}, start={Start}) — skipping clip",
                            endUrl,
                            _playbackUrlFromStart);
                        _playbackStartLogLocal = null;
                        _playbackUrlFromStart = null;
                        return;
                    }

                    startAt = _playbackStartLogLocal;
                    startUrl = _playbackUrlFromStart;
                    _playbackStartLogLocal = null;
                    _playbackUrlFromStart = null;
                }

                _ = Task.Run(() => TryCreateClipAsync(startAt!.Value, logLocal, startUrl!, performer));
            }
        }

        private static async Task TryCreateClipAsync(
            DateTime startLocal,
            DateTime endLocal,
            string startUrl,
            string? performer)
        {
            double wallDuration = (endLocal - startLocal).TotalSeconds;
            if (wallDuration < MinWallClipSeconds)
            {
                Log.Debug($"[VRChat VVMW] Ignoring span under {MinWallClipSeconds}s ({wallDuration:0.###}s)");
                return;
            }

            DateTime paddedStartLocal = startLocal.AddSeconds(-ClipLeadPaddingSeconds);
            DateTime paddedEndLocal = endLocal.AddSeconds(ClipTailPaddingSeconds);
            double paddedWallDuration = (paddedEndLocal - paddedStartLocal).TotalSeconds;

            string videoTitle = await ResolveVideoDisplayTitleAsync(startUrl);
            string clipTitle = BuildClipMetadataTitle(performer, videoTitle);
            string fileNameBase = BuildVvmwClipFileNameBase(endLocal, performer, videoTitle, startUrl);
            string safeFileBase = StorageService.SanitizeGameNameForFolder(fileNameBase);

            Log.Information($"[VRChat VVMW] PlaybackEnded → clip title: “{clipTitle}” | file: {safeFileBase}.mp4 (padding -{ClipLeadPaddingSeconds:0}s/+{ClipTailPaddingSeconds:0}s)");

            // Wait for post-reaction tail before extracting/saving clip.
            if (ClipTailPaddingSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(ClipTailPaddingSeconds));

            if (await OBSService.TrySaveReplayBufferTailAsClipAsync(paddedWallDuration, clipTitle, safeFileBase, IgdbId))
                return;

            Recording? rec = FindVrChatSessionRecording();
            if (rec?.FilePath == null)
            {
                Log.Information("[VRChat VVMW] Replay buffer clip unavailable and no session file; skipping clip");
                return;
            }

            double startSec = (paddedStartLocal - rec.StartTime).TotalSeconds;
            double endSec = (paddedEndLocal - rec.StartTime).TotalSeconds;
            if (startSec < 0)
                startSec = 0;

            double wallEnd = (DateTime.Now - rec.StartTime).TotalSeconds + 1.5;
            endSec = Math.Min(endSec, wallEnd);

            if (endSec - startSec < MinWallClipSeconds)
            {
                Log.Debug("[VRChat VVMW] Session clip range under minimum; skipping");
                return;
            }

            try
            {
                await EnsureFileReady(rec.FilePath);
                await ClipService.CreateClipFromSessionTimeRange(
                    rec,
                    startSec,
                    endSec,
                    clipTitle,
                    safeFileBase,
                    clipIgdbId: IgdbId,
                    appendTimestampToPreferredFileName: false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[VRChat VVMW] Session clip extraction failed; will retry when session file is finalized");
                lock (DeferredLock)
                {
                    Deferred.Add(new DeferredVvmwClip(rec.FilePath, startSec, endSec, clipTitle, safeFileBase, rec.Game, IgdbId));
                }
            }
        }

        /// <summary>Clip library title shown in UI / metadata: <c>表演者 - 影片標題</c> (unknown performer → <c>未知</c>).</summary>
        private static string BuildClipMetadataTitle(string? performer, string videoTitle)
        {
            string p = string.IsNullOrWhiteSpace(performer) ? "未知" : performer.Trim();
            return $"{p} - {videoTitle}";
        }

        /// <summary>File name without extension: <c>yyyy-MM-dd_{表演者}_{影片標題}</c>.</summary>
        private static string BuildVvmwClipFileNameBase(DateTime endedLocal, string? performer, string videoDisplayTitle, string startUrl)
        {
            string datePart = endedLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string performerPart = string.IsNullOrWhiteSpace(performer)
                ? "未知"
                : StorageService.SanitizeGameNameForFolder(performer.Trim());
            string titlePart = StorageService.SanitizeGameNameForFolder(videoDisplayTitle.Trim());
            if (string.IsNullOrEmpty(titlePart) || string.Equals(titlePart, "Unknown", StringComparison.Ordinal))
                titlePart = StorageService.SanitizeGameNameForFolder(TitleFromVideoUrlFallback(startUrl));
            return $"{datePart}_{performerPart}_{titlePart}";
        }

        /// <summary>Fetches display title (e.g. YouTube oEmbed); falls back to URL heuristics.</summary>
        private static async Task<string> ResolveVideoDisplayTitleAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return TitleFromVideoUrlFallback(null);

            string pageUrl = url.Trim();
            if (!pageUrl.Contains("://", StringComparison.Ordinal))
                pageUrl = "https://" + pageUrl;

            try
            {
                Uri uri = new Uri(pageUrl, UriKind.Absolute);
                string host = uri.Host;

                if (host.Contains("youtu", StringComparison.OrdinalIgnoreCase))
                {
                    string oembedUrl = "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(pageUrl);
                    using var req = new HttpRequestMessage(HttpMethod.Get, oembedUrl);
                    req.Headers.TryAddWithoutValidation("User-Agent", "Segra/1.0 (VRChat VVMW)");
                    using HttpResponseMessage resp = await OembedHttp.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        await using Stream stream = await resp.Content.ReadAsStreamAsync();
                        using JsonDocument doc = await JsonDocument.ParseAsync(stream);
                        if (doc.RootElement.TryGetProperty("title", out JsonElement titleEl))
                        {
                            string? t = titleEl.GetString();
                            if (!string.IsNullOrWhiteSpace(t))
                                return t.Trim();
                        }
                    }
                }
                else if (host.Contains("vimeo", StringComparison.OrdinalIgnoreCase))
                {
                    string oembedUrl = "https://vimeo.com/api/oembed.json?url=" + Uri.EscapeDataString(pageUrl);
                    using var req = new HttpRequestMessage(HttpMethod.Get, oembedUrl);
                    req.Headers.TryAddWithoutValidation("User-Agent", "Segra/1.0 (VRChat VVMW)");
                    using HttpResponseMessage resp = await OembedHttp.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        await using Stream stream = await resp.Content.ReadAsStreamAsync();
                        using JsonDocument doc = await JsonDocument.ParseAsync(stream);
                        if (doc.RootElement.TryGetProperty("title", out JsonElement titleEl))
                        {
                            string? t = titleEl.GetString();
                            if (!string.IsNullOrWhiteSpace(t))
                                return t.Trim();
                        }
                    }
                }
                else
                {
                    // Direct file / API download: no oEmbed — use filename from Content-Disposition when present.
                    string? fromHttp = await TryResolveTitleFromHttpFileUrlAsync(pageUrl);
                    if (!string.IsNullOrWhiteSpace(fromHttp))
                        return fromHttp.Trim();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[VRChat VVMW] oEmbed title lookup failed; using URL fallback");
            }

            return TitleFromVideoUrlFallback(url);
        }

        /// <summary>
        /// Uses HEAD, then a ranged GET, to read <c>Content-Disposition</c> <c>filename</c> / <c>filename*</c>.
        /// Many download APIs expose a human-readable name there; otherwise returns null.
        /// </summary>
        private static async Task<string?> TryResolveTitleFromHttpFileUrlAsync(string pageUrl)
        {
            try
            {
                using (var headReq = new HttpRequestMessage(HttpMethod.Head, pageUrl))
                {
                    headReq.Headers.TryAddWithoutValidation("User-Agent", "Segra/1.0 (VRChat VVMW)");
                    using HttpResponseMessage headResp = await OembedHttp.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead);
                    string? t = ExtractTitleFromContentDispositionResponse(headResp);
                    if (!string.IsNullOrWhiteSpace(t))
                        return t;
                }

                using var getReq = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                getReq.Headers.Range = new RangeHeaderValue(0, 0);
                getReq.Headers.TryAddWithoutValidation("User-Agent", "Segra/1.0 (VRChat VVMW)");
                using HttpResponseMessage getResp = await OembedHttp.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead);
                try
                {
                    string? t2 = ExtractTitleFromContentDispositionResponse(getResp);
                    if (!string.IsNullOrWhiteSpace(t2))
                        return t2;
                    return null;
                }
                finally
                {
                    await SafeDrainResponseBodyIfSmallAsync(getResp);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[VRChat VVMW] HTTP Content-Disposition probe failed for {Url}", pageUrl);
                return null;
            }
        }

        private static async Task SafeDrainResponseBodyIfSmallAsync(HttpResponseMessage resp)
        {
            try
            {
                if (resp.StatusCode == HttpStatusCode.PartialContent)
                {
                    await resp.Content.CopyToAsync(Stream.Null);
                    return;
                }

                long? len = resp.Content.Headers.ContentLength;
                if (len is > 0 and <= 8192)
                    await resp.Content.CopyToAsync(Stream.Null);
            }
            catch
            {
                // Ignore incomplete drain; connection will be recycled.
            }
        }

        private static string? ExtractTitleFromContentDispositionResponse(HttpResponseMessage resp)
        {
            string? raw = null;
            if (resp.Content.Headers.TryGetValues("Content-Disposition", out IEnumerable<string>? vals))
                raw = string.Join(" ", vals);

            ContentDispositionHeaderValue? cd = resp.Content.Headers.ContentDisposition;
            if (cd == null && !string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    cd = ContentDispositionHeaderValue.Parse(raw);
                }
                catch
                {
                    cd = null;
                }
            }

            if (cd != null)
            {
                string? n = !string.IsNullOrWhiteSpace(cd.FileNameStar) ? cd.FileNameStar : cd.FileName;
                if (!string.IsNullOrWhiteSpace(n))
                    return FileNameStemAsVideoTitle(n.Trim().Trim('"'));
            }

            if (!string.IsNullOrWhiteSpace(raw) && TryParseFilenameFromRawContentDisposition(raw, out string fn))
                return FileNameStemAsVideoTitle(fn);

            return null;
        }

        private static bool TryParseFilenameFromRawContentDisposition(string raw, out string fileName)
        {
            fileName = "";
            Match m = Regex.Match(raw, @"filename\*\s*=\s*[^']*''([^;\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
            {
                string enc = m.Groups[1].Value.Trim().Trim('"');
                try
                {
                    fileName = Uri.UnescapeDataString(enc);
                    return fileName.Length > 0;
                }
                catch
                {
                    return false;
                }
            }

            m = Regex.Match(raw, @"filename\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
            {
                fileName = m.Groups[1].Value.Trim();
                return fileName.Length > 0;
            }

            m = Regex.Match(raw, @"filename\s*=\s*([^;\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success)
                return false;
            fileName = m.Groups[1].Value.Trim().Trim('"');
            return fileName.Length > 0;
        }

        private static string FileNameStemAsVideoTitle(string fileName)
        {
            string name = Path.GetFileName(fileName.Replace('\\', '/'));
            string stem = Path.GetFileNameWithoutExtension(name);
            return string.IsNullOrEmpty(stem) ? name : stem;
        }

        private static string TitleFromVideoUrlFallback(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "VRChat video";

            string u = url.Trim();
            try
            {
                if (!u.Contains("://", StringComparison.Ordinal))
                    u = "https://" + u;

                var uri = new Uri(u, UriKind.Absolute);
                string host = uri.Host;

                if (host.Contains("youtu", StringComparison.OrdinalIgnoreCase))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        u,
                        @"(?:youtu\.be/|youtube\.com/watch\?v=|youtube\.com/embed/)([a-zA-Z0-9_-]{6,})",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                        return m.Groups[1].Value;
                }

                string last = uri.Segments.Length > 0
                    ? uri.Segments[^1].TrimEnd('/')
                    : "";
                if (!string.IsNullOrEmpty(last))
                    return StorageService.SanitizeGameNameForFolder(last);

                return StorageService.SanitizeGameNameForFolder(host);
            }
            catch
            {
                return StorageService.SanitizeGameNameForFolder(u);
            }
        }

        /// <summary>Canonical form so equivalent links (e.g. YouTube watch vs youtu.be, trailing slash) compare equal.</summary>
        private static string NormalizeUrlForCompare(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            string u = url.Trim();
            if (!u.Contains("://", StringComparison.Ordinal))
                u = "https://" + u;

            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri))
                return url.Trim();

            string host = uri.IdnHost ?? uri.Host;
            if (host.Contains("youtu", StringComparison.OrdinalIgnoreCase))
            {
                Match m = Regex.Match(
                    uri.AbsoluteUri,
                    @"(?:youtu\.be/|youtube\.com/watch\?v=|youtube\.com/embed/)([a-zA-Z0-9_-]{6,})",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (m.Success)
                    return "yt:" + m.Groups[1].Value.ToLowerInvariant();
            }

            var b = new UriBuilder(uri) { Fragment = "" };
            string path = b.Path;
            if (path.Length > 1 && path.EndsWith('/'))
                path = path.TrimEnd('/');
            return $"{b.Scheme.ToLowerInvariant()}://{b.Host.ToLowerInvariant()}{path}{b.Query}";
        }

        /// <summary>Parses <c>[VVMW] PlaybackStart |</c> or <c>PlaybackEnded |</c> pipe-separated <c>Key=value</c> segments.</summary>
        private static bool TryParseVvmwPipeFields(string line, string eventName, out Dictionary<string, string> fields)
        {
            fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string marker = "[VVMW] " + eventName;
            int i = line.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0)
                return false;

            i = line.IndexOf('|', i + marker.Length);
            if (i < 0)
                return false;

            string payload = line.Substring(i + 1).Trim();
            if (payload.Length == 0)
                return false;

            foreach (string segment in payload.Split(new[] { " | " }, StringSplitOptions.None))
            {
                string seg = segment.Trim();
                int eq = seg.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = seg.Substring(0, eq).Trim();
                string value = seg.Substring(eq + 1).Trim();
                if (key.Length > 0)
                    fields[key] = value;
            }

            return fields.Count > 0;
        }

        private static Recording? FindVrChatSessionRecording()
        {
            var r = AppState.Instance.Recording;
            if (r?.FilePath == null)
                return null;
            string exe = r.ExePath ?? "";
            if (exe.EndsWith("VRChat.exe", StringComparison.OrdinalIgnoreCase))
                return r;
            return null;
        }

        private static bool TryParseLeadingTimestamp(string line, out DateTime local)
        {
            local = default;
            if (line.Length < 19)
                return false;
            return DateTime.TryParseExact(
                line.AsSpan(0, 19),
                "yyyy.MM.dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out local);
        }

        private sealed record DeferredVvmwClip(
            string SessionFilePath,
            double StartSec,
            double EndSec,
            string Title,
            string FileNameBase,
            string Game,
            int? IgdbId);
    }
}
