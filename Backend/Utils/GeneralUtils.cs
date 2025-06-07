using Serilog;

namespace Segra.Backend.Utils
{
    public static class GeneralUtils
    {
        public enum GpuVendor
        {
            Unknown,
            Nvidia,
            AMD,
            Intel
        }

        private static readonly List<string> internalGpuIdentifiers = new List<string>
        {
            // Intel integrated GPUs
            "HD Graphics",         // Broadwell (5xxx), Skylake (510–530), Kaby Lake (610/620), Comet Lake, etc.
            "Iris Graphics",       // Skylake Iris 540/550
            "Iris Pro Graphics",   // Broadwell Iris Pro 6200
            "Iris Plus Graphics",  // Kaby Lake / Whiskey Lake
            "UHD Graphics",        // Coffee Lake and newer
            "Iris Xe Graphics",    // Tiger Lake and newer

            // AMD integrated GPUs
            "Radeon R7 Graphics",    // Kaveri / Carrizo APU series
            "Radeon R5 Graphics",    // Kaveri / Carrizo APU series
            "Radeon Vega",           // Raven Ridge / Picasso / Renoir APUs (e.g. Vega 8, Vega 11)
            "Radeon Graphics"        // Zen+ / Zen2 APUs (generic naming on 4000/5000G “Graphics”)
        };

        // Cache the detected GPU vendor to avoid repeated WMI queries
        private static GpuVendor? _cachedGpuVendor = null;

        public static GpuVendor DetectGpuVendor()
        {
            // Return cached value if available
            if (_cachedGpuVendor.HasValue)
            {
                return _cachedGpuVendor.Value;
            }

            try
            {
                // First try to find active displays - these are GPUs actually connected to monitors
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_VideoController WHERE CurrentHorizontalResolution > 0 AND CurrentVerticalResolution > 0"))
                {
                    List<System.Management.ManagementObject> gpus = searcher.Get().Cast<System.Management.ManagementObject>().ToList();
                    
                    // Sort GPUs - external GPUs first, then internal ones
                    gpus.Sort((a, b) => {
                        string nameA = a["Name"]?.ToString() ?? string.Empty;
                        string nameB = b["Name"]?.ToString() ?? string.Empty;
                        
                        bool isAInternal = internalGpuIdentifiers.Any(id => nameA.Contains(id, StringComparison.OrdinalIgnoreCase));
                        bool isBInternal = internalGpuIdentifiers.Any(id => nameB.Contains(id, StringComparison.OrdinalIgnoreCase));
                        
                        // External GPUs come first (false before true)
                        return isAInternal.CompareTo(isBInternal);
                    });
                    
                    foreach (var gpu in gpus)
                    {
                        string name = gpu["Name"]?.ToString()?.ToLower() ?? string.Empty;
                        
                        if (name.Contains("nvidia"))
                        {
                            Log.Information($"Detected NVIDIA GPU: {gpu["Name"]}");
                            _cachedGpuVendor = GpuVendor.Nvidia;
                            return GpuVendor.Nvidia;
                        }
                        else if (name.Contains("amd") || name.Contains("radeon") || name.Contains("ati"))
                        {
                            Log.Information($"Detected AMD GPU: {gpu["Name"]}");
                            _cachedGpuVendor = GpuVendor.AMD;
                            return GpuVendor.AMD;
                        }
                        else if (name.Contains("intel"))
                        {
                            Log.Information($"Detected Intel GPU: {gpu["Name"]}");
                            _cachedGpuVendor = GpuVendor.Intel;
                            return GpuVendor.Intel;
                        }
                    }
                }
                
                // Fallback: check all video controllers if the above didn't find any active ones
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (System.Management.ManagementObject gpu in searcher.Get().Cast<System.Management.ManagementObject>())
                    {
                        string name = gpu["Name"]?.ToString()?.ToLower() ?? string.Empty;
                        
                        if (name.Contains("nvidia"))
                        {
                            Log.Information($"Detected NVIDIA GPU: {gpu["Name"]}");
                            _cachedGpuVendor = GpuVendor.Nvidia;
                            return GpuVendor.Nvidia;
                        }
                        else if (name.Contains("amd") || name.Contains("radeon") || name.Contains("ati"))
                        {
                            Log.Information($"Detected AMD GPU: {gpu["Name"]}");
                            _cachedGpuVendor = GpuVendor.AMD;
                            return GpuVendor.AMD;
                        }
                        else if (name.Contains("intel"))
                        {
                            Log.Information($"Detected Intel GPU: {gpu["Name"]}");
                            _cachedGpuVendor = GpuVendor.Intel;
                            return GpuVendor.Intel;
                        }
                    }
                }
                
                Log.Warning("Could not identify GPU vendor, will default to CPU encoding if GPU encoding is selected");
                return GpuVendor.Unknown;
            }
            catch (Exception ex)
            {
                Log.Error($"Error detecting GPU vendor: {ex.Message}");
                return GpuVendor.Unknown;
            }
        }
    }
}