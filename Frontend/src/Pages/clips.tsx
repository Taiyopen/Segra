import { useSettings } from '../Context/SettingsContext';
import ContentCard from '../Components/ContentCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import { Content } from "../Models/types";
import { useScroll } from '../Context/ScrollContext';
import { useEffect, useRef, useState } from 'react';
import { MdOutlineContentCut } from 'react-icons/md';
import { useClipping } from '../Context/ClippingContext';
import ContentFilters from '../Components/ContentFilters';

export default function Clips() {
  const { state } = useSettings();
  const { setSelectedVideo } = useSelectedVideo();
  const { scrollPositions, setScrollPosition } = useScroll();
  const containerRef = useRef<HTMLDivElement>(null);
  const isSettingScroll = useRef(false);
  const {clippingProgress} = useClipping();
  
  // State for filtered items
  const clipItems = state.content.filter((video) => video.type === 'Clip');
  const [filteredItems, setFilteredItems] = useState<Content[]>(clipItems);

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  useEffect(() => {
    if (containerRef.current && scrollPositions.clips > 0) {
      isSettingScroll.current = true;
      containerRef.current.scrollTop = scrollPositions.clips;
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
        if (currentPos !== undefined && currentPos !== scrollPositions.clips) {
          setScrollPosition('clips', currentPos);
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
        <h1 className="text-3xl font-bold">Clips</h1>
        <ContentFilters 
          items={clipItems} 
          onFilteredItemsChange={setFilteredItems} 
          sectionId="clips" 
        />
      </div>
      

      {(clipItems.length > 0 || Object.keys(clippingProgress).length > 0) ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {Object.values(clippingProgress).map((progress) => (
            <ContentCard key={progress.id} type="Clip" isLoading />
          ))}
          {filteredItems.map((video, index) => (
            <ContentCard
              key={index}
              content={video}
              onClick={handlePlay}
              type={'Clip'}
            />
          ))}
        </div>
      ) : (
        <div className="flex flex-col items-center justify-center py-16 text-base-content/70">
          <MdOutlineContentCut className="w-16 h-16 mb-4" />
          <p className="text-lg font-medium">No clips available</p>
          <p className="mt-1">Create clips from your recordings to see them here</p>
        </div>
      )}
    </div>
  );
}
