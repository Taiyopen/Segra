import { MdOutlineContentCut } from 'react-icons/md';
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
      title="Clips"
      Icon={MdOutlineContentCut}
      progressItems={clippingProgress}
      isProgressVisible={Object.keys(clippingProgress).length > 0}
      progressCardElement={progressCardElement}
    />
  );
}
