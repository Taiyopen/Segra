import React, { useRef, useState, useEffect, useMemo } from "react";
import { Content, BookmarkType, Selection } from "../Models/types";
import { sendMessageToBackend } from "../Utils/MessageUtils";
import { useSettings } from "../Context/SettingsContext";
import { DndProvider } from "react-dnd";
import { HTML5Backend } from "react-dnd-html5-backend";
import { useAuth } from "../Hooks/useAuth";
import { useSelections } from "../Context/SelectionsContext";
import { useUploads } from "../Context/UploadContext";
import { useModal } from "../Context/ModalContext";
import UploadModal from '../Components/UploadModal';
import { IconType } from "react-icons";
import { FaGun } from "react-icons/fa6";
import { MdAddBox, MdBookmark, MdBookmarkAdd, MdCleaningServices, MdMovieCreation, MdOutlineHandshake, MdPause, MdPlayArrow, MdReplay10, MdForward10 } from "react-icons/md";
import { IoSkull, IoAdd, IoRemove } from "react-icons/io5";
import SelectionCard from '../Components/SelectionCard';

// Converts time string in format "HH:MM:SS.mmm" to seconds
const timeStringToSeconds = (timeStr: string): number => {
    const [time, milliseconds] = timeStr.split('.');
    const [hours, minutes, seconds] = time.split(':').map(Number);
    return hours * 3600 + minutes * 60 + seconds + (milliseconds ? Number(`0.${milliseconds}`) : 0);
};

// Fetches a video thumbnail from the backend for a specific timestamp
const fetchThumbnailAtTime = async (videoPath: string, timeInSeconds: number): Promise<string> => {
    const url = `http://localhost:2222/api/thumbnail?input=${encodeURIComponent(videoPath)}&time=${timeInSeconds}`;
    const response = await fetch(url);
    
    if (!response.ok) {
        throw new Error(`Failed to fetch thumbnail: ${response.statusText}`);
    }
    
    const blob = await response.blob();
    return URL.createObjectURL(blob);
};

