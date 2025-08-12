import { useSettings } from '../Context/SettingsContext';
import ContentCard from '../Components/ContentCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import { useScroll } from '../Context/ScrollContext';
import { useEffect, useRef, useState } from 'react';
import { Content } from "../Models/types";
import { MdReplay30 } from 'react-icons/md';
import ContentFilters from '../Components/ContentFilters';

export default function ReplayBuffer() {
  const { state } = useSettings();
  const { setSelectedVideo } = useSelectedVideo();
  const { scrollPositions, setScrollPosition } = useScroll();
  const containerRef = useRef<HTMLDivElement>(null);
  const isSettingScroll = useRef(false);
  
  // State for filtered items
  const bufferItems = state.content.filter((video) => video.type === 'Buffer');
  const [filteredItems, setFilteredItems] = useState<Content[]>(bufferItems);

  // Update filtered items when content changes
  useEffect(() => {
    const newBufferItems = state.content.filter((video) => video.type === 'Buffer');
    setFilteredItems(newBufferItems);
  }, [state.content]);

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  useEffect(() => {
    if (containerRef.current && scrollPositions.replayBuffer > 0) {
      isSettingScroll.current = true;
      containerRef.current.scrollTop = scrollPositions.replayBuffer;
      setTimeout(() => {
        isSettingScroll.current = false;
      }, 100);
    }
  }, []); // Only run on mount

  const scrollTimeout = useRef<NodeJS.Timeout>();

  const handleScroll = () => {
    if (containerRef.current && !isSettingScroll.current) {
      if (scrollTimeout.current) {
        clearTimeout(scrollTimeout.current);
      }

      scrollTimeout.current = setTimeout(() => {
        const currentPos = containerRef.current?.scrollTop;
        if (currentPos !== undefined && currentPos !== scrollPositions.replayBuffer) {
          setScrollPosition('replayBuffer', currentPos);
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
        <h1 className="text-3xl font-bold">Replay Buffer</h1>
        <ContentFilters 
          items={bufferItems} 
          onFilteredItemsChange={setFilteredItems} 
          sectionId="replayBuffer" 
        />
      </div>
      

      {bufferItems.length > 0 ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {filteredItems.map((video, index) => (
            <ContentCard
              key={index}
              content={video}
              onClick={handlePlay}
              type={'Buffer'}
            />
          ))}
        </div>
      ) : (
        <div className="flex flex-col items-center justify-center py-16 text-base-content/70">
          <MdReplay30 className="w-16 h-16 mb-4" />
          <p className="text-lg font-medium">No replay buffer recordings available</p>
          <p className="mt-1">Enable replay buffer to save your gameplay moments</p>
        </div>
      )}
    </div>
  );
}
