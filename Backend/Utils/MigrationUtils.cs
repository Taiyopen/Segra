using Segra.Backend.Models;
using Serilog;

namespace Segra.Backend.Utils;

internal static class MigrationUtils
{
    private record Migration(string Id, Action Apply);

    private static string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra");
    private static string MigrationsFolder = Path.Combine(appDataDir, ".migrations");
    private static string AppliedPath => Path.Combine(MigrationsFolder, "applied.json");

    public static void RunMigrations()
    {
        try
        {
            EnsureStateStorage();

            var applied = LoadApplied();
            var migrations = GetMigrations();

            foreach (var migration in migrations)
            {
                if (applied.Contains(migration.Id))
                {
                    Log.Information("Migration already applied: {MigrationId}", migration.Id);
                    continue;
                }

                try
                {
                    Log.Information("Applying migration: {MigrationId}", migration.Id);
                    migration.Apply();
                    applied.Add(migration.Id);
                    SaveApplied(applied);
                    Log.Information("Migration completed: {MigrationId}", migration.Id);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Migration failed: {MigrationId}", migration.Id);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RunMigrations encountered an error");
        }
    }

    private static HashSet<string> LoadApplied()
    {
        try
        {
            if (!File.Exists(AppliedPath)) return new HashSet<string>();
            var json = File.ReadAllText(AppliedPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("applied", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                        set.Add(el.GetString() ?? string.Empty);
                }
            }
            return set;
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    private static void SaveApplied(HashSet<string> applied)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { applied = applied.ToArray() });
            File.WriteAllText(AppliedPath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed saving applied migrations state");
        }
    }

    private static void EnsureStateStorage()
    {
        try
        {
            if (!Directory.Exists(MigrationsFolder))
            {
                var dir = Directory.CreateDirectory(MigrationsFolder);
                try { dir.Attributes |= FileAttributes.Hidden; } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed ensuring migrations folder");
        }
    }

    private static List<Migration> GetMigrations()
    {
        return new List<Migration>
        {
            new("0001_waveforms_json", Apply_0001_WaveformsJson)
        };
    }

    // Migration 0001: Remove legacy .audio folder and generate waveform JSONs for existing content
    private static void Apply_0001_WaveformsJson()
    {
        string contentRoot = Settings.Instance.ContentFolder;

        // 1) Remove the legacy .audio folder if present
        try
        {
            string audioFolder = Path.Combine(contentRoot, ".audio");
            if (Directory.Exists(audioFolder))
            {
                Log.Information("Deleting legacy audio folder: {Path}", audioFolder);
                Directory.Delete(audioFolder, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete legacy .audio folder");
        }

        // 2) Generate waveform JSONs for each mp4 if missing
        foreach (Content.ContentType type in Enum.GetValues(typeof(Content.ContentType)))
        {
            string typeFolder = Path.Combine(contentRoot, type.ToString().ToLower() + "s");
            if (!Directory.Exists(typeFolder)) continue;

            string targetWaveformFolder = Path.Combine(contentRoot, ".waveforms", type.ToString().ToLower() + "s");
            if (!Directory.Exists(targetWaveformFolder))
            {
                var dir = Directory.CreateDirectory(targetWaveformFolder);
                try { dir.Attributes |= FileAttributes.Hidden; } catch { /* ignore */ }
            }

            foreach (var mp4 in Directory.EnumerateFiles(typeFolder, "*.mp4", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(mp4);
                    string jsonPath = Path.Combine(targetWaveformFolder, name + ".peaks.json");
                    if (File.Exists(jsonPath))
                    {
                        Log.Debug("Waveform already exists, skipping: {Path}", jsonPath);
                        continue;
                    }

                    Log.Information("Generating waveform for: {File}", mp4);
                    ContentUtils.CreateWaveformFile(mp4, type);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed generating waveform for file in migration: {File}", mp4);
                }
            }
        }
    }
}

