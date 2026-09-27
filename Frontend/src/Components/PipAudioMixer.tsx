import { useEffect, useState } from 'react';
import { Gamepad2, Headphones, Mic, Volume2, VolumeX } from 'lucide-react';

const MIXER_URL = 'http://localhost:2222/api/audio-mixer';
// Same scale as the OBS mixer: -60 dB to 0 dB, green up to -20, yellow up to -9, red above
const MIN_DB = -60;
const METER_GRADIENT =
  'linear-gradient(to right, #34d399 0%, #34d399 66.7%, #facc15 66.7%, #facc15 85%, #f87171 85%)';

type MixerSource = {
  id: string;
  name: string;
  kind: 'mic' | 'desktop' | 'game' | 'voice';
  peakDb: number;
  muted: boolean;
  volume: number;
};

const KIND_ICONS = { mic: Mic, desktop: Volume2, game: Gamepad2, voice: Headphones };

function useAudioMixer(poll: boolean, intervalMs = 90): MixerSource[] | null {
  const [sources, setSources] = useState<MixerSource[] | null>(null);

  useEffect(() => {
    if (!poll) {
      setSources(null);
      return;
    }

    let cancelled = false;

    const tick = async () => {
      try {
        const res = await fetch(MIXER_URL);
        if (!res.ok || cancelled) return;
        const data = (await res.json()) as { sources: MixerSource[] };
        if (!cancelled && Array.isArray(data.sources)) setSources(data.sources);
      } catch {
        // Server down — ignore until next tick
      }
    };

    void tick();
    const id = window.setInterval(() => void tick(), intervalMs);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, [poll, intervalMs]);

  return poll ? sources : null;
}

function dbToFraction(db: number): number {
  if (!Number.isFinite(db)) return 0;
  return Math.min(1, Math.max(0, (db - MIN_DB) / -MIN_DB));
}

/** OBS-style mixer for the PiP window: one row per recorded audio source. */
export default function PipAudioMixer({ poll }: { poll: boolean }) {
  const sources = useAudioMixer(poll);
  if (!sources?.length) return null;

  return (
    <div className="max-h-[4.75rem] space-y-1 overflow-y-auto px-1">
      {sources.map((s) => {
        const Icon = s.muted ? VolumeX : (KIND_ICONS[s.kind] ?? Volume2);
        const volumePct = `${Math.round(s.volume * 100)}%`;
        return (
          <div
            key={s.id}
            className={`flex items-center gap-1.5 text-[9px] leading-none ${s.muted ? 'text-white/35' : 'text-white/70'}`}
            title={`${s.name}${s.muted ? '（靜音）' : ''} · 音量 ${volumePct}`}
          >
            <Icon
              className={`h-3 w-3 shrink-0 ${s.muted ? 'text-red-400/80' : ''}`}
              strokeWidth={2}
            />
            <span className="w-14 shrink-0 truncate">{s.name}</span>
            <div className="relative h-1.5 min-w-0 flex-1 overflow-hidden rounded-full bg-white/10">
              {!s.muted && (
                <div
                  className="absolute inset-0 transition-[clip-path] duration-75"
                  style={{
                    backgroundImage: METER_GRADIENT,
                    clipPath: `inset(0 ${100 - dbToFraction(s.peakDb) * 100}% 0 0)`,
                  }}
                />
              )}
            </div>
            <span className="w-7 shrink-0 text-right tabular-nums">
              {s.muted ? '靜音' : volumePct}
            </span>
          </div>
        );
      })}
    </div>
  );
}
