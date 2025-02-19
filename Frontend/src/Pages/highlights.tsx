import {useSettings} from '../Context/SettingsContext';
import ContentCard from '../Components/VideoCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import {Content} from "../Models/types";
import { HiOutlineSparkles } from 'react-icons/hi';

export default function Highlights() {
  const {state} = useSettings();
  const {setSelectedVideo} = useSelectedVideo();

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  return (
    <div className="p-5 space-y-6 rounded-lg">
      <h1 className="text-3xl font-bold mb-4">Highlights</h1>
      {state.content.filter((video) => video.type === 'Highlight').length > 0 ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
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
          <p className="text-lg font-medium">Coming Soon</p>
          <p className="mt-1">Your AI generated highlights will appear here</p>
        </div>
      )}
    </div>
  );
}
