import React, { useRef, useState, useEffect, useLayoutEffect, useMemo, useCallback } from 'react';
import {
  Content,
  BookmarkType,
  BOOKMARK_TYPE_LABELS,
  Segment,
  Bookmark,
  displayTrackName,
} from '../Models/types';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import {
  isPendingEditSourceType,
  resolvePendingEditSourceType,
  sendMoveToPendingEdit,
  sendMoveOutOfPendingEdit,
} from '../Utils/PendingEditUtils';
import {
  isRecordingSourceType,
  resolveRecordingSourceType,
  sendMoveOutOfReadyToDelete,
} from '../Utils/ReadyToDeleteUtils';
import { useSettings, useSettingsUpdater } from '../Context/SettingsContext';
import { useAppState } from '../Context/AppStateContext';
import { useSelectedVideo } from '../Context/SelectedVideoContext';
import { DndProvider } from 'react-dnd';
import { HTML5Backend } from 'react-dnd-html5-backend';
import { useAuth } from '../Hooks/useAuth.tsx';
import { useSegments } from '../Context/SegmentsContext';
import { useUploads } from '../Context/UploadContext';
import { useModal } from '../Context/ModalContext';
import UploadModal from '../Components/UploadModal';
import {
  Trash2,
  SquarePlus,
  BookmarkPlus,
  Clapperboard,
  Pause,
  Play,
  RotateCcw,
  RotateCw,
  Upload,
  Volume2,
  VolumeX,
  Volume1,
  Maximize,
  Minimize,
  Skull,
  Plus,
  Minus,
  ZoomIn,
  ZoomOut,
  Headphones,
  Copy,
  Check,
  Repeat,
  ArrowLeftToLine,
  ArrowRightToLine,
} from 'lucide-react';
import { useContentPlaylist } from '../Hooks/useContentPlaylist';
import VideoPlaylistPanel from '../Components/VideoPlaylistPanel';
import SegmentCard from '../Components/SegmentCard';
import { useAudioTracks } from '../Hooks/useAudioTracks';
import { useNativeElementAudio } from '../Hooks/useNativeElementAudio';
import { AnimatePresence, motion } from 'framer-motion';
import Button from '../Components/Button';
import VideoTopInfoBar from '../Components/VideoTopInfoBar';
import { useSegmentUndo } from '../Hooks/useSegmentUndo';
import { useVideoZoomPan } from '../Hooks/useVideoZoomPan';
import { useTimelineWaveform } from '../Hooks/useTimelineWaveform';
import { useBookmarkFilter } from '../Hooks/useBookmarkFilter';
import { getIconMapping } from '../Utils/BookmarkIcons';
import {
  timeStringToSeconds,
  PLAYBACK_RATE_MIN,
  PLAYBACK_RATE_MAX,
  PLAYBACK_RATE_STEP,
  clampPlaybackRate,
  formatPlaybackRateLabel,
  playbackRateSliderPercent,
  fetchThumbnailAtTime,
  getWaveformUrl,
} from '../Utils/VideoEditorUtils';

