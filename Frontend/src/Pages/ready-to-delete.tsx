import { Trash2 } from 'lucide-react';
import ContentPage from '../Components/ContentPage';

export default function ReadyToDelete() {
  return (
    <ContentPage
      contentType="ReadyToDelete"
      sectionId="readyToDelete"
      title="準備刪除"
      Icon={Trash2}
    />
  );
}
