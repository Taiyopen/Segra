import { HiOutlineSparkles } from 'react-icons/hi';
import { useAiHighlights } from '../Context/AiHighlightsContext';
import ContentPage from '../Components/ContentPage';
import AiContentCard from '../Components/AiContentCard';

export default function Highlights() {
  const { aiProgress } = useAiHighlights();

  // Pre-render the progress card element
  const progressCardElement =
    Object.keys(aiProgress).length > 0 ? (
      <AiContentCard key="ai-highlight-progress" progress={Object.values(aiProgress)[0]} />
    ) : null;

  return (
    <ContentPage
      contentType="Highlight"
      sectionId="highlights"
      title="Highlights"
      Icon={HiOutlineSparkles}
      progressItems={aiProgress}
      isProgressVisible={Object.keys(aiProgress).length > 0}
      progressCardElement={progressCardElement}
    />
  );
}
