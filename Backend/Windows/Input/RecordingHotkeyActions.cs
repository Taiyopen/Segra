using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Segra.Backend.Core.Models;
using Segra.Backend.Recorder;
using Serilog;

namespace Segra.Backend.Windows.Input
{
    internal static class RecordingHotkeyActions
    {
        private static int _replaySaveInProgress;
        /// <summary>Returns true when a manual bookmark was added (same rules as F8).</summary>
        public static bool TryCreateBookmark()
        {
            var recording = Settings.Instance.State.Recording;
            var recordingMode = Settings.Instance.RecordingMode;

            if (recording != null && (recordingMode == RecordingMode.Session || recordingMode == RecordingMode.Hybrid))
            {
                recording.Bookmarks.Add(new Bookmark
                {
                    Type = BookmarkType.Manual,
                    Time = DateTime.Now - recording.StartTime
                });
                Task.Run(PlayBookmarkSound);
                return true;
            }

            return false;
        }

        /// <summary>Returns true when save replay was triggered (same rules as F10).</summary>
        public static bool TrySaveReplayBuffer()
        {
            var recording = Settings.Instance.State.Recording;
            var recordingMode = Settings.Instance.RecordingMode;

            if (recording != null && (recordingMode == RecordingMode.Buffer || recordingMode == RecordingMode.Hybrid))
            {
                // Ignore duplicate hotkey/UI triggers while a save is running. Without this, a second
                // queued save runs after ResetReplayBuffer and produces an empty clip.
                if (Interlocked.CompareExchange(ref _replaySaveInProgress, 1, 0) != 0)
                {
                    Log.Debug("Save replay buffer ignored: save already in progress");
                    return false;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await OBSService.SaveReplayBuffer();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _replaySaveInProgress, 0);
                    }
                });
                Task.Run(PlayBookmarkSound);
                return true;
            }

            return false;
        }

        private static void PlayBookmarkSound()
        {
            using var audioStream = new MemoryStream(Properties.Resources.bookmark);
            using var audioReader = new WaveFileReader(audioStream);
            var sampleProvider = audioReader.ToSampleProvider();
            var volumeProvider = new VolumeSampleProvider(sampleProvider)
            {
                Volume = Settings.Instance.SoundEffectsVolume
            };

            using var waveOut = new WasapiOut(AudioClientShareMode.Shared, 100);
            waveOut.Init(volumeProvider);
            waveOut.Play();

            while (waveOut.PlaybackState == PlaybackState.Playing)
                Thread.Sleep(10);
        }
    }
}
