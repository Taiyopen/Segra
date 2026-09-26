import { useCallback, useEffect, useState } from 'react';
import { Segment } from '../Models/types';

const MAX_SEGMENT_UNDO = 40;

function cloneSegmentsForUndo(segs: Segment[]): Segment[] {
  return segs.map((s) => ({
    ...s,
    mutedAudioTracks: s.mutedAudioTracks ? [...s.mutedAudioTracks] : undefined,
    audioTrackVolumes: s.audioTrackVolumes ? { ...s.audioTrackVolumes } : undefined,
  }));
}

/**
 * Undo/redo history for the editor's timeline segments. The history is cleared whenever
 * `resetKey` changes (the editor switched to another video).
 */
export function useSegmentUndo(
  segments: Segment[],
  updateSegmentsArray: (seg: Segment[]) => void,
  removeSegment: (id: number) => void,
  clearAllSegments: () => void,
  resetKey: string,
) {
  const [, setSegmentUndoPast] = useState<Segment[][]>([]);
  const [, setSegmentUndoFuture] = useState<Segment[][]>([]);

  useEffect(() => {
    setSegmentUndoPast([]);
    setSegmentUndoFuture([]);
  }, [resetKey]);

  const pushSegmentUndoSnapshot = useCallback(() => {
    setSegmentUndoFuture([]);
    setSegmentUndoPast((past) => {
      const next = [...past, cloneSegmentsForUndo(segments)];
      while (next.length > MAX_SEGMENT_UNDO) next.shift();
      return next;
    });
  }, [segments]);

  const undoSegmentHistory = useCallback(() => {
    setSegmentUndoPast((past) => {
      if (past.length === 0) return past;
      const restored = past[past.length - 1];
      setSegmentUndoFuture((future) => [...future, cloneSegmentsForUndo(segments)]);
      updateSegmentsArray(restored);
      return past.slice(0, -1);
    });
  }, [updateSegmentsArray, segments]);

  const redoSegmentHistory = useCallback(() => {
    setSegmentUndoFuture((future) => {
      if (future.length === 0) return future;
      const restored = future[future.length - 1];
      setSegmentUndoPast((past) => [...past, cloneSegmentsForUndo(segments)]);
      updateSegmentsArray(restored);
      return future.slice(0, -1);
    });
  }, [updateSegmentsArray, segments]);

  const removeSegmentWithUndo = useCallback(
    (id: number) => {
      pushSegmentUndoSnapshot();
      removeSegment(id);
    },
    [pushSegmentUndoSnapshot, removeSegment],
  );

  const clearAllSegmentsWithUndo = useCallback(() => {
    pushSegmentUndoSnapshot();
    clearAllSegments();
  }, [pushSegmentUndoSnapshot, clearAllSegments]);

  return {
    pushSegmentUndoSnapshot,
    undoSegmentHistory,
    redoSegmentHistory,
    removeSegmentWithUndo,
    clearAllSegmentsWithUndo,
  };
}
