import { Inbox } from 'lucide-react';
import ContentPage from '../Components/ContentPage';

export default function PendingEdit() {
  return (
    <ContentPage contentType="PendingEdit" sectionId="pendingEdit" title="待剪輯" Icon={Inbox} />
  );
}
