import { useAppState } from '../Context/AppStateContext';
import ContentCard from './ContentCard';
import { matchesContentCategory, useSelectedVideo } from '../Context/SelectedVideoContext';
import { Content, ContentType, MENU_ITEM, MENU_ITEM_LABELS } from '../Models/types';
import { useScroll } from '../Context/ScrollContext';
import { useLayoutEffect, useRef, useState, useMemo, useEffect, useCallback } from 'react';
import type { LucideIcon } from 'lucide-react';
import { FileUp, Trash2, Inbox, FolderOutput } from 'lucide-react';
import { AnimatePresence, motion } from 'framer-motion';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import {
  isPendingEditSourceType,
  resolvePendingEditSourceType,
  sendMoveToPendingEdit,
  sendMoveOutOfPendingEdit,
} from '../Utils/PendingEditUtils';
import {
  isRecordingSourceType,
  resolveRecordingSourceType,
  sendMoveOutOfReadyToDelete,
} from '../Utils/ReadyToDeleteUtils';
import ContentFilters, { SortOption } from './ContentFilters';
import { useModal } from '../Context/ModalContext';
import { useImports } from '../Context/ImportContext';
import Button from './Button';
import { useDeleteConfirmation } from '../Hooks/useDeleteConfirmation';

// Escape a filename for use inside a CSS attribute-selector string. Windows
// filenames can't contain " or \, but escape defensively all the same.
const escapeAttrValue = (value: string) => value.replace(/["\\]/g, '\\$&');

interface ContentPageProps {
  contentType: ContentType;
  sectionId: string;
  title: string;
  Icon: LucideIcon;
  progressItems?: Record<string, any>; // For AI highlights or clipping progress
  isProgressVisible?: boolean;
  progressCardElement?: React.ReactNode; // Direct element instead of component
}

export default function ContentPage({
  contentType,
  sectionId,
  title,
  Icon,
  progressItems = {},
  isProgressVisible = false,
  progressCardElement,
}: ContentPageProps) {
  const state = useAppState();
  const { setSelectedVideo, stickySourceCategory } = useSelectedVideo();
  const { scrollPositions, setScrollPosition } = useScroll();
  const { isModalOpen } = useModal();
  const confirmDelete = useDeleteConfirmation();
  const { imports } = useImports();
  const containerRef = useRef<HTMLDivElement>(null);
  const isSettingScroll = useRef(false);

  // When a single-file import finishes, scroll the freshly imported card into
  // view. The completed import message doesn't carry the stored filename, so we
  // snapshot the current items and diff once the reloaded content arrives.
  const handledImportIdsRef = useRef<Set<string>>(new Set());
  const importSnapshotRef = useRef<Set<string> | null>(null);

  const [selectedItems, setSelectedItems] = useState<Set<string>>(new Set());
  const [isCtrlPressed, setIsCtrlPressed] = useState(false);
  const [highlightedFileName, setHighlightedFileName] = useState<string | null>(null);
  const highlightTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const contentItems = useMemo(
    () =>
      state.content.filter((video) =>
        matchesContentCategory(video, contentType, stickySourceCategory),
      ),
    [state.content, contentType, stickySourceCategory],
  );
  const [selectedGames, setSelectedGames] = useState<string[]>(() => {
    try {
      const saved = localStorage.getItem(`${sectionId}-filters`);
      return saved ? JSON.parse(saved) : [];
    } catch {
      return [];
    }
  });

  const [sortOption, setSortOption] = useState<SortOption>(() => {
    try {
      const saved = localStorage.getItem(`${sectionId}-sort`);
      return saved ? JSON.parse(saved) : 'newest';
    } catch {
      return 'newest';
    }
  });

  const uniqueGames = useMemo(() => {
    const games = contentItems.map((item) => item.game);
    const uniqueGameList = [...new Set(games)].sort();
    // Add "Imported" to the list if any items are imported
    if (contentItems.some((item) => item.isImported)) {
      return ['Imported', ...uniqueGameList];
    }
    return uniqueGameList;
  }, [contentItems]);

  useEffect(() => {
    const availableFilters = new Set(uniqueGames);

    setSelectedGames((prev) => {
      const validFilters = prev.filter((game) => availableFilters.has(game));
      if (validFilters.length === prev.length) return prev;
      localStorage.setItem(`${sectionId}-filters`, JSON.stringify(validFilters));
      return validFilters;
    });
  }, [uniqueGames, sectionId]);

  const filteredItems = useMemo(() => {
    let filtered = [...contentItems];

    if (selectedGames.length > 0) {
      filtered = filtered.filter((item) => {
        if (selectedGames.includes('Imported') && item.isImported) {
          return true;
        }
        return selectedGames.filter((g) => g !== 'Imported').includes(item.game);
      });
    }

    filtered.sort((a, b) => {
      switch (sortOption) {
        case 'newest':
          return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
        case 'oldest':
          return new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime();
        case 'size':
          return (b.fileSizeKb ?? 0) - (a.fileSizeKb ?? 0);
        case 'duration': {
          const toSecs = (dur: string) =>
            dur.split(':').reduce((acc, t) => 60 * acc + (parseInt(t, 10) || 0), 0);
          return toSecs(b.duration) - toSecs(a.duration);
        }
        case 'game': {
          const byGame = a.game.localeCompare(b.game);
          return byGame !== 0
            ? byGame
            : new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
        }
        default:
          return 0;
      }
    });

    return filtered;
  }, [contentItems, selectedGames, sortOption]);

  const handleGameFilterChange = (games: string[]) => {
    setSelectedGames(games);
    localStorage.setItem(`${sectionId}-filters`, JSON.stringify(games));
  };

  const handleSortChange = (option: SortOption) => {
    setSortOption(option);
    localStorage.setItem(`${sectionId}-sort`, JSON.stringify(option));
  };

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  const handleCardClick = useCallback(
    (video: Content, event?: React.MouseEvent) => {
      if (event?.ctrlKey || isCtrlPressed) {
        setSelectedItems((prev) => {
          const newSet = new Set(prev);
          if (newSet.has(video.fileName)) {
            newSet.delete(video.fileName);
          } else {
            newSet.add(video.fileName);
          }
          return newSet;
        });
      } else {
        if (selectedItems.size === 0) {
          handlePlay(video);
        } else {
          setSelectedItems(new Set());
        }
      }
    },
    [isCtrlPressed, selectedItems.size],
  );

  const handleDeleteSelected = useCallback(() => {
    if (selectedItems.size === 0) return;

    const items = Array.from(selectedItems).map((fileName) => {
      const item = state.content.find((c) => c.fileName === fileName);
      return {
        FileName: fileName,
        ContentType: item?.type ?? contentType,
      };
    });

    const count = items.length;
    confirmDelete({
      title: `刪除 ${count} 支影片？`,
      description: `確定要永久刪除選取的 ${count} 支影片嗎？

刪除後無法復原。`,
      onConfirm: () => {
        sendMessageToBackend('DeleteMultipleContent', { Items: items });
        setSelectedItems(new Set());
      },
    });
  }, [selectedItems, contentType, state.content, confirmDelete]);

  const handleMoveSelectedToPendingEdit = useCallback(() => {
    if (selectedItems.size === 0) return;
    if (contentType !== 'Session' && contentType !== 'Buffer') return;

    const items = Array.from(selectedItems).flatMap((fileName) => {
      const item = state.content.find((c) => c.fileName === fileName);
      // Skip items already moved to pending edit (sticky leftovers)
      if (item?.type === 'PendingEdit' || item?.type === 'ReadyToDelete') return [];
      const sourceType =
        item?.type === 'Session' || item?.type === 'Buffer' ? item.type : contentType;
      return [{ fileName, contentType: sourceType }];
    });

    if (items.length === 0) {
      setSelectedItems(new Set());
      return;
    }

    sendMoveToPendingEdit(items);
    setSelectedItems(new Set());
  }, [selectedItems, contentType, state.content]);

  const handleMoveSelectedOutOfPendingEdit = useCallback(
    (targetType?: 'Session' | 'Buffer') => {
      if (selectedItems.size === 0) return;
      if (contentType !== 'PendingEdit') return;

      const items = Array.from(selectedItems).map((fileName) => {
        const item = state.content.find((c) => c.fileName === fileName);
        return {
          fileName,
          targetType: targetType ?? resolvePendingEditSourceType(item?.pendingEditSourceType),
        };
      });
      sendMoveOutOfPendingEdit(items);
      setSelectedItems(new Set());
    },
    [selectedItems, contentType, state.content],
  );

  const handleMoveSelectedOutOfReadyToDelete = useCallback(
    (targetType?: 'Session' | 'Buffer') => {
      if (selectedItems.size === 0) return;
      if (contentType !== 'ReadyToDelete') return;

      const items = Array.from(selectedItems).map((fileName) => {
        const item = state.content.find((c) => c.fileName === fileName);
        return {
          fileName,
          targetType: targetType ?? resolveRecordingSourceType(item?.readyToDeleteSourceType),
        };
      });
      sendMoveOutOfReadyToDelete(items);
      setSelectedItems(new Set());
    },
    [selectedItems, contentType, state.content],
  );

  const selectedPendingEditNeedsTargetChoice = useMemo(() => {
    if (contentType !== 'PendingEdit' || selectedItems.size === 0) return false;
    return Array.from(selectedItems).some((fileName) => {
      const item = state.content.find((c) => c.fileName === fileName);
      return !isPendingEditSourceType(item?.pendingEditSourceType);
    });
  }, [contentType, selectedItems, state.content]);

  const selectedReadyToDeleteNeedsTargetChoice = useMemo(() => {
    if (contentType !== 'ReadyToDelete' || selectedItems.size === 0) return false;
    return Array.from(selectedItems).some((fileName) => {
      const item = state.content.find((c) => c.fileName === fileName);
      return !isRecordingSourceType(item?.readyToDeleteSourceType);
    });
  }, [contentType, selectedItems, state.content]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (isModalOpen) return;
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;

      if (e.key === 'Control') {
        setIsCtrlPressed(true);
      }

      if (e.ctrlKey && e.key === 'a') {
        e.preventDefault();
        if (selectedItems.size === filteredItems.length && filteredItems.length > 0) {
          setSelectedItems(new Set());
        } else {
          setSelectedItems(new Set(filteredItems.map((item) => item.fileName)));
        }
      }

      if (e.key === 'Escape') {
        setSelectedItems(new Set());
      }

      if (e.key === 'Delete' && selectedItems.size > 0) {
        e.preventDefault();
        handleDeleteSelected();
      }
    };

    const handleKeyUp = (e: KeyboardEvent) => {
      if (e.key === 'Control') {
        setIsCtrlPressed(false);
      }
    };

    const handleBlur = () => {
      setIsCtrlPressed(false);
    };

    window.addEventListener('keydown', handleKeyDown);
    window.addEventListener('keyup', handleKeyUp);
    window.addEventListener('blur', handleBlur);

    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      window.removeEventListener('keyup', handleKeyUp);
      window.removeEventListener('blur', handleBlur);
    };
  }, [selectedItems, filteredItems, isModalOpen, handleDeleteSelected]);

  const prevContentFileNamesRef = useRef<string>('');

  useEffect(() => {
    const currentKey = contentItems.map((item) => item.fileName).join(',');

    if (currentKey === prevContentFileNamesRef.current) return;
    prevContentFileNamesRef.current = currentKey;

    const validFileNames = new Set(contentItems.map((item) => item.fileName));

    setSelectedItems((prev) => {
      let hasInvalid = false;
      prev.forEach((fileName) => {
        if (!validFileNames.has(fileName)) {
          hasInvalid = true;
        }
      });
      if (!hasInvalid) return prev; // Return same reference if nothing changed

      const newSet = new Set<string>();
      prev.forEach((fileName) => {
        if (validFileNames.has(fileName)) {
          newSet.add(fileName);
        }
      });
      return newSet;
    });
  }, [contentItems]);

  // Detect a single-file import completing and remember the items that existed
  // just before its reloaded content arrives.
  useEffect(() => {
    for (const importItem of Object.values(imports)) {
      if (
        importItem.status === 'done' &&
        importItem.totalFiles === 1 &&
        !handledImportIdsRef.current.has(importItem.id)
      ) {
        handledImportIdsRef.current.add(importItem.id);
        importSnapshotRef.current = new Set(contentItems.map((item) => item.fileName));
      }
    }
  }, [imports, contentItems]);

  // Once the reloaded content includes the newly imported item, scroll to it.
  useEffect(() => {
    const snapshot = importSnapshotRef.current;
    if (!snapshot) return;

    const newItem = contentItems.find((item) => item.isImported && !snapshot.has(item.fileName));
    if (!newItem) return; // Reloaded content hasn't arrived yet

    importSnapshotRef.current = null;

    // Pulse the card border twice to draw the eye to it (0.7s delay + 2x0.9s = 2.5s).
    setHighlightedFileName(newItem.fileName);
    if (highlightTimeoutRef.current) clearTimeout(highlightTimeoutRef.current);
    highlightTimeoutRef.current = setTimeout(() => setHighlightedFileName(null), 2600);

    requestAnimationFrame(() => {
      containerRef.current
        ?.querySelector(`[data-content-filename="${escapeAttrValue(newItem.fileName)}"]`)
        ?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    });
  }, [contentItems]);

  useEffect(() => {
    return () => {
      if (highlightTimeoutRef.current) clearTimeout(highlightTimeoutRef.current);
    };
  }, []);

  useLayoutEffect(() => {
    const position =
      sectionId === 'clips'
        ? scrollPositions.clips
        : sectionId === 'highlights'
          ? scrollPositions.highlights
          : sectionId === 'replayBuffer'
            ? scrollPositions.replayBuffer
            : sectionId === 'sessions'
              ? scrollPositions.sessions
              : sectionId === 'pendingEdit'
                ? scrollPositions.pendingEdit
                : 0;

    if (containerRef.current && position > 0) {
      isSettingScroll.current = true;
      containerRef.current.scrollTop = position;
      setTimeout(() => {
        isSettingScroll.current = false;
      }, 100);
    }
  }, []); // Only run on mount

  const scrollTimeout = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleScroll = () => {
    if (containerRef.current && !isSettingScroll.current) {
      if (scrollTimeout.current) {
        clearTimeout(scrollTimeout.current);
      }

      scrollTimeout.current = setTimeout(() => {
        const currentPos = containerRef.current?.scrollTop;
        if (currentPos === undefined) return;

        const pageKey =
          sectionId === 'clips'
            ? 'clips'
            : sectionId === 'highlights'
              ? 'highlights'
              : sectionId === 'replayBuffer'
                ? 'replayBuffer'
                : sectionId === 'sessions'
                  ? 'sessions'
                  : sectionId === 'pendingEdit'
                    ? 'pendingEdit'
                    : null;

        if (pageKey) {
          setScrollPosition(pageKey, currentPos);
        }
      }, 500);
    }
  };

  const [marqueeRect, setMarqueeRect] = useState<{
    left: number;
    top: number;
    width: number;
    height: number;
  } | null>(null);

  const handleMarqueeMouseDown = (e: React.MouseEvent) => {
    const container = containerRef.current;
    if (!container || e.button !== 0) return;

    const target = e.target as HTMLElement;
    if (
      target.closest(
        '[data-content-filename], button, input, select, a, .dropdown, .menu, [data-marquee-ignore]',
      )
    ) {
      return;
    }

    const startRect = container.getBoundingClientRect();
    if (e.clientX - startRect.left >= container.clientWidth) return; // Scrollbar

    e.preventDefault();

    const drag = {
      anchorX: e.clientX - startRect.left + container.scrollLeft,
      anchorY: e.clientY - startRect.top + container.scrollTop,
      startClientX: e.clientX,
      startClientY: e.clientY,
      clientX: e.clientX,
      clientY: e.clientY,
      baseSelection: e.ctrlKey ? new Set(selectedItems) : new Set<string>(),
      additive: e.ctrlKey,
      didDrag: false,
      raf: 0,
    };

    const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);

    const update = () => {
      const c = containerRef.current;
      if (!c) return;
      const cRect = c.getBoundingClientRect();

      const zone = 48;
      const offsetY = drag.clientY - cRect.top;
      if (offsetY < zone) {
        c.scrollTop -= Math.min(24, Math.ceil((zone - offsetY) / 4));
      } else if (offsetY > c.clientHeight - zone) {
        c.scrollTop += Math.min(24, Math.ceil((offsetY - (c.clientHeight - zone)) / 4));
      }

      const x = clamp(drag.clientX - cRect.left, 0, c.clientWidth) + c.scrollLeft;
      const y = clamp(drag.clientY - cRect.top, 0, c.clientHeight) + c.scrollTop;
      const left = Math.min(drag.anchorX, x);
      const top = Math.min(drag.anchorY, y);
      const width = Math.abs(drag.anchorX - x);
      const height = Math.abs(drag.anchorY - y);
      setMarqueeRect({ left, top, width, height });

      const ids = new Set(drag.baseSelection);
      c.querySelectorAll('[data-content-filename]').forEach((el) => {
        const r = el.getBoundingClientRect();
        const elLeft = r.left - cRect.left + c.scrollLeft;
        const elTop = r.top - cRect.top + c.scrollTop;
        const intersects =
          left < elLeft + r.width &&
          left + width > elLeft &&
          top < elTop + r.height &&
          top + height > elTop;
        if (intersects) {
          const fileName = el.getAttribute('data-content-filename');
          if (fileName) ids.add(fileName);
        }
      });
      setSelectedItems(ids);
    };

    const tick = () => {
      if (drag.didDrag) update();
      drag.raf = requestAnimationFrame(tick);
    };
    drag.raf = requestAnimationFrame(tick);

    const finish = (fromMouseUp: boolean) => {
      cancelAnimationFrame(drag.raf);
      window.removeEventListener('mousemove', handleMove);
      window.removeEventListener('mouseup', handleUp);
      window.removeEventListener('blur', handleWindowBlur);
      setMarqueeRect(null);
      if (fromMouseUp && !drag.didDrag && !drag.additive) {
        setSelectedItems(new Set());
      }
    };

    const handleMove = (ev: MouseEvent) => {
      drag.clientX = ev.clientX;
      drag.clientY = ev.clientY;
      if (!drag.didDrag) {
        const dx = ev.clientX - drag.startClientX;
        const dy = ev.clientY - drag.startClientY;
        if (dx * dx + dy * dy >= 16) drag.didDrag = true;
      }
    };

    const handleUp = () => finish(true);
    const handleWindowBlur = () => finish(false);

    window.addEventListener('mousemove', handleMove);
    window.addEventListener('mouseup', handleUp);
    window.addEventListener('blur', handleWindowBlur);
  };

  const progressValues = Object.values(progressItems);
  const hasProgress = progressValues.length > 0;

  return (
    <div
      ref={containerRef}
      className="relative p-5 space-y-6 overflow-y-scroll h-full bg-base-200 overflow-x-hidden"
      onScroll={handleScroll}
      onMouseDown={handleMarqueeMouseDown}
    >
      <div className="flex justify-between items-center mb-4">
        <div className="flex items-center gap-3">
          <h1 className="text-3xl font-bold">{title}</h1>
        </div>
        <div className="flex items-center gap-2">
          {(sectionId === 'sessions' || sectionId === 'replayBuffer') && (
            <Button
              variant="ghost"
              size="sm"
              className="no-animation h-8 gap-1 border border-base-400 hover:border-opacity-75"
              disabled={selectedItems.size === 0}
              title={
                selectedItems.size === 0
                  ? '先按住 Ctrl 點選卡片（或 Ctrl+A 全選），將選取的影片移至「待剪輯」'
                  : '將選取的影片移至「待剪輯」（不會被儲存空間自動清理刪除）'
              }
              onClick={handleMoveSelectedToPendingEdit}
            >
              <Inbox size={16} />
              移至待剪輯
            </Button>
          )}
          {(sectionId === 'sessions' || sectionId === 'replayBuffer') && (
            <Button
              variant="primary"
              size="sm"
              className="no-animation h-8 gap-1"
              onClick={() => sendMessageToBackend('ImportFile', { sectionId })}
            >
              <FileUp size={16} />
              匯入
            </Button>
          )}
          {sectionId === 'pendingEdit' && (
            <Button
              variant="ghost"
              size="sm"
              className="no-animation h-8 gap-1 border border-base-400 hover:border-opacity-75"
              disabled={selectedItems.size === 0 || selectedPendingEditNeedsTargetChoice}
              title={
                selectedItems.size === 0
                  ? '先按住 Ctrl 點選卡片（或 Ctrl+A 全選），將選取的影片移回原本分類'
                  : selectedPendingEditNeedsTargetChoice
                    ? `選取項目含舊檔（無來源記錄）；請用下方多選列選擇移回 ${MENU_ITEM_LABELS[MENU_ITEM.Sessions]} 或 ${MENU_ITEM_LABELS[MENU_ITEM.ReplayBuffer]}`
                    : `將選取的影片移出「待剪輯」，回到原本的 ${MENU_ITEM_LABELS[MENU_ITEM.Sessions]} / ${MENU_ITEM_LABELS[MENU_ITEM.ReplayBuffer]}`
              }
              onClick={() => handleMoveSelectedOutOfPendingEdit()}
            >
              <FolderOutput size={16} />
              移出待剪輯
            </Button>
          )}
          {sectionId === 'readyToDelete' && (
            <Button
              variant="ghost"
              size="sm"
              className="no-animation h-8 gap-1 border border-base-400 hover:border-opacity-75"
              disabled={selectedItems.size === 0 || selectedReadyToDeleteNeedsTargetChoice}
              title={
                selectedItems.size === 0
                  ? '先按住 Ctrl 點選卡片（或 Ctrl+A 全選），將選取的影片搬回原本分類'
                  : selectedReadyToDeleteNeedsTargetChoice
                    ? `選取項目含舊檔（無來源記錄）；請用下方多選列選擇移回 ${MENU_ITEM_LABELS[MENU_ITEM.Sessions]} 或 ${MENU_ITEM_LABELS[MENU_ITEM.ReplayBuffer]}`
                    : `將選取的影片移出「準備刪除」，回到原本的 ${MENU_ITEM_LABELS[MENU_ITEM.Sessions]} / ${MENU_ITEM_LABELS[MENU_ITEM.ReplayBuffer]}`
              }
              onClick={() => handleMoveSelectedOutOfReadyToDelete()}
            >
              <FolderOutput size={16} />
              移出準備刪除
            </Button>
          )}
          <ContentFilters
            uniqueGames={uniqueGames}
            onGameFilterChange={handleGameFilterChange}
            onSortChange={handleSortChange}
            sectionId={sectionId}
            selectedGames={selectedGames}
            sortOption={sortOption}
          />
        </div>
      </div>

      {contentItems.length > 0 || hasProgress ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {isProgressVisible && progressCardElement}

          {filteredItems.map((video) => (
            <ContentCard
              key={video.fileName}
              content={video}
              onClick={(v, e) => handleCardClick(v, e)}
              type={contentType}
              isSelected={selectedItems.has(video.fileName)}
              isSelectionMode={isCtrlPressed || selectedItems.size > 0 || marqueeRect !== null}
              isHighlighted={video.fileName === highlightedFileName}
            />
          ))}
        </div>
      ) : (
        <div className="flex flex-col items-center justify-center h-64 text-gray-500">
          <Icon size={60} className="mb-4" />
          <p className="text-xl">還沒有{title}</p>
        </div>
      )}

      {marqueeRect && (
        <div
          className="absolute z-40 pointer-events-none border border-primary bg-primary/10 rounded-sm"
          style={{
            left: marqueeRect.left,
            top: marqueeRect.top,
            width: marqueeRect.width,
            height: marqueeRect.height,
            margin: 0,
          }}
        />
      )}

      <AnimatePresence>
        {selectedItems.size > 0 && (
          <motion.div
            initial={{ opacity: 0, y: marqueeRect ? 0 : 50 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: marqueeRect ? 0 : 50 }}
            transition={{ duration: 0.2 }}
            data-marquee-ignore
            className="fixed bottom-3 left-1/2 -translate-x-1/2 bg-base-300 border border-base-400 rounded-xl px-4 py-2 flex items-center gap-3 shadow-lg z-50"
          >
            <span className="text-sm text-gray-300">已選取 {selectedItems.size} 支</span>
            {(sectionId === 'sessions' || sectionId === 'replayBuffer') && (
              <Button
                variant="ghost"
                size="sm"
                className="h-8 border border-base-400"
                onClick={handleMoveSelectedToPendingEdit}
              >
                <Inbox size={16} />
                待剪輯
              </Button>
            )}
            {sectionId === 'pendingEdit' &&
              (selectedPendingEditNeedsTargetChoice ? (
                <>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-8 border border-base-400"
                    onClick={() => handleMoveSelectedOutOfPendingEdit('Session')}
                  >
                    <FolderOutput size={16} />
                    {MENU_ITEM_LABELS[MENU_ITEM.Sessions]}
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-8 border border-base-400"
                    onClick={() => handleMoveSelectedOutOfPendingEdit('Buffer')}
                  >
                    <FolderOutput size={16} />
                    {MENU_ITEM_LABELS[MENU_ITEM.ReplayBuffer]}
                  </Button>
                </>
              ) : (
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-8 border border-base-400"
                  onClick={() => handleMoveSelectedOutOfPendingEdit()}
                >
                  <FolderOutput size={16} />
                  移出待剪輯
                </Button>
              ))}
            {sectionId === 'readyToDelete' &&
              (selectedReadyToDeleteNeedsTargetChoice ? (
                <>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-8 border border-base-400"
                    onClick={() => handleMoveSelectedOutOfReadyToDelete('Session')}
                  >
                    <FolderOutput size={16} />
                    {MENU_ITEM_LABELS[MENU_ITEM.Sessions]}
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-8 border border-base-400"
                    onClick={() => handleMoveSelectedOutOfReadyToDelete('Buffer')}
                  >
                    <FolderOutput size={16} />
                    {MENU_ITEM_LABELS[MENU_ITEM.ReplayBuffer]}
                  </Button>
                </>
              ) : (
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-8 border border-base-400"
                  onClick={() => handleMoveSelectedOutOfReadyToDelete()}
                >
                  <FolderOutput size={16} />
                  移出準備刪除
                </Button>
              ))}
            <Button variant="danger" size="sm" className="h-8" onClick={handleDeleteSelected}>
              <Trash2 size={16} />
              刪除
            </Button>
            <Button
              variant="primary"
              size="sm"
              className="h-8"
              onClick={() => setSelectedItems(new Set())}
            >
              取消
            </Button>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
