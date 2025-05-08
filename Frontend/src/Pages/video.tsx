import React, { useRef, useState, useEffect, useMemo } from "react";
import { Content, BookmarkType, Selection, Bookmark } from "../Models/types";
import { sendMessageToBackend } from "../Utils/MessageUtils";
import { useSettings } from "../Context/SettingsContext";
import { DndProvider } from "react-dnd";
import { HTML5Backend } from "react-dnd-html5-backend";
import { useAuth } from "../Hooks/useAuth.tsx";
import { useSelections } from "../Context/SelectionsContext";
import { useUploads } from "../Context/UploadContext";
import { useModal } from "../Context/ModalContext";
import UploadModal from '../Components/UploadModal';
import { IconType } from "react-icons";
import { FaGun, FaTrashCan } from "react-icons/fa6";
import { MdAddBox, MdBookmark, MdBookmarkAdd, MdCleaningServices, MdMovieCreation, MdOutlineHandshake, MdPause, MdPlayArrow, MdReplay10, MdForward10, MdBookmarks, MdOutlineFileUpload, MdVolumeUp, MdVolumeOff, MdVolumeMute, MdVolumeDown } from "react-icons/md";
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
    const { contentFolder } = useSettings();
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
    const [isVideoHovered, setIsVideoHovered] = useState(false);

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
        
        // Apply saved volume and muted state on load
        vid.volume = volume;
        vid.muted = isMuted;
        
        const onLoadedMetadata = () => {
            setDuration(vid.duration);
            setZoom(1);
        };

        const onPlay = () => setIsPlaying(true);
        const onPause = () => setIsPlaying(false);
        const onVolumeChange = () => {
            if (vid) {
                setVolume(vid.volume);
                setIsMuted(vid.muted);
                
                // Save to localStorage when volume changes
                localStorage.setItem('segra-volume', vid.volume.toString());
                localStorage.setItem('segra-muted', vid.muted.toString());
            }
        };
        
        vid.addEventListener("loadedmetadata", onLoadedMetadata);
        vid.addEventListener("play", onPlay);
        vid.addEventListener("pause", onPause);
        vid.addEventListener("volumechange", onVolumeChange);
        
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
            vid.removeEventListener("volumechange", onVolumeChange);
            window.removeEventListener("keydown", handleKeyDown);
        };
    }, [volume, isMuted]);

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

    // Create refs to track zoom state and position
    const wheelZoomRef = useRef(zoom);
    const initializedRef = useRef(false);
    const cursorPositionRef = useRef({ x: 0, time: 0 });
    
    // Update the wheel zoom ref when zoom changes from other sources (buttons)
    useEffect(() => {
        wheelZoomRef.current = zoom;
    }, [zoom]);
    
    // Initialize the timeline scroll position once when loaded
    useEffect(() => {
        if (scrollContainerRef.current && duration > 0 && !initializedRef.current) {
            initializedRef.current = true;
        }
    }, [duration, scrollContainerRef.current]);
    
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
            
            // Store cursor position for reference
            cursorPositionRef.current = {
                x: cursorX,
                time: timeAtCursor
            };
            
            // Calculate new zoom level
            const zoomFactor = e.deltaY < 0 ? 1.2 : 0.8;
            const newZoom = Math.min(Math.max(wheelZoomRef.current * zoomFactor, 1), 50);
            
            // Update zoom ref immediately
            wheelZoomRef.current = newZoom;
            
            // Calculate new scroll position based on cursor time point
            const newPixelsPerSecond = basePixelsPerSecond * newZoom;
            const newCursorPosition = timeAtCursor * newPixelsPerSecond;
            const newScrollLeft = newCursorPosition - cursorX;
            
            // Apply scroll position immediately
            requestAnimationFrame(() => {
                if (container) {
                    container.scrollLeft = newScrollLeft;
                    
                    // Double check the position after the frame renders
                    requestAnimationFrame(() => {
                        if (container) {
                            const currentScrollLeft = container.scrollLeft;
                            if (Math.abs(currentScrollLeft - newScrollLeft) > 5) {
                                container.scrollLeft = newScrollLeft;
                            }
                        }
                    });
                }
            });
            
            // Update React state
            setZoom(newZoom);
        };
        
        const wheelEventOptions: AddEventListenerOptions = { passive: false };
        container.addEventListener("wheel", handleWheel, wheelEventOptions);
        
        return () => {
            container.removeEventListener("wheel", handleWheel, wheelEventOptions);
        };
    }, [duration, containerWidth]); // Remove zoom from dependencies to prevent recreation

    const handleZoomChange = (increment: boolean) => {
        if (!scrollContainerRef.current) return;
        
        const container = scrollContainerRef.current;
        const scrollLeft = container.scrollLeft;
        
        // Calculate time at marker position (current time)
        const basePixelsPerSecond = containerWidth / duration;
        const oldPixelsPerSecond = basePixelsPerSecond * zoom;
        
        // Use current time as the focus point for zooming
        const markerTime = currentTime;
        const markerPosition = markerTime * oldPixelsPerSecond;
        
        // Calculate new zoom
        const newZoom = increment ? zoom * 1.5 : zoom * 0.5;
        const finalZoom = Math.min(Math.max(newZoom, 1), 50);
        
        // Update zoom state
        setZoom(finalZoom);
        
        // Calculate new scroll position to keep marker in view
        setTimeout(() => {
            if (container) {
                const newPixelsPerSecond = basePixelsPerSecond * finalZoom;
                const newMarkerPosition = markerTime * newPixelsPerSecond;
                
                // Calculate new scroll position to center on marker
                // Adjust scroll to position marker at same relative position
                const viewportWidth = container.clientWidth;
                const markerOffset = markerPosition - scrollLeft;
                const visibleRatio = markerOffset / viewportWidth;
                const newScrollPosition = newMarkerPosition - (visibleRatio * viewportWidth);
                
                container.scrollLeft = newScrollPosition;
            }
        }, 0);
    };

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
        const zoomRatio = zoom / 50 * 100;
        const selectionDuration = Math.max(0.1, (duration * 0.0019) * (100 / zoomRatio));
        const end = currentTime + selectionDuration;
        
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
        if (selections.length === 0) {
            setShowNoSegmentsIndicator(true);
            setTimeout(() => setShowNoSegmentsIndicator(false), 2000);
            return;
        }

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
        // Ensure video is paused before opening upload modal
        if (videoRef.current && !videoRef.current.paused) {
            videoRef.current.pause();
        }
        
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
            time: formattedTime
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
            Id: bookmarkId
        });
        
        // Add visual feedback for the user
        console.log(`Added bookmark at ${formattedTime}`);
    };

    const handleDeleteBookmark = (bookmarkId: number) => {
        // Find the bookmark in the video's bookmarks array
        const bookmarkIndex = video.bookmarks.findIndex(b => b.id === bookmarkId);
        
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
                Id: bookmarkId
            });
            
            console.log(`Deleted bookmark with ID ${bookmarkId}`);
        }
    };

    // Handle volume change
    const handleVolumeChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const newVolume = parseFloat(e.target.value);
        setVolume(newVolume);
        
        if (videoRef.current) {
            videoRef.current.volume = newVolume;
            
            // If we're adjusting volume from zero or to zero, update muted state
            if (newVolume === 0) {
                videoRef.current.muted = true;
                setIsMuted(true);
            } else if (isMuted) {
                videoRef.current.muted = false;
                setIsMuted(false);
            }
            
            // Save to localStorage
            localStorage.setItem('segra-volume', newVolume.toString());
            localStorage.setItem('segra-muted', videoRef.current.muted.toString());
        }
    };

    // Toggle mute state
    const toggleMute = () => {
        if (videoRef.current) {
            const newMutedState = !videoRef.current.muted;
            videoRef.current.muted = newMutedState;
            setIsMuted(newMutedState);
            
            // Save to localStorage
            localStorage.setItem('segra-muted', newMutedState.toString());
        }
    };

    return (
        <DndProvider backend={HTML5Backend}>
            <div className="flex w-full h-full" ref={containerRef}>
                <div className="flex-1 p-4 w-full lg:w-3/4">
                    <div className="relative">
                        <video
                            autoPlay
                            className="relative rounded-lg w-full overflow-hidden aspect-video max-h-[calc(100vh-100px)] md:max-h-[calc(100vh-200px)]"
                            src={getVideoPath()}
                            ref={videoRef}
                            onClick={togglePlayPause}
                            onMouseEnter={() => setIsVideoHovered(true)}
                            onMouseLeave={() => setIsVideoHovered(false)}
                        />
                        
                        {/* Volume control that appears on hover */}
                        <div 
                            className={`absolute bottom-4 right-4 bg-black/70 rounded-lg p-2 flex items-center gap-2 transition-opacity duration-300 ${isVideoHovered ? 'opacity-100' : 'opacity-0'}`}
                            onMouseEnter={() => setIsVideoHovered(true)}
                        >
                            <button 
                                onClick={toggleMute}
                                className="text-white hover:text-accent transition-colors"
                            >
                                {isMuted || volume === 0 ? (
                                    <MdVolumeOff className="w-6 h-6" />
                                ) : volume < 0.33 ? (
                                    <MdVolumeMute className="w-6 h-6" />
                                ) : volume < 0.67 ? (
                                    <MdVolumeDown className="w-6 h-6" />
                                ) : (
                                    <MdVolumeUp className="w-6 h-6" />
                                )}
                            </button>
                            
                            <div className="w-24 flex items-center">
                                <input
                                    type="range"
                                    min="0"
                                    max="1"
                                    step="0.01"
                                    value={volume}
                                    onChange={handleVolumeChange}
                                    className="w-full h-1 bg-gray-600 rounded-lg appearance-none cursor-pointer accent-accent"
                                />
                            </div>
                        </div>
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
                                            : `${referenceBookmark.type}${referenceBookmark.subtype ? ` - ${referenceBookmark.subtype}` : ''} (${referenceBookmark.time})`
                                        }
                                        style={{
                                            left: `${leftPos}px`
                                        }}
                                        onContextMenu={(e) => {
                                            e.preventDefault();
                                            handleDeleteBookmark(referenceBookmark.id);
                                        }}
                                    >
                                        <div
                                            className="bg-[#EFAF2B] w-[26px] h-[26px] rounded-full flex items-center justify-center mb-0"
                                        >
                                            {isCluster ? (
                                                <MdBookmarks size={16} />
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
                                            ref={(element) => {
                                                if (element) {
                                                    // If scrollWidth > clientWidth, content doesn't fit
                                                    element.style.visibility = 
                                                        element.scrollWidth <= element.clientWidth ? 'visible' : 'hidden';
                                                }
                                            }}
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
                    <div className="flex items-center justify-between gap-4 py-1">
                        <div className="flex items-center gap-3">
                            <div className="flex items-center gap-0 bg-base-300 rounded-lg">
                                <button
                                    onClick={() => skipTime(-10)}
                                    className="btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent"
                                >
                                    <MdReplay10 className="w-6 h-6" />
                                </button>
                                <button
                                    onClick={handlePlayPause}
                                    className="btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent"
                                    data-tip={isPlaying ? "Pause" : "Play"}
                                >
                                    {isPlaying ? <MdPause className="w-6 h-6" /> : <MdPlayArrow className="w-6 h-6" />}
                                </button>
                                <button
                                    onClick={() => skipTime(10)}
                                    className="btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent"
                                    data-tip="Forward 10s"
                                >
                                    <MdForward10 className="w-6 h-6" />
                                </button>
                            </div>
                            {(video.type === "Clip" || video.type === "Highlight") && (
                                <button
                                    className="btn btn-sm btn-secondary h-10 px-6 text-gray-400 hover:text-accent flex items-center"
                                    onClick={handleUpload}
                                    disabled={uploads[video.fileName + ".mp4"]?.status === 'uploading' || uploads[video.fileName + ".mp4"]?.status === 'processing'}
                                >
                                    <MdOutlineFileUpload className="w-6 h-6" />
                                    <span>Upload</span>
                                </button>
                            )}
                            {(video.type === "Session" || video.type === "Buffer") && (
                                <>
                                    <button
                                        className={`btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent flex items-center gap-1`}
                                        onClick={handleCreateClip}
                                    >
                                        <MdMovieCreation className="w-6 h-6" />
                                        <span>Create Clip</span>
                                    </button>
                                    <div className="indicator">
                                        <button className={`btn btn-sm btn-secondary h-10 text-gray-400 hover:text-accent flex items-center gap-1`} onClick={handleAddSelection}>
                                            {showNoSegmentsIndicator && <span className="indicator-item badge badge-sm badge-primary animate-pulse"></span>}
                                            <MdAddBox className="w-6 h-6" />
                                            <span>Add Segment</span>
                                        </button>
                                    </div>
                                </>
                            )}
                        </div>

                        <div className="flex items-center gap-3">
                            {(video.type === "Session" || video.type === "Buffer") && (
                                <>
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
                                                className={`btn btn-sm btn-secondary border-none h-10 transition-colors ${selectedBookmarkTypes.has(type)
                                                        ? 'text-accent'
                                                        : 'text-gray-400'}`}
                                            >
                                                {React.createElement(iconMapping[type] || IoSkull, {
                                                    className: "w-6 h-6"
                                                })}
                                            </button>
                                        ))}
                                    </div>
                                </>
                            )}

                            <div className="flex items-center gap-1 bg-base-300 px-0 rounded-lg h-10">
                                <button
                                    onClick={() => handleZoomChange(false)}
                                    className="btn btn-sm btn-secondary h-10 disabled:opacity-100"
                                    disabled={zoom <= 1}
                                >
                                    <IoRemove className="w-4 h-4" />
                                </button>
                                <span className="text-sm font-medium text-center text-gray-300">
                                    {Math.round(zoom * 100 / 5) * 5}%
                                </span>
                                <button
                                    onClick={() => handleZoomChange(true)}
                                    className="btn btn-sm btn-secondary h-10"
                                    disabled={zoom >= 50}
                                >
                                    <IoAdd className="w-4 h-4" />
                                </button>
                            </div>
                        </div>
                    </div>
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
                        <div className="flex items-center gap-0 bg-base-300 px-0 rounded-lg h-10 my-2 mr-3 tooltip">
                            <button
                                className="btn btn-sm btn-neutral h-10 text-gray-400 hover:text-accent w-full py-0"
                                onClick={clearAllSelections}
                                disabled={selections.length === 0}
                            >
                                <FaTrashCan className="w-5 h-5" />
                                <span>Remove</span>
                            </button>
                        </div>
                    </div>
                )}
            </div>
        </DndProvider>
    );
}
