import React, { useEffect, useRef, useState } from 'react';

const clampVideoScale = (s: number) => Math.min(Math.max(s, 1), 4);

/**
 * Zooming into the video element itself (1x to 4x, Ctrl/Meta + wheel or buttons) and panning it by
 * dragging while zoomed. The pan is clamped so the video stays inside the player.
 */
export function useVideoZoomPan(
  videoRef: React.RefObject<HTMLVideoElement | null>,
  playerContainerRef: React.RefObject<HTMLDivElement | null>,
) {
  // Scale and pan state for zooming into the video element itself
  const [videoScale, setVideoScale] = useState(1);
  const [videoTranslate, setVideoTranslate] = useState({ x: 0, y: 0 });
  const [isPanning, setIsPanning] = useState(false);
  const videoPanStartRef = useRef<{ x: number; y: number } | null>(null);
  const videoLastPointerRef = useRef<number | null>(null);
  const panMovedRef = useRef(false);
  const videoScaleRef = useRef<number>(videoScale);

  useEffect(() => {
    videoScaleRef.current = videoScale;
  }, [videoScale]);

  // Clamp translation so the video remains at least partially visible
  const clampTranslate = (t: { x: number; y: number }) => {
    const el = playerContainerRef.current;
    const vid = videoRef.current;
    if (!el || !vid) return t;
    const vw = el.clientWidth;
    const vh = el.clientHeight;
    const sw = vid.clientWidth * videoScaleRef.current;
    const sh = vid.clientHeight * videoScaleRef.current;

    // Horizontal clamp: if video wider than container, allow panning between left and right edges.
    // Otherwise center horizontally.
    let minX: number;
    let maxX: number;
    if (sw > vw) {
      minX = vw - sw; // video right edge aligns with container right
      maxX = 0; // video left edge aligns with container left
    } else {
      // center
      minX = maxX = (vw - sw) / 2;
    }

    // Vertical clamp: enforce the requested rules.
    // - when panning down, if top of video moves below top of parent, clip to top (y <= 0)
    // - when panning up, if bottom of video moves above bottom of parent, clip to bottom (y >= vh - sh)
    let minY: number;
    let maxY: number;
    if (sh > vh) {
      minY = vh - sh; // bottom of video aligned with bottom of parent
      maxY = 0; // top of video aligned with top of parent
    } else {
      // center vertically
      minY = maxY = (vh - sh) / 2;
    }

    return {
      x: Math.max(minX, Math.min(maxX, t.x)),
      y: Math.max(minY, Math.min(maxY, t.y)),
    };
  };

  // Change video scale, optionally focusing on a specific point, otherwise center
  const applyVideoScale = (desiredScale: number, focusPoint?: { x: number; y: number }) => {
    const videoEl = videoRef.current;
    if (!videoEl) return;

    const previousScale = videoScaleRef.current || 1;
    const nextScale = clampVideoScale(desiredScale);
    if (previousScale === nextScale) return;

    const cx = focusPoint?.x ?? videoEl.clientWidth / 2;
    const cy = focusPoint?.y ?? videoEl.clientHeight / 2;
    const ratio = nextScale / previousScale;

    videoScaleRef.current = nextScale;
    setVideoScale(nextScale);
    setVideoTranslate((prev) => {
      if (nextScale === 1) {
        return { x: 0, y: 0 };
      }

      const x = prev.x - (cx - prev.x) * (ratio - 1);
      const y = prev.y - (cy - prev.y) * (ratio - 1);
      return clampTranslate({ x, y });
    });
  };

  const resetVideoZoom = () => {
    setVideoTranslate({ x: 0, y: 0 });
    applyVideoScale(1);
  };

  // Wheel zoom handler for the video element (use Ctrl/Meta to activate)
  const onVideoWheel = (e: React.WheelEvent) => {
    if (!(e.ctrlKey || e.metaKey)) return;
    e.preventDefault();
    const videoEl = e.currentTarget;
    const rect = videoEl.getBoundingClientRect();
    const currentScale = videoScaleRef.current || 1;
    const localX = (e.clientX - rect.left) / currentScale;
    const localY = (e.clientY - rect.top) / currentScale;
    const factor = e.deltaY < 0 ? 1.12 : 0.9;

    applyVideoScale(currentScale * factor, { x: localX, y: localY });
  };

  // Pointer handlers for panning the video when zoomed
  const onVideoPointerDown = (e: React.PointerEvent) => {
    if (videoScaleRef.current <= 1) return;
    (e.target as Element).setPointerCapture(e.pointerId);
    setIsPanning(true);
    // Reset pan-moved flag for this gesture
    panMovedRef.current = false;
    videoPanStartRef.current = { x: e.clientX - videoTranslate.x, y: e.clientY - videoTranslate.y };
    videoLastPointerRef.current = e.pointerId;
  };

  const onVideoPointerMove = (e: React.PointerEvent) => {
    if (!isPanning || !videoPanStartRef.current) return;
    const start = videoPanStartRef.current;
    const dx = e.clientX - start.x;
    const dy = e.clientY - start.y;
    // If movement exceeds a small threshold, mark this gesture as a pan so we can suppress click
    if (Math.hypot(dx, dy) > 4) panMovedRef.current = true;
    setVideoTranslate((_prev) => clampTranslate({ x: dx, y: dy }));
  };

  const onVideoPointerUp = (e: React.PointerEvent) => {
    try {
      (e.target as Element).releasePointerCapture?.(e.pointerId);
    } catch (err) {
      // ignore pointer release errors
      // console.debug('pointer release error', err);
    }
    setIsPanning(false);
    videoPanStartRef.current = null;
    videoLastPointerRef.current = null;
  };

  /** True when the click that just happened ended a pan gesture (and clears the flag). */
  const consumePanClick = () => {
    if (!panMovedRef.current) return false;
    panMovedRef.current = false;
    return true;
  };

  return {
    videoScale,
    videoTranslate,
    isPanning,
    videoScaleRef,
    applyVideoScale,
    resetVideoZoom,
    consumePanClick,
    onVideoWheel,
    onVideoPointerDown,
    onVideoPointerMove,
    onVideoPointerUp,
  };
}
