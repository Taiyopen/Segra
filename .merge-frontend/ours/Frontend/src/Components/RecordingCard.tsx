import { useState, useEffect, useLayoutEffect, useCallback, useRef } from 'react';
import { createPortal } from 'react-dom';

import { motion, AnimatePresence } from 'framer-motion';

import { PreRecording, Recording, GameResponse, Game } from '../Models/types';

import { Gamepad2, Monitor, Ellipsis, Ban, OctagonX } from 'lucide-react';

import { useSettings } from '../Context/SettingsContext';

import { useAppState } from '../Context/AppStateContext';

import { sendMessageToBackend, stopRecordingSlot } from '../Utils/MessageUtils';

import Button from './Button';

import RecordingPreviewAudioMeters from './RecordingPreviewAudioMeters';

import { useRecordingPreview } from '../Hooks/useRecordingPreview';

const pad = (n: number) => String(n).padStart(2, '0');

interface RecordingCardProps {
  recording?: Recording;

  preRecording?: PreRecording;
}

const RecordingCard: React.FC<RecordingCardProps> = ({ recording, preRecording }) => {
  const timerRef = useRef<HTMLSpanElement>(null);

  const { showGameBackground } = useSettings();

  const { gameList } = useAppState();

  const [coverUrl, setCoverUrl] = useState<string | null>(null);

  const lastFetchedGameRef = useRef<string | null>(null);

  const [showShockwave, setShowShockwave] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const menuAnchorRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLUListElement>(null);
  const [menuPosition, setMenuPosition] = useState({ top: 0, left: 0 });

  const slot = preRecording?.slot ?? recording?.slot ?? 0;

  const slotLabel = slot === 1 ? 'Recording 2' : 'Recording 1';

  const recordingOngoing =
    !!recording?.startTime && (recording.endTime == null || recording.endTime === undefined);

  const { previewEnabled, previewFrameSrc, hasPreviewFrame } = useRecordingPreview(
    recordingOngoing,

    !!recording,

    recording,

    slot,
  );

  const showMonitoringPanel = !!preRecording || (!!recording && recordingOngoing);

  const gameName = preRecording ? preRecording.game : recording?.game;

  const gameListEntry = gameList.find((g) => g.name === gameName);

  const canBlockGame = !!gameListEntry && gameListEntry.executables.length > 0;

  const handleAddToBlocklist = useCallback(() => {
    if (!gameListEntry) return;

    const game: Game = {
      name: gameListEntry.name,

      paths: gameListEntry.executables,
    };

    sendMessageToBackend('AddToBlacklist', { game });

    stopRecordingSlot(slot);
  }, [gameListEntry, slot]);

  const updateMenuPosition = useCallback(() => {
    const anchor = menuAnchorRef.current;
    if (!anchor) return;

    const rect = anchor.getBoundingClientRect();
    const menuWidth = 208;
    const menuHeight = menuRef.current?.offsetHeight ?? 48;
    const spaceBelow = window.innerHeight - rect.bottom;
    const openUp = spaceBelow < menuHeight + 8;

    setMenuPosition({
      top: openUp ? rect.top - menuHeight - 4 : rect.bottom + 4,
      left: Math.min(window.innerWidth - menuWidth - 8, Math.max(8, rect.right - menuWidth)),
    });
  }, []);

  useEffect(() => {
    if (!menuOpen) return;

    updateMenuPosition();
    const closeMenu = () => setMenuOpen(false);
    const handleScroll = () => closeMenu();
    const handlePointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (menuAnchorRef.current?.contains(target) || menuRef.current?.contains(target)) return;
      closeMenu();
    };

    window.addEventListener('resize', updateMenuPosition);
    window.addEventListener('scroll', handleScroll, true);
    window.addEventListener('pointerdown', handlePointerDown);

    return () => {
      window.removeEventListener('resize', updateMenuPosition);
      window.removeEventListener('scroll', handleScroll, true);
      window.removeEventListener('pointerdown', handlePointerDown);
    };
  }, [menuOpen, updateMenuPosition]);

  useLayoutEffect(() => {
    if (menuOpen) updateMenuPosition();
  }, [menuOpen, updateMenuPosition]);

  useEffect(() => {
    const handleMessage = (event: CustomEvent) => {
      if (event.detail?.method === 'BookmarkCreated') {
        setShowShockwave(true);

        setTimeout(() => setShowShockwave(false), 600);
      }
    };

    window.addEventListener('websocket-message', handleMessage as EventListener);

    return () => window.removeEventListener('websocket-message', handleMessage as EventListener);
  }, []);

  useEffect(() => {
    if (preRecording) {
      if (timerRef.current) timerRef.current.textContent = '00:00';

      return;
    }

    if (!recording?.startTime) return;

    const startTime = new Date(recording.startTime).getTime();

    const updateElapsedTime = () => {
      if (!timerRef.current) return;

      const now = Date.now();

      const secondsElapsed = Math.max(0, Math.floor((now - startTime) / 1000));

      const hours = Math.floor(secondsElapsed / 3600);

      const minutes = Math.floor((secondsElapsed % 3600) / 60);

      const seconds = secondsElapsed % 60;

      timerRef.current.textContent =
        hours > 0
          ? `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`
          : `${pad(minutes)}:${pad(seconds)}`;
    };

    updateElapsedTime();

    const intervalId = setInterval(updateElapsedTime, 1000);

    return () => clearInterval(intervalId);
  }, [recording?.startTime, preRecording]);

  const fetchGameData = useCallback(async () => {
    if (!showGameBackground) {
      setCoverUrl(null);

      return;
    }

    const gameName = preRecording ? preRecording.game : recording?.game;

    if (!gameName || gameName === 'Manual Recording') {
      setCoverUrl(null);

      return;
    }

    try {
      const coverImageId = recording?.coverImageId || preRecording?.coverImageId;

      if (coverImageId) {
        setCoverUrl(`https://segra.tv/api/games/cover/${coverImageId}`);

        lastFetchedGameRef.current = gameName;

        return;
      }

      if (lastFetchedGameRef.current === gameName) {
        return;
      }

      const response = await fetch(
        `https://segra.tv/api/games/search?name=${encodeURIComponent(gameName)}`,
      );

      if (!response.ok) {
        throw new Error('Game not found');
      }

      const data: GameResponse = await response.json();

      if (data.game?.cover?.image_id) {
        setCoverUrl(`https://segra.tv/api/games/cover/${data.game.cover.image_id}`);
      }

      lastFetchedGameRef.current = gameName;
    } catch (error) {
      console.error('Error fetching game data:', error);

      setCoverUrl(null);

      lastFetchedGameRef.current = gameName;
    }
  }, [preRecording, recording, showGameBackground]);

  useEffect(() => {
    fetchGameData();
  }, [fetchGameData]);

  return (
    <div className="px-2 mb-2">
      <div className="group bg-base-300 border border-base-400 border-opacity-75 rounded-lg px-3 py-3.5 cursor-default relative isolate">
        {showShockwave && (
          <div className="absolute inset-0 z-20 pointer-events-none overflow-hidden rounded-lg">
            <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-0 h-0 rounded-full bg-primary/40 animate-shockwave" />
          </div>
        )}

        {coverUrl && showGameBackground && (
          <div className="absolute inset-0 z-0 opacity-25">
            <div
              className="absolute inset-0 rounded-[7px]"
              style={{
                backgroundImage: `url(${coverUrl})`,

                backgroundSize: 'cover',

                backgroundPosition: 'center',

                backgroundRepeat: 'no-repeat',
              }}
            ></div>
          </div>
        )}

        <div className="flex items-center justify-between mb-1 relative z-10">
          <div className="flex items-center">
            <span
              className={`w-3 h-3 shrink-0 mb-0.5 rounded-full mr-1.5 ${preRecording ? 'bg-orange-500' : 'bg-red-500'}`}
            ></span>

            <span className="text-gray-200 text-sm font-medium">
              {preRecording ? preRecording.status : slotLabel}
            </span>

            {!preRecording && (
              <div
                className={`tooltip tooltip-right ${recording?.isUsingGameHook ? 'tooltip-success' : 'tooltip-warning'} flex items-center ml-1.5 [&::before]:delay-200 [&::after]:delay-200`}
                data-tip={`${recording?.isUsingGameHook ? 'Game capture (using game hook)' : 'Display capture (not using game hook)'}`}
              >
                <div className={`swap swap-flip cursor-default overflow-hidden justify-center`}>
                  <input type="checkbox" checked={recording?.isUsingGameHook} />

                  <div className={`swap-on`}>
                    <Gamepad2 className="h-5 w-5 text-gray-300" />
                  </div>

                  <div className={`swap-off`}>
                    <Monitor className="h-5 w-5 text-gray-300 scale-90" />
                  </div>
                </div>
              </div>
            )}
          </div>

          <div className="flex items-center gap-0.5">
            <button
              type="button"
              title={`停止 ${slotLabel}`}
              aria-label={`停止 ${slotLabel}`}
              className="cursor-pointer rounded-full p-1 hover:bg-white/10 active:bg-white/10 flex items-center justify-center text-gray-300/90 hover:text-gray-200"
              onClick={(event) => {
                event.stopPropagation();
                stopRecordingSlot(slot);
              }}
            >
              <OctagonX className="h-5 w-5 text-gray-300" />
            </button>

            {canBlockGame && (
              <div
                className={`flex items-center transition-opacity delay-200 ${menuOpen ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'}`}
              >
                <button
                  ref={menuAnchorRef}
                  type="button"
                  aria-expanded={menuOpen}
                  aria-haspopup="menu"
                  className="cursor-pointer rounded-full p-0.5 hover:bg-white/10 active:bg-white/10 flex items-center justify-center"
                  onClick={(event) => {
                    event.stopPropagation();
                    setMenuOpen((open) => !open);
                  }}
                >
                  <Ellipsis className="h-5 w-5 text-gray-300" />
                </button>

                {menuOpen &&
                  createPortal(
                    <ul
                      ref={menuRef}
                      role="menu"
                      className="menu fixed z-[99999] w-52 rounded-box border border-base-400 bg-base-300 p-2 shadow-lg"
                      style={{ top: menuPosition.top, left: menuPosition.left }}
                    >
                      <li role="none">
                        <Button
                          variant="menuDanger"
                          onClick={() => {
                            setMenuOpen(false);
                            handleAddToBlocklist();
                          }}
                        >
                          <Ban size={20} />
                          <span>Add to Block List</span>
                        </Button>
                      </li>
                    </ul>,
                    document.body,
                  )}
              </div>
            )}
          </div>
        </div>

        <div className="flex items-center text-gray-400 text-sm relative z-10 min-w-0">
          <div className="flex items-center min-w-0 w-full">
            <span ref={timerRef} className="tabular-nums shrink-0">
              00:00
            </span>

            <p className="truncate ml-2 min-w-0">{gameName}</p>
          </div>
        </div>

        <AnimatePresence initial={false}>
          {showMonitoringPanel && (
            <motion.div
              initial={{ opacity: 0, height: 0, marginTop: 0 }}
              animate={{
                opacity: 1,

                height: 'auto',

                marginTop: 8,

                transition: {
                  duration: 0.25,

                  height: { type: 'spring', stiffness: 300, damping: 30 },
                },
              }}
              exit={{ opacity: 0, height: 0, marginTop: 0, transition: { duration: 0.2 } }}
              className="relative z-10 w-full overflow-hidden"
            >
              <div className="relative aspect-video w-full overflow-hidden rounded bg-black">
                {recording && previewEnabled && (
                  <img
                    src={previewFrameSrc ?? undefined}
                    alt=""
                    className={`relative z-[1] h-full w-full object-contain transition-opacity duration-200 ${hasPreviewFrame ? 'opacity-100' : 'opacity-0'}`}
                  />
                )}

                <div
                  className={`pointer-events-none absolute inset-0 z-[2] flex flex-col items-center justify-center gap-1 bg-black/25 px-3 text-center text-xs text-gray-500 transition-opacity duration-200 ${
                    recording && previewEnabled && hasPreviewFrame ? 'opacity-0' : 'opacity-100'
                  }`}
                >
                  {preRecording && <span>{preRecording.status}</span>}

                  {recording && previewEnabled && !hasPreviewFrame && <span>載入預覽…</span>}

                  {recording && !previewEnabled && <span>預覽已關閉（快捷鍵可開啟）</span>}
                </div>

                <RecordingPreviewAudioMeters poll={showMonitoringPanel && slot === 0} />
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </div>
  );
};

export default RecordingCard;
