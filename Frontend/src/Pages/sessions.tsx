import {useSettings} from '../Context/SettingsContext';
import ContentCard from '../Components/ContentCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import { Content } from "../Models/types";
import { MdOutlinePlayCircleOutline } from 'react-icons/md';
import { useScroll } from '../Context/ScrollContext';
import { useEffect, useRef, useState } from 'react';
import ContentFilters from '../Components/ContentFilters';

export default function Sessions() {
  const {state} = useSettings();
  const {recording} = state;
  const {setSelectedVideo} = useSelectedVideo();
  const { scrollPositions, setScrollPosition } = useScroll();
  const containerRef = useRef<HTMLDivElement>(null);
  const isSettingScroll = useRef(false);
  
  // State for filtered items
  const sessionItems = state.content.filter((video) => video.type === 'Session');
  const [filteredItems, setFilteredItems] = useState<Content[]>(sessionItems);

  // Update filtered items when content changes
  useEffect(() => {
    const newSessionItems = state.content.filter((video) => video.type === 'Session');
    setFilteredItems(newSessionItems);
  }, [state.content]);

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  useEffect(() => {
    if (containerRef.current && scrollPositions.sessions > 0) {
      isSettingScroll.current = true;
      containerRef.current.scrollTop = scrollPositions.sessions;
      // Reset flag after scroll is complete
      setTimeout(() => {
        isSettingScroll.current = false;
      }, 100);
    }
  }, []); // Only run on mount

  const scrollTimeout = useRef<NodeJS.Timeout>();

  const handleScroll = () => {
    if (containerRef.current && !isSettingScroll.current) {
      // Clear any existing timeout
      if (scrollTimeout.current) {
        clearTimeout(scrollTimeout.current);
      }

      // Set new timeout
      scrollTimeout.current = setTimeout(() => {
        const currentPos = containerRef.current?.scrollTop;
        if (currentPos !== undefined && currentPos !== scrollPositions.sessions) {
          setScrollPosition('sessions', currentPos);
        }
      }, 500);
    }
  };

  return (
    <div 
      ref={containerRef} 
      className="p-5 space-y-6 overflow-y-auto h-full bg-base-200 overflow-x-hidden"
      onScroll={handleScroll}>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-3xl font-bold">Full Sessions</h1>
        <ContentFilters 
          items={sessionItems} 
          onFilteredItemsChange={setFilteredItems} 
          sectionId="sessions" 
        />
      </div>
      

      {sessionItems.length > 0 ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {recording && recording.endTime !== null && <ContentCard key={-1} type="Session" isLoading />}
          {filteredItems.map((video, index) => (
            <ContentCard
              key={index}
              content={video}
              onClick={handlePlay}
              type={'Session'}
            />
          ))}
        </div>
      ) : (
        <div className="flex flex-col items-center justify-center py-16 text-base-content/70">
          <MdOutlinePlayCircleOutline className="w-16 h-16 mb-4" />
          <p className="text-lg font-medium">No full sessions available</p>
          <p className="mt-1">Start recording to see your full sessions here</p>
        </div>
      )}
    </div>
  );
}
