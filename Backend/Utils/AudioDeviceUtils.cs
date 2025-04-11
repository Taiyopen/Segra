using NAudio.CoreAudioApi;
using Segra.Models;
using System.Text.RegularExpressions;

namespace Segra.Backend.Utils
{
    internal class AudioDeviceUtils
    {
        private static string GetCleanDeviceName(string friendlyName)
        {
            // Looks for patterns like "Microphone (2- Shure MV7)" or "Speakers (Sound BlasterX AE-5 Plus)"
            var match = Regex.Match(friendlyName, @"\((?:\d+\-\s*)?([^\)]+)\)");
            
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value.Trim();
            }
            
            // Fallback to original name if pattern doesn't match
            return friendlyName;
        }
        
        public static List<AudioDevice> GetInputDevices()
        {
            var devices = new List<AudioDevice>();
            var enumerator = new MMDeviceEnumerator();
            var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

            // Add default device first with (Default)
            var defaultDeviceName = GetCleanDeviceName(defaultDevice.FriendlyName);
            devices.Add(new AudioDevice { 
                Id = defaultDevice.ID, 
                Name = defaultDeviceName + " (Default)", 
                IsDefault = true 
            });

            foreach (var device in collection)
            {
                if (device.ID != defaultDevice.ID)
                {
                    var cleanName = GetCleanDeviceName(device.FriendlyName);
                    devices.Add(new AudioDevice { Id = device.ID, Name = cleanName, IsDefault = false });
                }
            }

            return devices;
        }

        public static List<AudioDevice> GetOutputDevices()
        {
            var devices = new List<AudioDevice>();
            var enumerator = new MMDeviceEnumerator();
            var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            // Add default device first with (Default)
            var defaultDeviceName = GetCleanDeviceName(defaultDevice.FriendlyName);
            devices.Add(new AudioDevice { 
                Id = defaultDevice.ID, 
                Name = defaultDeviceName + " (Default)", 
                IsDefault = true 
            });

            foreach (var device in collection)
            {
                if (device.ID != defaultDevice.ID)
                {
                    var cleanName = GetCleanDeviceName(device.FriendlyName);
                    devices.Add(new AudioDevice { Id = device.ID, Name = cleanName, IsDefault = false });
                }
            }

            return devices;
        }
    }
}