export default function VideoComponent({ video }: { video: Content }) {
    // Context hooks
    const { state, contentFolder } = useSettings();
    const { session } = useAuth();
    const { uploads } = useUploads();
    const { openModal, closeModal } = useModal();
    const { 
        selections, 
        addSelection, 
        updateSelection, 
        removeSelection, 
        updateSelectionsArray,
        clearAllSelections 
    } = useSelections();
    
    // Refs
    const videoRef = useRef<HTMLVideoElement>(null);
    const scrollContainerRef = useRef<HTMLDivElement>(null);
    const containerRef = useRef<HTMLDivElement>(null);
    const latestDraggedSelectionRef = useRef<Selection | null>(null);

    // Video state
    const [currentTime, setCurrentTime] = useState(0);
    const [duration, setDuration] = useState(0);
    const [zoom, setZoom] = useState(1);
    const [containerWidth, setContainerWidth] = useState(0);
    const [isPlaying, setIsPlaying] = useState(true);

    // Interaction state
    const [isDragging, setIsDragging] = useState(false);
    const [isInteracting, setIsInteracting] = useState(false);
    const [hoveredSelectionId, setHoveredSelectionId] = useState<number | null>(null);
    const [isHoveredByTimeline, setIsHoveredByTimeline] = useState(false);
    const [dragState, setDragState] = useState<{ id: number | null; offset: number }>({ 
        id: null, 
        offset: 0 
    });
    const [resizingSelectionId, setResizingSelectionId] = useState<number | null>(null);
    const [resizeDirection, setResizeDirection] = useState<"start" | "end" | null>(null);

    // Computed values
    const basePixelsPerSecond = duration > 0 ? containerWidth / duration : 0;
    const pixelsPerSecond = basePixelsPerSecond * zoom;
    const sortedSelections = [...selections].sort((a, b) => a.startTime - b.startTime);

    // Icon mapping
    const iconMapping: Record<BookmarkType, IconType> = {
        Manual: MdBookmark,
        Kill: FaGun,
        Assist: MdOutlineHandshake,
        Death: IoSkull
    };

    // Refreshes the thumbnail for a selection, updating loading states appropriately
    const refreshSelectionThumbnail = async (selection: Selection): Promise<void> => {
        updateSelection({...selection, isLoading: true});
        try {
            const contentFileName = `${contentFolder}/${video.type.toLowerCase()}s/${video.fileName}.mp4`;
            const thumbnailUrl = await fetchThumbnailAtTime(contentFileName, selection.startTime);
            updateSelection({...selection, thumbnailDataUrl: thumbnailUrl, isLoading: false});
        } catch {
            updateSelection({...selection, isLoading: false});
        }
    };

    // Initialize video metadata and setup keyboard controls
    useEffect(() => {
        const vid = videoRef.current;
        if (!vid) return;
        
        const onLoadedMetadata = () => {
            setDuration(vid.duration);
            setZoom(1);
        };

        const onPlay = () => setIsPlaying(true);
        const onPause = () => setIsPlaying(false);
        
        vid.addEventListener("loadedmetadata", onLoadedMetadata);
        vid.addEventListener("play", onPlay);
        vid.addEventListener("pause", onPause);
        
        const handleKeyDown = (e: KeyboardEvent) => {
            const target = e.target as HTMLElement;
            if (e.code === "Space" && target.tagName !== 'INPUT' && target.tagName !== 'TEXTAREA') {
                e.preventDefault();
                handlePlayPause();
            }
        };
        
        window.addEventListener("keydown", handleKeyDown);
        
        return () => {
            vid.removeEventListener("loadedmetadata", onLoadedMetadata);
            vid.removeEventListener("play", onPlay);
            vid.removeEventListener("pause", onPause);
            window.removeEventListener("keydown", handleKeyDown);
        };
    }, []);

    // Handle video playback time updates using requestAnimationFrame for smooth updates
    useEffect(() => {
        const vid = videoRef.current;
        if (!vid) return;
        let rafId = 0;
        const updateCurrentTime = () => {
            setCurrentTime(vid.currentTime);
            if (!vid.paused && !vid.ended) {
                rafId = requestAnimationFrame(updateCurrentTime);
            }
        };
        const onPlay = () => {
            rafId = requestAnimationFrame(updateCurrentTime);
        };
        const onPause = () => {
            cancelAnimationFrame(rafId);
        };
        vid.addEventListener("play", onPlay);
        vid.addEventListener("pause", onPause);
        if (!vid.paused) onPlay();
        return () => {
            vid.removeEventListener("play", onPlay);
            vid.removeEventListener("pause", onPause);
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
        window.addEventListener("resize", handleResize);
        return () => {
            window.removeEventListener("resize", handleResize);
        };
    }, []);

    // Handle timeline zooming with mouse wheel
    useEffect(() => {
        const container = scrollContainerRef.current;
        if (!container) return;
        const handleWheel = (e: WheelEvent) => {
            e.preventDefault();
            if (duration === 0) return;
            const zoomFactor = e.deltaY < 0 ? 1.1 : 0.9;
            const newZoom = Math.min(Math.max(zoom * zoomFactor, 1), 10);
            const rect = container.getBoundingClientRect();
            const cursorX = e.clientX - rect.left + container.scrollLeft;
            const basePixelsPerSecond = duration > 0 ? containerWidth / duration : 0;
            const oldPixelsPerSecond = basePixelsPerSecond * zoom;
            const timeAtCursor = cursorX / oldPixelsPerSecond;
            setZoom(newZoom);
            const newPPS = basePixelsPerSecond * newZoom;
            const newScrollLeft = timeAtCursor * newPPS - (e.clientX - rect.left);
            container.scrollLeft = newScrollLeft;
        };
        const wheelEventOptions: AddEventListenerOptions = {passive: false};
        container.addEventListener("wheel", handleWheel, wheelEventOptions);
        return () => {
            container.removeEventListener("wheel", handleWheel, wheelEventOptions);
        };
    }, [zoom, duration, containerWidth]);

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
            const newTime = videoRef.current.currentTime + seconds;
            videoRef.current.currentTime = Math.max(0, Math.min(newTime, videoRef.current.duration));
        }
    };

    // Toggle video play/pause state
    const togglePlayPause = () => {
        handlePlayPause();
    };

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

    // Format time in seconds to "MM:SS" display format
    const formatTime = (time: number) => {
        const minutes = Math.floor(time / 60);
        const seconds = Math.floor(time % 60).toString().padStart(2, "0");
        return `${minutes}:${seconds}`;
    };

    // Generate major and minor tick marks for the timeline based on zoom level
    const generateTicks = () => {
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
        return {majorTicks, minorTicks};
    };

    const {majorTicks, minorTicks} = generateTicks();

    // Add a new selection at the current video position
    const handleAddSelection = async () => {
        if (!videoRef.current) return;
        const start = currentTime;
        const end = Math.min(currentTime + 10, duration);
        const newSelection: Selection = {
            id: Date.now(),
            type: video.type,
            startTime: start,
            endTime: end,
            thumbnailDataUrl: undefined,
            isLoading: true,
            fileName: video.fileName,
            game: video.game,
        };
        addSelection(newSelection);
        try {
            const contentFileName = `${contentFolder}/${video.type.toLowerCase()}s/${video.fileName}.mp4`;
            const thumbnailUrl = await fetchThumbnailAtTime(contentFileName, start);
            updateSelection({...newSelection, thumbnailDataUrl: thumbnailUrl, isLoading: false});
        } catch {
            updateSelection({...newSelection, isLoading: false});
        }
    };

    // Create a clip from current selections
    const handleCreateClip = () => {
        const params = {
            Selections: selections.map((s) => ({
                type: s.type,
                fileName: s.fileName,
                game: s.game,
                startTime: s.startTime,
                endTime: s.endTime,
            }))
        };
        sendMessageToBackend("CreateClip", params);
    };

    // Handle selection drag and drop operations
    const handleSelectionDragStart = (e: React.MouseEvent<HTMLDivElement>, id: number) => {
        e.stopPropagation();
        if (!scrollContainerRef.current) return;
        const rect = scrollContainerRef.current.getBoundingClientRect();
        const dragPos = e.clientX - rect.left + scrollContainerRef.current.scrollLeft;
        const cursorTime = dragPos / pixelsPerSecond;
        const sel = selections.find((s) => s.id === id);
        if (sel) {
            setDragState({id, offset: cursorTime - sel.startTime});
            setIsInteracting(true);
        }
    };

    const handleSelectionDrag = (e: React.MouseEvent<HTMLDivElement>) => {
        if (dragState.id == null || !scrollContainerRef.current) return;
        const rect = scrollContainerRef.current.getBoundingClientRect();
        const dragPos = e.clientX - rect.left + scrollContainerRef.current.scrollLeft;
        const cursorTime = dragPos / pixelsPerSecond;

        const sel = selections.find((s) => s.id === dragState.id);
        if (sel) {
            const segLength = sel.endTime - sel.startTime;
            let newStart = cursorTime - dragState.offset;
            newStart = Math.max(0, Math.min(newStart, duration - segLength));
            const updatedSelection = {...sel, startTime: newStart, endTime: newStart + segLength};
            updateSelection(updatedSelection);
            latestDraggedSelectionRef.current = updatedSelection;
        }
    };

    const handleSelectionDragEnd = async () => {
        const draggedId = dragState.id;
        setDragState({id: null, offset: 0});
        setTimeout(() => setIsInteracting(false), 0);
        if (draggedId != null && latestDraggedSelectionRef.current) {
            await refreshSelectionThumbnail(latestDraggedSelectionRef.current);
            latestDraggedSelectionRef.current = null;
        }
    };

    // Handle global mouse up events for drag operations
    useEffect(() => {
        const handleGlobalMouseUp = async () => {
            handleMarkerDragEnd();
            if (dragState.id !== null) {
                await handleSelectionDragEnd();
            }
            if (resizingSelectionId !== null) {
                await handleSelectionResizeEnd();
            }

        };
        window.addEventListener("mouseup", handleGlobalMouseUp);
        return () => {
            window.removeEventListener("mouseup", handleGlobalMouseUp);
        };
    }, [dragState.id, resizingSelectionId]);

    // Handle selection resize operations
    const handleResizeStart = (
        e: React.MouseEvent<HTMLDivElement>,
        id: number,
        direction: "start" | "end"
    ) => {
        e.stopPropagation();
        setResizingSelectionId(id);
        setResizeDirection(direction);
        setIsInteracting(true);
    };

    const handleSelectionResize = (e: React.MouseEvent<HTMLDivElement>) => {
        if (!scrollContainerRef.current || resizingSelectionId == null || !resizeDirection) return;
        const rect = scrollContainerRef.current.getBoundingClientRect();
        const pos = e.clientX - rect.left + scrollContainerRef.current.scrollLeft;
        const t = pos / pixelsPerSecond;
        const sel = selections.find((s) => s.id === resizingSelectionId);
        if (!sel) return;

        let updatedSelection;
        if (resizeDirection === "start") {
            const newStart = Math.max(0, Math.min(t, sel.endTime - 0.1));
            updatedSelection = {...sel, startTime: newStart};
        } else {
            const newEnd = Math.min(duration, Math.max(t, sel.startTime + 0.1));
            updatedSelection = {...sel, endTime: newEnd};
        }
        latestDraggedSelectionRef.current = updatedSelection;
        updateSelection(updatedSelection);
    };

    const handleSelectionResizeEnd = async () => {
        setResizingSelectionId(null);
        setResizeDirection(null);
        if (latestDraggedSelectionRef.current) {
            await refreshSelectionThumbnail(latestDraggedSelectionRef.current);
            latestDraggedSelectionRef.current = null;
        }
    };

    // Handle right-click to remove selection
    const handleSelectionContextMenu = (e: React.MouseEvent<HTMLDivElement>, id: number) => {
        e.preventDefault();
        removeSelection(id);
    };

    // Move selection card in the sidebar
    const moveCard = (dragIndex: number, hoverIndex: number) => {
        const newSelections = [...selections];
        const [removed] = newSelections.splice(dragIndex, 1);
        newSelections.splice(hoverIndex, 0, removed);
        updateSelectionsArray(newSelections);
    };

    // Get video source URL
    const getVideoPath = (): string => {
        const contentFileName = `${contentFolder}/${video.type.toLowerCase()}s/${video.fileName}.mp4`;
        return `http://localhost:2222/api/content?input=${encodeURIComponent(contentFileName)}&type=${video.type.toLowerCase()}`;
    };

    // Handle video upload operation
    const handleUpload = () => {
        openModal(
            <UploadModal
                video={video}
                onClose={closeModal}
                onUpload={(title, visibility) => {
                    const parameters = {
                        FilePath: video.filePath,
                        JWT: session?.access_token,
                        Game: video.game,
                        Title: title,
                        Description: "", // TODO: implement description
                        Visibility: visibility // TODO: implement description
                    };

                    sendMessageToBackend('UploadContent', parameters);
                }}
            />
        );
    };

    // Group overlapping bookmarks for timeline display
    const groupOverlappingBookmarks = (bookmarks: any[], pixelsPerSecond: number) => {
        if (!bookmarks?.length) return [];

        // If at max zoom (10), return each bookmark as its own group
        if (zoom >= 10) {
            return bookmarks.map(bookmark => [bookmark]);
        }

        const OVERLAP_THRESHOLD = 20; // pixels
        const groups: any[] = [];
        let currentGroup: any[] = [];

        const sortedBookmarks = [...bookmarks].sort((a, b) =>
            timeStringToSeconds(a.time) - timeStringToSeconds(b.time)
        );

        sortedBookmarks.forEach((bookmark, index) => {
            const currentTime = timeStringToSeconds(bookmark.time);
            const currentPos = currentTime * pixelsPerSecond;

            if (index === 0) {
                currentGroup = [bookmark];
            } else {
                const prevTime = timeStringToSeconds(sortedBookmarks[index - 1].time);
                const prevPos = prevTime * pixelsPerSecond;

                if (Math.abs(currentPos - prevPos) < OVERLAP_THRESHOLD) {
                    currentGroup.push(bookmark);
                } else {
                    if (currentGroup.length > 0) {
                        groups.push([...currentGroup]);
                    }
                    currentGroup = [bookmark];
                }
            }

            if (index === sortedBookmarks.length - 1 && currentGroup.length > 0) {
                groups.push(currentGroup);
            }
        });

        return groups;
    };

    const [selectedBookmarkTypes, setSelectedBookmarkTypes] = useState<Set<BookmarkType>>(new Set(Object.values(BookmarkType)));

    const availableBookmarkTypes = useMemo(() => {
        const types = new Set<BookmarkType>();
        video.bookmarks.forEach(bookmark => types.add(bookmark.type));
        return Array.from(types);
    }, [video.bookmarks]);

    const filteredBookmarks = useMemo(() => {
        return video.bookmarks.filter(bookmark => selectedBookmarkTypes.has(bookmark.type));
    }, [video.bookmarks, selectedBookmarkTypes]);

    const toggleBookmarkType = (type: BookmarkType) => {
        setSelectedBookmarkTypes(prev => {
            const newSet = new Set(prev);
            if (newSet.has(type)) {
                newSet.delete(type);
            } else {
                newSet.add(type);
            }
            return newSet;
        });
    };

    const handleAddBookmark = () => {
        console.log("Add bookmark at current time");
    };

    const handleZoomChange = (increment: boolean) => {
        setZoom(prev => {
            const newZoom = increment ? prev * 1.1 : prev * 0.9;
            return Math.min(Math.max(newZoom, 1), 10);
        });
    };

    return (
        <DndProvider backend={HTML5Backend}>
            <div className="flex w-full h-full" ref={containerRef}>
                <div className="flex-1 p-4 w-full lg:w-3/4">
                    <div>
                        <video
                            autoPlay
                            className="relative rounded-lg w-full overflow-hidden aspect-video max-h-[calc(100vh-100px)] md:max-h-[calc(100vh-200px)]"
                            src={getVideoPath()}
                            ref={videoRef}
                            onClick={togglePlayPause}
                        />
                    </div>
                    <div
                        className="timeline-wrapper mt-2 relative overflow-x-scroll overflow-y-hidden w-full select-none"
                        ref={scrollContainerRef}
                        onMouseMove={(e) => {
                            handleSelectionDrag(e);
                            handleSelectionResize(e);
                            handleMarkerDrag(e);
                        }}
                    >
                        <div
                            className="ticks-container relative h-[42px]"
                            style={{
                                width: `${duration * pixelsPerSecond}px`,
                                minWidth: "100%",
                                overflow: "hidden"
                            }}
                        >
                            {groupOverlappingBookmarks(filteredBookmarks, pixelsPerSecond).map((group, groupIndex) => {
                                const isCluster = group.length > 1;
                                const referenceBookmark = group[0];
                                const timeInSeconds = timeStringToSeconds(referenceBookmark.time);
                                const leftPos = timeInSeconds * pixelsPerSecond;
                                const Icon = iconMapping[referenceBookmark.type as BookmarkType] || IoSkull;

                                return (
                                    <div
                                        key={`bookmark-${groupIndex}`}
                                        className="tooltip absolute bottom-0 transform -translate-x-1/2 cursor-pointer z-10 flex flex-col items-center text-[#25272e]"
                                        data-tip={isCluster
                                            ? `${group.length} bookmarks at ${referenceBookmark.time}`
                                            : `${referenceBookmark.type} - ${referenceBookmark.subtype} (${referenceBookmark.time})`
                                        }
                                        style={{
                                            left: `${leftPos}px`
                                        }}
                                    >
                                        <div
                                            className="bg-[#EFAF2B] w-[26px] h-[26px] rounded-full flex items-center justify-center mb-0"
                                        >
                                            {isCluster ? (
                                                <span className="text-sm font-bold">{group.length}</span>
                                            ) : (
                                                <>
                                                    <Icon size={16} />
                                                </>
                                            )}
                                        </div>
                                        <div
                                            className="w-[2px] h-[16px] bg-[#EFAF2B]"
                                        />
                                    </div>
                                );
                            })}
                            {minorTicks.map((tickTime, index) => {
                                if (tickTime >= duration) return null;
                                const leftPos = tickTime * pixelsPerSecond;
                                return (
                                    <div
                                        key={`minor-${index}`}
                                        className="absolute bottom-0 h-[6px] border-l border-white/20"
                                        style={{
                                            left: `${leftPos}px`
                                        }}
                                    />
                                );
                            })}
                            {majorTicks.map((tickTime, index) => {
                                if (tickTime > duration) return null;
                                const leftPos = tickTime * pixelsPerSecond;
                                return (
                                    <div
                                        key={`major-${index}`}
                                        className="absolute bottom-0 text-center text-white select-none -translate-x-1/2 whitespace-nowrap"
                                        style={{
                                            left: `${leftPos}px`
                                        }}
                                    >
                                        <span
                                            className="absolute bottom-full left-1/2 -translate-x-1/2 text-xs mb-[3px]"
                                        >
                                            {formatTime(tickTime)}
                                        </span>
                                        <div
                                            className="w-[2px] h-[10px] bg-white mx-auto"
                                        />
                                    </div>
                                );
                            })}
                        </div>
                        <div
                            className="timeline-container relative h-[50px] w-full bg-[#2a2a2a] rounded-[10px] overflow-hidden cursor-pointer"
                            style={{
                                width: `${duration * pixelsPerSecond}px`,
                                minWidth: "100%"
                            }}
                            onClick={handleTimelineClick}
                        >
                            {sortedSelections.map((sel) => {
                                const left = sel.startTime * pixelsPerSecond;
                                const width = (sel.endTime - sel.startTime) * pixelsPerSecond;
                                const isHovered = sel.id === hoveredSelectionId;
                                const hidden = sel.fileName !== video.fileName;
                                return (
                                    <div
                                        key={sel.id}
                                        className={`absolute top-0 left-0 h-full shadow cursor-move ${hidden ? "hidden" : ""} transition-colors overflow-hidden ${isHovered && !isHoveredByTimeline ? "bg-accent" : "bg-gray-700"}`}
                                        style={{
                                            left: `${left}px`,
                                            width: `${width}px`,
                                        }}
                                        onMouseEnter={() => {
                                            setHoveredSelectionId(sel.id);
                                            setIsHoveredByTimeline(true);
                                        }}
                                        onMouseLeave={() => {
                                            setHoveredSelectionId(null);
                                            setIsHoveredByTimeline(false);
                                        }}
                                        onMouseDown={(e) => handleSelectionDragStart(e, sel.id)}
                                        onContextMenu={(e) => handleSelectionContextMenu(e, sel.id)}
                                    >
                                        <div
                                            className="absolute top-0 left-0 right-0 text-center text-white font-semibold text-xs select-none pt-[2px] whitespace-nowrap overflow-hidden text-ellipsis"
                                        >
                                            {formatTime(sel.startTime)} - {formatTime(sel.endTime)}
                                        </div>
                                        <div
                                            className="absolute top-0 -left-[7px] w-[17px] h-full bg-transparent cursor-col-resize"
                                            onMouseDown={(e) => handleResizeStart(e, sel.id, "start")}
                                        />
                                        <div
                                            className="absolute top-0 -right-[7px] w-[17px] h-full bg-transparent cursor-col-resize"
                                            onMouseDown={(e) => handleResizeStart(e, sel.id, "end")}
                                        />
                                    </div>
                                );
                            })}
                            <div
                                className="marker absolute top-0 left-0 w-1 h-full bg-accent rounded-lg -translate-x-1/2 cursor-pointer"
                                style={{
                                    left: `${currentTime * pixelsPerSecond}px`,
                                }}
                                onMouseDown={handleMarkerDragStart}
                            />
                        </div>
                    </div>
                    {video.type === "Clip" && (
                        <div className="mt-2">
                            <button
                                className="btn btn-secondary"
                                onClick={handleUpload}
                                disabled={uploads[video.fileName + ".mp4"]?.status === 'uploading' || uploads[video.fileName + ".mp4"]?.status === 'processing'}
                            >
                                Upload
                            </button>
                        </div>
                    )}

                    {video.type === "Session" && (
                        <div className="flex items-center justify-between gap-4 py-1">
                            <div className="flex items-center gap-3">
                                <div className="flex items-center gap-0 bg-base-300 rounded-lg">
                                    <button
                                        onClick={() => skipTime(-10)}
                                        className="btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent tooltip tooltip-secondary tooltip-bottom"
                                        data-tip="Rewind 10s"
                                    >
                                        <MdReplay10 className="w-6 h-6" />
                                    </button>
                                    <button
                                        onClick={handlePlayPause}
                                        className="btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent tooltip tooltip-secondary tooltip-bottom"
                                        data-tip={isPlaying ? "Pause" : "Play"}
                                    >
                                        {isPlaying ? <MdPause className="w-6 h-6" /> : <MdPlayArrow className="w-6 h-6" />}
                                    </button>
                                    <button
                                        onClick={() => skipTime(10)}
                                        className="btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent tooltip tooltip-secondary tooltip-bottom"
                                        data-tip="Forward 10s"
                                    >
                                        <MdForward10 className="w-6 h-6" />
                                    </button>
                                </div>
                                <button
                                    className="btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent tooltip tooltip-secondary tooltip-bottom" data-tip="Create Clip"
                                    disabled={state.isCreatingClip}
                                    onClick={handleCreateClip}
                                >
                                    <MdMovieCreation className="w-6 h-6" />
                                    {state.isCreatingClip && (
                                        <span className="loading loading-spinner loading-xs" />
                                    )}
                                </button>
                                <button className="btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent tooltip tooltip-secondary tooltip-bottom" data-tip="Add Segment" onClick={handleAddSelection}>
                                    <MdAddBox className="w-6 h-6" />
                                </button>
                            </div>

                            <div className="flex items-center gap-3">
                                <div className="flex items-center gap-2 bg-base-300 rounded-lg">
                                    <button
                                        onClick={handleAddBookmark}
                                        className="btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent tooltip tooltip-secondary tooltip-bottom" data-tip="Add Bookmark"
                                    >
                                        <MdBookmarkAdd className="w-6 h-6" />
                                    </button>
                                </div>
                                <div className="flex items-center gap-0 bg-base-300 px-0 rounded-lg h-10">
                                    {availableBookmarkTypes.map(type => (
                                        <button
                                            key={type}
                                            onClick={() => toggleBookmarkType(type)}
                                            className={`btn btn-sm btn-secondary border-none h-10 transition-colors ${
                                                selectedBookmarkTypes.has(type) 
                                                    ? 'text-accent' 
                                                    : 'text-gray-400'
                                            }`}
                                        >
                                            {React.createElement(iconMapping[type] || IoSkull, {
                                                className: "w-6 h-6"
                                            })}
                                        </button>
                                    ))}
                                </div>

                                <div className="flex items-center gap-2 bg-base-300 px-0 rounded-lg h-10">
                                    <button
                                        onClick={() => handleZoomChange(false)}
                                        className="btn btn-sm btn-secondary h-10 disabled:opacity-100"
                                        disabled={zoom <= 1}
                                    >
                                        <IoRemove className="w-4 h-4" />
                                    </button>
                                    <span className="text-sm font-medium w-12 text-center text-gray-300">
                                        {Math.round(zoom * 100 / 5) * 5}%
                                    </span>
                                    <button
                                        onClick={() => handleZoomChange(true)}
                                        className="btn btn-sm btn-secondary h-10"
                                        disabled={zoom >= 10}
                                    >
                                        <IoAdd className="w-4 h-4" />
                                    </button>
                                </div>
                            </div>
                        </div>
                    )}
                </div>
                {video.type === "Session" && (
                    <div className="bg-base-300 text-neutral-content w-52 2xl:w-72 flex flex-col h-full pl-4 pr-1 pt-4">
                        <div className="overflow-y-scroll flex-1  mt-1 p-1">
                            {selections.map((sel, index) => (
                                <SelectionCard
                                    key={sel.id}
                                    selection={sel}
                                    index={index}
                                    moveCard={moveCard}
                                    formatTime={formatTime}
                                    isHovered={hoveredSelectionId === sel.id}
                                    setHoveredSelectionId={setHoveredSelectionId}
                                    removeSelection={removeSelection}
                                />
                            ))}
                        </div>
                        <div className="flex items-center gap-0 bg-base-300 px-0 rounded-lg h-10 my-2 mr-3 tooltip" data-tip="Remove All">
                            <button
                                className="btn btn-sm btn-neutral h-10 text-gray-400 hover:text-accent w-full py-0"
                                onClick={clearAllSelections}
                                disabled={selections.length === 0}
                            >
                                <MdCleaningServices className="w-6 h-6" />
                            </button>
                        </div>
                    </div>
                )}
            </div>
        </DndProvider>
    );
}
