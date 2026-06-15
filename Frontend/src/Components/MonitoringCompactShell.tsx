import { useEffect, useState } from 'react';
import { BookmarkPlus, History, Monitor, OctagonX, PictureInPicture2 } from 'lucide-react';
import Button from './Button';
import RecordingPreviewAudioMeters from './RecordingPreviewAudioMeters';
import { useMonitoringLayout } from '../Context/MonitoringLayoutContext';
import { useSettings } from '../Context/SettingsContext';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import { useRecordingPreview } from '../Hooks/useRecordingPreview';

export default function MonitoringCompactShell() {
  const settings = useSettings();
  const { hasLoadedObs, recording, preRecording } = settings.state;
  const recordingMode = settings.recordingMode;
  const { exitMonitoringLayout } = useMonitoringLayout();
  const [buttonCooldown, setButtonCooldown] = useState(false);
  const [saveReplayCooldown, setSaveReplayCooldown] = useState(false);
  const [showShockwave, setShowShockwave] = useState(false);

  const recordingOngoing =
    !!recording?.startTime && (recording.endTime == null || recording.endTime === undefined);

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

  return (
    <div className="flex h-screen w-screen flex-col bg-base-300 text-gray-200">
      <header className="flex shrink-0 items-center justify-between border-b border-base-400 px-2 py-1.5">
        <div className="flex items-center gap-1.5 text-xs text-gray-400">
          <PictureInPicture2 className="h-4 w-4 shrink-0 text-gray-500" aria-hidden />
          <span>極簡監控</span>
        </div>
        <Button
          variant="ghost"
          size="xs"
          className="h-7 min-h-0 px-2 text-xs"
          onClick={exitMonitoringLayout}
        >
          結束監控
        </Button>
      </header>

      <div className="flex min-h-0 flex-1 flex-col gap-2 p-2">
        <div className="relative min-h-0 flex-1 overflow-hidden rounded-lg border border-base-400 bg-black">
          {showShockwave && (
            <div className="pointer-events-none absolute inset-0 z-20 overflow-hidden rounded-lg">
              <div className="animate-shockwave absolute left-1/2 top-1/2 h-0 w-0 -translate-x-1/2 -translate-y-1/2 rounded-full bg-primary/40" />
            </div>
          )}
          <div className="relative flex h-full min-h-0 w-full flex-col items-center justify-center overflow-hidden">
            {!inactive && recording && previewEnabled ? (
              <img
                src={previewFrameSrc ?? undefined}
                alt=""
                className={`relative z-[1] max-h-full max-w-full object-contain transition-opacity duration-200 ${hasPreviewFrame ? 'opacity-100' : 'opacity-0'}`}
              />
            ) : null}
            <div
              className={`pointer-events-none absolute inset-0 z-[2] flex flex-col items-center justify-center gap-1 bg-black/20 px-3 text-center text-xs text-gray-500 transition-opacity duration-200 ${hideStatusOverlay ? 'opacity-0' : 'opacity-100'}`}
            >
              {inactive && <span>尚未錄製</span>}
              {preRecording && <span>{preRecording.status}</span>}
              {!inactive && recording && previewEnabled && !hasPreviewFrame && (
                <span>載入預覽…</span>
              )}
              {!inactive && recording && !previewEnabled && <span>預覽已關閉（快捷鍵可開啟）</span>}
            </div>
            <RecordingPreviewAudioMeters poll={hasLoadedObs} compact />
          </div>
        </div>

        <div className="flex shrink-0 justify-center gap-1.5">
          <Button
            variant="primary"
            size="sm"
            className="min-h-9 flex-1 gap-1 px-2 text-xs"
            disabled={buttonCooldown || !hasLoadedObs || stopDisabledWhileFinalizing}
            onClick={startStopRecording}
            title={recording || preRecording ? '停止錄製' : '開始錄製'}
          >
            {recording || preRecording ? (
              <>
                <OctagonX className="h-4 w-4 shrink-0" />
                停止
              </>
            ) : (
              <>
                <Monitor className="h-4 w-4 shrink-0" />
                錄製
              </>
            )}
          </Button>
          <Button
            variant="primary"
            size="sm"
            className="min-h-9 flex-1 gap-1 px-2 text-xs"
            disabled={!canBookmark}
            onClick={addBookmark}
            title="標記（與快捷鍵相同）"
          >
            <BookmarkPlus className="h-4 w-4 shrink-0" />
            標記
          </Button>
          <Button
            variant="primary"
            size="sm"
            className="min-h-9 flex-1 gap-1 px-2 text-xs"
            disabled={!canSaveReplay || saveReplayCooldown}
            onClick={saveReplayBuffer}
            title="儲存重播緩存（與快捷鍵相同）"
          >
            <History className="h-4 w-4 shrink-0" />
            重播
          </Button>
        </div>
      </div>
    </div>
  );
}
