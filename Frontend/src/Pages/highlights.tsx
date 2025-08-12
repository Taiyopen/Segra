import {useSettings} from '../Context/SettingsContext';
import ContentCard from '../Components/ContentCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import { useScroll } from '../Context/ScrollContext';
import { useEffect, useRef, useState } from 'react';
import {Content} from "../Models/types";
import { HiOutlineSparkles } from 'react-icons/hi';
import { useAiHighlights } from '../Context/AiHighlightsContext';
import AiContentCard from '../Components/AiContentCard';
import ContentFilters from '../Components/ContentFilters';

export default function Highlights() {
  const {state} = useSettings();
  const {setSelectedVideo} = useSelectedVideo();
  const {scrollPositions, setScrollPosition} = useScroll();
  const containerRef = useRef<HTMLDivElement>(null);
  const isSettingScroll = useRef(false);
  const {aiProgress} = useAiHighlights();

  const aiProgressValues = Object.values(aiProgress);
  const hasAiProgress = aiProgressValues.length > 0;
  
  // State for filtered items
  const highlightItems = state.content.filter((video) => video.type === 'Highlight');
  const [filteredItems, setFilteredItems] = useState<Content[]>(highlightItems);

  // Update filtered items when content changes
  useEffect(() => {
    const newHighlightItems = state.content.filter((video) => video.type === 'Highlight');
    setFilteredItems(newHighlightItems);
  }, [state.content]);

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  useEffect(() => {
    if (containerRef.current && scrollPositions.highlights > 0) {
      isSettingScroll.current = true;
      containerRef.current.scrollTop = scrollPositions.highlights;
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
        if (currentPos !== undefined && currentPos !== scrollPositions.highlights) {
          setScrollPosition('highlights', currentPos);
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
        <h1 className="text-3xl font-bold">Highlights</h1>
        <ContentFilters 
          items={highlightItems} 
          onFilteredItemsChange={setFilteredItems} 
          sectionId="highlights" 
        />
      </div>
      

      {(highlightItems.length > 0 || hasAiProgress) ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {aiProgressValues.map((progress) => (
            <AiContentCard key={progress.id} progress={progress} />
          ))}
          {filteredItems.map((video, index) => (
            <ContentCard
              key={index}
              content={video}
              onClick={handlePlay}
              type={'Highlight'}
            />
          ))}
        </div>
      ) : (
        <div className="flex flex-col items-center justify-center py-16 text-base-content/70">
          <HiOutlineSparkles className="w-16 h-16 mb-4" />
          <p className="text-lg font-medium">Segra AI Highlights</p>
          <p className="mt-1">Your AI generated highlights will appear here</p>
        </div>
      )}
    </div>
  );
}
