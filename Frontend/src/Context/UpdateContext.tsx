import { createContext, useContext, useState, ReactNode, useEffect } from 'react';
import { isUpdateProgressMessage, isReleaseNotesMessage, ReleaseNote } from '../Models/WebSocketMessages';
import { useModal } from './ModalContext';
import ReleaseNotesModal from '../Components/ReleaseNotesModal';

export interface UpdateProgress {
  version: string;
  progress: number;
  status: 'downloading' | 'downloaded' | 'ready' | 'error';
  message: string;
}

interface UpdateContextType {
  updateInfo: UpdateProgress | null;
  releaseNotes: ReleaseNote[];
  openReleaseNotesModal: (filterVersion?: string | null) => void;
  clearUpdateInfo: () => void;
}

const UpdateContext = createContext<UpdateContextType | undefined>(undefined);

export function UpdateProvider({ children }: { children: ReactNode }) {
  const [updateInfo, setUpdateInfo] = useState<UpdateProgress | null>(null);
  const [releaseNotes, setReleaseNotes] = useState<ReleaseNote[]>([]);
  const { openModal, closeModal } = useModal();

  useEffect(() => {
    const handleWebSocketMessage = (event: CustomEvent<any>) => {
      const message = event.detail;
      
      if (isUpdateProgressMessage(message)) {
        setUpdateInfo(message.content);
      }
      
      if (isReleaseNotesMessage(message)) {
        // Handle the ReleaseNotes message
        if (message.content && message.content.releaseNotesList) {
          setReleaseNotes(message.content.releaseNotesList);
        }
      }
    };

    window.addEventListener('websocket-message', handleWebSocketMessage as EventListener);

    return () => {
      window.removeEventListener('websocket-message', handleWebSocketMessage as EventListener);
    };
  }, []);

  const clearUpdateInfo = () => {
    setUpdateInfo(null);
    setReleaseNotes([]);
  };

  const openReleaseNotesModal = (filterVersion: string | null = __APP_VERSION__) => {
    openModal(
      <ReleaseNotesModal 
        releaseNotes={releaseNotes} 
        onClose={closeModal}
        filterVersion={filterVersion}
      />
    );
  };

  return (
    <UpdateContext.Provider value={{ 
      updateInfo, 
      releaseNotes, 
      openReleaseNotesModal,
      clearUpdateInfo 
    }}>
      {children}
    </UpdateContext.Provider>
  );
}

export function useUpdate() {
  const context = useContext(UpdateContext);
  if (context === undefined) {
    throw new Error('useUpdate must be used within an UpdateProvider');
  }
  return context;
}
