using Segra.Backend.Core.Models;

namespace Segra.Backend.Recorder
{
    /// <summary>
    /// Which of OBS's six mixer tracks a multi-track recording writes, and what each track is called.
    /// Kept free of OBS types so it can be tested without libobs.
    /// </summary>
    internal static class AudioTrackLayout
    {
        public const int MaxTracks = 6;

        private const uint AllTracks = 0x3Fu;

        /// <summary>
        /// ORs together the tracks every configured source writes to. <paramref name="gameMask"/> and
        /// <paramref name="voiceMask"/> are null when that source isn't part of the recording.
        /// Falls back to track 1 so the output always has audio.
        /// </summary>
        public static uint ComputeTracksMask(
            IEnumerable<DeviceSetting> inputDevices,
            IEnumerable<DeviceSetting> outputDevices,
            uint? gameMask,
            uint? voiceMask)
        {
            uint mask = 0;
            foreach (var device in inputDevices.Concat(outputDevices).Where(d => !string.IsNullOrEmpty(d.Id)))
                mask |= device.AudioTrackMask & AllTracks;
            if (gameMask.HasValue)
                mask |= gameMask.Value & AllTracks;
            if (voiceMask.HasValue)
                mask |= voiceMask.Value & AllTracks;

            return mask == 0 ? 1u : mask;
        }

        /// <summary>Custom track names where set, otherwise "Track N".</summary>
        public static List<string> ResolveTrackNames(IReadOnlyList<string>? customNames)
        {
            var names = new List<string>(MaxTracks);
            for (int t = 0; t < MaxTracks; t++)
            {
                names.Add(customNames != null && t < customNames.Count && !string.IsNullOrWhiteSpace(customNames[t])
                    ? customNames[t].Trim()
                    : $"Track {t + 1}");
            }
            return names;
        }
    }
}
