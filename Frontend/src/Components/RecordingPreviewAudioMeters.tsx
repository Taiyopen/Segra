import { useRecordingAudioLevels, maxPeak, peakSummary } from '../Hooks/useRecordingAudioLevels';

type Props = {
  /** When false, polling stops and the overlay hides. */
  poll: boolean;
  compact?: boolean;
  /** Minimal inline bars for PiP / floating monitor window. */
  pip?: boolean;
};

/** Overlay peak meters for recording preview (HTTP `/api/recording-audio-levels`). */
export default function RecordingPreviewAudioMeters({ poll, compact, pip }: Props) {
  const levels = useRecordingAudioLevels(poll);
  if (!poll) return null;

  const maxIn = maxPeak(levels?.inputTracks);
  const maxOut = maxPeak(levels?.outputTracks);
  const tipIn = peakSummary(levels?.inputTracks);
  const tipOut = peakSummary(levels?.outputTracks);

  if (pip) {
    return (
      <div
        className="flex gap-2 px-1"
        title={
          [tipIn && `輸入 ${tipIn}`, tipOut && `輸出 ${tipOut}`].filter(Boolean).join(' · ') ||
          undefined
        }
      >
        <div className="h-1 min-w-0 flex-1 overflow-hidden rounded-full bg-white/10">
          <div
            className="h-full rounded-full bg-emerald-400/90 transition-[width] duration-75"
            style={{ width: `${Math.round(maxIn * 100)}%` }}
          />
        </div>
        <div className="h-1 min-w-0 flex-1 overflow-hidden rounded-full bg-white/10">
          <div
            className="h-full rounded-full bg-sky-400/90 transition-[width] duration-75"
            style={{ width: `${Math.round(maxOut * 100)}%` }}
          />
        </div>
      </div>
    );
  }

  const barH = compact ? 'h-1' : 'h-1.5';
  const label = compact ? 'text-[9px]' : 'text-[10px]';
  const pct = compact ? 'text-[8px]' : 'text-[9px]';

  return (
    <div
      className={`pointer-events-none absolute inset-x-0 bottom-0 z-10 border-t border-white/10 bg-black/60 px-2 py-1 backdrop-blur-[2px] ${label} text-gray-300`}
    >
      <div className="flex gap-2">
        <div className="min-w-0 flex-1" title={tipIn || undefined}>
          <div className={`mb-0.5 flex justify-between gap-1 leading-none text-gray-400 ${label}`}>
            <span className="truncate">輸入</span>
            <span className={`shrink-0 tabular-nums text-gray-500 ${pct}`}>
              {Math.round(maxIn * 100)}%
            </span>
          </div>
          <div className={`${barH} overflow-hidden rounded-full bg-gray-700/90`}>
            <div
              className={`${barH} max-w-full rounded-full bg-emerald-400/95 transition-[width] duration-75`}
              style={{ width: `${Math.round(maxIn * 100)}%` }}
            />
          </div>
        </div>
        <div className="min-w-0 flex-1" title={tipOut || undefined}>
          <div className={`mb-0.5 flex justify-between gap-1 leading-none text-gray-400 ${label}`}>
            <span className="truncate">輸出</span>
            <span className={`shrink-0 tabular-nums text-gray-500 ${pct}`}>
              {Math.round(maxOut * 100)}%
            </span>
          </div>
          <div className={`${barH} overflow-hidden rounded-full bg-gray-700/90`}>
            <div
              className={`${barH} max-w-full rounded-full bg-sky-400/95 transition-[width] duration-75`}
              style={{ width: `${Math.round(maxOut * 100)}%` }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
