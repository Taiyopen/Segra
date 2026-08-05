import { useCallback, useEffect, useState } from 'react';
import { ArrowUp, Folder, Film, HardDrive } from 'lucide-react';
import Button from './Button';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import { Content } from '../Models/types';

export interface BrowseFolderEntry {
  name: string;
  path: string;
  kind: 'folder';
}

export interface BrowseVideoEntry {
  name: string;
  path: string;
  kind: 'video';
  sizeBytes: number;
  modifiedAt: string;
}

interface BrowseDirectoryResult {
  ok: boolean;
  error?: string;
  path?: string;
  parentPath?: string | null;
  folders?: BrowseFolderEntry[];
  videos?: BrowseVideoEntry[];
}

const LAST_PATH_KEY = 'browseVideos.lastPath';

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

export function toBrowseContent(video: BrowseVideoEntry): Content {
  const fileName = video.name.replace(/\.[^.]+$/, '');
  return {
    type: 'External',
    title: fileName,
    game: 'Unknown',
    bookmarks: [],
    fileName,
    filePath: video.path,
    fileSize: formatSize(video.sizeBytes),
    fileSizeKb: Math.round(video.sizeBytes / 1024),
    duration: '00:00:00',
    createdAt: video.modifiedAt,
    isImported: true,
  };
}

interface BrowseFileSidebarProps {
  selectedPath?: string | null;
  onOpenVideo: (content: Content) => void;
}

export default function BrowseFileSidebar({ selectedPath, onOpenVideo }: BrowseFileSidebarProps) {
  const [currentPath, setCurrentPath] = useState<string>('');
  const [parentPath, setParentPath] = useState<string | null>(null);
  const [folders, setFolders] = useState<BrowseFolderEntry[]>([]);
  const [videos, setVideos] = useState<BrowseVideoEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const requestDirectory = useCallback((path?: string) => {
    setLoading(true);
    setError(null);
    sendMessageToBackend('BrowseListDirectory', path ? { Path: path } : {});
  }, []);

  useEffect(() => {
    const saved = localStorage.getItem(LAST_PATH_KEY);
    requestDirectory(saved || undefined);
  }, [requestDirectory]);

  useEffect(() => {
    const handleMessage = (event: CustomEvent) => {
      const message = event.detail;
      if (message?.method !== 'BrowseDirectoryResult') return;

      const result = message.content as BrowseDirectoryResult;
      setLoading(false);

      if (!result?.ok) {
        setError(result?.error || '無法讀取資料夾');
        return;
      }

      setCurrentPath(result.path || '');
      setParentPath(result.parentPath ?? null);
      setFolders(result.folders || []);
      setVideos(result.videos || []);
      setError(null);

      if (result.path) {
        localStorage.setItem(LAST_PATH_KEY, result.path);
      }
    };

    window.addEventListener('websocket-message', handleMessage as EventListener);
    return () => {
      window.removeEventListener('websocket-message', handleMessage as EventListener);
    };
  }, []);

  return (
    <aside className="w-72 shrink-0 h-full bg-base-300 border-r border-base-400 flex flex-col overflow-hidden">
      <div className="px-3 py-2.5 border-b border-base-400 space-y-2 shrink-0">
        <div className="flex items-center gap-1.5">
          <Button
            variant="ghost"
            size="sm"
            className="h-8 px-2 border border-base-400"
            disabled={!parentPath || loading}
            onClick={() => parentPath && requestDirectory(parentPath)}
            title="上一層"
          >
            <ArrowUp size={16} />
          </Button>
          <Button
            variant="primary"
            size="sm"
            className="h-8 flex-1 gap-1"
            disabled={loading}
            onClick={() => sendMessageToBackend('BrowseSelectFolder')}
          >
            <HardDrive size={14} />
            選擇資料夾
          </Button>
        </div>
        <p className="text-[11px] text-base-content/50 truncate" title={currentPath}>
          {currentPath || (loading ? '載入中…' : '')}
        </p>
      </div>

      <div className="px-3 py-1.5 text-[10px] uppercase tracking-wide text-base-content/40 border-b border-base-400 shrink-0">
        {loading ? '載入中…' : `${folders.length} 資料夾 · ${videos.length} 影片`}
      </div>

      <div className="flex-1 overflow-y-auto min-h-0">
        {error && <div className="p-3 text-error text-sm">{error}</div>}
        {!error && !loading && folders.length === 0 && videos.length === 0 && (
          <div className="p-6 text-center text-base-content/40 text-sm">此資料夾沒有內容</div>
        )}
        <ul className="divide-y divide-base-400/50">
          {folders.map((folder) => (
            <li key={folder.path}>
              <button
                type="button"
                className="w-full flex items-center gap-2.5 px-3 py-2 text-left hover:bg-base-200 transition-colors"
                onClick={() => requestDirectory(folder.path)}
              >
                <Folder size={16} className="text-warning shrink-0" />
                <span className="truncate text-sm">{folder.name}</span>
              </button>
            </li>
          ))}
          {videos.map((video) => {
            const isSelected = selectedPath === video.path;
            return (
              <li key={video.path}>
                <button
                  type="button"
                  className={`w-full flex items-center gap-2.5 px-3 py-2 text-left transition-colors ${
                    isSelected ? 'bg-primary/15' : 'hover:bg-base-200'
                  }`}
                  onClick={() => onOpenVideo(toBrowseContent(video))}
                >
                  <Film size={16} className="text-primary shrink-0" />
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-sm">{video.name}</div>
                    <div className="text-[11px] text-base-content/45">
                      {formatSize(video.sizeBytes)}
                    </div>
                  </div>
                </button>
              </li>
            );
          })}
        </ul>
      </div>
    </aside>
  );
}
