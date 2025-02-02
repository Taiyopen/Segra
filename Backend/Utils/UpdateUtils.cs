using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Segra.Backend.Utils
{
    public static class UpdateUtils
    {
        public static async Task UpdateAppIfNecessary()
        {
            try
            {
                Log.Information("Checking if update is necessary");

                // Read access to Segra. Remove when Public
                string token = "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";

                var source = new GithubSource("https://github.com/Segergren/Segra", token, false);
                var mgr = new UpdateManager(source);

                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    Log.Information("No update available");
                    return; // no update available
                }

                Log.Information("Installing update " + newVersion);
                await mgr.DownloadUpdatesAsync(newVersion);

                mgr.ApplyUpdatesAndRestart(newVersion);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }
        }
    }
}
