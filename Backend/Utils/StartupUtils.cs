using System;
using System.IO;
using Serilog;

namespace Segra.Backend.Utils
{
    internal static class StartupUtils
    {
        public static void SetStartupStatus(bool enable)
        {
            try
            {
                string startMenuShortcut = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "Segra.lnk"
                );

                string startupShortcut = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    "Segra.lnk"
                );

                if (enable)
                {
                    if (File.Exists(startMenuShortcut))
                    {
                        File.Copy(startMenuShortcut, startupShortcut, true);
                        Log.Information("Copied Segra shortcut to startup folder");
                    }
                    else
                    {
                        Log.Error("Segra shortcut not found in Start Menu");
                    }
                }
                else
                {
                    if (File.Exists(startupShortcut))
                    {
                        File.Delete(startupShortcut);
                        Log.Information("Removed Segra shortcut from startup folder");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to set startup status: {ex.Message}");
            }
        }

        public static bool GetStartupStatus()
        {
            try
            {
                string startupShortcut = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    "Segra.lnk"
                );
                return File.Exists(startupShortcut);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to get startup status: {ex.Message}");
                return false;
            }
        }
    }
}
