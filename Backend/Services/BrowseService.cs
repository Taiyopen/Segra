using Segra.Backend.App;
using Segra.Backend.Shared;
using Serilog;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Segra.Backend.Services
{
    /// <summary>
    /// Lists local folders for the in-app video file browser and tracks
    /// authorized roots so ContentServer can stream those files.
    /// </summary>
    public static class BrowseService
    {
        private static readonly ConcurrentDictionary<string, byte> AuthorizedRoots = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v"];

        public static void AuthorizeRoot(string path)
        {
            string? canonical = TryCanonicalizeDirectory(path);
            if (canonical == null) return;
            AuthorizedRoots[canonical] = 0;
        }

        public static bool IsPathAuthorized(string canonicalFilePath)
        {
            foreach (var root in AuthorizedRoots.Keys)
            {
                string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
                    ? root
                    : root + Path.DirectorySeparatorChar;

                if (canonicalFilePath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                    canonicalFilePath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static async Task HandleListDirectory(JsonElement parameters)
        {
            try
            {
                string? requestedPath = null;
                if (parameters.ValueKind == JsonValueKind.Object &&
                    parameters.TryGetProperty("Path", out JsonElement pathElement) &&
                    pathElement.ValueKind == JsonValueKind.String)
                {
                    requestedPath = pathElement.GetString();
                }

                if (string.IsNullOrWhiteSpace(requestedPath))
                {
                    requestedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                    if (string.IsNullOrWhiteSpace(requestedPath) || !Directory.Exists(requestedPath))
                    {
                        requestedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    }
                }

                string? canonical = TryCanonicalizeDirectory(requestedPath);
                if (canonical == null || !Directory.Exists(canonical))
                {
                    await MessageService.SendFrontendMessage("BrowseDirectoryResult", new
                    {
                        ok = false,
                        error = "找不到此資料夾",
                        path = requestedPath
                    });
                    return;
                }

                AuthorizeRoot(canonical);

                string? parent = Directory.GetParent(canonical)?.FullName;
                var folderEntries = new List<(string name, string path)>();
                var videoEntries = new List<(string name, string path, long sizeBytes, string modifiedAt)>();

                try
                {
                    foreach (string dir in Directory.EnumerateDirectories(canonical))
                    {
                        try
                        {
                            var info = new DirectoryInfo(dir);
                            if ((info.Attributes & FileAttributes.Hidden) != 0 ||
                                (info.Attributes & FileAttributes.System) != 0)
                            {
                                continue;
                            }

                            folderEntries.Add((info.Name, PathUtils.Normalize(info.FullName)));
                        }
                        catch
                        {
                            // Skip inaccessible entries
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed enumerating directories under {Path}", canonical);
                }

                try
                {
                    foreach (string file in Directory.EnumerateFiles(canonical))
                    {
                        try
                        {
                            string ext = Path.GetExtension(file);
                            if (!VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                                continue;

                            var info = new FileInfo(file);
                            if ((info.Attributes & FileAttributes.Hidden) != 0)
                                continue;

                            videoEntries.Add((
                                info.Name,
                                PathUtils.Normalize(info.FullName),
                                info.Length,
                                info.LastWriteTimeUtc.ToString("o")));
                        }
                        catch
                        {
                            // Skip inaccessible entries
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed enumerating files under {Path}", canonical);
                }

                folderEntries.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                videoEntries.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

                var folders = folderEntries.Select(f => new
                {
                    name = f.name,
                    path = f.path,
                    kind = "folder"
                }).ToList();

                var videos = videoEntries.Select(v => new
                {
                    name = v.name,
                    path = v.path,
                    kind = "video",
                    sizeBytes = v.sizeBytes,
                    modifiedAt = v.modifiedAt
                }).ToList();

                await MessageService.SendFrontendMessage("BrowseDirectoryResult", new
                {
                    ok = true,
                    path = PathUtils.Normalize(canonical),
                    parentPath = parent != null ? PathUtils.Normalize(parent) : null,
                    folders,
                    videos
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HandleListDirectory failed");
                await MessageService.SendFrontendMessage("BrowseDirectoryResult", new
                {
                    ok = false,
                    error = ex.Message
                });
            }
        }

        public static async Task HandleSelectFolder()
        {
            string? selectedPath = null;
            var tcs = new TaskCompletionSource<string?>();

            var staThread = new Thread(() =>
            {
                try
                {
                    using var fbd = new FolderBrowserDialog
                    {
                        Description = "選擇要瀏覽的資料夾",
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = false
                    };

                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        tcs.SetResult(fbd.SelectedPath);
                    }
                    else
                    {
                        tcs.SetResult(null);
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            selectedPath = await tcs.Task;

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                Log.Information("Browse folder selection cancelled");
                return;
            }

            AuthorizeRoot(selectedPath);
            await HandleListDirectory(JsonSerializer.SerializeToElement(new { Path = selectedPath }));
        }

        private static string? TryCanonicalizeDirectory(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (!Directory.Exists(full))
                    return null;
                return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return null;
            }
        }
    }
}
