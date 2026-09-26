import { useEffect, useRef, useState } from 'react';
import {
  ArrowLeft,
  ChevronLeft,
  ChevronRight,
  FolderOutput,
  Inbox,
  PenLine,
  Trash2,
} from 'lucide-react';
import { Content, MENU_ITEM } from '../Models/types';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import { openFileLocation } from '../Utils/FileUtils';
import { useSelectedVideo } from '../Context/SelectedVideoContext';
import Button from './Button';

// Title bar above the video editor: back, playlist prev/next, move/rename/delete, and file info.
interface VideoTopInfoBarProps {
  video: Content;
  currentIndex: number;
  playlistCount: number;
  prevVideo: Content | null;
  nextVideo: Content | null;
  onSelectVideo: (video: Content) => void;
  onDelete: () => void;
  onMoveToPendingEdit: () => void;
  showMoveToPendingEdit: boolean;
  onMoveOutOfPendingEdit: (targetType?: 'Session' | 'Buffer') => void;
  showMoveOutOfPendingEdit: boolean;
  pendingEditSourceType?: 'Session' | 'Buffer';
  onMoveOutOfReadyToDelete: (targetType?: 'Session' | 'Buffer') => void;
  showMoveOutOfReadyToDelete: boolean;
  readyToDeleteSourceType?: 'Session' | 'Buffer';
}

