namespace Segra.Backend.Recorder
{
    internal enum AlwaysOnBufferAction
    {
        None,
        Start,
        Stop,
        Restart
    }

    /// <summary>
    /// When the always-on display replay buffer should run and how it recovers from failures.
    /// Kept free of OBS types so it can be tested without libobs.
    /// </summary>
    internal static class AlwaysOnBufferPolicy
    {
        /// <summary>A buffer that ran at least this long before failing likely hit something transient.</summary>
        public static readonly TimeSpan RetryAfterRunningFor = TimeSpan.FromMinutes(5);

        public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Runs only while enabled and nothing else owns the recorder: no slot recording or about to record.
        /// </summary>
        public static bool ShouldRun(bool enabled, bool obsInitialized, bool isExiting, bool anyRecording, bool anyPreRecording)
        {
            return enabled && obsInitialized && !isExiting && !anyRecording && !anyPreRecording;
        }

        /// <summary>
        /// Decides what to do with the buffer. <paramref name="lastKey"/> is the configuration it was last started
        /// with; it is kept after a failed start, so a buffer that failed stays off until the configuration changes
        /// (or it is cleared by a recording or by turning the setting off).
        /// </summary>
        public static AlwaysOnBufferAction Decide(bool shouldRun, bool isRunning, string configKey, string? lastKey)
        {
            if (isRunning)
            {
                if (!shouldRun)
                    return AlwaysOnBufferAction.Stop;
                return configKey == lastKey ? AlwaysOnBufferAction.None : AlwaysOnBufferAction.Restart;
            }

            if (!shouldRun || configKey == lastKey)
                return AlwaysOnBufferAction.None;
            return AlwaysOnBufferAction.Start;
        }

        /// <summary>Retries once after a failure only if the buffer had been running for a while.</summary>
        public static bool ShouldRetryAfterFailure(TimeSpan ranFor)
        {
            return ranFor >= RetryAfterRunningFor;
        }
    }
}
