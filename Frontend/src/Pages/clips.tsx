import { Clapperboard } from 'lucide-react';
import { useClipping } from '../Context/ClippingContext';
import ContentPage from '../Components/ContentPage';
import ContentCard from '../Components/ContentCard';

export default function Clips() {
  const { clippingProgress } = useClipping();

  // Pre-render the progress card element
  const progressCardElement =
    Object.keys(clippingProgress).length > 0 ? (
      <ContentCard key="clipping-progress" type="Clip" isLoading />
    ) : null;

  return (
    <ContentPage
      contentType="Clip"
      sectionId="clips"
      title="剪輯片段"
      Icon={Clapperboard}
      progressItems={clippingProgress}
      isProgressVisible={Object.keys(clippingProgress).length > 0}
      progressCardElement={progressCardElement}
    />
  );
}
