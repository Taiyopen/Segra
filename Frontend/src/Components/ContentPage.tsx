import { useSettings } from '../Context/SettingsContext';
import ContentCard from './ContentCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import { Content, ContentType } from "../Models/types";
import { useScroll } from '../Context/ScrollContext';
import { useEffect, useRef, useState, useMemo } from 'react';
import { IconType } from 'react-icons';
import ContentFilters, { SortOption } from './ContentFilters';
import AiContentCard from './AiContentCard';

interface ContentPageProps {
  contentType: ContentType;
  sectionId: string;
  title: string;
  Icon: IconType;
  progressItems?: Record<string, any>; // For AI highlights or clipping progress
  isProgressVisible?: boolean;
  progressCardElement?: React.ReactNode; // Direct element instead of component
}

export default function ContentPage({
  contentType,
  sectionId,
  title,
  Icon,
  progressItems = {},
  isProgressVisible = false,
  progressCardElement
}: ContentPageProps) {
  const { state } = useSettings();
  const { setSelectedVideo } = useSelectedVideo();
  const { scrollPositions, setScrollPosition } = useScroll();
  const containerRef = useRef<HTMLDivElement>(null);
  const isSettingScroll = useRef(false);

  // Get content items of the specified type
  const contentItems = state.content.filter((video) => video.type === contentType);

  // Filter and sort state
  const [selectedGames, setSelectedGames] = useState<string[]>(() => {
    try {
      const saved = localStorage.getItem(`${sectionId}-filters`);
      return saved ? JSON.parse(saved) : [];
    } catch {
      return [];
    }
  });

  const [sortOption, setSortOption] = useState<SortOption>(() => {
    try {
      const saved = localStorage.getItem(`${sectionId}-sort`);
      return saved ? JSON.parse(saved) : "newest";
    } catch {
      return "newest";
    }
  });

  // Get unique games for filter dropdown
  const uniqueGames = useMemo(() => {
    const games = contentItems.map(item => item.game);
    return [...new Set(games)].sort();
  }, [contentItems]);

  // Apply filters and sorting
  const filteredItems = useMemo(() => {
    let filtered = [...contentItems];

    // Apply game filter
    if (selectedGames.length > 0) {
      filtered = filtered.filter(item => selectedGames.includes(item.game));
    }

    // Apply sorting
    filtered.sort((a, b) => {
      switch (sortOption) {
        case "newest":
          return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
        case "oldest":
          return new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime();
        case "size":
          return (b.fileSizeKb ?? 0) - (a.fileSizeKb ?? 0);
        case "duration": {
          const toSecs = (dur: string) =>
            dur.split(":").reduce((acc, t) => 60 * acc + (parseInt(t, 10) || 0), 0);
          return toSecs(b.duration) - toSecs(a.duration);
        }
        case "game": {
          const byGame = a.game.localeCompare(b.game);
          return byGame !== 0 ? byGame : new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
        }
        default:
          return 0;
      }
    });

    return filtered;
  }, [contentItems, selectedGames, sortOption]);

  // Handle filter changes
  const handleGameFilterChange = (games: string[]) => {
    setSelectedGames(games);
    localStorage.setItem(`${sectionId}-filters`, JSON.stringify(games));
  };

  // Handle sort changes
  const handleSortChange = (option: SortOption) => {
    setSortOption(option);
    localStorage.setItem(`${sectionId}-sort`, JSON.stringify(option));
  };

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  // Restore scroll position on mount
  useEffect(() => {
    // Type-safe access to scroll positions
    const position = sectionId === 'clips' ? scrollPositions.clips :
      sectionId === 'highlights' ? scrollPositions.highlights :
        sectionId === 'replayBuffer' ? scrollPositions.replayBuffer :
          sectionId === 'sessions' ? scrollPositions.sessions : 0;

    if (containerRef.current && position > 0) {
      isSettingScroll.current = true;
      containerRef.current.scrollTop = position;
      setTimeout(() => {
        isSettingScroll.current = false;
      }, 100);
    }
  }, []); // Only run on mount

  const scrollTimeout = useRef<NodeJS.Timeout>();

  // Save scroll position when scrolling
  const handleScroll = () => {
    if (containerRef.current && !isSettingScroll.current) {
      if (scrollTimeout.current) {
        clearTimeout(scrollTimeout.current);
      }

      scrollTimeout.current = setTimeout(() => {
        const currentPos = containerRef.current?.scrollTop;
        if (currentPos === undefined) return;

        // Type-safe scroll position update
        const pageKey = sectionId === 'clips' ? 'clips' :
          sectionId === 'highlights' ? 'highlights' :
            sectionId === 'replayBuffer' ? 'replayBuffer' :
              sectionId === 'sessions' ? 'sessions' : null;

        if (pageKey) {
          setScrollPosition(pageKey, currentPos);
        }
      }, 500);
    }
  };

  // Check if we have progress items
  const progressValues = Object.values(progressItems);
  const hasProgress = progressValues.length > 0;

  return (
    <div
      ref={containerRef}
      className="p-5 space-y-6 overflow-y-auto h-full bg-base-200 overflow-x-hidden"
      onScroll={handleScroll}>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-3xl font-bold">{title}</h1>
        <ContentFilters
          uniqueGames={uniqueGames}
          onGameFilterChange={handleGameFilterChange}
          onSortChange={handleSortChange}
          sectionId={sectionId}
          selectedGames={selectedGames}
          sortOption={sortOption}
        />
      </div>

      {(contentItems.length > 0 || hasProgress) ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {/* Show progress card if applicable */}
          {isProgressVisible && progressCardElement}

          {/* Show content cards */}
          {filteredItems.map((video, index) => (
            <ContentCard
              key={index}
              content={video}
              onClick={() => handlePlay(video)}
              type={contentType}
            />
          ))}

          {/* Show AI content cards for highlights if applicable */}
          {contentType === 'Highlight' && hasProgress &&
            progressValues.map((progress: any, index) => (
              <AiContentCard
                key={`ai-${index}`}
                progress={progress}
              />
            ))
          }
        </div>
      ) : (
        <div className="flex flex-col items-center justify-center h-64 text-gray-500">
          <Icon className="text-6xl mb-4" />
          <p className="text-xl">No {title.toLowerCase()} found</p>
        </div>
      )}
    </div>
  );
}
