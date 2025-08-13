import { MdReplay30 } from 'react-icons/md';
import ContentPage from '../Components/ContentPage';

export default function ReplayBuffer() {
  return (
    <ContentPage
      contentType="Buffer"
      sectionId="replayBuffer"
      title="Replay Buffer"
      Icon={MdReplay30}
    />
  );
}
