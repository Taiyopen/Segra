import {useSettings} from '../Context/SettingsContext';
import ContentCard from '../Components/ContentCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import { Content } from "../Models/types";
import { MdOutlinePlayCircleOutline } from 'react-icons/md';

export default function Videos() {
  const {state} = useSettings();
  const {recording} = state;
  const {setSelectedVideo} = useSelectedVideo();

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  return (
    <div className="p-5 space-y-6 rounded-lg">
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
