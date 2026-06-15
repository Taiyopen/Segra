import { useEffect, useState } from 'react';

const LEVELS_URL = 'http://localhost:2222/api/recording-audio-levels';

export type RecordingAudioLevelTrack = {
  index: number;
  id: string;
  name: string;
  peak: number;
};

export type RecordingAudioLevelsPayload = {
  inputTracks: RecordingAudioLevelTrack[];
  outputTracks: RecordingAudioLevelTrack[];
};

function clamp01(n: number) {
  if (!Number.isFinite(n)) return 0;
  return Math.min(1, Math.max(0, n));
}

/** Polls Windows endpoint peaks for configured capture/render devices (same routing as OBS). */
export function useRecordingAudioLevels(
  poll: boolean,
  intervalMs = 90,
): RecordingAudioLevelsPayload | null {
  const [levels, setLevels] = useState<RecordingAudioLevelsPayload | null>(null);

  useEffect(() => {
    if (!poll) {
      setLevels(null);
      return;
    }

    let cancelled = false;

    const tick = async () => {
      try {
        const res = await fetch(LEVELS_URL);
        if (!res.ok || cancelled) return;
        const data = (await res.json()) as RecordingAudioLevelsPayload;
        if (cancelled || !Array.isArray(data.inputTracks) || !Array.isArray(data.outputTracks)) {
          return;
        }
        setLevels(data);
      } catch {
        // Offline / CORS / server down — ignore until next tick
      }
    };

    void tick();
    const id = window.setInterval(() => void tick(), intervalMs);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, [poll, intervalMs]);

  return poll ? levels : null;
}

export function maxPeak(tracks: RecordingAudioLevelTrack[] | undefined): number {
  if (!tracks?.length) return 0;
  return clamp01(Math.max(...tracks.map((t) => clamp01(t.peak))));
}

export function peakSummary(tracks: RecordingAudioLevelTrack[] | undefined): string {
  if (!tracks?.length) return '';
  return tracks
    .map((t) => `${t.name || `軌道 ${t.index + 1}`}: ${Math.round(clamp01(t.peak) * 100)}%`)
    .join('\n');
}
