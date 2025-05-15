import {useSettings} from '../Context/SettingsContext';
import ContentCard from '../Components/ContentCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import { Content } from "../Models/types";
import { MdOutlinePlayCircleOutline } from 'react-icons/md';
import { useScroll } from '../Context/ScrollContext';
import { useEffect, useRef } from 'react';

export default function Videos() {
  const {state} = useSettings();
  const {recording} = state;
  const {setSelectedVideo} = useSelectedVideo();
  const { scrollPositions, setScrollPosition } = useScroll();
  const containerRef = useRef<HTMLDivElement>(null);
  const isSettingScroll = useRef(false);

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  useEffect(() => {
    if (containerRef.current && scrollPositions.videos > 0) {
      isSettingScroll.current = true;
      containerRef.current.scrollTop = scrollPositions.videos;
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
        if (currentPos !== undefined && currentPos !== scrollPositions.videos) {
          setScrollPosition('videos', currentPos);
        }
      }, 500);
    }
  };

  return (
    <div 
      ref={containerRef} 
      className="p-5 space-y-6 rounded-lg overflow-y-auto h-full"
      onScroll={handleScroll}>
      <h1 className="text-3xl font-bold mb-4">Full Sessions</h1>
      {state.content.filter((video) => video.type === 'Session').length > 0 ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {recording && recording.endTime !== null && <ContentCard key={-1} type="Session" isLoading />}
          {state.content
            .filter((video) => video.type === 'Session')
            .map((video, index) => (
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
