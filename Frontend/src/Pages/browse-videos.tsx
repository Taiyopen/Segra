import { FolderOpen } from 'lucide-react';
import BrowseFileSidebar from '../Components/BrowseFileSidebar';
import { useSelectedVideo } from '../Context/SelectedVideoContext';

/** Browse page when no video is open yet — sidebar + empty player placeholder. */
export default function BrowseVideos() {
  const { setSelectedVideo } = useSelectedVideo();

  return (
    <div className="flex h-full w-full overflow-hidden bg-base-200">
      <BrowseFileSidebar onOpenVideo={setSelectedVideo} />
      <div className="flex-1 min-w-0 flex flex-col items-center justify-center text-base-content/40 gap-3 px-8">
        <FolderOpen size={48} className="opacity-50" />
        <p className="text-sm text-center">從左側選擇影片，即可在此開啟完整播放與剪輯視窗</p>
      </div>
    </div>
  );
}