export default function VideoTopInfoBar({
  video,
  currentIndex,
  playlistCount,
  prevVideo,
  nextVideo,
  onSelectVideo,
  onDelete,
  onMoveToPendingEdit,
  showMoveToPendingEdit,
  onMoveOutOfPendingEdit,
  showMoveOutOfPendingEdit,
  pendingEditSourceType,
  onMoveOutOfReadyToDelete,
  showMoveOutOfReadyToDelete,
  readyToDeleteSourceType,
}: VideoTopInfoBarProps) {
  const { setSelectedVideo } = useSelectedVideo();
  const [isRenaming, setIsRenaming] = useState(false);
  const [renameValue, setRenameValue] = useState('');
  const renameInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setIsRenaming(false);
  }, [video.fileName]);

  const startRenaming = () => {
    setRenameValue(video.title || video.fileName || '');
    setIsRenaming(true);
    setTimeout(() => {
      renameInputRef.current?.focus();
      renameInputRef.current?.select();
    }, 0);
  };

  const commitRename = () => {
    if (!isRenaming) return;
    setIsRenaming(false);
    const trimmed = renameValue.trim();
    const invalidChars = /[<>:"/\\|?*]/;
    if (!trimmed || invalidChars.test(trimmed)) return;
    if (trimmed === (video.title || video.fileName || '')) return;
    sendMessageToBackend('RenameContent', {
      FileName: video.fileName,
      ContentType: video.type,
      Title: trimmed,
    });
  };

  const created = new Date(video.createdAt);
  const isValidDate = !isNaN(created.getTime());
  const locale = Intl.DateTimeFormat().resolvedOptions().locale?.toLowerCase() || '';
  const isUS = locale.includes('-us');
  const createdDateStr = !isValidDate
    ? video.createdAt
    : isUS
      ? created.toLocaleDateString('en-US', { year: 'numeric', month: '2-digit', day: '2-digit' })
      : `${created.getFullYear()}-${String(created.getMonth() + 1).padStart(2, '0')}-${String(created.getDate()).padStart(2, '0')}`;

  const createdTimeStr = !isValidDate
    ? ''
    : created.toLocaleTimeString(isUS ? 'en-US' : undefined, {
        hour: '2-digit',
        minute: '2-digit',
        hour12: isUS,
      });

  const positionLabel =
    currentIndex >= 0 && playlistCount > 0
      ? `${currentIndex + 1} / ${playlistCount}`
      : playlistCount > 0
        ? `— / ${playlistCount}`
        : null;

  return (
    <div className="flex items-center gap-2 px-2 py-1 mb-2 text-xs leading-tight text-gray-300 border rounded-lg shrink-0 bg-base-300 border-base-400">
      <Button
        variant="ghost"
        size="xs"
        className="h-6 min-h-0 px-1"
        onClick={() => setSelectedVideo(null)}
        aria-label="Back"
        title="返回列表"
      >
        <ArrowLeft className="w-4 h-4" />
      </Button>

      {playlistCount > 1 && (
        <div className="flex items-center border rounded-md join border-base-400 shrink-0">
          <Button
            variant="ghost"
            size="xs"
            className="h-6 min-h-0 px-1 join-item"
            disabled={!prevVideo}
            onClick={() => prevVideo && onSelectVideo(prevVideo)}
            aria-label="上一部"
            title="上一部"
          >
            <ChevronLeft className="w-4 h-4" />
          </Button>
          {positionLabel && (
            <span className="px-1 text-[10px] tabular-nums text-gray-400 join-item">
              {positionLabel}
            </span>
          )}
          <Button
            variant="ghost"
            size="xs"
            className="h-6 min-h-0 px-1 join-item"
            disabled={!nextVideo}
            onClick={() => nextVideo && onSelectVideo(nextVideo)}
            aria-label="下一部"
            title="下一部"
          >
            <ChevronRight className="w-4 h-4" />
          </Button>
        </div>
      )}

      <div className="flex items-center gap-1 shrink-0">
        {showMoveToPendingEdit && (
          <Button
            variant="ghost"
            size="xs"
            className="h-6 min-h-0 gap-1 px-1.5 border border-base-400"
            onClick={onMoveToPendingEdit}
            title="移至待剪輯（不會被儲存空間自動清理刪除）"
          >
            <Inbox className="w-3.5 h-3.5" />
            <span className="hidden lg:inline">待剪輯</span>
          </Button>
        )}
        {showMoveOutOfPendingEdit &&
          (pendingEditSourceType ? (
            <Button
              variant="ghost"
              size="xs"
              className="h-6 min-h-0 gap-1 px-1.5 border border-base-400"
              onClick={() => onMoveOutOfPendingEdit(pendingEditSourceType)}
              title={`移出待剪輯，回到 ${pendingEditSourceType === 'Buffer' ? MENU_ITEM.ReplayBuffer : MENU_ITEM.Sessions}`}
            >
              <FolderOutput className="w-3.5 h-3.5" />
              <span className="hidden lg:inline">移出待剪輯</span>
            </Button>
          ) : (
            <>
              <Button
                variant="ghost"
                size="xs"
                className="h-6 min-h-0 gap-1 px-1.5 border border-base-400"
                onClick={() => onMoveOutOfPendingEdit('Session')}
                title={`舊檔無來源記錄：移回 ${MENU_ITEM.Sessions}`}
              >
                <FolderOutput className="w-3.5 h-3.5" />
                <span className="hidden lg:inline">→ Sessions</span>
              </Button>
              <Button
                variant="ghost"
                size="xs"
                className="h-6 min-h-0 gap-1 px-1.5 border border-base-400"
                onClick={() => onMoveOutOfPendingEdit('Buffer')}
                title={`舊檔無來源記錄：移回 ${MENU_ITEM.ReplayBuffer}`}
              >
                <FolderOutput className="w-3.5 h-3.5" />
                <span className="hidden lg:inline">→ Buffer</span>
              </Button>
            </>
          ))}
        {showMoveOutOfReadyToDelete &&
          (readyToDeleteSourceType ? (
            <Button
              variant="ghost"
              size="xs"
              className="h-6 min-h-0 gap-1 px-1.5 border border-base-400"
              onClick={() => onMoveOutOfReadyToDelete(readyToDeleteSourceType)}
              title={`移出準備刪除，回到 ${readyToDeleteSourceType === 'Buffer' ? MENU_ITEM.ReplayBuffer : MENU_ITEM.Sessions}`}
            >
              <FolderOutput className="w-3.5 h-3.5" />
              <span className="hidden lg:inline">移出準備刪除</span>
            </Button>
          ) : (
            <>
              <Button
                variant="ghost"
                size="xs"
                className="h-6 min-h-0 gap-1 px-1.5 border border-base-400"
                onClick={() => onMoveOutOfReadyToDelete('Session')}
                title={`舊檔無來源記錄：移回 ${MENU_ITEM.Sessions}`}
              >
                <FolderOutput className="w-3.5 h-3.5" />
                <span className="hidden lg:inline">→ Sessions</span>
              </Button>
              <Button
                variant="ghost"
                size="xs"
                className="h-6 min-h-0 gap-1 px-1.5 border border-base-400"
                onClick={() => onMoveOutOfReadyToDelete('Buffer')}
                title={`舊檔無來源記錄：移回 ${MENU_ITEM.ReplayBuffer}`}
              >
                <FolderOutput className="w-3.5 h-3.5" />
                <span className="hidden lg:inline">→ Buffer</span>
              </Button>
            </>
          ))}
        <Button
          variant="ghost"
          size="xs"
          className="h-6 min-h-0 px-1.5 border border-base-400"
          onClick={startRenaming}
          aria-label="Rename"
          title="重新命名（同步更新磁碟檔名）"
        >
          <PenLine className="w-3.5 h-3.5" />
        </Button>
        <Button
          variant="ghost"
          size="xs"
          className="h-6 min-h-0 px-1.5 text-error hover:text-error"
          onClick={onDelete}
          aria-label="Delete"
          title="刪除影片"
        >
          <Trash2 className="w-3.5 h-3.5" />
        </Button>
      </div>

      <div className="flex items-center gap-2 overflow-hidden min-w-0">
        {isRenaming ? (
          <input
            ref={renameInputRef}
            type="text"
            value={renameValue}
            onChange={(e) => setRenameValue(e.target.value)}
            onBlur={commitRename}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                commitRename();
              } else if (e.key === 'Escape') {
                e.preventDefault();
                setIsRenaming(false);
              }
            }}
            className="min-w-0 max-w-48 px-1 py-0 font-medium truncate bg-base-200 border rounded outline-none border-base-400 focus:border-primary"
            placeholder={video.game || 'Untitled'}
          />
        ) : (
          <button
            type="button"
            onClick={startRenaming}
            className="min-w-0 font-medium truncate whitespace-nowrap hover:underline"
            title="點擊重新命名"
          >
            {video.title || video.game}
          </button>
        )}
        <span className="shrink-0">•</span>
        <span className="whitespace-nowrap shrink-0">
          Created: {createdDateStr}
          {createdTimeStr ? ` ${createdTimeStr}` : ''}
        </span>
        <span className="shrink-0">•</span>
        <span className="whitespace-nowrap shrink-0">Size: {video.fileSize}</span>
        <span className="shrink-0">•</span>
        <span className="flex items-center gap-1 min-w-0">
          <span className="whitespace-nowrap shrink-0">Location:</span>
          <a
            className="text-gray-300 cursor-pointer hover:underline hover:text-gray-200 truncate"
            onClick={() => openFileLocation(video.filePath)}
          >
            {video.filePath.replace(/\\/g, '/')}
          </a>
        </span>
      </div>
    </div>
  );
}
