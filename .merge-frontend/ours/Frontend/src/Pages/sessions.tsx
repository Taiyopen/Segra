import { Play } from 'lucide-react';
import ContentPage from '../Components/ContentPage';
import { useAppState } from '../Context/AppStateContext';
import { isRecordingFinishing as isAnyRecordingFinishing } from '../Models/types';
import ContentCard from '../Components/ContentCard';

export default function Sessions() {
  const appState = useAppState();

  const isRecordingFinishing = isAnyRecordingFinishing(appState);
  const progressCardElement = isRecordingFinishing ? (
    <ContentCard key="recording-progress" type="Session" isLoading />
  ) : null;

  return (
    <ContentPage
      contentType="Session"
      sectionId="sessions"
      title="Sessions"
      Icon={Play}
      progressItems={isRecordingFinishing ? { recording: true } : {}}
      isProgressVisible={isRecordingFinishing}
      progressCardElement={progressCardElement}
    />
  );
}
