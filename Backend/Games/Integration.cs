namespace Segra.Backend.Games
{
    public abstract class Integration
    {
        public string? ExePath { get; set; }
        /// <summary>OBS output slot (0 or 1) this integration writes bookmarks to.</summary>
        public int RecordingSlot { get; set; }

        internal Segra.Backend.Core.Models.Recording? ActiveRecording =>
            Segra.Backend.Core.Models.AppState.Instance.GetRecording(RecordingSlot);

        public abstract Task Start();
        public abstract Task Shutdown();
    }
}