export default function VideoComponent({ video }: { video: Content }) {
  // Context hooks
  const settings = useSettings();
  const appState = useAppState();
  const updateSettings = useSettingsUpdater();
  const { setSelectedVideo, stickySourceCategory, pinStickySourceCategory } = useSelectedVideo();
  const { session } = useAuth();
  const { uploads } = useUploads();
  const { openModal, closeModal } = useModal();
  const {
    segments,
    addSegment,
    updateSegment,
    removeSegment,
    updateSegmentsArray,
    clearAllSegments,
    renameSegmentsForVideo,
  } = useSegments();

  const { playlist, currentIndex, prevVideo, nextVideo, viewType } = useContentPlaylist(video);
  const isStickyInSourceCategory =
    stickySourceCategory?.fileName === video.fileName &&
    (stickySourceCategory.sourceType === 'Session' || stickySourceCategory.sourceType === 'Buffer');
  const showMoveToPendingEdit =
    (video.type === 'Session' || video.type === 'Buffer') && !isStickyInSourceCategory;
  const showMoveOutOfPendingEdit = video.type === 'PendingEdit' && !isStickyInSourceCategory;
  const showMoveOutOfReadyToDelete = video.type === 'ReadyToDelete';

  // Refs (declared early — sync effect may capture playback across path changes)
  const videoRef = useRef<HTMLVideoElement>(null);
  const userWantsPlayingRef = useRef(true);
  const resumeAfterPathChangeRef = useRef<{
    fileName: string;
    time: number;
    wasPlaying: boolean;
  } | null>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const playerContainerRef = useRef<HTMLDivElement>(null);
  const latestDraggedSegmentRef = useRef<Segment | null>(null);
  const pendingScrollRef = useRef<number | null>(null);
  const zoomAnimationRef = useRef<number>(0);

  const navigateAfterRemoval = useCallback(() => {
    if (nextVideo) {
      setSelectedVideo(nextVideo);
    } else if (prevVideo) {
      setSelectedVideo(prevVideo);
    } else {
      setSelectedVideo(null);
    }
  }, [nextVideo, prevVideo, setSelectedVideo]);

  const handleSelectPlaylistVideo = useCallback(
    (item: Content) => {
      setSelectedVideo(item);
    },
    [setSelectedVideo],
  );

  const handleDeleteVideo = useCallback(() => {
    sendMessageToBackend('DeleteContent', {
      FileName: video.fileName,
      ContentType: video.type,
    });
    navigateAfterRemoval();
  }, [video.fileName, video.type, navigateAfterRemoval]);

  const handleMoveToPendingEdit = useCallback(() => {
    if (video.type === 'Session' || video.type === 'Buffer') {
      pinStickySourceCategory(video.fileName, video.type);
    }
    sendMoveToPendingEdit([{ fileName: video.fileName, contentType: video.type }]);
  }, [video.fileName, video.type, pinStickySourceCategory]);

  const handleMoveOutOfPendingEdit = useCallback(
    (targetType?: 'Session' | 'Buffer') => {
      sendMoveOutOfPendingEdit([
        {
          fileName: video.fileName,
          targetType: targetType ?? resolvePendingEditSourceType(video.pendingEditSourceType),
        },
      ]);
    },
    [video.fileName, video.pendingEditSourceType],
  );

  const handleMoveOutOfReadyToDelete = useCallback(
    (targetType?: 'Session' | 'Buffer') => {
      sendMoveOutOfReadyToDelete([
        {
          fileName: video.fileName,
          targetType: targetType ?? resolveRecordingSourceType(video.readyToDeleteSourceType),
        },
      ]);
    },
    [video.fileName, video.readyToDeleteSourceType],
  );

  useEffect(() => {
    // Browse videos are transient and never live in AppState.content.
    // Without this guard the effect treats them as "removed" and clears selection.
    if (video.type === 'External') return;

    const updated = appState.content.find((item) => item.fileName === video.fileName);
    if (updated) {
      if (updated.type !== video.type || updated.filePath !== video.filePath) {
        if (updated.filePath !== video.filePath) {
          const el = videoRef.current;
          resumeAfterPathChangeRef.current = {
            fileName: video.fileName,
            time: el?.currentTime ?? 0,
            wasPlaying: el ? !el.paused : false,
          };
          renameSegmentsForVideo(video.fileName, updated.fileName, updated.filePath);
        }
        setSelectedVideo(updated);
      }
      return;
    }

    // Renamed on disk: fileName changes but metadata identity stays the same
    const renamed = appState.content.find(
      (item) =>
        item.type === video.type &&
        item.createdAt === video.createdAt &&
        item.game === video.game &&
        item.duration === video.duration &&
        item.fileSizeKb === video.fileSizeKb,
    );
    if (renamed) {
      setSelectedVideo(renamed);
      return;
    }

    if (nextVideo) setSelectedVideo(nextVideo);
    else if (prevVideo) setSelectedVideo(prevVideo);
    else setSelectedVideo(null);
  }, [
    appState.content,
    video.fileName,
    video.type,
    video.filePath,
    video.createdAt,
    video.game,
    video.duration,
    video.fileSizeKb,
    setSelectedVideo,
    prevVideo,
    nextVideo,
    renameSegmentsForVideo,
  ]);

  const supportsClipWorkflow =
    video.type === 'Session' ||
    video.type === 'Buffer' ||
    video.type === 'PendingEdit' ||
    video.type === 'ReadyToDelete' ||
    video.type === 'External' ||
    isStickyInSourceCategory;
  const showBufferStyleCopy =
    viewType === 'Buffer' ||
    video.type === 'External' ||
    video.type === 'ReadyToDelete' ||
    (video.type === 'PendingEdit' && !isStickyInSourceCategory);

  // Resume playback after a same-video path change (e.g. move to 待剪輯)
  useEffect(() => {
    const pending = resumeAfterPathChangeRef.current;
    if (!pending || pending.fileName !== video.fileName) return;

    const el = videoRef.current;
    if (!el) return;

    const resume = () => {
      const stillPending = resumeAfterPathChangeRef.current;
      if (!stillPending || stillPending.fileName !== video.fileName) return;
      el.currentTime = stillPending.time;
      setCurrentTime(stillPending.time);
      if (stillPending.wasPlaying) {
        void el.play().catch(() => {
          /* autoplay may be blocked; ignore */
        });
      }
      resumeAfterPathChangeRef.current = null;
    };

    if (el.readyState >= 1) {
      resume();
      return;
    }

    el.addEventListener('loadedmetadata', resume, { once: true });
    return () => el.removeEventListener('loadedmetadata', resume);
  }, [video.filePath, video.fileName]);

  // Audio tracks
  const audioTracks = useAudioTracks(videoRef, video);
  // Single-track content (clips, default recordings): route the video element's own audio
  // through the native sink so Discord/OBS app-audio capture includes it.
  useNativeElementAudio(videoRef, video);
  const [showAudioTracks, setShowAudioTracks] = useState(false);
  const [timelineAudioMenu, setTimelineAudioMenu] = useState<{
    segId: number;
    x: number;
    y: number;
    flipUp: boolean;
    visible: boolean;
  } | null>(null);

  // Video state
  const [currentTime, setCurrentTime] = useState(0);
  // Seed duration from content metadata so the timeline and waveform render
  // immediately; the video element refines it on loadedmetadata.
  const metadataDuration = useMemo(() => {
    const seconds = timeStringToSeconds(video.duration);
    return Number.isFinite(seconds) && seconds > 0 ? seconds : 0;
  }, [video.duration]);
  const [duration, setDuration] = useState(metadataDuration);
  useEffect(() => {
    setDuration(metadataDuration);
  }, [video.filePath, metadataDuration]);
  const [zoom, setZoom] = useState(1);

  const {
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
  } = useVideoZoomPan(videoRef, playerContainerRef);

  // Close timeline audio menu when clicking outside
  useEffect(() => {
    if (!timelineAudioMenu?.visible) return;
    const handleClickOutside = () =>
      setTimelineAudioMenu((prev) => (prev ? { ...prev, visible: false } : null));
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [timelineAudioMenu?.visible]);

  // Container state
  const [containerWidth, setContainerWidth] = useState(0);
  const [isPlaying, setIsPlaying] = useState(true);
  const [showNoSegmentsIndicator, setShowNoSegmentsIndicator] = useState(false);
  const [volume, setVolume] = useState(() => {
    // Initialize volume from localStorage or default to 1
    const savedVolume = localStorage.getItem('segra-volume');
    return savedVolume ? parseFloat(savedVolume) : 1;
  });
  const [isMuted, setIsMuted] = useState(() => {
    // Initialize muted state from localStorage or default to false
    return localStorage.getItem('segra-muted') === 'true';
  });
  const [playbackRate, setPlaybackRate] = useState(() => {
    const saved = localStorage.getItem('segra-playbackRate');
    return saved ? clampPlaybackRate(parseFloat(saved)) : 1;
  });
  const [controlsVisible, setControlsVisible] = useState(true);
  const controlsHideTimeoutRef = useRef<number | null>(null);
  const isPointerOverControlsRef = useRef(false);
  const [isPointerInPlayer, setIsPointerInPlayer] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);

  useEffect(() => {
    if (!controlsVisible) {
      setShowAudioTracks(false);
    }
  }, [controlsVisible]);

  // Interaction state
  const [isDragging, setIsDragging] = useState(false);
  const [isInteracting, setIsInteracting] = useState(false);
  const [hoveredSegmentId, setHoveredSegmentId] = useState<number | null>(null);
  /** 剪輯「起點／終點」按鈕作用的片段：上次在時間軸或側欄選取、或曾修改過的片段 */
  const [clipEditTargetSegmentId, setClipEditTargetSegmentId] = useState<number | null>(null);
  const [dragState, setDragState] = useState<{ id: number | null; offset: number }>({
    id: null,
    offset: 0,
  });
  const dragCandidateRef = useRef<{ id: number; startClientX: number; offset: number } | null>(
    null,
  );
  const [resizingSegmentId, setResizingSegmentId] = useState<number | null>(null);
  const [resizeDirection, setResizeDirection] = useState<'start' | 'end' | null>(null);
  // Read at resize-end (the mouseup handler's effect doesn't depend on the state).
  const resizeDirectionRef = useRef<'start' | 'end' | null>(null);
  const resizeCandidateRef = useRef<{
    id: number;
    direction: 'start' | 'end';
    startClientX: number;
  } | null>(null);
  const resizePlaybackRef = useRef<{ wasPlaying: boolean; cursorTime: number } | null>(null);

  const videoWrapperClassName = [
    'block relative w-full',
    isFullscreen ? 'h-full' : 'rounded-lg overflow-hidden h-full',
  ]
    .filter(Boolean)
    .join(' ');

  // Computed values
  const basePixelsPerSecond = duration > 0 ? containerWidth / duration : 0;
  const pixelsPerSecond = basePixelsPerSecond * zoom;
  const waveformCanvasRef = useTimelineWaveform({
    enabled: settings.showAudioWaveformInTimeline,
    peaksUrl: getWaveformUrl(appState.cacheFolder, video.type, video.fileName),
    scrollContainerRef,
    pixelsPerSecond,
    duration,
  });

  // Make sure bookmarks are only shown when we have valid duration and zoom
  // Prevents weird positioning on initial load
  const bookmarksReady = duration > 0 && pixelsPerSecond > 0;
  const sortedSegments = useMemo(
    () => [...segments].sort((a, b) => a.startTime - b.startTime),
    [segments],
  );
  /** Segments for this video only, sorted by start time — used for clip preview / playhead snapping */
  const sortedClipSegments = useMemo(
    () =>
      [...segments]
        .filter((s) => s.filePath === video.filePath)
        .sort((a, b) => a.startTime - b.startTime),
    [segments, video.filePath],
  );
  const sortedClipSegmentsRef = useRef(sortedClipSegments);
  useEffect(() => {
    sortedClipSegmentsRef.current = sortedClipSegments;
  }, [sortedClipSegments]);

  const [clipPreviewLoop, setClipPreviewLoop] = useState(false);
  const clipPreviewLoopRef = useRef(false);
  useEffect(() => {
    clipPreviewLoopRef.current = clipPreviewLoop;
  }, [clipPreviewLoop]);

  useEffect(() => {
    if (sortedClipSegments.length === 0 && clipPreviewLoop) {
      setClipPreviewLoop(false);
    }
  }, [sortedClipSegments.length, clipPreviewLoop]);

  useEffect(() => {
    setClipPreviewLoop(false);
    setClipEditTargetSegmentId(null);
  }, [video.filePath]);

  useEffect(() => {
    if (clipEditTargetSegmentId == null) return;
    const exists = segments.some(
      (s) => s.id === clipEditTargetSegmentId && s.filePath === video.filePath,
    );
    if (!exists) setClipEditTargetSegmentId(null);
  }, [segments, video.filePath, clipEditTargetSegmentId]);

  const segmentsRef = useRef(segments);
  useEffect(() => {
    segmentsRef.current = segments;
  }, [segments]);

  const {
    pushSegmentUndoSnapshot,
    undoSegmentHistory,
    redoSegmentHistory,
    removeSegmentWithUndo,
    clearAllSegmentsWithUndo,
  } = useSegmentUndo(
    segments,
    updateSegmentsArray,
    removeSegment,
    clearAllSegments,
    video.filePath,
  );

  const handleSidebarSegmentSeek = useCallback(
    (segment: Segment) => {
      if (segment.filePath !== video.filePath) return;
      setClipEditTargetSegmentId(segment.id);
      const el = videoRef.current;
      const dur =
        duration > 0 && Number.isFinite(duration)
          ? duration
          : el && Number.isFinite(el.duration) && el.duration > 0
            ? el.duration
            : segment.startTime + 1;
      const t = Math.max(0, Math.min(segment.startTime, dur));
      if (el) {
        el.currentTime = t;
      }
      setCurrentTime(t);
    },
    [video.filePath, duration],
  );

  // Track in-flight thumbnail requests to avoid stale overwrites
  const thumbnailReqTokenRef = useRef<Map<number, number>>(new Map());

  // Refreshes the thumbnail for a segment without overwriting live fields
  const refreshSegmentThumbnail = async (segment: Segment): Promise<void> => {
    const id = segment.id;
    // Ref updates after render; right after updateSegment(...), ref can still hold stale row.
    // Merge caller `segment` over ref so start/end changes from the caller are never clobbered.
    const current = segmentsRef.current.find((s) => s.id === id);
    const merged = current ? { ...current, ...segment } : segment;

    updateSegment({ ...merged, isLoading: true });

    // Bump request token for this id
    const nextToken = (thumbnailReqTokenRef.current.get(id) ?? 0) + 1;
    thumbnailReqTokenRef.current.set(id, nextToken);

    try {
      const latest = segmentsRef.current.find((s) => s.id === id) ?? merged;
      // Use the video's filePath from metadata instead of constructing it
      const thumbnailUrl = await fetchThumbnailAtTime(video.filePath, latest.startTime);

      // Only apply if this is the latest request for this segment
      if (thumbnailReqTokenRef.current.get(id) === nextToken) {
        const newest = segmentsRef.current.find((s) => s.id === id) ?? latest;
        updateSegment({ ...newest, thumbnailDataUrl: thumbnailUrl, isLoading: false });
      }
    } catch {
      if (thumbnailReqTokenRef.current.get(id) === nextToken) {
        const newest = segmentsRef.current.find((s) => s.id === id) ?? merged;
        updateSegment({ ...newest, isLoading: false });
      }
    }
  };

  // Initialize video metadata and setup keyboard controls
  useEffect(() => {
    const vid = videoRef.current;
    if (!vid) return;

    // Apply saved volume and muted state on load
    // When multi-track is active, the hook controls muting
    if (!audioTracks.isMultiTrack) {
      vid.volume = volume;
      vid.muted = isMuted;
    }
    // Apply saved playback rate
    vid.playbackRate = playbackRate;

    const onLoadedMetadata = () => {
      setDuration(vid.duration);
      setZoom(1);
    };

    let resumingFromHide = false;
    const onPlay = () => {
      userWantsPlayingRef.current = true;
      setIsPlaying(true);
    };
    const onPause = () => {
      if (resumingFromHide) return;
      if (document.visibilityState === 'hidden' && userWantsPlayingRef.current) {
        resumingFromHide = true;
        void vid.play().finally(() => {
          resumingFromHide = false;
        });
        return;
      }
      userWantsPlayingRef.current = false;
      setIsPlaying(false);
    };
    const onVisibilityChange = () => {
      if (document.visibilityState !== 'visible') return;
      if (userWantsPlayingRef.current && vid.paused) {
        void vid.play().catch(() => {});
      }
    };
    const onVolumeChange = () => {
      if (vid) {
        // When multi-track audio is active, the video is muted by the hook
        if (audioTracks.isMultiTrack) return;

        setVolume(vid.volume);
        setIsMuted(vid.muted);

        // Save to localStorage when volume changes
        localStorage.setItem('segra-volume', vid.volume.toString());
        localStorage.setItem('segra-muted', vid.muted.toString());
      }
    };

    const onRateChange = () => {
      if (vid) {
        const r = clampPlaybackRate(vid.playbackRate || 1);
        setPlaybackRate(r);
        localStorage.setItem('segra-playbackRate', r.toString());
      }
    };

    vid.addEventListener('loadedmetadata', onLoadedMetadata);
    vid.addEventListener('play', onPlay);
    vid.addEventListener('pause', onPause);
    vid.addEventListener('volumechange', onVolumeChange);
    vid.addEventListener('ratechange', onRateChange);
    document.addEventListener('visibilitychange', onVisibilityChange);

    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      const isTyping =
        target.tagName === 'INPUT' ||
        target.tagName === 'TEXTAREA' ||
        (target as any).isContentEditable;

      // Space to toggle play/pause globally (unless typing)
      if ((e.code === 'Space' || e.key === ' ' || e.key === 'Spacebar') && !isTyping) {
        if (e.repeat) return; // avoid rapid toggle on key repeat
        e.preventDefault();
        handlePlayPause();
        return;
      }

      // F to toggle fullscreen overlay (unless typing)
      if ((e.key === 'f' || e.key === 'F') && !isTyping) {
        if (e.repeat) return;
        e.preventDefault();
        toggleFullscreen();
        return;
      }

      // Arrow keys: seek 5s back/forward (allow holding)
      if ((e.key === 'ArrowLeft' || e.code === 'ArrowLeft') && !isTyping) {
        e.preventDefault();
        showControlsTemporarily();
        skipTime(-5);
        return;
      }
      if ((e.key === 'ArrowRight' || e.code === 'ArrowRight') && !isTyping) {
        e.preventDefault();
        showControlsTemporarily();
        skipTime(5);
        return;
      }

      // , / . : approximate previous / next frame (allow holding)
      if ((e.key === ',' || e.code === 'Comma') && !isTyping) {
        e.preventDefault();
        showControlsTemporarily();
        const el = videoRef.current;
        if (el) {
          const fps = Math.max(1, Math.min(240, settings.frameRate || 60));
          if (!el.paused) el.pause();
          skipTime(-1 / fps);
        }
        return;
      }
      if ((e.key === '.' || e.code === 'Period') && !isTyping) {
        e.preventDefault();
        showControlsTemporarily();
        const el = videoRef.current;
        if (el) {
          const fps = Math.max(1, Math.min(240, settings.frameRate || 60));
          if (!el.paused) el.pause();
          skipTime(1 / fps);
        }
        return;
      }

      // Volume up/down (5% steps, allow holding)
      if ((e.key === 'ArrowUp' || e.code === 'ArrowUp') && !isTyping) {
        e.preventDefault();
        setPlayerVolume((videoRef.current?.volume ?? volume) + 0.05);
        showControlsTemporarily();
        return;
      }
      if ((e.key === 'ArrowDown' || e.code === 'ArrowDown') && !isTyping) {
        e.preventDefault();
        setPlayerVolume((videoRef.current?.volume ?? volume) - 0.05);
        showControlsTemporarily();
        return;
      }

      // Mute/unmute
      if ((e.key === 'm' || e.key === 'M') && !isTyping) {
        e.preventDefault();
        toggleMute();
        showControlsTemporarily();
        return;
      }

      // 片段復原／重做（Ctrl+Z、Ctrl+Y；Mac：⌘+Z、⌘+⇧+Z 或 Ctrl+Y）
      if (supportsClipWorkflow && (e.ctrlKey || e.metaKey) && !isTyping) {
        const yz = e.key === 'z' || e.key === 'Z';
        const yy = e.key === 'y' || e.key === 'Y';
        if (yy || (yz && e.shiftKey)) {
          if (e.repeat) return;
          e.preventDefault();
          redoSegmentHistory();
          showControlsTemporarily();
          return;
        }
        if (yz && !e.shiftKey) {
          if (e.repeat) return;
          e.preventDefault();
          undoSegmentHistory();
          showControlsTemporarily();
          return;
        }
      }

      if (e.key === 'Escape' && isFullscreen) {
        e.preventDefault();
        exitFullscreen();
      }
    };

    const keyOptions: AddEventListenerOptions & EventListenerOptions = { capture: true };
    window.addEventListener('keydown', handleKeyDown, keyOptions);

    // No DOM fullscreen; overlay UI + backend Photino OS fullscreen (not work-area maximize)

    return () => {
      vid.removeEventListener('loadedmetadata', onLoadedMetadata);
      vid.removeEventListener('play', onPlay);
      vid.removeEventListener('pause', onPause);
      vid.removeEventListener('volumechange', onVolumeChange);
      vid.removeEventListener('ratechange', onRateChange);
      document.removeEventListener('visibilitychange', onVisibilityChange);
      window.removeEventListener('keydown', handleKeyDown, keyOptions as any);
    };
  }, [
    volume,
    isMuted,
    isFullscreen,
    audioTracks.isMultiTrack,
    supportsClipWorkflow,
    undoSegmentHistory,
    redoSegmentHistory,
    settings.frameRate,
  ]);

  // Per-segment audio override state, kept in refs for the rAF loop below.
  // `segmentsDirtyRef` is separate from the id ref because `null` is already
  // the valid "no active segment" id, so id comparison alone can't detect a
  // segment deletion when the deleted one was active.
  const activeSegmentIdRef = useRef<number | null>(null);
  const segmentsDirtyRef = useRef<boolean>(true);
  const audioTracksRef = useRef(audioTracks);
  useLayoutEffect(() => {
    audioTracksRef.current = audioTracks;
  });

  useEffect(() => {
    segmentsDirtyRef.current = true;
  }, [segments]);

  // Clean up overrides when multi-track is deactivated
  useEffect(() => {
    if (audioTracks.isMultiTrack) {
      // Sync master mute/volume from saved state when entering multi-track
      audioTracks.setMasterMuted(localStorage.getItem('segra-muted') === 'true');
      const savedVol = localStorage.getItem('segra-volume');
      audioTracks.setMasterVolume(savedVol ? parseFloat(savedVol) : 1);
    } else {
      audioTracks.setMuteOverride(null);
      audioTracks.setVolumeOverride(null);
    }
  }, [
    audioTracks.isMultiTrack,
    audioTracks.setMasterMuted,
    audioTracks.setMuteOverride,
    audioTracks.setVolumeOverride,
  ]);

  // Handle video playback time updates using requestAnimationFrame for smooth updates.
  // Also checks per-segment audio overrides each frame (cheap: one find + early return).
  useEffect(() => {
    const vid = videoRef.current;
    if (!vid) return;
    let rafId = 0;
    const tick = () => {
      let t = vid.currentTime;

      if (clipPreviewLoopRef.current) {
        const sorted = sortedClipSegmentsRef.current;
        if (sorted.length > 0) {
          const eps = 1e-3;
          const inside = sorted.some((s) => t >= s.startTime - eps && t <= s.endTime + eps);
          if (!inside) {
            const nextStart = sorted.find((s) => s.startTime > t)?.startTime;
            const seekTo = nextStart ?? sorted[0].startTime;
            if (Math.abs(vid.currentTime - seekTo) > 1e-4) {
              vid.currentTime = seekTo;
              t = seekTo;
            }
          }
        }
      }

      // While resizing a segment the video previews the dragged edge; the
      // playhead must not follow it.
      if (resizeDirectionRef.current == null) {
        setCurrentTime(t);
      }

      // Per-segment audio mute/volume override
      const at = audioTracksRef.current;
      if (at.isMultiTrack) {
        const segs = segmentsRef.current;
        const activeSeg = segs.find((s) => t >= s.startTime && t <= s.endTime);
        const activeId = activeSeg?.id ?? null;

        if (activeId !== activeSegmentIdRef.current || segmentsDirtyRef.current) {
          activeSegmentIdRef.current = activeId;
          segmentsDirtyRef.current = false;
          if (activeSeg) {
            at.setMuteOverride(activeSeg.mutedAudioTracks ?? []);
            at.setVolumeOverride(activeSeg.audioTrackVolumes ?? null);
          } else {
            at.setMuteOverride(null);
            at.setVolumeOverride(null);
          }
        }
      }

      if (!vid.paused && !vid.ended) {
        rafId = requestAnimationFrame(tick);
      }
    };
    const onPlay = () => {
      rafId = requestAnimationFrame(tick);
    };
    const onPause = () => {
      cancelAnimationFrame(rafId);
    };
    vid.addEventListener('play', onPlay);
    vid.addEventListener('pause', onPause);
    if (!vid.paused) onPlay();
    return () => {
      vid.removeEventListener('play', onPlay);
      vid.removeEventListener('pause', onPause);
      cancelAnimationFrame(rafId);
    };
  }, []);

  // Update container width on window resize
  useEffect(() => {
    if (scrollContainerRef.current) {
      setContainerWidth(scrollContainerRef.current.clientWidth);
    }

    const handleResize = () => {
      if (scrollContainerRef.current) {
        setContainerWidth(scrollContainerRef.current.clientWidth);
      }
    };

    const preventPageZoom = (e: WheelEvent) => {
      if (e.ctrlKey || e.metaKey) {
        e.preventDefault();
      }
    };

    window.addEventListener('resize', handleResize);
    window.addEventListener('wheel', preventPageZoom, { passive: false });

    return () => {
      window.removeEventListener('resize', handleResize);
      window.removeEventListener('wheel', preventPageZoom);
    };
  }, []);

  // Create refs to track zoom state
  const wheelZoomRef = useRef(zoom);

  // Update the wheel zoom ref when zoom changes from other sources (buttons)
  useEffect(() => {
    wheelZoomRef.current = zoom;
  }, [zoom]);

  // TODO: refactor
  const showControlsTemporarily = () => {
    setControlsVisible(true);

    if (controlsHideTimeoutRef.current) {
      clearTimeout(controlsHideTimeoutRef.current);
      controlsHideTimeoutRef.current = null;
    }

    if (isPointerOverControlsRef.current) {
      return;
    }

    controlsHideTimeoutRef.current = window.setTimeout(() => {
      if (!isPointerOverControlsRef.current) {
        setControlsVisible(false);
      }
      controlsHideTimeoutRef.current = null;
    }, 2500);
  };

  const handleControlsMouseEnter = () => {
    isPointerOverControlsRef.current = true;

    if (controlsHideTimeoutRef.current) {
      clearTimeout(controlsHideTimeoutRef.current);
      controlsHideTimeoutRef.current = null;
    }

    setControlsVisible(true);
  };

  const handleControlsMouseLeave = () => {
    isPointerOverControlsRef.current = false;
    showControlsTemporarily();
  };

  // Handle timeline zooming with mouse wheel
  useEffect(() => {
    const container = scrollContainerRef.current;
    if (!container) return;

    const handleWheel = (e: WheelEvent) => {
      e.preventDefault();
      if (duration === 0) return;

      // Get container dimensions
      const rect = container.getBoundingClientRect();
      const cursorX = e.clientX - rect.left;
      const scrollLeft = container.scrollLeft;

      // Calculate base pixels per second for time conversion
      const basePixelsPerSecond = containerWidth / duration;
      const oldPixelsPerSecond = basePixelsPerSecond * wheelZoomRef.current;

      // Calculate time at cursor position
      const timeAtCursor = (cursorX + scrollLeft) / oldPixelsPerSecond;

      // Calculate new zoom level
      const zoomFactor = e.deltaY < 0 ? 1.2 : 0.8;
      const newZoom = Math.min(Math.max(wheelZoomRef.current * zoomFactor, 1), 1000);

      // Update zoom ref immediately
      wheelZoomRef.current = newZoom;

      // Calculate new scroll position based on cursor time point
      const newPixelsPerSecond = basePixelsPerSecond * newZoom;
      const newCursorPosition = timeAtCursor * newPixelsPerSecond;
      const newScrollLeft = newCursorPosition - cursorX;

      // Cancel any running button zoom animation
      cancelAnimationFrame(zoomAnimationRef.current);

      // Store scroll target — useLayoutEffect applies after React commits DOM
      pendingScrollRef.current = newScrollLeft;
      setZoom(newZoom);
    };

    const wheelEventOptions: AddEventListenerOptions = { passive: false };
    container.addEventListener('wheel', handleWheel, wheelEventOptions);

    return () => {
      container.removeEventListener('wheel', handleWheel, wheelEventOptions);
    };
  }, [duration, containerWidth]); // Remove zoom from dependencies to prevent recreation

  const handleZoomChange = (increment: boolean) => {
    if (!scrollContainerRef.current) return;

    const container = scrollContainerRef.current;
    const scrollLeft = container.scrollLeft;
    const bpps = containerWidth / duration;

    // Anchor point: keep current time marker at same viewport position
    const markerTime = currentTime;
    const markerViewportX = markerTime * bpps * zoom - scrollLeft;

    // Compute target zoom
    const newZoom = increment ? zoom * 1.5 : zoom * 0.5;
    const targetZoom = Math.min(Math.max(newZoom, 1), 1000);

    // Cancel any running animation
    cancelAnimationFrame(zoomAnimationRef.current);

    const startZoom = zoom;
    const animDuration = 200;
    const startTime = performance.now();

    const animate = (now: number) => {
      const elapsed = now - startTime;
      const t = Math.min(elapsed / animDuration, 1);
      const eased = 1 - Math.pow(1 - t, 3); // ease-out cubic

      const currentZoom = startZoom + (targetZoom - startZoom) * eased;

      // Compute scroll so marker stays at same viewport x
      pendingScrollRef.current = markerTime * bpps * currentZoom - markerViewportX;
      wheelZoomRef.current = currentZoom;
      setZoom(currentZoom);

      if (t < 1) {
        zoomAnimationRef.current = requestAnimationFrame(animate);
      }
    };

    zoomAnimationRef.current = requestAnimationFrame(animate);
  };

  // Apply pending scroll synchronously before browser paint
  useLayoutEffect(() => {
    if (pendingScrollRef.current !== null && scrollContainerRef.current) {
      scrollContainerRef.current.scrollLeft = pendingScrollRef.current;
      pendingScrollRef.current = null;
    }
  }, [zoom]);

  // Video control functions
  const handlePlayPause = () => {
    if (videoRef.current) {
      if (videoRef.current.paused) {
        videoRef.current.play();
      } else {
        videoRef.current.pause();
      }
    }
  };

  const skipTime = (seconds: number) => {
    if (videoRef.current) {
      const el = videoRef.current;
      const dur = Number.isFinite(el.duration) && el.duration > 0 ? el.duration : duration;
      const newTime = Math.max(0, Math.min(el.currentTime + seconds, dur));
      el.currentTime = newTime;
      setCurrentTime(newTime);
    }
  };

  const setPlayerVolume = (vol: number) => {
    const target = Math.max(0, Math.min(1, vol));
    const el = videoRef.current;
    if (!el) {
      setVolume(target);
      return;
    }
    if (audioTracks.isMultiTrack) {
      // Route to master volume -- purely a preview control
      audioTracks.setMasterVolume(target);
      if (target === 0) {
        audioTracks.setMasterMuted(true);
        setIsMuted(true);
      } else if (audioTracks.masterMuted) {
        audioTracks.setMasterMuted(false);
        setIsMuted(false);
      }
    } else {
      el.volume = target;
      if (target === 0) {
        el.muted = true;
        setIsMuted(true);
      } else if (el.muted) {
        el.muted = false;
        setIsMuted(false);
      }
    }
    setVolume(target);
    localStorage.setItem('segra-volume', target.toString());
    localStorage.setItem(
      'segra-muted',
      (audioTracks.isMultiTrack ? audioTracks.masterMuted : el.muted).toString(),
    );
  };

  // Click handler for the video element which suppresses clicks that are actually pans
  const onVideoClick = (e: React.MouseEvent) => {
    if (consumePanClick()) {
      // This click is the end of a pan gesture — ignore it
      e.stopPropagation();
      return;
    }
    togglePlayPause();
  };

  // Toggle video play/pause state
  const togglePlayPause = () => {
    handlePlayPause();
  };

  // Fullscreen: backend Photino OS fullscreen (+ in-page overlay for controls)
  const enterFullscreen = () => {
    setIsFullscreen(true);
    sendMessageToBackend('ToggleFullscreen', { enabled: true });
  };

  const exitFullscreen = () => {
    setIsFullscreen(false);
    sendMessageToBackend('ToggleFullscreen', { enabled: false });
  };

  const toggleFullscreen = () => {
    if (isFullscreen) exitFullscreen();
    else enterFullscreen();
  };

  // Prevent page scrollbars while our overlay is active
  useEffect(() => {
    const el = document.documentElement;
    const body = document.body;
    if (isFullscreen) {
      el.style.overflow = 'hidden';
      body.style.overflow = 'hidden';
    } else {
      el.style.overflow = '';
      body.style.overflow = '';
    }
    resetVideoZoom();
    return () => {
      el.style.overflow = '';
      body.style.overflow = '';
    };
  }, [isFullscreen]);

  // Handle clicks on the timeline to seek video
  const handleTimelineClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (isInteracting || !scrollContainerRef.current) return;
    const rect = scrollContainerRef.current.getBoundingClientRect();
    const clickPos = e.clientX - rect.left + scrollContainerRef.current.scrollLeft;
    const newTime = clickPos / pixelsPerSecond;
    const clampedTime = Math.max(0, Math.min(newTime, duration));
    setCurrentTime(clampedTime);
    if (videoRef.current) {
      videoRef.current.currentTime = clampedTime;
    }
  };

  // Handle timeline marker drag interactions
  const handleMarkerDragStart = (e: React.MouseEvent<HTMLDivElement>) => {
    e.stopPropagation();
    setIsDragging(true);
    setIsInteracting(true);
  };

  const handleMarkerDrag = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!isDragging || !scrollContainerRef.current) return;
    const rect = scrollContainerRef.current.getBoundingClientRect();
    const dragPos = e.clientX - rect.left + scrollContainerRef.current.scrollLeft;
    const newTime = dragPos / pixelsPerSecond;
    setCurrentTime(Math.max(0, Math.min(newTime, duration)));
    if (videoRef.current) {
      videoRef.current.currentTime = newTime;
    }
  };

  const handleMarkerDragEnd = () => {
    setIsDragging(false);
    setTimeout(() => setIsInteracting(false), 0);
  };

  useEffect(() => {
    document.body.classList.toggle('dragging-playhead', isDragging);
    return () => document.body.classList.remove('dragging-playhead');
  }, [isDragging]);

  // Format time in seconds to "HH:MM:SS" when needed, otherwise "MM:SS"
  const formatTime = (time: number) => {
    const totalSeconds = Math.max(0, Math.floor(time));
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    if (hours > 0) {
      return `${hours.toString()}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
    }
    return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
  };

  // Generate major and minor tick marks for the timeline based on zoom level
  const { majorTicks, minorTicks } = useMemo(() => {
    const maxTicks = 10;
    const minTickSpacing = 50;
    const totalPixels = duration * pixelsPerSecond;
    let majorTickInterval = Math.ceil(duration / maxTicks);
    let approxTickSpacing = totalPixels / (duration / majorTickInterval);
    while (approxTickSpacing < minTickSpacing) {
      majorTickInterval *= 2;
      approxTickSpacing = totalPixels / (duration / majorTickInterval);
    }
    const majorTicks: number[] = [];
    for (let t = majorTickInterval; t < duration; t += majorTickInterval) {
      majorTicks.push(t);
    }
    const minorTicks: number[] = [];
    const minorTicksPerMajor = 9;
    const minorInterval = majorTickInterval / minorTicksPerMajor;
    for (let t = minorInterval; t < duration; t += minorInterval) {
      if (Math.abs(t % majorTickInterval) < 0.0001) continue;
      minorTicks.push(t);
    }
    return { majorTicks, minorTicks };
  }, [duration, pixelsPerSecond]);

  // Add a new segment at the current video position
  const handleAddSegment = async () => {
    if (!videoRef.current) return;
    const start = currentTime;
    // Default to 10% of the visible timeline, capped at 2 minutes and clamped to video duration
    const visibleDuration = duration / zoom;
    const segmentDuration = Math.min(120, Math.max(1, visibleDuration * 0.1));
    const end = Math.min(start + segmentDuration, duration);

    const newSegment: Segment = {
      id: Date.now(),
      type: video.type,
      startTime: start,
      endTime: end,
      thumbnailDataUrl: undefined,
      isLoading: true,
      fileName: video.fileName,
      filePath: video.filePath,
      game: video.game,
      title: video.title,
      igdbId: video.igdbId,
      mutedAudioTracks:
        segments.length > 0
          ? segments[segments.length - 1].mutedAudioTracks
          : audioTracks.isMultiTrack
            ? [...audioTracks.mutedTracks]
            : undefined,
    };
    pushSegmentUndoSnapshot();
    addSegment(newSegment);
    setClipEditTargetSegmentId(newSegment.id);
    // Kick off thumbnail generation; uses latest state and guards against stale overwrites
    refreshSegmentThumbnail(newSegment);
  };

  const CLIP_MIN_DURATION_SEC = 0.1;

  const clipEditTargetSegment = useMemo((): Segment | null => {
    if (clipEditTargetSegmentId == null) return null;
    const segs = segments.filter((s) => s.filePath === video.filePath);
    return segs.find((s) => s.id === clipEditTargetSegmentId) ?? null;
  }, [segments, video.filePath, clipEditTargetSegmentId]);

  const handleSetClipStartFromPlayhead = () => {
    const seg = clipEditTargetSegment;
    if (!seg || duration <= 0) {
      setShowNoSegmentsIndicator(true);
      setTimeout(() => setShowNoSegmentsIndicator(false), 1300);
      return;
    }
    const t = Math.max(0, Math.min(videoRef.current?.currentTime ?? currentTime, duration));
    const newStart = Math.max(0, Math.min(t, seg.endTime - CLIP_MIN_DURATION_SEC));
    const updated = { ...seg, startTime: newStart };
    pushSegmentUndoSnapshot();
    updateSegment(updated);
    void refreshSegmentThumbnail(updated);
    if (videoRef.current) {
      videoRef.current.currentTime = newStart;
      setCurrentTime(newStart);
    }
  };

  const handleSetClipEndFromPlayhead = () => {
    const seg = clipEditTargetSegment;
    if (!seg || duration <= 0) {
      setShowNoSegmentsIndicator(true);
      setTimeout(() => setShowNoSegmentsIndicator(false), 1300);
      return;
    }
    const t = Math.max(0, Math.min(videoRef.current?.currentTime ?? currentTime, duration));
    const newEnd = Math.max(seg.startTime + CLIP_MIN_DURATION_SEC, Math.min(t, duration));
    const updated = { ...seg, endTime: newEnd };
    pushSegmentUndoSnapshot();
    updateSegment(updated);
    void refreshSegmentThumbnail(updated);
    if (videoRef.current) {
      videoRef.current.currentTime = newEnd;
      setCurrentTime(newEnd);
    }
  };

  const handleToggleClipPreviewLoop = () => {
    if (sortedClipSegments.length === 0) {
      setShowNoSegmentsIndicator(true);
      setTimeout(() => setShowNoSegmentsIndicator(false), 1300);
      return;
    }
    setClipPreviewLoop((prev) => {
      const next = !prev;
      if (next && videoRef.current && sortedClipSegments.length > 0) {
        const start = sortedClipSegments[0].startTime;
        videoRef.current.currentTime = start;
        setCurrentTime(start);
      }
      return next;
    });
  };

  // Create a clip from current segments
  const handleCreateClip = () => {
    if (segments.length === 0) {
      setShowNoSegmentsIndicator(true);
      setTimeout(() => setShowNoSegmentsIndicator(false), 1300);
      return;
    }

    const params = {
      Segments: segments.map((s) => ({
        id: s.id,
        type: s.type,
        fileName: s.fileName,
        filePath: s.filePath,
        game: s.game,
        title: s.title,
        startTime: s.startTime,
        endTime: s.endTime,
        igdbId: s.igdbId,
        mutedAudioTracks: s.mutedAudioTracks,
        audioTrackVolumes: s.audioTrackVolumes,
      })),
    };
    sendMessageToBackend('CreateClip', params);
  };

  // Create a lossless clip from current segments (stream copy, no re-encode)
  const handleCreateLosslessClip = () => {
    if (segments.length === 0) {
      setShowNoSegmentsIndicator(true);
      setTimeout(() => setShowNoSegmentsIndicator(false), 1300);
      return;
    }

    const params = {
      Segments: segments.map((s) => ({
        id: s.id,
        type: s.type,
        fileName: s.fileName,
        filePath: s.filePath,
        game: s.game,
        title: s.title,
        startTime: s.startTime,
        endTime: s.endTime,
        igdbId: s.igdbId,
        mutedAudioTracks: s.mutedAudioTracks,
        audioTrackVolumes: s.audioTrackVolumes,
      })),
      lossless: true,
    };
    sendMessageToBackend('CreateClip', params);
  };

  // Handle segment drag and drop operations (drag start removed to allow segment click-through)

  const handleSegmentDrag = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!scrollContainerRef.current) return;
    if ((e.buttons & 1) !== 1 && dragState.id == null) return;
    const rect = scrollContainerRef.current.getBoundingClientRect();
    const dragPos = e.clientX - rect.left + scrollContainerRef.current.scrollLeft;
    const cursorTime = dragPos / pixelsPerSecond;

    // If no active drag, see if we should start due to threshold
    if (dragState.id == null) {
      const cand = dragCandidateRef.current;
      if (!cand) return;
      const delta = Math.abs(e.clientX - cand.startClientX);
      if (delta <= 3) return; // not enough movement yet
      pushSegmentUndoSnapshot();
      setDragState({ id: cand.id, offset: cand.offset });
      setIsInteracting(true);
    }

    const activeId = dragState.id ?? dragCandidateRef.current?.id;
    const activeOffset =
      dragState.id != null ? dragState.offset : (dragCandidateRef.current?.offset ?? 0);
    if (activeId == null) return;
    const seg = segments.find((s) => s.id === activeId);
    if (seg) {
      const segLength = seg.endTime - seg.startTime;
      let newStart = cursorTime - activeOffset;
      newStart = Math.max(0, Math.min(newStart, duration - segLength));
      const updatedSegment = { ...seg, startTime: newStart, endTime: newStart + segLength };
      updateSegment(updatedSegment);
      setClipEditTargetSegmentId(updatedSegment.id);
      latestDraggedSegmentRef.current = updatedSegment;
    }
  };

  const handleSegmentDragEnd = () => {
    const draggedId = dragState.id;
    setDragState({ id: null, offset: 0 });
    dragCandidateRef.current = null;
    setTimeout(() => setIsInteracting(false), 0);
    if (draggedId != null && latestDraggedSegmentRef.current) {
      const seg = latestDraggedSegmentRef.current;
      latestDraggedSegmentRef.current = null;
      void refreshSegmentThumbnail(seg);
    }
  };

  // Handle global mouse up events for drag operations
  useEffect(() => {
    const handleGlobalMouseUp = () => {
      handleMarkerDragEnd();
      if (dragState.id !== null) {
        handleSegmentDragEnd();
      }
      if (
        resizingSegmentId !== null ||
        resizeDirectionRef.current != null ||
        resizePlaybackRef.current != null
      ) {
        handleSegmentResizeEnd();
      }
      dragCandidateRef.current = null;
      resizeCandidateRef.current = null;
    };
    window.addEventListener('mouseup', handleGlobalMouseUp);
    return () => {
      window.removeEventListener('mouseup', handleGlobalMouseUp);
    };
  }, [dragState.id, resizingSegmentId]);

  // Start a potential drag on mousedown without blocking click-through
  const handleSegmentMouseDown = (e: React.MouseEvent<HTMLDivElement>, id: number) => {
    if (!scrollContainerRef.current) return;
    const rect = scrollContainerRef.current.getBoundingClientRect();
    const dragPos = e.clientX - rect.left + scrollContainerRef.current.scrollLeft;
    const cursorTime = dragPos / pixelsPerSecond;
    const seg = segments.find((s) => s.id === id);
    if (seg?.filePath === video.filePath) {
      setClipEditTargetSegmentId(id);
    }
    if (seg) {
      dragCandidateRef.current = {
        id,
        startClientX: e.clientX,
        offset: cursorTime - seg.startTime,
      };
    }
  };

  // Prepare to resize on drag (click-through on simple click)
  const handleResizeMouseDown = (
    e: React.MouseEvent<HTMLDivElement>,
    id: number,
    direction: 'start' | 'end',
  ) => {
    // Do not stop propagation so timeline click can still happen
    resizeCandidateRef.current = { id, direction, startClientX: e.clientX };
    const s = segments.find((x) => x.id === id);
    if (s?.filePath === video.filePath) {
      setClipEditTargetSegmentId(id);
    }
    // Freeze playback at the grab point so the cursor restore isn't drifted
    // by playback continuing before the drag threshold is crossed
    if (!resizePlaybackRef.current && videoRef.current) {
      const wasPlaying = !videoRef.current.paused;
      resizePlaybackRef.current = { wasPlaying, cursorTime: videoRef.current.currentTime };
      if (wasPlaying) videoRef.current.pause();
    }
  };

  const handleSegmentResize = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!scrollContainerRef.current) return;
    if ((e.buttons & 1) !== 1 && resizingSegmentId == null) return;
    const rect = scrollContainerRef.current.getBoundingClientRect();
    const pos = e.clientX - rect.left + scrollContainerRef.current.scrollLeft;
    const t = pos / pixelsPerSecond;
    // If no active resize yet, check if we should start (threshold)
    if (resizingSegmentId == null || !resizeDirection) {
      const cand = resizeCandidateRef.current;
      if (!cand) return;
      const delta = Math.abs(e.clientX - cand.startClientX);
      if (delta <= 3) return; // not enough movement
      pushSegmentUndoSnapshot();
      setResizingSegmentId(cand.id);
      setResizeDirection(cand.direction);
      resizeDirectionRef.current = cand.direction;
      setIsInteracting(true);
    }

    const activeId = resizingSegmentId ?? resizeCandidateRef.current?.id ?? null;
    const activeDir = resizeDirection ?? resizeCandidateRef.current?.direction ?? null;
    if (activeId == null || !activeDir) return;
    const seg = segments.find((s) => s.id === activeId);
    if (!seg) return;

    let updatedSegment;
    if (activeDir === 'start') {
      const newStart = Math.max(0, Math.min(t, seg.endTime - 0.1));
      updatedSegment = { ...seg, startTime: newStart };
    } else {
      const newEnd = Math.min(duration, Math.max(t, seg.startTime + 0.1));
      updatedSegment = { ...seg, endTime: newEnd };
    }
    latestDraggedSegmentRef.current = updatedSegment;
    updateSegment(updatedSegment);
    setClipEditTargetSegmentId(updatedSegment.id);

    // While resizing, preview the frame at the active edge; the playhead stays put
    const edgeTime = activeDir === 'start' ? updatedSegment.startTime : updatedSegment.endTime;
    if (videoRef.current) {
      videoRef.current.currentTime = Math.max(0, Math.min(edgeTime, duration));
    }
  };

  const handleSegmentResizeEnd = () => {
    const direction = resizeDirectionRef.current;
    resizeDirectionRef.current = null;
    setResizingSegmentId(null);
    setResizeDirection(null);
    resizeCandidateRef.current = null;
    setTimeout(() => setIsInteracting(false), 0);
    const seg = latestDraggedSegmentRef.current;
    latestDraggedSegmentRef.current = null;
    const playback = resizePlaybackRef.current;
    resizePlaybackRef.current = null;
    if (playback && videoRef.current) {
      if (direction === 'start' && seg) {
        // Moving the start edge lands the playhead on the new start
        const t = Math.max(0, Math.min(seg.startTime, duration));
        videoRef.current.currentTime = t;
        setCurrentTime(t);
      } else if (direction != null) {
        // Moving the end edge leaves the playhead where it was
        videoRef.current.currentTime = playback.cursorTime;
      }
      // No direction: simple click on a handle; the click-through seek decides
      if (playback.wasPlaying) {
        void videoRef.current.play();
      }
    }
    // Thumbnail is the start frame, so only refresh when the start edge moved.
    if (seg && direction === 'start') {
      void refreshSegmentThumbnail(seg);
    }
  };

  // Right-click to remove segment disabled to keep segments click-through

  // Move segment card in the sidebar
  const moveCard = (dragIndex: number, hoverIndex: number) => {
    const newSegments = [...segments];
    const [removed] = newSegments.splice(dragIndex, 1);
    newSegments.splice(hoverIndex, 0, removed);
    updateSegmentsArray(newSegments);
    if (removed.filePath === video.filePath) {
      setClipEditTargetSegmentId(removed.id);
    }
  };

  // Get video source URL - use the filePath from metadata
  const getVideoPath = (): string => {
    return `http://localhost:2222/api/content?input=${encodeURIComponent(video.filePath)}&type=${video.type.toLowerCase()}`;
  };

  // Handle video upload operation
  const handleUpload = () => {
    // Ensure video is paused before opening upload modal
    if (videoRef.current && !videoRef.current.paused) {
      videoRef.current.pause();
    }

    openModal(
      <UploadModal
        key={`${Math.random()}`}
        video={video}
        onClose={closeModal}
        onUpload={(title, description, visibility) => {
          const parameters = {
            FilePath: video.filePath,
            JWT: session?.access_token,
            Game: video.game,
            Title: title,
            Description: description,
            Visibility: visibility,
            IgdbId: video.igdbId?.toString(),
          };

          sendMessageToBackend('UploadContent', parameters);
        }}
      />,
    );
  };

  const [fileCopied, setFileCopied] = useState(false);

  const handleCopyFile = () => {
    sendMessageToBackend('CopyFileToClipboard', { FilePath: video.filePath });
    setFileCopied(true);
    setTimeout(() => setFileCopied(false), 1500);
  };

  const { selectedBookmarkTypes, availableBookmarkTypes, filteredBookmarks, toggleBookmarkType } =
    useBookmarkFilter(video.bookmarks);

  const handleAddBookmark = () => {
    if (!videoRef.current) return;

    const currentTimeInSeconds = videoRef.current.currentTime;
    // Format time as HH:MM:SS.mmm for consistency with backend
    const hours = Math.floor(currentTimeInSeconds / 3600);
    const minutes = Math.floor((currentTimeInSeconds % 3600) / 60);
    const seconds = Math.floor(currentTimeInSeconds % 60);
    const milliseconds = Math.floor((currentTimeInSeconds % 1) * 1000);

    const formattedTime = `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}.${milliseconds.toString().padStart(3, '0')}`;

    // Default to Manual bookmark type if not specified
    const bookmarkType = BookmarkType.Manual;

    // Generate a random ID between 1 and MAX_INT
    const bookmarkId = Math.floor(Math.random() * 2147483647) + 1;

    // Create a new bookmark object
    const newBookmark: Bookmark = {
      id: bookmarkId,
      type: bookmarkType,
      time: formattedTime,
    };

    // Add the bookmark to the video's bookmarks array
    video.bookmarks.push(newBookmark);

    // Force a re-render to show the new bookmark
    const bookmarks = [...video.bookmarks];
    video.bookmarks = bookmarks;

    // Send message to backend to add bookmark
    sendMessageToBackend('AddBookmark', {
      FilePath: video.filePath,
      Type: bookmarkType,
      Time: formattedTime,
      ContentType: video.type,
      Id: bookmarkId,
    });
  };

  const handleDeleteBookmark = (bookmarkId: number) => {
    // Find the bookmark in the video's bookmarks array
    const bookmarkIndex = video.bookmarks.findIndex((b) => b.id === bookmarkId);

    if (bookmarkIndex !== -1) {
      // Remove the bookmark from the array
      video.bookmarks.splice(bookmarkIndex, 1);

      // Force a re-render to update the UI
      const bookmarks = [...video.bookmarks];
      video.bookmarks = bookmarks;

      // Send message to backend to delete the bookmark
      sendMessageToBackend('DeleteBookmark', {
        FilePath: video.filePath,
        ContentType: video.type,
        Id: bookmarkId,
      });
    }
  };

  // Handle volume change
  const handleVolumeChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newVolume = parseFloat(e.target.value);
    setPlayerVolume(newVolume);
  };

  // Toggle mute state
  const toggleMute = () => {
    if (videoRef.current) {
      if (audioTracks.isMultiTrack) {
        // Master mute for multi-track: silences all audio elements
        const newMuted = !audioTracks.masterMuted;
        audioTracks.setMasterMuted(newMuted);
        setIsMuted(newMuted);
        localStorage.setItem('segra-muted', newMuted.toString());
        return;
      }

      const newMutedState = !videoRef.current.muted;
      videoRef.current.muted = newMutedState;
      setIsMuted(newMutedState);
      localStorage.setItem('segra-muted', newMutedState.toString());
    }
  };

  const setPlaybackRateForPlayer = (rate: number) => {
    const r = clampPlaybackRate(rate);
    if (videoRef.current) videoRef.current.playbackRate = r;
    setPlaybackRate(r);
    localStorage.setItem('segra-playbackRate', r.toString());
  };

  return (
    <DndProvider backend={HTML5Backend}>
      <div className="flex w-full h-full overflow-hidden bg-base-200" ref={containerRef}>
        <div className="flex flex-col flex-1 w-full h-full p-4 pb-2 overflow-hidden lg:w-3/4">
          <VideoTopInfoBar
            video={video}
            currentIndex={currentIndex}
            playlistCount={playlist.length}
            prevVideo={prevVideo}
            nextVideo={nextVideo}
            onSelectVideo={handleSelectPlaylistVideo}
            onDelete={handleDeleteVideo}
            onMoveToPendingEdit={handleMoveToPendingEdit}
            showMoveToPendingEdit={showMoveToPendingEdit}
            onMoveOutOfPendingEdit={handleMoveOutOfPendingEdit}
            showMoveOutOfPendingEdit={showMoveOutOfPendingEdit}
            pendingEditSourceType={
              isPendingEditSourceType(video.pendingEditSourceType)
                ? video.pendingEditSourceType
                : undefined
            }
            onMoveOutOfReadyToDelete={handleMoveOutOfReadyToDelete}
            showMoveOutOfReadyToDelete={showMoveOutOfReadyToDelete}
            readyToDeleteSourceType={
              isRecordingSourceType(video.readyToDeleteSourceType)
                ? video.readyToDeleteSourceType
                : undefined
            }
          />
          <div
            className={`${isFullscreen ? 'fixed inset-0 z-50 overflow-hidden bg-black' : 'relative flex-1 min-h-0 overflow-hidden'} ${!controlsVisible && isPointerInPlayer ? 'cursor-none' : ''}`}
            ref={playerContainerRef}
            onMouseMove={() => {
              setIsPointerInPlayer(true);
              showControlsTemporarily();
            }}
            onMouseLeave={() => {
              setIsPointerInPlayer(false);
              isPointerOverControlsRef.current = false;
              if (controlsHideTimeoutRef.current) {
                clearTimeout(controlsHideTimeoutRef.current);
                controlsHideTimeoutRef.current = null;
              }
              controlsHideTimeoutRef.current = window.setTimeout(() => {
                setControlsVisible(false);
                controlsHideTimeoutRef.current = null;
              }, 600);
            }}
          >
            <div className={videoWrapperClassName}>
              <video
                autoPlay
                className="w-full h-full object-contain"
                src={getVideoPath()}
                ref={videoRef}
                onSeeked={(e) => setCurrentTime(e.currentTarget.currentTime)}
                onClick={onVideoClick}
                onDoubleClick={toggleFullscreen}
                onPointerDown={onVideoPointerDown}
                onPointerMove={onVideoPointerMove}
                onPointerUp={onVideoPointerUp}
                onWheel={onVideoWheel}
                style={{
                  backgroundColor: 'black',
                  objectFit: 'contain' as const,
                  transform: `translate(${videoTranslate.x}px, ${videoTranslate.y}px) scale(${videoScale})`,
                  transformOrigin: '0 0',
                  touchAction: videoScale > 1 ? 'none' : undefined,
                  cursor: videoScale > 1 && isPanning ? 'grabbing' : undefined,
                }}
              />
            </div>

            <div
              className={`absolute left-0 right-0 bottom-0 bg-black/70 pb-2 flex flex-col gap-2 transition-transform duration-300 select-none ${controlsVisible ? 'translate-y-0' : 'translate-y-full'}`}
              onMouseEnter={handleControlsMouseEnter}
              onMouseLeave={handleControlsMouseLeave}
            >
              <input
                type="range"
                min={0}
                max={Math.max(0.01, duration)}
                step={0.01}
                value={Math.min(currentTime, duration)}
                onChange={(e) => {
                  const t = parseFloat(e.target.value);
                  setCurrentTime(t);
                  if (videoRef.current) videoRef.current.currentTime = t;
                }}
                onPointerUp={(e) => (e.currentTarget as HTMLInputElement).blur()}
                onMouseUp={(e) => (e.currentTarget as HTMLInputElement).blur()}
                onTouchEnd={(e) => (e.currentTarget as HTMLInputElement).blur()}
                className="w-full h-5 -my-2 bg-center bg-no-repeat bg-[length:100%_4px] hover:bg-[length:100%_7px] transition-[background-size] duration-300 appearance-none cursor-pointer [&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:w-0 [&::-webkit-slider-thumb]:h-0 [&::-moz-range-thumb]:w-0 [&::-moz-range-thumb]:h-0 [&::-moz-range-thumb]:border-0"
                style={{
                  backgroundImage: `linear-gradient(to right, var(--color-accent) ${(Math.min(currentTime, duration) / Math.max(0.01, duration)) * 100}%, #4b5563 ${(Math.min(currentTime, duration) / Math.max(0.01, duration)) * 100}%)`,
                }}
              />

              <div className="flex items-center justify-between px-3">
                <div className="flex items-center gap-3">
                  <button
                    onClick={togglePlayPause}
                    className="text-white transition-colors cursor-pointer hover:text-accent"
                    aria-label={isPlaying ? 'Pause' : 'Play'}
                  >
                    {isPlaying ? <Pause className="w-5 h-5" /> : <Play className="w-5 h-5" />}
                  </button>

                  <div className="flex items-center">
                    <button
                      onClick={toggleMute}
                      className="text-white transition-colors cursor-pointer hover:text-accent"
                      aria-label={isMuted ? 'Unmute' : 'Mute'}
                    >
                      {isMuted || volume === 0 ? (
                        <VolumeX className="w-5 h-5" />
                      ) : volume < 0.2 ? (
                        <VolumeX className="w-5 h-5" />
                      ) : volume < 0.7 ? (
                        <Volume1 className="w-5 h-5" />
                      ) : (
                        <Volume2 className="w-5 h-5" />
                      )}
                    </button>
                    <input
                      type="range"
                      min="0"
                      max="1"
                      step="0.02"
                      value={volume}
                      onChange={handleVolumeChange}
                      onPointerUp={(e) => (e.currentTarget as HTMLInputElement).blur()}
                      onMouseUp={(e) => (e.currentTarget as HTMLInputElement).blur()}
                      onTouchEnd={(e) => (e.currentTarget as HTMLInputElement).blur()}
                      className="w-20 ml-2 h-1 rounded-lg appearance-none cursor-pointer [&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:w-2 [&::-webkit-slider-thumb]:h-2 [&::-webkit-slider-thumb]:rounded-full [&::-webkit-slider-thumb]:bg-white [&::-moz-range-thumb]:w-2 [&::-moz-range-thumb]:h-2 [&::-moz-range-thumb]:rounded-full [&::-moz-range-thumb]:bg-white [&::-moz-range-thumb]:border-0"
                      style={{
                        backgroundImage: `linear-gradient(to right, var(--color-accent) ${(isMuted ? 0 : volume) * 100}%, #4b5563 ${(isMuted ? 0 : volume) * 100}%)`,
                      }}
                    />
                  </div>

                  <span className="text-xs tabular-nums text-white/90">
                    {formatTime(currentTime)} / {formatTime(duration)}
                  </span>
                </div>

                <div className="flex items-center gap-2">
                  {audioTracks.isMultiTrack && (
                    <div className="relative">
                      <button
                        onClick={() => setShowAudioTracks(!showAudioTracks)}
                        className={`flex items-center justify-center p-1 text-white cursor-pointer transition-colors border rounded-md border-base-400 hover:text-accent hover:bg-accent/20 ${showAudioTracks ? 'text-accent bg-accent/20' : ''} ${audioTracks.soloTrack != null ? 'ring-1 ring-accent/50' : ''}`}
                        title={
                          audioTracks.soloTrack != null
                            ? '多音軌：單獨播放中（只播出選取的音軌）'
                            : '多音軌音量與單獨播放'
                        }
                      >
                        <Headphones className="w-4 h-4" />
                      </button>
                      <div
                        className={`absolute bottom-full right-0 mb-2 p-2 bg-black/90 rounded-lg border border-base-400 min-w-[17rem] z-50 transition-all duration-300 origin-bottom-right ${showAudioTracks ? 'opacity-100 translate-y-0 pointer-events-auto' : 'opacity-0 translate-y-2 pointer-events-none'}`}
                      >
                        {audioTracks.tracks.map((track) => {
                          const solo = audioTracks.soloTrack;
                          const isEffectivelyMuted =
                            solo !== null
                              ? solo !== track.index
                              : audioTracks.mutedTracks.has(track.index);
                          const isSolo = solo === track.index;
                          const vol = audioTracks.volumes[track.index] ?? 1;
                          return (
                            <div
                              key={track.index}
                              className="flex items-center justify-between gap-2 py-0.5"
                            >
                              <button
                                type="button"
                                onClick={(e) => {
                                  e.preventDefault();
                                  audioTracks.toggleSolo(track.index);
                                }}
                                className={`shrink-0 min-w-[1.5rem] h-5 px-1 text-[10px] font-semibold rounded border transition-colors ${
                                  isSolo
                                    ? 'border-accent text-accent bg-accent/20'
                                    : 'border-white/30 text-white/60 hover:border-white/50 hover:text-white/90'
                                }`}
                                title="單獨播出此音軌（再按一次可解除）"
                                aria-label="單獨播出此音軌"
                                aria-pressed={isSolo}
                              >
                                S
                              </button>
                              <label className="flex items-center gap-2 min-w-0 cursor-pointer flex-1">
                                <input
                                  type="checkbox"
                                  checked={!isEffectivelyMuted}
                                  onChange={() => audioTracks.toggleTrackMute(track.index)}
                                  className="checkbox checkbox-primary checkbox-xs shrink-0"
                                />
                                <span className="text-xs text-white/80 truncate select-none">
                                  {displayTrackName(track.name).replace(' (Default)', '')}
                                </span>
                              </label>
                              <div className="flex items-center gap-2 shrink-0">
                                <input
                                  type="range"
                                  min="0"
                                  max="1"
                                  step="0.02"
                                  value={vol}
                                  onChange={(e) =>
                                    audioTracks.setTrackVolume(
                                      track.index,
                                      parseFloat(e.target.value),
                                    )
                                  }
                                  className={`w-16 h-1 rounded-lg appearance-none cursor-pointer [&::-webkit-slider-thumb]:appearance-none [&::-moz-range-thumb]:border-0 ${
                                    isEffectivelyMuted
                                      ? '[&::-webkit-slider-thumb]:w-0 [&::-webkit-slider-thumb]:h-0 [&::-moz-range-thumb]:w-0 [&::-moz-range-thumb]:h-0'
                                      : '[&::-webkit-slider-thumb]:w-2 [&::-webkit-slider-thumb]:h-2 [&::-webkit-slider-thumb]:rounded-full [&::-webkit-slider-thumb]:bg-[var(--color-accent)] [&::-moz-range-thumb]:w-2 [&::-moz-range-thumb]:h-2 [&::-moz-range-thumb]:rounded-full [&::-moz-range-thumb]:bg-[var(--color-accent)]'
                                  }`}
                                  style={{
                                    backgroundImage: `linear-gradient(to right, var(--color-accent) ${(isEffectivelyMuted ? 0 : vol) * 100}%, #4b5563 ${(isEffectivelyMuted ? 0 : vol) * 100}%)`,
                                  }}
                                />
                                <span className="text-[10px] text-white/50 w-7 text-right tabular-nums">
                                  {Math.round(vol * 100)}%
                                </span>
                              </div>
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  )}

                  <button
                    onClick={() => {
                      const current = videoScaleRef.current || 1;
                      applyVideoScale(current - 0.5);
                    }}
                    disabled={videoScale <= 1}
                    className="flex items-center justify-center p-1 text-white cursor-pointer transition-colors border rounded-md border-base-400 hover:text-accent hover:bg-accent/20 disabled:opacity-50 disabled:cursor-not-allowed"
                    aria-label="縮小畫面"
                  >
                    <ZoomOut className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => {
                      const current = videoScaleRef.current || 1;
                      applyVideoScale(current + 0.5);
                    }}
                    disabled={videoScale >= 4}
                    className="flex items-center justify-center p-1 text-white cursor-pointer transition-colors border rounded-md border-base-400 hover:text-accent hover:bg-accent/20 disabled:opacity-50 disabled:cursor-not-allowed"
                    aria-label="放大畫面"
                  >
                    <ZoomIn className="w-4 h-4" />
                  </button>
                  <div
                    className="flex items-center gap-2 px-2 py-1 border rounded-md border-base-400"
                    title="播放速度"
                  >
                    <span className="text-xs font-medium text-white tabular-nums w-8 text-center shrink-0">
                      {formatPlaybackRateLabel(playbackRate)}
                    </span>
                    <input
                      type="range"
                      min={PLAYBACK_RATE_MIN}
                      max={PLAYBACK_RATE_MAX}
                      step={PLAYBACK_RATE_STEP}
                      value={playbackRate}
                      onChange={(e) => setPlaybackRateForPlayer(parseFloat(e.target.value))}
                      onPointerUp={(e) => (e.currentTarget as HTMLInputElement).blur()}
                      onMouseUp={(e) => (e.currentTarget as HTMLInputElement).blur()}
                      onTouchEnd={(e) => (e.currentTarget as HTMLInputElement).blur()}
                      className="w-20 h-1 rounded-lg appearance-none cursor-pointer [&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:w-2 [&::-webkit-slider-thumb]:h-2 [&::-webkit-slider-thumb]:rounded-full [&::-webkit-slider-thumb]:bg-[var(--color-accent)] [&::-moz-range-thumb]:w-2 [&::-moz-range-thumb]:h-2 [&::-moz-range-thumb]:rounded-full [&::-moz-range-thumb]:bg-[var(--color-accent)] [&::-moz-range-thumb]:border-0"
                      style={{
                        backgroundImage: `linear-gradient(to right, var(--color-accent) ${playbackRateSliderPercent(playbackRate)}%, #4b5563 ${playbackRateSliderPercent(playbackRate)}%)`,
                      }}
                      aria-label="播放速度"
                      aria-valuemin={PLAYBACK_RATE_MIN}
                      aria-valuemax={PLAYBACK_RATE_MAX}
                      aria-valuenow={playbackRate}
                      aria-valuetext={formatPlaybackRateLabel(playbackRate)}
                    />
                  </div>

                  <button
                    onClick={toggleFullscreen}
                    onPointerUp={(e) => e.currentTarget.blur()}
                    onMouseUp={(e) => e.currentTarget.blur()}
                    onTouchEnd={(e) => e.currentTarget.blur()}
                    className="text-white cursor-pointer transition-colors hover:text-accent"
                    aria-label={isFullscreen ? '離開全螢幕' : '全螢幕'}
                  >
                    {isFullscreen ? (
                      <Minimize className="w-5 h-5" />
                    ) : (
                      <Maximize className="w-5 h-5" />
                    )}
                  </button>
                </div>
              </div>
            </div>
          </div>
          <div
            className="relative w-full mt-2 overflow-x-scroll overflow-y-hidden select-none shrink-0 timeline-wrapper"
            ref={scrollContainerRef}
            onMouseMove={(e) => {
              handleSegmentDrag(e);
              handleSegmentResize(e);
              handleMarkerDrag(e);
            }}
          >
            <div
              className="ticks-container relative h-[42px]"
              style={{
                width: `${duration * pixelsPerSecond}px`,
                minWidth: '100%',
                overflow: 'hidden',
              }}
            >
              {bookmarksReady && (
                <AnimatePresence initial={false}>
                  {filteredBookmarks.map((bookmark, index) => {
                    const timeInSeconds = timeStringToSeconds(bookmark.time);
                    const leftPos = timeInSeconds * pixelsPerSecond;
                    const Icon =
                      getIconMapping(video.igdbId)[bookmark.type as BookmarkType] || Skull;

                    return (
                      <motion.div
                        key={`bookmark-${bookmark.id ?? index}`}
                        initial={{ opacity: 0, scale: 0.5 }}
                        animate={{ opacity: 1, scale: 1 }}
                        exit={{ opacity: 0, scale: 0.5 }}
                        transition={{ duration: 0.1 }}
                        className="tooltip absolute bottom-0 transform -translate-x-1/2 cursor-pointer z-10 flex flex-col items-center text-[#25272e]"
                        data-tip={`${BOOKMARK_TYPE_LABELS[bookmark.type] ?? bookmark.type}${bookmark.subtype ? ` - ${BOOKMARK_TYPE_LABELS[bookmark.subtype] ?? bookmark.subtype}` : ''} (${bookmark.time})`}
                        style={{ left: `${leftPos}px` }}
                        onClick={() => {
                          const seekTo = Math.max(
                            0,
                            timeInSeconds - (bookmark.type == BookmarkType.Manual ? 10 : 5),
                          );
                          setCurrentTime(seekTo);
                          if (videoRef.current) {
                            videoRef.current.currentTime = seekTo;
                          }
                        }}
                        onContextMenu={(e) => {
                          e.preventDefault();
                          handleDeleteBookmark(bookmark.id);
                        }}
                      >
                        <div className="bg-[#EFAF2B] w-[26px] h-[26px] rounded-full flex items-center justify-center mb-0">
                          <Icon size={18} strokeWidth={2.5} />
                        </div>
                        <div className="w-[2px] h-[16px] bg-[#EFAF2B]" />
                      </motion.div>
                    );
                  })}
                </AnimatePresence>
              )}
              {minorTicks.map((tickTime) => {
                if (tickTime >= duration) return null;
                const leftPos = tickTime * pixelsPerSecond;
                return (
                  <div
                    key={`minor-${tickTime}`}
                    className="absolute bottom-0 h-[6px] border-l border-white/20"
                    style={{
                      left: `${leftPos}px`,
                    }}
                  />
                );
              })}
              {majorTicks.map((tickTime) => {
                if (tickTime > duration) return null;
                const leftPos = tickTime * pixelsPerSecond;
                return (
                  <div
                    key={`major-${tickTime}`}
                    className="absolute bottom-0 text-center text-white -translate-x-1/2 select-none whitespace-nowrap"
                    style={{
                      left: `${leftPos}px`,
                    }}
                  >
                    <span className="absolute bottom-full left-1/2 -translate-x-1/2 text-xs mb-[3px]">
                      {formatTime(tickTime)}
                    </span>
                    <div className="w-[2px] h-[10px] bg-white mx-auto" />
                  </div>
                );
              })}
            </div>
            <div
              className="timeline-container bg-base-300 border border-base-400 rounded-lg relative h-[50px] w-full overflow-hidden"
              style={{
                width: `${duration * pixelsPerSecond}px`,
                minWidth: '100%',
              }}
              onClick={handleTimelineClick}
            >
              {settings.showAudioWaveformInTimeline && (
                <canvas
                  ref={waveformCanvasRef}
                  height={49}
                  className="absolute top-0 pointer-events-none"
                  style={{
                    height: '49px',
                    opacity: 0.6,
                  }}
                />
              )}
              {sortedSegments.map((seg) => {
                const left = seg.startTime * pixelsPerSecond;
                const width = (seg.endTime - seg.startTime) * pixelsPerSecond;
                const hidden = seg.fileName !== video.fileName;
                return (
                  <>
                    <div
                      key={seg.id}
                      className={`absolute top-0 left-0 h-full cursor-move ${hidden ? 'hidden' : ''} transition-colors rounded-r-sm rounded-l-sm shadow-md
                                                bg-primary/20 border border-primary/20`}
                      style={{ left: `${left}px`, width: `${width}px` }}
                      onMouseEnter={() => {
                        setHoveredSegmentId(seg.id);
                      }}
                      onMouseLeave={() => {
                        setHoveredSegmentId(null);
                      }}
                      onMouseDown={(e) => handleSegmentMouseDown(e, seg.id)}
                      onContextMenu={(e) => {
                        e.preventDefault();
                        removeSegmentWithUndo(seg.id);
                      }}
                    >
                      <div className="absolute left-0 top-0 h-full w-[3px] bg-accent/80 rounded-l-sm pointer-events-none" />
                      <div className="absolute right-0 top-0 h-full w-[3px] bg-accent/80 rounded-r-sm pointer-events-none" />

                      {audioTracks.isMultiTrack &&
                        video.audioTrackNames &&
                        video.audioTrackNames.length > 1 && (
                          <button
                            className={`absolute top-[4px] right-[8px] flex items-center justify-center w-4 h-4 rounded z-10 pointer-events-auto cursor-pointer transition-opacity bg-black/45 text-white/70 hover:bg-black/65 ${hoveredSegmentId === seg.id || (timelineAudioMenu?.segId === seg.id && timelineAudioMenu.visible) ? 'opacity-100' : 'opacity-0'}`}
                            onMouseDown={(e) => e.stopPropagation()}
                            onClick={(e) => {
                              e.stopPropagation();
                              if (seg.filePath === video.filePath) {
                                setClipEditTargetSegmentId(seg.id);
                              }
                              if (
                                timelineAudioMenu?.segId === seg.id &&
                                timelineAudioMenu.visible
                              ) {
                                setTimelineAudioMenu((prev) =>
                                  prev ? { ...prev, visible: false } : null,
                                );
                                return;
                              }
                              const rect = e.currentTarget.getBoundingClientRect();
                              const trackCount = video.audioTrackNames?.length ?? 0;
                              const estimatedHeight = 16 + trackCount * 24;
                              const fitsBelow =
                                rect.bottom + 4 + estimatedHeight <= window.innerHeight;
                              const top = fitsBelow
                                ? rect.bottom + 4
                                : Math.max(8, rect.top - 4 - estimatedHeight);
                              const next = {
                                segId: seg.id,
                                x: rect.left,
                                y: top,
                                flipUp: !fitsBelow,
                                visible: false,
                              };
                              setTimelineAudioMenu(next);
                              requestAnimationFrame(() =>
                                setTimelineAudioMenu((prev) =>
                                  prev ? { ...prev, visible: true } : null,
                                ),
                              );
                            }}
                          >
                            <Headphones className="w-2.5 h-2.5" />
                          </button>
                        )}

                      <div
                        className="absolute top-0 -left-[7px] z-20 w-[14px] h-full bg-transparent cursor-col-resize pointer-events-auto"
                        onMouseDown={(e) => handleResizeMouseDown(e, seg.id, 'start')}
                        aria-label="調整區段起點"
                      />
                      <div
                        className="absolute top-0 -right-[7px] z-20 w-[14px] h-full bg-transparent cursor-col-resize pointer-events-auto"
                        onMouseDown={(e) => handleResizeMouseDown(e, seg.id, 'end')}
                        aria-label="調整區段終點"
                      />
                    </div>
                  </>
                );
              })}
              <div
                className="absolute top-0 left-0 z-10 w-1 h-full -translate-x-1/2 rounded-sm shadow cursor-pointer marker bg-accent"
                style={{ left: `${currentTime * pixelsPerSecond}px` }}
                onMouseDown={handleMarkerDragStart}
              />
            </div>
          </div>
          {timelineAudioMenu &&
            (() => {
              const menuSeg = segments.find((s) => s.id === timelineAudioMenu.segId);
              if (!menuSeg || !video.audioTrackNames) return null;
              const mutedTracks = menuSeg.mutedAudioTracks ?? [];
              const trackVolumes = menuSeg.audioTrackVolumes ?? {};
              return (
                <div
                  className={`fixed p-2 bg-black/90 rounded-lg border border-base-400 min-w-48 z-[200] cursor-default transition-all duration-300 ${
                    timelineAudioMenu.visible
                      ? 'opacity-100 translate-y-0 pointer-events-auto'
                      : timelineAudioMenu.flipUp
                        ? 'opacity-0 translate-y-2 pointer-events-none'
                        : 'opacity-0 -translate-y-2 pointer-events-none'
                  }`}
                  style={{ left: timelineAudioMenu.x, top: timelineAudioMenu.y }}
                  onMouseDown={(e) => e.stopPropagation()}
                  onClick={(e) => e.stopPropagation()}
                >
                  {video.audioTrackNames.map((name, i) => {
                    const isMuted = mutedTracks.includes(i);
                    const vol = trackVolumes[i] ?? 1;
                    return (
                      <div key={i} className="flex items-center justify-between gap-2 py-0.5">
                        <label className="flex items-center gap-2 min-w-0 cursor-pointer">
                          <input
                            type="checkbox"
                            checked={!isMuted}
                            onChange={() => {
                              setClipEditTargetSegmentId(menuSeg.id);
                              pushSegmentUndoSnapshot();
                              let newMuted: number[];
                              if (isMuted) {
                                // Enabling this track
                                if (i === 0) {
                                  // Enabling Full Mix: mute all individual tracks
                                  newMuted = (video.audioTrackNames ?? [])
                                    .map((_, idx) => idx)
                                    .filter((idx) => idx !== 0);
                                } else {
                                  // Enabling an individual track: mute Full Mix
                                  newMuted = mutedTracks.filter((t) => t !== i);
                                  if (!newMuted.includes(0)) newMuted.push(0);
                                }
                              } else {
                                // Muting this track
                                newMuted = [...mutedTracks, i];
                              }
                              updateSegment({ ...menuSeg, mutedAudioTracks: newMuted });
                            }}
                            className="checkbox checkbox-primary checkbox-xs shrink-0"
                          />
                          <span className="text-xs text-white/80 truncate">
                            {name.replace(' (Default)', '')}
                          </span>
                        </label>
                        <div className="flex items-center gap-2 shrink-0">
                          <input
                            type="range"
                            min="0"
                            max="1"
                            step="0.02"
                            value={vol}
                            onChange={(e) => {
                              setClipEditTargetSegmentId(menuSeg.id);
                              pushSegmentUndoSnapshot();
                              const newVolumes = {
                                ...trackVolumes,
                                [i]: parseFloat(e.target.value),
                              };
                              updateSegment({ ...menuSeg, audioTrackVolumes: newVolumes });
                            }}
                            className={`w-16 h-1 rounded-lg appearance-none cursor-pointer [&::-webkit-slider-thumb]:appearance-none [&::-moz-range-thumb]:border-0 ${
                              isMuted
                                ? '[&::-webkit-slider-thumb]:w-0 [&::-webkit-slider-thumb]:h-0 [&::-moz-range-thumb]:w-0 [&::-moz-range-thumb]:h-0'
                                : '[&::-webkit-slider-thumb]:w-2 [&::-webkit-slider-thumb]:h-2 [&::-webkit-slider-thumb]:rounded-full [&::-webkit-slider-thumb]:bg-[var(--color-accent)] [&::-moz-range-thumb]:w-2 [&::-moz-range-thumb]:h-2 [&::-moz-range-thumb]:rounded-full [&::-moz-range-thumb]:bg-[var(--color-accent)]'
                            }`}
                            style={{
                              backgroundImage: `linear-gradient(to right, var(--color-accent) ${(isMuted ? 0 : vol) * 100}%, #4b5563 ${(isMuted ? 0 : vol) * 100}%)`,
                            }}
                          />
                          <span className="text-[10px] text-white/50 w-7 text-right tabular-nums">
                            {Math.round(vol * 100)}%
                          </span>
                        </div>
                      </div>
                    );
                  })}
                </div>
              );
            })()}
          <div className="flex items-center justify-between gap-4 py-1 shrink-0">
            <div className="flex items-center gap-3">
              <div className="flex items-center border rounded-lg join bg-base-300 border-base-400">
                <button
                  onClick={() => skipTime(-5)}
                  className="h-10 text-gray-300 btn btn-sm btn-secondary hover:text-accent join-item"
                >
                  <RotateCcw className="w-5 h-5" />
                </button>
                <button
                  onClick={handlePlayPause}
                  className="h-10 text-gray-300 btn btn-sm btn-secondary hover:text-accent join-item"
                  data-tip={isPlaying ? '暫停' : '播放'}
                >
                  {isPlaying ? <Pause className="w-5 h-5" /> : <Play className="w-5 h-5" />}
                </button>
                <button
                  onClick={() => skipTime(5)}
                  className="h-10 text-gray-300 btn btn-sm btn-secondary hover:text-accent join-item"
                  data-tip="快轉 5 秒"
                >
                  <RotateCw className="w-5 h-5" />
                </button>
              </div>
              {(video.type === 'Clip' || video.type === 'Highlight') && (
                <>
                  <Button
                    variant="primary"
                    size="sm"
                    className="h-10 px-5 hover:text-accent"
                    onClick={handleUpload}
                    disabled={
                      uploads[video.fileName + '.mp4']?.status === 'uploading' ||
                      uploads[video.fileName + '.mp4']?.status === 'processing'
                    }
                  >
                    <Upload className="w-5 h-5" />
                    <span>上傳</span>
                  </Button>
                  <Button
                    variant="primary"
                    size="sm"
                    className="h-10 hover:text-accent"
                    onClick={handleCopyFile}
                  >
                    <label
                      className={`swap overflow-hidden justify-center ${fileCopied ? 'swap-active' : ''}`}
                    >
                      <div className="swap-off">
                        <Copy className="w-5 h-5" />
                      </div>
                      <div className="swap-on">
                        <Check className="w-5 h-5" />
                      </div>
                    </label>
                    <span>複製</span>
                  </Button>
                </>
              )}
              {supportsClipWorkflow && (
                <>
                  <Button
                    variant="primary"
                    size="sm"
                    className="h-10 gap-1 hover:text-accent"
                    onClick={handleCreateClip}
                  >
                    <Clapperboard className="w-5 h-5" />
                    <span>建立片段</span>
                  </Button>
                  <Button
                    variant="primary"
                    size="sm"
                    className="h-10 gap-1 hover:text-accent"
                    onClick={handleCreateLosslessClip}
                  >
                    <Clapperboard className="w-5 h-5" />
                    <span>無損片段</span>
                  </Button>
                  <div className="flex items-center border rounded-lg join bg-base-300 border-base-400">
                    <button
                      type="button"
                      onClick={handleSetClipStartFromPlayhead}
                      disabled={sortedClipSegments.length === 0 || clipEditTargetSegment == null}
                      title="把播放位置設為「上次選取或修改的區段」的起點（在時間軸或側欄點區段、拖曳、調整邊界或新增區段後，會記住那個區段）"
                      className="h-10 px-2 text-gray-300 btn btn-sm btn-secondary hover:text-accent join-item gap-1 disabled:opacity-40"
                    >
                      <ArrowLeftToLine className="w-4 h-4 shrink-0" />
                      <span className="hidden sm:inline text-xs">起點</span>
                    </button>
                    <button
                      type="button"
                      onClick={handleSetClipEndFromPlayhead}
                      disabled={sortedClipSegments.length === 0 || clipEditTargetSegment == null}
                      title="把播放位置設為「上次選取或修改的區段」的終點（選取規則與起點按鈕相同）"
                      className="h-10 px-2 text-gray-300 btn btn-sm btn-secondary hover:text-accent join-item gap-1 disabled:opacity-40"
                    >
                      <ArrowRightToLine className="w-4 h-4 shrink-0" />
                      <span className="hidden sm:inline text-xs">終點</span>
                    </button>
                    <button
                      type="button"
                      onClick={handleToggleClipPreviewLoop}
                      disabled={sortedClipSegments.length === 0}
                      title="預覽剪輯：依時間順序播放所有區段，播完一段會自動跳到下一段的起點；最後一段結束後回到第一段（循環）"
                      className={`h-10 px-2 btn btn-sm btn-secondary join-item gap-1 disabled:opacity-40 ${
                        clipPreviewLoop ? 'text-accent' : 'text-gray-300 hover:text-accent'
                      }`}
                    >
                      <Repeat className="w-4 h-4 shrink-0" />
                      <span className="hidden sm:inline text-xs">預覽</span>
                    </button>
                  </div>
                  <Button
                    variant="primary"
                    size="sm"
                    className={`h-10 gap-1 hover:text-accent ${showNoSegmentsIndicator ? 'segment-hint-flash' : ''}`}
                    onClick={handleAddSegment}
                  >
                    <SquarePlus className="w-5 h-5" />
                    <span>新增區段</span>
                  </Button>
                </>
              )}
              {showBufferStyleCopy && (
                <Button
                  variant="primary"
                  size="sm"
                  className="h-10 hover:text-accent"
                  onClick={handleCopyFile}
                >
                  <label
                    className={`swap overflow-hidden justify-center ${fileCopied ? 'swap-active' : ''}`}
                  >
                    <div className="swap-off">
                      <Copy className="w-5 h-5" />
                    </div>
                    <div className="swap-on">
                      <Check className="w-5 h-5" />
                    </div>
                  </label>
                  <span>複製</span>
                </Button>
              )}
            </div>

            <div className="flex items-center gap-3">
              {supportsClipWorkflow && (
                <>
                  {availableBookmarkTypes.length > 0 && (
                    <div className="flex items-center h-10 gap-0 px-0 border rounded-lg bg-base-300 join border-base-400">
                      {availableBookmarkTypes.map((type) => (
                        <button
                          key={type}
                          onClick={() => toggleBookmarkType(type)}
                          className={`btn btn-sm btn-secondary border-none transition-colors join-item px-2 ${selectedBookmarkTypes.has(type) ? 'text-accent' : 'text-gray-300'}`}
                        >
                          {React.createElement(getIconMapping(video.igdbId)[type] || Skull, {
                            className: 'w-5 h-5',
                          })}
                        </button>
                      ))}
                    </div>
                  )}
                  <div className="flex items-center gap-2 rounded-lg bg-base-300">
                    <Button
                      variant="primary"
                      size="sm"
                      className="h-10 hover:text-accent"
                      onClick={handleAddBookmark}
                    >
                      <BookmarkPlus className="w-5 h-5" />
                    </Button>
                  </div>
                </>
              )}

              <div className="flex items-center h-10 gap-1 px-0 border rounded-lg bg-base-300 border-base-400">
                <button
                  onClick={() => handleZoomChange(false)}
                  className="btn btn-sm btn-secondary disabled:opacity-100 disabled:bg-base-300"
                  disabled={zoom <= 1}
                >
                  <Minus className="w-4 h-4" />
                </button>
                <span className="text-sm font-medium text-center text-gray-300">
                  {zoom < 10 ? zoom.toFixed(1) : Math.round(zoom)}x
                </span>
                <button
                  onClick={() => handleZoomChange(true)}
                  className="btn btn-sm btn-secondary"
                  disabled={zoom >= 1000}
                >
                  <Plus className="w-4 h-4" />
                </button>
              </div>
            </div>
          </div>
          <VideoPlaylistPanel
            playlist={playlist}
            currentVideo={video}
            currentIndex={currentIndex}
            onSelectVideo={handleSelectPlaylistVideo}
          />
        </div>
        {supportsClipWorkflow && (
          <div className="flex flex-col h-full pt-4 pl-4 pr-1 border-l bg-base-300 text-neutral-content w-52 2xl:w-70.25 border-base-400">
            <div className="flex-1 p-1 mt-1 overflow-y-scroll">
              {segments.map((seg, index) => (
                <SegmentCard
                  key={seg.id}
                  segment={seg}
                  index={index}
                  moveCard={moveCard}
                  formatTime={formatTime}
                  isHovered={hoveredSegmentId === seg.id}
                  setHoveredSegmentId={setHoveredSegmentId}
                  removeSegment={removeSegmentWithUndo}
                  audioTrackNames={video.audioTrackNames}
                  onMutedAudioTracksChange={(id, mutedTracks) => {
                    pushSegmentUndoSnapshot();
                    updateSegment({
                      ...segments.find((s) => s.id === id)!,
                      mutedAudioTracks: mutedTracks,
                    });
                  }}
                  onAudioTrackVolumesChange={(id, volumes) => {
                    pushSegmentUndoSnapshot();
                    updateSegment({
                      ...segments.find((s) => s.id === id)!,
                      audioTrackVolumes: volumes,
                    });
                  }}
                  onClipEditTarget={setClipEditTargetSegmentId}
                  onSidebarSegmentClick={handleSidebarSegmentSeek}
                  onSegmentCardDragBegin={pushSegmentUndoSnapshot}
                />
              ))}
            </div>
            <div className="flex items-center justify-between my-3 mr-3">
              <label className="flex items-center cursor-pointer">
                <input
                  type="checkbox"
                  name="clipClearSegmentsAfterCreatingClip"
                  checked={settings.clipClearSegmentsAfterCreatingClip}
                  onChange={(e) =>
                    updateSettings({ clipClearSegmentsAfterCreatingClip: e.target.checked })
                  }
                  className="checkbox checkbox-sm checkbox-accent"
                />
                <span className="ml-2 text-sm">建立片段後清除區段</span>
              </label>
            </div>
            <div className="flex items-center h-10 gap-0 px-0 mb-2 mr-3 rounded-lg bg-base-300 tooltip">
              <Button
                variant="primary"
                size="sm"
                className="w-full h-10 py-0 hover:text-accent"
                onClick={clearAllSegmentsWithUndo}
                disabled={segments.length === 0}
              >
                <Trash2 className="w-4 h-4" />
                <span>全部清除</span>
              </Button>
            </div>
          </div>
        )}
      </div>
    </DndProvider>
  );
}
