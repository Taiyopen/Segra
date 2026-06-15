import { useEffect, useState, type MouseEvent, type ReactNode } from 'react';
import { BookmarkPlus, GripHorizontal, History, Pin, PinOff, Square, Video, X } from 'lucide-react';
import RecordingPreviewAudioMeters from './RecordingPreviewAudioMeters';
import { useMonitoringLayout } from '../Context/MonitoringLayoutContext';
import { useSettings } from '../Context/SettingsContext';
import { sendMessageToBackend } from '../Utils/MessageUtils';
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

export default function MonitoringCompactShell() {
  const settings = useSettings();
  const { hasLoadedObs, recording, preRecording } = settings.state;
  const recordingMode = settings.recordingMode;
  const { exitMonitoringLayout } = useMonitoringLayout();
  const [buttonCooldown, setButtonCooldown] = useState(false);
  const [saveReplayCooldown, setSaveReplayCooldown] = useState(false);
  const [showShockwave, setShowShockwave] = useState(false);
  const [elapsedSec, setElapsedSec] = useState(0);
  const [topMost, setTopMost] = useState(() => {
    try {
      return localStorage.getItem('segra.monitoring.topmost') !== 'false';
    } catch {
      return true;
    }
  });

  const recordingOngoing =
    !!recording?.startTime && (recording.endTime == null || recording.endTime === undefined);

  const isLive = !!(recording || preRecording);

  const { previewEnabled, previewFrameSrc, hasPreviewFrame } = useRecordingPreview(
    recordingOngoing,
    !!recording,
    recording,
  );

  const inactive = !preRecording && !recording;
  const hideStatusOverlay = !!(recording && previewEnabled && hasPreviewFrame);
  const canBookmark =
    !!recording && recordingOngoing && (recordingMode === 'Session' || recordingMode === 'Hybrid');
  const canSaveReplay =
    !!recording && recordingOngoing && (recordingMode === 'Buffer' || recordingMode === 'Hybrid');

  const stopDisabledWhileFinalizing = !!(
    settings.state.recording &&
    recording &&
    recording.endTime !== null
  );

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
    if (!recordingOngoing || !recording?.startTime) {
      setElapsedSec(0);
      return;
    }

    const startMs = new Date(recording.startTime).getTime();
    const tick = () => {
      const sec = Math.max(0, Math.floor((Date.now() - startMs) / 1000));
      setElapsedSec(sec);
    };
    tick();
    const id = window.setInterval(tick, 1000);
    return () => window.clearInterval(id);
  }, [recordingOngoing, recording?.startTime]);

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
    sendMessageToBackend(
      settings.state.recording || settings.state.preRecording ? 'StopRecording' : 'StartRecording',
    );
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

  const beginWindowDrag = (event: MouseEvent) => {
    if (event.button !== 0) return;
    event.preventDefault();
    sendMessageToBackend('BeginMonitoringWindowDrag');
  };

  return (
    <div className="monitoring-pip-root h-screen w-screen overflow-hidden rounded-3xl bg-black shadow-[0_12px_40px_rgba(0,0,0,0.55)] ring-1 ring-white/10">
      <div className="relative flex h-full w-full min-h-0 flex-col overflow-hidden">
        {/* Draggable header + 16:9 preview */}
        <div
          className="relative shrink-0 cursor-grab active:cursor-grabbing"
          onMouseDown={beginWindowDrag}
        >
          <div className="relative z-30 flex items-center gap-1.5 px-2 py-1.5">
            <div
              className="pointer-events-none absolute inset-0 bg-gradient-to-b from-black/70 via-black/35 to-transparent"
              aria-hidden
            />
            <GripHorizontal className="relative h-3.5 w-3.5 shrink-0 text-white/35" aria-hidden />
            <div className="relative flex min-w-0 flex-1 items-center gap-2">
              {recordingOngoing ? (
                <>
                  <span className="relative flex h-2 w-2 shrink-0">
                    <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-red-400 opacity-75" />
                    <span className="relative inline-flex h-2 w-2 rounded-full bg-red-500" />
                  </span>
                  <span className="truncate text-[11px] font-medium tracking-wide text-white/90">
                    REC {formatDuration(elapsedSec)}
                  </span>
                </>
              ) : preRecording ? (
                <span className="truncate text-[11px] font-medium text-amber-200/90">準備中…</span>
              ) : (
                <span className="truncate text-[11px] font-medium text-white/50">Segra 監控</span>
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

          <div className="relative aspect-video w-full overflow-hidden bg-black">
            {!inactive && recording && previewEnabled && hasPreviewFrame && previewFrameSrc ? (
              <img src={previewFrameSrc} alt="" className="h-full w-full object-contain" />
            ) : (
              <div className="absolute inset-0 flex items-center justify-center bg-gradient-to-b from-base-300/80 to-black">
                <Video className="h-10 w-10 text-white/15" strokeWidth={1.25} aria-hidden />
              </div>
            )}

            <div
              className={`pointer-events-none absolute inset-0 flex flex-col items-center justify-center bg-black/35 px-4 text-center text-xs text-white/70 transition-opacity duration-300 ${hideStatusOverlay ? 'opacity-0' : 'opacity-100'}`}
            >
              {inactive && <span>點下方按鈕開始錄製</span>}
              {preRecording && <span>{preRecording.status}</span>}
              {!inactive && recording && previewEnabled && !hasPreviewFrame && (
                <span>載入預覽…</span>
              )}
              {!inactive && recording && !previewEnabled && <span>預覽已關閉（快捷鍵可開啟）</span>}
            </div>

            {showShockwave && (
              <div className="pointer-events-none absolute inset-0 z-20 overflow-hidden">
                <div className="animate-shockwave absolute left-1/2 top-1/2 h-0 w-0 -translate-x-1/2 -translate-y-1/2 rounded-full bg-primary/50" />
              </div>
            )}
          </div>
        </div>

        {/* Bottom controls */}
        <div className="monitoring-no-drag relative z-40 mt-auto shrink-0 px-3 pb-3 pt-2">
          <div className="relative space-y-2.5">
            <RecordingPreviewAudioMeters poll={hasLoadedObs && isLive} pip />

            <div className="flex items-center justify-center gap-3 rounded-full bg-black/40 px-4 py-2.5 backdrop-blur-md ring-1 ring-white/10">
              <PipIconButton
                title={
                  !hasLoadedObs
                    ? 'OBS 載入中…'
                    : recording || preRecording
                      ? '停止錄製'
                      : '開始錄製'
                }
                disabled={buttonCooldown || stopDisabledWhileFinalizing}
                onClick={startStopRecording}
                variant={recording || preRecording ? 'danger' : 'default'}
              >
                {recording || preRecording ? (
                  <Square className="h-4 w-4 fill-current" strokeWidth={0} />
                ) : (
                  <span className="block h-3.5 w-3.5 rounded-full bg-red-500 ring-2 ring-white/90" />
                )}
              </PipIconButton>

              <PipIconButton
                title="標記（與快捷鍵相同）"
                disabled={!canBookmark}
                onClick={addBookmark}
                variant="accent"
              >
                <BookmarkPlus className="h-[18px] w-[18px]" strokeWidth={2} />
              </PipIconButton>

              <PipIconButton
                title="儲存重播緩存（與快捷鍵相同）"
                disabled={!canSaveReplay || saveReplayCooldown}
                onClick={saveReplayBuffer}
              >
                <History className="h-[18px] w-[18px]" strokeWidth={2} />
              </PipIconButton>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
