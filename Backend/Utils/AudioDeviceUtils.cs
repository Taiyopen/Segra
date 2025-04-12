using NAudio.CoreAudioApi;
using Segra.Models;
using System.Text.RegularExpressions;

namespace Segra.Backend.Utils
{
    internal class AudioDeviceUtils
    {
        private static string GetCleanDeviceName(string friendlyName)
        {

            // If it's Voicemeeter, return the original name
            if (friendlyName.Contains("Voicemeeter"))
            {
                return friendlyName;
            }

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

            // Sort devices by name (except the default which stays at the top)
            var defaultDev = devices[0];
            var sortedDevices = devices.Skip(1).OrderBy(d => d.Name).ToList();
            sortedDevices.Insert(0, defaultDev);
            
            return sortedDevices;
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

            // Sort devices by name (except the default which stays at the top)
            var defaultDev = devices[0];
            var sortedDevices = devices.Skip(1).OrderBy(d => d.Name).ToList();
            sortedDevices.Insert(0, defaultDev);
            
            return sortedDevices;
        }
    }
}
