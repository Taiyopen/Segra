using Microsoft.Win32;
using Serilog;
using System;
using System.IO;
using System.Reflection;

namespace Segra.Backend.Utils
{
    internal static class StartupUtils
    {
        private const string StartupKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string AppName = "Segra";

        public static void SetStartupStatus(bool enable)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupKey, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string appPath = Assembly.GetExecutingAssembly().Location;
                            // Use the actual executable, not the DLL
                            appPath = Path.ChangeExtension(appPath, ".exe");
                            key.SetValue(AppName, $"\"{appPath}\"");
                            Log.Information("Added Segra to startup programs");
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                            Log.Information("Removed Segra from startup programs");
                        }
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
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupKey, false))
                {
                    return key?.GetValue(AppName) != null;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to get startup status: {ex.Message}");
                return false;
            }
        }
    }
}
