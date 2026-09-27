import { useEffect, useState, type MouseEvent, type ReactNode } from 'react';
import {
  BookmarkPlus,
  CirclePause,
  CirclePlay,
  GripHorizontal,
  History,
  Pin,
  PinOff,
  Square,
  Video,
  X,
} from 'lucide-react';
import PipAudioMixer from './PipAudioMixer';
import { useMonitoringLayout } from '../Context/MonitoringLayoutContext';
import { useSettings } from '../Context/SettingsContext';
import { useAppState } from '../Context/AppStateContext';
import {
  getActiveRecordings,
  getActivePreRecordings,
  hasLiveRecordingActivity,
  isRecordingFinishing,
  type PreRecording,
  type Recording,
} from '../Models/types';
import { sendMessageToBackend, stopRecordingSlot } from '../Utils/MessageUtils';
import { useRecordingPreview } from '../Hooks/useRecordingPreview';

function formatDuration(totalSeconds: number): string {
  const m = Math.floor(totalSeconds / 60);
  const s = totalSeconds % 60;
  return `${m}:${s.toString().padStart(2, '0')}`;
}

function PipIconButton({
  title,
  disabled,
  onClick,
  variant = 'default',
  children,
}: {
  title: string;
  disabled?: boolean;
  onClick: () => void;
  variant?: 'default' | 'danger' | 'accent';
  children: ReactNode;
}) {
  const variantClass =
    variant === 'danger'
      ? 'bg-red-500 text-white hover:bg-red-400 active:scale-95'
      : variant === 'accent'
        ? 'bg-primary/90 text-base-300 hover:bg-primary active:scale-95'
        : 'bg-white/15 text-white hover:bg-white/25 active:scale-95';

  return (
    <button
      type="button"
      title={title}
      disabled={disabled}
      onClick={(event) => {
        event.stopPropagation();
        onClick();
      }}
      className={`monitoring-no-drag flex h-11 w-11 shrink-0 items-center justify-center rounded-full backdrop-blur-md transition-all duration-150 disabled:cursor-not-allowed disabled:opacity-35 ${variantClass}`}
    >
      {children}
    </button>
  );
}

function PipElapsedTimer({ startTime }: { startTime: Date | string }) {
  const [elapsedSec, setElapsedSec] = useState(0);

  useEffect(() => {
    const startMs = new Date(startTime).getTime();
    const tick = () => {
      setElapsedSec(Math.max(0, Math.floor((Date.now() - startMs) / 1000)));
    };
    tick();
    const id = window.setInterval(tick, 1000);
    return () => window.clearInterval(id);
  }, [startTime]);

  return <>{formatDuration(elapsedSec)}</>;
}

type PipPanel = {
  key: string;
  slot: number;
  shortLabel: string;
  gameName?: string;
  preRecording?: PreRecording;
  recording?: Recording;
};

function PipHeaderStatus({ panels }: { panels: PipPanel[] }) {
  if (panels.length === 0) {
    return <span className="truncate text-[11px] font-medium text-white/50">Segra 監控</span>;
  }

  const showSlotNumber = panels.length > 1;

  return (
    <div className="flex min-w-0 flex-1 flex-col gap-0.5">
      {panels.map((panel) => {
        const isPre = !!panel.preRecording;
        const recording = panel.recording;
        const recordingOngoing =
          !!recording?.startTime && (recording.endTime == null || recording.endTime === undefined);

        const statusText = isPre
          ? showSlotNumber
            ? `錄製 ${panel.shortLabel}`
            : '準備中…'
          : showSlotNumber
            ? `REC ${panel.shortLabel}`
            : 'REC';
        const gamePart = panel.gameName ? ` ${panel.gameName}` : '';

        return (
          <div key={panel.key} className="flex min-w-0 items-center gap-1.5">
            <span className="relative flex h-1.5 w-1.5 shrink-0">
              {recordingOngoing && (
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-red-400 opacity-75" />
              )}
              <span
                className={`relative inline-flex h-1.5 w-1.5 rounded-full ${isPre ? 'bg-amber-400' : 'bg-red-500'}`}
              />
            </span>
            <span className="truncate text-[11px] font-medium tracking-wide text-white/90">
              {statusText}
              {gamePart}
              {recordingOngoing && recording?.startTime && (
                <>
                  {' '}
                  <PipElapsedTimer startTime={recording.startTime} />
                </>
              )}
            </span>
          </div>
        );
      })}
    </div>
  );
}

