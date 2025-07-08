import {useSettings} from '../Context/SettingsContext';
import ContentCard from '../Components/ContentCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import { useScroll } from '../Context/ScrollContext';
import { useEffect, useRef } from 'react';
import {Content} from "../Models/types";
import { HiOutlineSparkles } from 'react-icons/hi';
import { useAiHighlights } from '../Context/AiHighlightsContext';
import AiContentCard from '../Components/AiContentCard';

export default function Highlights() {
  const {state} = useSettings();
  const {setSelectedVideo} = useSelectedVideo();
  const {scrollPositions, setScrollPosition} = useScroll();
  const containerRef = useRef<HTMLDivElement>(null);
  const isSettingScroll = useRef(false);
  const {aiProgress} = useAiHighlights();

  const aiProgressValues = Object.values(aiProgress);
  const hasAiProgress = aiProgressValues.length > 0;

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  console.log(aiProgressValues);

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
      className="p-5 space-y-6 overflow-y-auto h-full bg-base-200"
      onScroll={handleScroll}>
      <h1 className="text-3xl font-bold mb-4">Highlights</h1>
      {(state.content.filter((video) => video.type === 'Highlight').length > 0 || hasAiProgress) ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {aiProgressValues.map((progress) => (
            <AiContentCard key={progress.id} progress={progress} />
          ))}
          {state.content
            .filter((video) => video.type === 'Highlight')
            .map((video, index) => (
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
