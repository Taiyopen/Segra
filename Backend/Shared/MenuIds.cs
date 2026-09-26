namespace Segra.Backend.Shared
{
    /// <summary>
    /// Sidebar menu IDs persisted in settings.json.
    /// These are not always the same as <see cref="FolderNames"/> (Replay Buffer is singular;
    /// 瀏覽影片 is the browse page, not 外部影片庫).
    /// </summary>
    public static class MenuIds
    {
        public const string Sessions = FolderNames.Sessions;
        /// <summary>Menu label is singular; disk folder is <see cref="FolderNames.Buffers"/>.</summary>
        public const string ReplayBuffer = "Replay Buffer";
        public const string PendingEdit = FolderNames.PendingEdit;
        public const string ReadyToDelete = FolderNames.ReadyToDelete;
        /// <summary>Browse page; not <see cref="FolderNames.External"/>.</summary>
        public const string BrowseVideos = "\u700F\u89BD\u5F71\u7247"; // 瀏覽影片
        public const string Clips = FolderNames.Clips;
        public const string Highlights = FolderNames.Highlights;
        public const string Settings = "Settings";
    }
}
