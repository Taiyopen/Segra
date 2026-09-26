import React, { useCallback, useEffect, useRef } from 'react';

// Render waveform bars onto a canvas for a given pixel range [regionLeft, regionLeft + canvas.width).
// peaksMax is the loudest absolute peak in the whole clip; bars are scaled against it so
// the loudest moment fills the canvas height regardless of the recording's overall level.
function renderWaveformRegion(
  canvas: HTMLCanvasElement,
  peaks: number[],
  peaksMax: number,
  totalWidth: number,
  regionLeft: number,
) {
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  const { width, height } = canvas;
  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = '#49515b';

  const columns = Math.floor(peaks.length / 2);
  if (columns === 0) return;
  const barWidth = totalWidth / columns;
  const denom = peaksMax > 0 ? peaksMax : 128;
  const maxBarHeight = height * 0.8;

  for (let px = 0; px < width; px++) {
    const worldX = regionLeft + px;
    const colStart = Math.max(0, Math.floor(worldX / barWidth));
    const colEnd = Math.min(columns, Math.ceil((worldX + 1) / barWidth));

    let maxAmp = 0;
    for (let i = colStart; i < colEnd; i++) {
      const amp = Math.max(Math.abs(peaks[i * 2]), Math.abs(peaks[i * 2 + 1]));
      if (amp > maxAmp) maxAmp = amp;
    }

    const amplitude = Math.min(1, maxAmp / denom);
    const barHeight = Math.max(1, amplitude * maxBarHeight);
    ctx.fillRect(px, height - barHeight, 1, barHeight);
  }
}

/**
 * Draws the audio waveform under the timeline. Only a 3x-viewport slice is rendered; scrolling
 * moves the canvas and redraws only when the viewport leaves that slice. Returns the canvas ref.
 */
export function useTimelineWaveform({
  enabled,
  peaksUrl,
  scrollContainerRef,
  pixelsPerSecond,
  duration,
}: {
  enabled: boolean;
  peaksUrl: string;
  scrollContainerRef: React.RefObject<HTMLDivElement | null>;
  pixelsPerSecond: number;
  duration: number;
}) {
  const waveformCanvasRef = useRef<HTMLCanvasElement>(null);
  const peaksRef = useRef<number[] | null>(null);
  const peaksMaxRef = useRef<number>(128);
  const waveformStateRef = useRef({ pixelsPerSecond: 0, duration: 0 });
  const waveformBufferRef = useRef({ regionLeft: 0, regionRight: 0 });

  useEffect(() => {
    waveformStateRef.current.pixelsPerSecond = pixelsPerSecond;
    waveformStateRef.current.duration = duration;
  }, [pixelsPerSecond, duration]);

  // Render a 3x-viewport buffer and position it; scrolling just moves the canvas
  const renderWaveformBuffer = useCallback(() => {
    const canvas = waveformCanvasRef.current;
    const peaks = peaksRef.current;
    const scroller = scrollContainerRef.current;
    if (!canvas || !peaks || peaks.length === 0 || !scroller) return;
    const { pixelsPerSecond: pps, duration: dur } = waveformStateRef.current;
    const totalWidth = dur * pps;
    const viewportWidth = scroller.clientWidth;
    const scrollLeft = scroller.scrollLeft;

    // Buffer: 3x viewport centered on current scroll, clamped to timeline bounds
    const bufferWidth = Math.min(viewportWidth * 3, Math.ceil(totalWidth));
    const regionLeft = Math.max(0, Math.floor(scrollLeft - viewportWidth));
    const regionRight = regionLeft + bufferWidth;

    if (canvas.width !== bufferWidth) canvas.width = bufferWidth;
    if (canvas.height !== 49) canvas.height = 49;

    canvas.style.left = `${regionLeft}px`;
    renderWaveformRegion(canvas, peaks, peaksMaxRef.current, totalWidth, regionLeft);
    waveformBufferRef.current = { regionLeft, regionRight };
  }, [scrollContainerRef]);

  // Reposition canvas on scroll; only re-render if scrolled past buffer edges
  const updateWaveformScroll = useCallback(() => {
    const canvas = waveformCanvasRef.current;
    const scroller = scrollContainerRef.current;
    if (!canvas || !peaksRef.current?.length || !scroller) return;
    const scrollLeft = scroller.scrollLeft;
    const viewportWidth = scroller.clientWidth;
    const { regionLeft, regionRight } = waveformBufferRef.current;

    // If viewport is fully within the buffer, no redraw needed
    if (scrollLeft >= regionLeft && scrollLeft + viewportWidth <= regionRight) return;

    // Scrolled past buffer — re-render a new buffer region
    renderWaveformBuffer();
  }, [renderWaveformBuffer, scrollContainerRef]);

  // Fetch waveform peaks data
  useEffect(() => {
    if (!enabled) {
      peaksRef.current = null;
      return;
    }

    peaksRef.current = null;
    waveformBufferRef.current = { regionLeft: 0, regionRight: 0 };
    const canvas = waveformCanvasRef.current;
    if (canvas) {
      const ctx = canvas.getContext('2d');
      if (ctx) ctx.clearRect(0, 0, canvas.width, canvas.height);
    }

    let cancelled = false;
    fetch(peaksUrl)
      .then((response) => response.json())
      .then((peaksData) => {
        if (cancelled) return;
        const data: number[] = Array.isArray(peaksData?.data) ? peaksData.data : [];
        peaksRef.current = data;
        let maxAbs = 0;
        for (let i = 0; i < data.length; i++) {
          const v = data[i] < 0 ? -data[i] : data[i];
          if (v > maxAbs) maxAbs = v;
        }
        peaksMaxRef.current = maxAbs > 0 ? maxAbs : 128;
        const canvas = waveformCanvasRef.current;
        if (canvas) {
          requestAnimationFrame(renderWaveformBuffer);
        }
      })
      .catch((error: Error) => {
        console.error('Error loading audio peaks:', error);
      });

    return () => {
      cancelled = true;
      peaksRef.current = null;
    };
  }, [enabled, renderWaveformBuffer, peaksUrl]);

  // Re-render waveform buffer when zoom changes
  useEffect(() => {
    if (!peaksRef.current?.length || pixelsPerSecond <= 0) return;
    waveformBufferRef.current = { regionLeft: 0, regionRight: 0 };
    const id = requestAnimationFrame(renderWaveformBuffer);
    return () => cancelAnimationFrame(id);
  }, [pixelsPerSecond, duration, renderWaveformBuffer]);

  // On scroll: check if buffer needs re-rendering (most scrolls are free)
  useEffect(() => {
    const scroller = scrollContainerRef.current;
    if (!scroller || !enabled) return;
    let rafId = 0;
    const onScroll = () => {
      cancelAnimationFrame(rafId);
      rafId = requestAnimationFrame(updateWaveformScroll);
    };
    scroller.addEventListener('scroll', onScroll, { passive: true });
    return () => {
      scroller.removeEventListener('scroll', onScroll);
      cancelAnimationFrame(rafId);
    };
  }, [enabled, updateWaveformScroll, scrollContainerRef]);

  return waveformCanvasRef;
}