function PipRecordingPreview({
  slot,
  gameName,
  recording,
  preRecording,
  compact,
  showStopButton,
  onStop,
}: {
  slot: number;
  gameName?: string;
  recording?: Recording;
  preRecording?: PreRecording;
  compact?: boolean;
  showStopButton?: boolean;
  onStop?: () => void;
}) {
  const recordingOngoing =
    !!recording?.startTime && (recording.endTime == null || recording.endTime === undefined);

  const { previewEnabled, previewFrameSrc, hasPreviewFrame } = useRecordingPreview(
    recordingOngoing,
    !!recording,
    recording,
    slot,
  );

  const inactive = !preRecording && !recording;
  const hideStatusOverlay = !!(recording && previewEnabled && hasPreviewFrame);

  return (
    <div
      className={`relative w-full overflow-hidden rounded-md bg-black ${compact ? 'aspect-[16/10]' : 'aspect-video'}`}
      title={gameName}
    >
      {!inactive && recording && previewEnabled && hasPreviewFrame && previewFrameSrc ? (
        <img src={previewFrameSrc} alt="" className="h-full w-full object-contain" />
      ) : (
        <div className="absolute inset-0 flex items-center justify-center bg-gradient-to-b from-base-300/80 to-black">
          <Video className="h-8 w-8 text-white/15" strokeWidth={1.25} aria-hidden />
        </div>
      )}
      <div
        className={`pointer-events-none absolute inset-0 flex flex-col items-center justify-center bg-black/35 px-3 text-center text-[10px] text-white/70 transition-opacity duration-300 ${hideStatusOverlay ? 'opacity-0' : 'opacity-100'}`}
      >
        {preRecording && <span>{preRecording.status}</span>}
        {!inactive && recording && previewEnabled && !hasPreviewFrame && <span>載入預覽…</span>}
        {!inactive && recording && !previewEnabled && <span>預覽已關閉</span>}
      </div>
      {showStopButton && onStop && (
        <button
          type="button"
          title={`停止錄製 ${slot + 1}`}
          aria-label={`停止錄製 ${slot + 1}`}
          onClick={(event) => {
            event.stopPropagation();
            onStop();
          }}
          className="monitoring-no-drag absolute right-1.5 top-1.5 z-10 flex h-7 w-7 items-center justify-center rounded-full bg-red-500/90 text-white shadow-md transition-colors hover:bg-red-400"
        >
          <Square className="h-3 w-3 fill-current" strokeWidth={0} />
        </button>
      )}
    </div>
  );
}

function PipAlwaysOnHeaderStatus() {
  return (
    <div className="flex min-w-0 items-center gap-1.5">
      <span className="relative inline-flex h-1.5 w-1.5 shrink-0 rounded-full bg-success" />
      <span className="truncate text-[11px] font-medium tracking-wide text-white/90">
        桌面重播緩衝中
      </span>
    </div>
  );
}

/** Live preview of the always-on display buffer (slot 0); `since` changes for each buffer run. */
function PipAlwaysOnPreview({ since }: { since: Date }) {
  const { previewEnabled, previewFrameSrc, hasPreviewFrame } = useRecordingPreview(
    true,
    true,
    { startTime: since },
    0,
  );
  const showFrame = previewEnabled && hasPreviewFrame && !!previewFrameSrc;

  return (
    <div className="relative aspect-video w-full overflow-hidden rounded-md bg-black">
      {showFrame ? (
        <img src={previewFrameSrc!} alt="" className="h-full w-full object-contain" />
      ) : (
        <div className="absolute inset-0 flex items-center justify-center bg-gradient-to-b from-base-300/80 to-black">
          <Video className="h-8 w-8 text-white/15" strokeWidth={1.25} aria-hidden />
        </div>
      )}
      <div
        className={`pointer-events-none absolute inset-0 flex flex-col items-center justify-center bg-black/35 px-3 text-center text-[10px] text-white/70 transition-opacity duration-300 ${showFrame ? 'opacity-0' : 'opacity-100'}`}
      >
        {previewEnabled ? <span>載入預覽…</span> : <span>預覽已關閉</span>}
      </div>
    </div>
  );
}

