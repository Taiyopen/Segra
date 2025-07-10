using Segra.Backend.Models;
using Serilog;

namespace Segra.Backend.Utils
{
    public static class MigrationUtils
    {
        public static void AddAudioToContentIfMissing()
        {
            Log.Information("Migration: Adding audio to content if missing");
            List<Content> contents = Settings.Instance.State.Content;
            Log.Information("Migration: Content count: {ContentCount}", contents.Count);
            if(contents.Count == 0)
            {
                Log.Information("Migration: No content found");
                return;
            }

            string contentFolder = Settings.Instance.ContentFolder;
            string audioFolderPath = Path.Combine(contentFolder, ".audio");

            foreach (Content content in contents)
            {
                if(Settings.Instance.State.Recording != null)
                {
                    Log.Information("Migration: Recording in progress, skipping audio migration");
                    return;
                }

                try {
                    string audioFilePath = Path.Combine(audioFolderPath, content.Type.ToString().ToLower() + "s", $"{content.FileName}.mp3");
                    if (File.Exists(audioFilePath))
                    {
                        continue;
                    }
                
                    Log.Information("Migration: Audio file not found for content: {ContentFileName}. Creating audio file...", content.FileName);

                    // Run in a separate thread to not block the main thread. This might create a lot of threads and cause lag if there are a lot of files to process.
                    // But not running the create audio file instantly causes the migration to freeze after ~20 files.
                    // TODO: Find a better solution 
                    Task.Run(() => ContentUtils.CreateAudioFile(content.FilePath, content.Type));
                }
                catch (Exception ex) {
                    Log.Error("Migration: Error adding audio to content: {ExceptionMessage}", ex.Message);
                }
            }
        }
    }
}