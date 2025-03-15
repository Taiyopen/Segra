import {useSettings} from '../Context/SettingsContext';
import ContentCard from '../Components/ContentCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import {Content} from "../Models/types";
import { MdOutlineContentCut } from 'react-icons/md';
import { useClipping } from '../Context/ClippingContext';

export default function Clips() {
  const {state} = useSettings();
  const {setSelectedVideo} = useSelectedVideo();
  const {clippingProgress} = useClipping();

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  return (
    <div className="p-5 space-y-6 rounded-lg">
      <h1 className="text-3xl font-bold mb-4">Clips</h1>
      {(state.content.filter((video) => video.type === 'Clip').length > 0 || Object.keys(clippingProgress).length > 0) ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {Object.values(clippingProgress).map((progress) => (
            <ContentCard key={progress.id} type="Clip" isLoading />
          ))}
          {state.content
            .filter((video) => video.type === 'Clip')
            .map((video, index) => (
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