export default function MonitoringCompactShell() {
  const settings = useSettings();
  const appState = useAppState();
  const { hasLoadedObs } = appState;
  const activeRecordings = getActiveRecordings(appState);
  const activePreRecordings = getActivePreRecordings(appState);
  const liveRecordings = activeRecordings.filter(
    (r) => r.endTime == null || r.endTime === undefined,
  );
  const primaryRecording = liveRecordings[0] ?? activeRecordings[0];
  const primaryPreRecording = activePreRecordings[0];
  const hasLiveActivity = hasLiveRecordingActivity(appState);
  const isDualLive = liveRecordings.length + activePreRecordings.length > 1;
  const recordingMode = settings.recordingMode;
  const { exitMonitoringLayout } = useMonitoringLayout();
  const [buttonCooldown, setButtonCooldown] = useState(false);
  const [saveReplayCooldown, setSaveReplayCooldown] = useState(false);
  const [showShockwave, setShowShockwave] = useState(false);
  const [topMost, setTopMost] = useState(() => {
    try {
      return localStorage.getItem('segra.monitoring.topmost') !== 'false';
    } catch {
      return true;
    }
  });

  const recordingOngoing =
    !!primaryRecording?.startTime &&
    (primaryRecording.endTime == null || primaryRecording.endTime === undefined);

  const isLive = !!(primaryRecording || primaryPreRecording);
  const inactive = !primaryPreRecording && !primaryRecording;
  // The always-on display buffer is not a recording; it only shows while nothing records
  const alwaysOnEnabled = settings.alwaysOnReplayBuffer;
  const alwaysOnActive = !!appState.alwaysOnBufferActive && !hasLiveActivity;
  const [alwaysOnSince, setAlwaysOnSince] = useState<Date | null>(null);
  const [alwaysOnToggleCooldown, setAlwaysOnToggleCooldown] = useState(false);

  useEffect(() => {
    setAlwaysOnSince((prev) => (alwaysOnActive ? (prev ?? new Date()) : null));
  }, [alwaysOnActive]);

  const canBookmark =
    !!primaryRecording &&
    recordingOngoing &&
    (recordingMode === 'Session' || recordingMode === 'Hybrid');
  const canSaveReplay =
    (!!primaryRecording &&
      recordingOngoing &&
      (recordingMode === 'Buffer' || recordingMode === 'Hybrid')) ||
    alwaysOnActive;

  const stopDisabledWhileFinalizing = isRecordingFinishing(appState);

  const pipPanels: PipPanel[] = [
    ...activePreRecordings.map((pre) => ({
      key: `pre-${pre.slot ?? pre.game}`,
      slot: pre.slot ?? 0,
      shortLabel: (pre.slot ?? 0) === 1 ? '2 · 準備中' : '1 · 準備中',
      gameName: pre.game,
      preRecording: pre,
      recording: undefined as Recording | undefined,
    })),
    ...liveRecordings.map((rec) => ({
      key: `rec-${rec.slot ?? rec.game}`,
      slot: rec.slot ?? 0,
      shortLabel: (rec.slot ?? 0) === 1 ? '2' : '1',
      gameName: rec.game,
      preRecording: undefined as PreRecording | undefined,
      recording: rec,
    })),
  ].sort((a, b) => a.slot - b.slot);

  useEffect(() => {
    const handleMessage = (event: CustomEvent) => {
      if (event.detail?.method === 'BookmarkCreated') {
        setShowShockwave(true);
        window.setTimeout(() => setShowShockwave(false), 600);
      }
    };
    window.addEventListener('websocket-message', handleMessage as EventListener);
    return () => window.removeEventListener('websocket-message', handleMessage as EventListener);
  }, []);

  useEffect(() => {
    sendMessageToBackend('SetMonitoringWindowTopMost', { enabled: topMost });
  }, [topMost]);

  const toggleTopMost = (event: MouseEvent) => {
    event.stopPropagation();
    setTopMost((prev) => {
      const next = !prev;
      try {
        localStorage.setItem('segra.monitoring.topmost', String(next));
      } catch {
        /* no-op */
      }
      return next;
    });
  };

  const startStopRecording = () => {
    setButtonCooldown(true);
    window.setTimeout(() => setButtonCooldown(false), 1000);
    sendMessageToBackend(hasLiveActivity ? 'StopRecording' : 'StartRecording');
  };

  const stopSlot = (slot: number) => {
    stopRecordingSlot(slot);
  };

  const addBookmark = () => {
    sendMessageToBackend('CreateRecordingBookmark');
  };

  const saveReplayBuffer = () => {
    if (saveReplayCooldown) return;
    setSaveReplayCooldown(true);
    window.setTimeout(() => setSaveReplayCooldown(false), 2000);
    sendMessageToBackend('SaveReplayBufferFromUi');
  };

  // Dedicated message: this window's settings copy may be stale, so it must not send a full settings update
  const toggleAlwaysOn = () => {
    if (alwaysOnToggleCooldown) return;
    setAlwaysOnToggleCooldown(true);
    window.setTimeout(() => setAlwaysOnToggleCooldown(false), 1000);
    sendMessageToBackend('SetAlwaysOnReplayBuffer', { enabled: !alwaysOnEnabled });
  };

  const beginWindowDrag = (event: MouseEvent) => {
    if (event.button !== 0) return;
    event.preventDefault();
    sendMessageToBackend('BeginMonitoringWindowDrag');
  };

  return (
    <div className="monitoring-pip-root h-screen w-screen overflow-hidden rounded-3xl bg-black shadow-[0_12px_40px_rgba(0,0,0,0.55)] ring-1 ring-white/10">
      <div className="relative flex h-full w-full min-h-0 flex-col overflow-hidden">
        <div
          className="relative shrink-0 cursor-grab active:cursor-grabbing"
          onMouseDown={beginWindowDrag}
        >
          <div className="relative z-30 flex items-center gap-1.5 px-2 py-1.5">
            <div
              className="pointer-events-none absolute inset-0 bg-gradient-to-b from-black/70 via-black/35 to-transparent"
              aria-hidden
            />
            <div className="relative flex h-7 w-7 shrink-0 items-center justify-center">
              <GripHorizontal className="h-3.5 w-3.5 text-white/35" aria-hidden />
            </div>
            <div className="relative min-w-0 flex-1">
              {isLive ? (
                <PipHeaderStatus panels={pipPanels} />
              ) : alwaysOnActive ? (
                <PipAlwaysOnHeaderStatus />
              ) : (
                <PipHeaderStatus panels={[]} />
              )}
            </div>
            <div className="relative ml-auto flex shrink-0 items-center gap-1 pr-0.5">
              <button
                type="button"
                title={topMost ? '取消置頂' : '視窗置頂'}
                onClick={toggleTopMost}
                onMouseDown={(event) => event.stopPropagation()}
                className={`monitoring-no-drag flex h-7 w-7 shrink-0 items-center justify-center rounded-full backdrop-blur-sm transition-colors ${
                  topMost
                    ? 'bg-primary/25 text-primary hover:bg-primary/35'
                    : 'bg-white/10 text-white/55 hover:bg-white/20 hover:text-white/85'
                }`}
              >
                {topMost ? (
                  <Pin className="h-3.5 w-3.5" strokeWidth={2} />
                ) : (
                  <PinOff className="h-3.5 w-3.5" strokeWidth={2} />
                )}
              </button>
              <button
                type="button"
                title="關閉監控視窗"
                onClick={(event) => {
                  event.stopPropagation();
                  exitMonitoringLayout();
                }}
                onMouseDown={(event) => event.stopPropagation()}
                className="monitoring-no-drag flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-black/55 text-white/85 backdrop-blur-sm transition-colors hover:bg-black/75 hover:text-white"
              >
                <X className="h-3.5 w-3.5" strokeWidth={2} />
              </button>
            </div>
          </div>

          <div
            className={`relative w-full overflow-hidden bg-black px-2 pb-2 ${isDualLive ? 'max-h-[calc(100vh-9rem)] overflow-y-auto' : ''}`}
          >
            {inactive && alwaysOnActive && alwaysOnSince ? (
              <PipAlwaysOnPreview since={alwaysOnSince} />
            ) : inactive ? (
              <div className="relative aspect-video w-full overflow-hidden rounded-md bg-black">
                <div className="absolute inset-0 flex flex-col items-center justify-center bg-gradient-to-b from-base-300/80 to-black px-4 text-center text-xs text-white/70">
                  <Video className="mb-2 h-10 w-10 text-white/15" strokeWidth={1.25} aria-hidden />
                  <span>點下方按鈕開始錄製</span>
                </div>
              </div>
            ) : (
              <div className={isDualLive ? 'space-y-2' : ''}>
                {pipPanels.map((panel) => (
                  <PipRecordingPreview
                    key={panel.key}
                    slot={panel.slot}
                    gameName={panel.gameName}
                    recording={panel.recording}
                    preRecording={panel.preRecording}
                    compact={isDualLive}
                    showStopButton={isDualLive}
                    onStop={() => stopSlot(panel.slot)}
                  />
                ))}
              </div>
            )}

            {showShockwave && (
              <div className="pointer-events-none absolute inset-0 z-20 overflow-hidden">
                <div className="animate-shockwave absolute left-1/2 top-1/2 h-0 w-0 -translate-x-1/2 -translate-y-1/2 rounded-full bg-primary/50" />
              </div>
            )}
          </div>
        </div>

        <div className="monitoring-no-drag relative z-40 mt-auto shrink-0 px-3 pb-3 pt-2">
          <div className="relative space-y-2.5">
            <PipAudioMixer poll={hasLoadedObs && (isLive || alwaysOnActive)} />

            <div className="flex items-center justify-center gap-3 rounded-full bg-black/40 px-4 py-2.5 backdrop-blur-md ring-1 ring-white/10">
              {!isDualLive && (
                <PipIconButton
                  title={!hasLoadedObs ? 'OBS 載入中…' : isLive ? '停止錄製' : '開始錄製'}
                  disabled={buttonCooldown || stopDisabledWhileFinalizing}
                  onClick={startStopRecording}
                  variant={isLive ? 'danger' : 'default'}
                >
                  {isLive ? (
                    <Square className="h-4 w-4 fill-current" strokeWidth={0} />
                  ) : (
                    <span className="block h-3.5 w-3.5 rounded-full bg-red-500 ring-2 ring-white/90" />
                  )}
                </PipIconButton>
              )}

              <PipIconButton
                title="標記（前景遊戲 · 與快捷鍵相同）"
                disabled={!canBookmark}
                onClick={addBookmark}
                variant="accent"
              >
                <BookmarkPlus className="h-[18px] w-[18px]" strokeWidth={2} />
              </PipIconButton>

              <PipIconButton
                title={
                  alwaysOnActive
                    ? '儲存桌面重播緩衝（與快捷鍵相同）'
                    : '儲存重播緩存（前景遊戲 · 與快捷鍵相同）'
                }
                disabled={!canSaveReplay || saveReplayCooldown}
                onClick={saveReplayBuffer}
              >
                <History className="h-[18px] w-[18px]" strokeWidth={2} />
              </PipIconButton>

              {!isLive && (
                <PipIconButton
                  title={alwaysOnEnabled ? '暫停桌面重播緩衝' : '開啟桌面重播緩衝'}
                  disabled={!hasLoadedObs || alwaysOnToggleCooldown}
                  onClick={toggleAlwaysOn}
                >
                  {alwaysOnEnabled ? (
                    <CirclePause className="h-[18px] w-[18px]" strokeWidth={2} />
                  ) : (
                    <CirclePlay className="h-[18px] w-[18px]" strokeWidth={2} />
                  )}
                </PipIconButton>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
