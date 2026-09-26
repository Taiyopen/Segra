import { useEffect, useRef } from 'react';
import { Content } from '../Models/types';
import { createNativeCaptureTap, type NativeAudioSink } from '../Services/nativeAudioSink';

// createMediaElementSource can only be called once per element. The graph stays
// with that element across clip changes; closing the context and skipping the
// next setup leaves the element with nowhere to play audio.
type ElementAudioGraph = {
  ctx: AudioContext;
  node: AudioWorkletNode;
  sink: NativeAudioSink;
};

const graphs = new WeakMap<HTMLMediaElement, ElementAudioGraph>();
const pendingGraphs = new WeakMap<HTMLMediaElement, Promise<ElementAudioGraph | null>>();
const discardedElements = new WeakSet<HTMLMediaElement>();

function destroyGraph(vid: HTMLMediaElement) {
  discardedElements.add(vid);
  const graph = graphs.get(vid);
  graphs.delete(vid);
  if (!graph) return;
  try {
    graph.sink.flush();
  } catch {
    // ignore
  }
  try {
    graph.node.disconnect();
  } catch {
    // ignore
  }
  try {
    graph.node.port.close();
  } catch {
    // ignore
  }
  graph.ctx.close().catch(() => {});
}

async function ensureGraph(vid: HTMLMediaElement): Promise<ElementAudioGraph | null> {
  if (discardedElements.has(vid)) return null;
  const existing = graphs.get(vid);
  if (existing) return existing;

  const pending = pendingGraphs.get(vid);
  if (pending) return pending;

  const build = (async (): Promise<ElementAudioGraph | null> => {
    try {
      const ctx = new AudioContext({ sampleRate: 48000, latencyHint: 'interactive' });
      if (discardedElements.has(vid)) {
        ctx.close().catch(() => {});
        return null;
      }

      const tap = await createNativeCaptureTap(ctx);
      if (!tap || discardedElements.has(vid)) {
        try {
          tap?.node.disconnect();
        } catch {
          // ignore
        }
        ctx.close().catch(() => {});
        return null;
      }

      const raced = graphs.get(vid);
      if (raced) {
        try {
          tap.node.disconnect();
        } catch {
          // ignore
        }
        ctx.close().catch(() => {});
        return raced;
      }

      const source = ctx.createMediaElementSource(vid);
      source.connect(tap.node);
      const graph: ElementAudioGraph = { ctx, node: tap.node, sink: tap.sink };
      if (discardedElements.has(vid)) {
        try {
          graph.node.disconnect();
        } catch {
          // ignore
        }
        ctx.close().catch(() => {});
        return null;
      }
      graphs.set(vid, graph);
      return graph;
    } catch {
      return null;
    }
  })().finally(() => {
    pendingGraphs.delete(vid);
  });

  pendingGraphs.set(vid, build);
  return build;
}

/**
 * Routes a single-audio-track <video> element's own audio through the native sink (played by
 * Segra.exe) so Windows application-audio capture (Discord/OBS) includes it. The element stays
 * the playback clock: its own A/V sync, volume, mute and playbackRate continue to apply, so no
 * scheduling logic is needed here. Multi-track content is left to useAudioTracks, and when the
 * native sink is unavailable the element keeps its default webview output.
 *
 * Note: the element's volume/mute are applied by Chromium's media pipeline upstream of the
 * MediaElementAudioSourceNode, which is what keeps the volume slider working through this path.
 */
export function useNativeElementAudio(
  videoRef: React.RefObject<HTMLVideoElement | null>,
  video: Content,
): void {
  const aliveRef = useRef(false);

  // StrictMode runs effect cleanup and setup back to back. Close the context only
  // when the page actually went away, so the same element is not sourced twice.
  useEffect(() => {
    aliveRef.current = true;
    const vid = videoRef.current;
    return () => {
      aliveRef.current = false;
      queueMicrotask(() => {
        if (aliveRef.current || !vid) return;
        destroyGraph(vid);
      });
    };
  }, [videoRef]);

  useEffect(() => {
    const vid = videoRef.current;
    if (!vid) return;

    const trackCount = video.audioTrackNames?.length ?? 0;
    // Multi-track playback owns the shared native player. Stop this element's PCM
    // without flushing that player.
    if (trackCount > 1) {
      graphs.get(vid)?.sink.stopForwarding();
      return;
    }
    // No graph yet and this clip is not single-track: leave the element's own output.
    if (trackCount !== 1 && !graphs.has(vid) && !pendingGraphs.has(vid)) return;

    let cancelled = false;
    let attached = false;
    let sink: NativeAudioSink | null = null;
    let ctx: AudioContext | null = null;

    const ensureRunning = () => {
      if (ctx && ctx.state === 'suspended') {
        ctx.resume().catch(() => {});
      }
    };
    const startNative = () => {
      try {
        sink?.play(ctx?.sampleRate ?? 48000, 2);
      } catch {
        // ignore
      }
    };
    const stopNative = () => {
      try {
        sink?.flush();
      } catch {
        // ignore
      }
    };

    const onPlay = () => {
      ensureRunning();
      startNative();
    };
    const onPause = () => {
      stopNative();
    };
    const onVolumeChange = () => {
      ensureRunning();
    };
    const onGesture = () => {
      ensureRunning();
    };

    const attach = (graph: ElementAudioGraph) => {
      if (cancelled || !aliveRef.current || discardedElements.has(vid)) return;
      sink = graph.sink;
      ctx = graph.ctx;
      attached = true;
      vid.addEventListener('play', onPlay);
      vid.addEventListener('pause', onPause);
      vid.addEventListener('volumechange', onVolumeChange);
      window.addEventListener('pointerdown', onGesture);
      window.addEventListener('keydown', onGesture);
      if (!vid.paused) onPlay();
    };

    const existing = graphs.get(vid);
    if (existing) {
      attach(existing);
    } else {
      void ensureGraph(vid).then((graph) => {
        if (!graph) return;
        attach(graph);
      });
    }

    return () => {
      cancelled = true;
      if (!attached) return;
      vid.removeEventListener('play', onPlay);
      vid.removeEventListener('pause', onPause);
      vid.removeEventListener('volumechange', onVolumeChange);
      window.removeEventListener('pointerdown', onGesture);
      window.removeEventListener('keydown', onGesture);
      stopNative();
    };
  }, [videoRef, video.filePath, video.fileName, video.audioTrackNames?.join('|')]);
}
