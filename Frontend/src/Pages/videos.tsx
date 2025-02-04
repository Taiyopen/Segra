import {useSettings} from '../Context/SettingsContext';
import ContentCard from '../Components/VideoCard';
import { useSelectedVideo } from "../Context/SelectedVideoContext";
import { Content } from "../Models/types";

export default function Videos() {
  const {state} = useSettings();
  const {recording} = state;
  const {setSelectedVideo} = useSelectedVideo();

  const handlePlay = (video: Content) => {
    setSelectedVideo(video);
  };

  return (
    <div className="p-5 space-y-6 rounded-lg">
      <h1 className="text-3xl font-bold mb-4">Videos</h1>
      {state.content.length > 0 ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {recording && recording.endTime !== null && <ContentCard key={-1} type="video" isLoading />}
          {state.content
            .filter((video) => video.type.toLocaleLowerCase() === 'video')
            .map((video, index) => (
            <ContentCard
              key={index}
              content={video}
              onClick={handlePlay}
              type={'video'}
            />
          ))}
        </div>
      ) : (
        <div className="flex flex-col items-center justify-center py-16 text-base-content/70">
          <svg
            className="w-16 h-16 mb-4"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={1.5}
              d="M15 10l4.553-2.276A1 1 0 0121 8.618v6.764a1 1 0 01-1.447.894L15 14M5 18h8a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v8a2 2 0 002 2z"
            />
          </svg>
          <p className="text-lg font-medium">No videos available</p>
          <p className="mt-1">Start recording to see your videos here</p>
        </div>
      )}
    </div>
  );
}
