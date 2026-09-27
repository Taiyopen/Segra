using ObsKit.NET.Audio;
using ObsKit.NET.Sources;
using Serilog;

namespace Segra.Backend.Recorder
{
    /// <summary>
    /// Live levels of every OBS audio source Segra records, for the PiP mixer. Meters attach when the mixer is read.
    /// Audio sources must be disposed through <see cref="DisposeSource"/> so a read never touches a released source.
    /// </summary>
    public static class AudioMixerService
    {
        private const float SilenceDb = -100f;
        // OBS reports about every 50 ms while audio flows; older than this counts as silence
        private const long StaleMs = 300;

        public sealed record MixerSource(string Id, string Name, string Kind, float PeakDb, bool Muted, float Volume);

        private sealed class MeterEntry
        {
            public required AudioMeter Meter;
            public float PeakDb = SilenceDb;
            public long UpdatedAt;
        }

        private static readonly object _lock = new();
        private static readonly Dictionary<Source, MeterEntry> _meters = new(ReferenceEqualityComparer.Instance);
        private static bool _shutDown;

        public static List<MixerSource> GetSnapshot()
        {
            var sources = OBSService.GetMixerSources();
            var result = new List<MixerSource>(sources.Count);
            long now = Environment.TickCount64;

            lock (_lock)
            {
                if (_shutDown)
                    return result;

                foreach (var gone in _meters.Keys.Where(s => !sources.Any(m => ReferenceEquals(m.Source, s))).ToList())
                    RemoveMeterLocked(gone);

                foreach (var (id, name, kind, source) in sources)
                {
                    if (source.IsDisposed)
                        continue;

                    try
                    {
                        if (!_meters.TryGetValue(source, out var entry))
                            entry = AttachMeterLocked(source);

                        float peakDb = entry == null || now - Volatile.Read(ref entry.UpdatedAt) > StaleMs
                            ? SilenceDb
                            : entry.PeakDb;
                        result.Add(new MixerSource(id, name, kind, peakDb, source.IsMuted, source.Volume));
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to read mixer state for {Name}", name);
                    }
                }
            }

            return result;
        }

        /// <summary>Detaches the source's meter, then disposes the source.</summary>
        public static void DisposeSource(Source source)
        {
            lock (_lock)
            {
                RemoveMeterLocked(source);
                source.Dispose();
            }
        }

        /// <summary>Releases every meter before OBS shuts down; later reads return nothing.</summary>
        public static void DisposeAll()
        {
            lock (_lock)
            {
                _shutDown = true;
                foreach (var source in _meters.Keys.ToList())
                    RemoveMeterLocked(source);
            }
        }

        private static MeterEntry? AttachMeterLocked(Source source)
        {
            var meter = new AudioMeter();
            var entry = new MeterEntry { Meter = meter };
            meter.LevelsUpdated += (_, levels) =>
            {
                float max = SilenceDb;
                foreach (float peak in levels.Peak)
                    if (peak > max) max = peak;
                entry.PeakDb = max;
                Volatile.Write(ref entry.UpdatedAt, Environment.TickCount64);
            };

            if (!meter.AttachSource(source))
            {
                meter.Dispose();
                return null;
            }

            _meters[source] = entry;
            return entry;
        }

        private static void RemoveMeterLocked(Source source)
        {
            if (!_meters.Remove(source, out var entry))
                return;

            try { entry.Meter.Dispose(); }
            catch (Exception ex) { Log.Debug(ex, "Failed to dispose audio meter"); }
        }
    }
}
