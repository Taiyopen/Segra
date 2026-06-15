import { useEffect, useRef, useState } from 'react';
import { Content } from '../Models/types';
import { useSettings } from '../Context/SettingsContext';
import { ChevronDown, ChevronUp } from 'lucide-react';
import { AnimatePresence, motion } from 'framer-motion';

interface VideoPlaylistPanelProps {
  playlist: Content[];
  currentVideo: Content;
  currentIndex: number;
  onSelectVideo: (video: Content) => void;
}

function getThumbnailPath(cacheFolder: string, video: Content): string {
  const folderName =
    video.type === 'Session'
      ? 'Full Sessions'
      : video.type === 'Buffer'
        ? 'Replay Buffers'
        : video.type === 'Clip'
          ? 'Clips'
          : video.type === 'PendingEdit'
            ? '待剪輯'
            : 'Highlights';
  const thumbnailPath = `${cacheFolder}/thumbnails/${folderName}/${video.fileName}.jpeg`;
  return `http://localhost:2222/api/thumbnail?input=${encodeURIComponent(thumbnailPath)}`;
}

function formatDuration(duration: string): string {
  try {
    const time = duration.split('.')[0];
    const [hours, minutes, seconds] = time.split(':').map(Number);
    if (hours > 0) {
      return `${hours}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
    }
    return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
  } catch {
    return '00:00';
  }
}

function markAsViewed(fileName: string) {
  try {
    const viewedContent = localStorage.getItem('viewed-content') || '{}';
    const viewedContentObj = JSON.parse(viewedContent);
    viewedContentObj[fileName] = true;
    localStorage.setItem('viewed-content', JSON.stringify(viewedContentObj));
  } catch {
    /* no-op */
  }
}

export default function VideoPlaylistPanel({
  playlist,
  currentVideo,
  currentIndex,
  onSelectVideo,
}: VideoPlaylistPanelProps) {
  const { state } = useSettings();
  const [expanded, setExpanded] = useState(() => {
    try {
      return localStorage.getItem('video-playlist-expanded') === 'true';
    } catch {
      return false;
    }
  });
  const scrollRef = useRef<HTMLDivElement>(null);
  const activeItemRef = useRef<HTMLButtonElement>(null);

  const toggleExpanded = () => {
    setExpanded((prev) => {
      const next = !prev;
      localStorage.setItem('video-playlist-expanded', String(next));
      return next;
    });
  };

  useEffect(() => {
    if (!expanded || !activeItemRef.current || !scrollRef.current) return;
    activeItemRef.current.scrollIntoView({
      behavior: 'smooth',
      inline: 'center',
      block: 'nearest',
    });
  }, [expanded, currentVideo.fileName]);

  if (playlist.length <= 1) return null;

  const positionLabel =
    currentIndex >= 0 ? `${currentIndex + 1} / ${playlist.length}` : `— / ${playlist.length}`;

  return (
    <div className="mt-2 shrink-0 border rounded-lg bg-base-300 border-base-400">
      <button
        type="button"
        onClick={toggleExpanded}
        className="flex items-center justify-between w-full px-3 py-1.5 text-xs text-gray-300 hover:text-gray-200"
        aria-expanded={expanded}
      >
        <span>
          影片列表 <span className="text-gray-400">({positionLabel})</span>
        </span>
        {expanded ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}
      </button>

      <AnimatePresence initial={false}>
        {expanded && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.2 }}
            className="overflow-hidden border-t border-base-400"
          >
            <div
              ref={scrollRef}
              className="flex items-start gap-2 p-2 overflow-x-auto"
              style={{ scrollbarWidth: 'thin' }}
            >
              {playlist.map((item, index) => {
                const isActive = item.fileName === currentVideo.fileName;
                return (
                  <button
                    key={item.fileName}
                    ref={isActive ? activeItemRef : undefined}
                    type="button"
                    onClick={() => {
                      markAsViewed(item.fileName);
                      onSelectVideo(item);
                    }}
                    className={`flex flex-col shrink-0 w-36 rounded-md overflow-hidden border transition-colors ${
                      isActive
                        ? 'border-primary ring-1 ring-primary'
                        : 'border-base-400 hover:border-gray-400'
                    }`}
                    title={item.title || item.game}
                  >
                    <div className="relative w-full h-[72px] shrink-0 overflow-hidden bg-black">
                      <img
                        src={getThumbnailPath(state.cacheFolder, item)}
                        alt=""
                        className="w-full h-full object-cover"
                        loading="lazy"
                        draggable={false}
                      />
                      <span className="absolute bottom-1 right-1 bg-black/75 text-white text-[10px] px-1 rounded">
                        {formatDuration(item.duration)}
                      </span>
                      <span className="absolute top-1 left-1 bg-black/75 text-white text-[10px] px-1 rounded tabular-nums">
                        {index + 1}
                      </span>
                    </div>
                    <div className="h-10 px-1.5 py-1 text-left bg-base-200 shrink-0">
                      <p className="text-[11px] text-gray-300 truncate leading-tight">
                        {item.title || item.game}
                      </p>
                      <p className="text-[10px] text-gray-500 truncate leading-tight">
                        {item.game}
                      </p>
                    </div>
                  </button>
                );
              })}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
