using NAudio.CoreAudioApi;
using Segra.Backend.Core.Models;
using System.Text.RegularExpressions;

namespace Segra.Backend.Windows.Audio
{
    internal static class AudioDeviceService
    {
        private static string GetCleanDeviceName(string friendlyName)
        {

            // If it's Voicemeeter, Elgato, GoXLR or BEACN, return the original name
            if (friendlyName.Contains("Voicemeeter") || friendlyName.Contains("Elgato") || friendlyName.Contains("GoXLR") || friendlyName.Contains("BEACN"))
            {
                return friendlyName;
            }
            else if (friendlyName.Contains("SteelSeries Sonar"))
            {
                int index = friendlyName.IndexOf("(");
                if (index > 0)
                {
                    return friendlyName.Substring(0, index).Trim();
                }
            }

            // Looks for patterns like "Microphone (2- Shure MV7)" or "Speakers (Sound BlasterX AE-5 Plus)" or "Stereo Mix (Realtek(R) Audio)"
            // Extract the main part of the device name, handling cases with nested parentheses
            var mainPattern = @"^([^(]+)\((.+)\)$";
            var match = Regex.Match(friendlyName, mainPattern);

            if (match.Success && match.Groups.Count > 2)
            {
                // Group 2 contains everything inside the main parentheses
                var deviceName = match.Groups[2].Value.Trim();
                return deviceName;
            }

            // Fallback to original name if pattern doesn't match
            return friendlyName;
        }

        public static List<AudioDevice> GetInputDevices() => GetDevices(DataFlow.Capture, Role.Communications);

        public static List<AudioDevice> GetOutputDevices() => GetDevices(DataFlow.Render, Role.Console);

        // defaultRole intentionally uses Console/Communications (not Multimedia) to match the
        // device OBS selects for WASAPI capture.
        private static List<AudioDevice> GetDevices(DataFlow dataFlow, Role defaultRole)
        {
            var devices = new List<AudioDevice>();
            using var enumerator = new MMDeviceEnumerator();
            var collection = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);

            try
            {
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(dataFlow, defaultRole);
                if (defaultDevice != null)
                {
                    var defaultDeviceName = GetCleanDeviceName(defaultDevice.FriendlyName);
                    devices.Add(new AudioDevice
                    {
                        Id = defaultDevice.ID,
                        Name = defaultDeviceName + " (Default)",
                        IsDefault = true
                    });
                }
            }
            catch
            {
                // No default device available
            }

            if (collection != null)
            {
                foreach (var device in collection)
                {
                    if (device == null) continue;
                    if (devices.Any(d => d.Id == device.ID)) continue;

                    try
                    {
                        var cleanName = GetCleanDeviceName(device.FriendlyName);
                        devices.Add(new AudioDevice { Id = device.ID, Name = cleanName, IsDefault = false });
                    }
                    catch
                    {
                        // Device name is invalid
                    }
                }
            }

            if (devices.Count == 0)
            {
                return devices;
            }

            var defaultDev = devices.FirstOrDefault(d => d.IsDefault);
            var sortedDevices = (defaultDev != null ? devices.Where(d => !d.IsDefault) : devices)
                .OrderBy(d => d.Name)
                .ToList();

            if (defaultDev != null)
            {
                sortedDevices.Insert(0, defaultDev);
            }

            return sortedDevices;
        }

        /// <summary>
        /// Peak level (0–1) for a capture or render endpoint, using Windows' mixer meter (same devices as Segra routes into OBS).
        /// </summary>
        public static float GetEndpointPeak(string deviceId, DataFlow flow)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using MMDevice device = string.IsNullOrEmpty(deviceId) || deviceId == "default"
                    ? enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia)
                    : enumerator.GetDevice(deviceId);
                return device.AudioMeterInformation.MasterPeakValue;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Maximum peak across all configured devices (e.g. all selected mics or all desktop outputs).
        /// </summary>
        public static float GetMaxPeakForDeviceSettings(IReadOnlyList<DeviceSetting>? devices, DataFlow flow)
        {
            if (devices == null || devices.Count == 0)
                return 0f;

            float max = 0f;
            foreach (var d in devices)
            {
                if (string.IsNullOrEmpty(d.Id))
                    continue;
                max = Math.Max(max, GetEndpointPeak(d.Id, flow));
            }

            return max;
        }
    }
}
