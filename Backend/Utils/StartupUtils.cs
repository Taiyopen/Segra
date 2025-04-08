using System.Reflection;
using Serilog;

namespace Segra.Backend.Utils
{
    internal static class StartupUtils
    {
        public static void SetStartupStatus(bool enable)
        {
            try
            {
                string exePath = Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".exe");
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string linkPath = Path.Combine(startupFolder, "Segra.lnk");
                if (enable)
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    object shell = Activator.CreateInstance(shellType);
                    object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                    shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                    shortcut.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exePath) });
                    shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                    Log.Information("Added Segra to startup");
                }
                else
                {
                    if (File.Exists(linkPath)) File.Delete(linkPath);
                    Log.Information("Removed Segra from startup");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }
        }

        public static bool GetStartupStatus()
        {
            try
            {
                string linkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Segra.lnk");
                return File.Exists(linkPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                return false;
            }
        }
    }
}
