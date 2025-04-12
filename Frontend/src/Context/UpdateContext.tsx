import { createContext, useContext, useState, ReactNode, useEffect } from 'react';
import { isUpdateProgressMessage, isReleaseNotesMessage, ReleaseNote, isShowReleaseNotesMessage } from '../Models/WebSocketMessages';
import { useModal } from './ModalContext';
import ReleaseNotesModal from '../Components/ReleaseNotesModal';
import { ReleaseNotesContext } from '../App';
import { sendMessageToBackend } from '../Utils/MessageUtils';

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
  checkForUpdates: () => void;
}

const UpdateContext = createContext<UpdateContextType | undefined>(undefined);

export function UpdateProvider({ children }: { children: ReactNode }) {
  const [updateInfo, setUpdateInfo] = useState<UpdateProgress | null>(null);
  const [releaseNotes, setReleaseNotes] = useState<ReleaseNote[]>([]);
  const { openModal, closeModal } = useModal();

  // Access the global release notes context
  const globalReleaseNotes = useContext(ReleaseNotesContext);

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
          // Also update the global release notes
          globalReleaseNotes.setReleaseNotes(message.content.releaseNotesList);
        }
      }

      if(isShowReleaseNotesMessage(message)) {
        openReleaseNotesModal(message.content);
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

  const checkForUpdates = () => {
    sendMessageToBackend('CheckForUpdates');
  };

  const openReleaseNotesModal = (filterVersion: string | null = __APP_VERSION__) => {
    openModal(
      <ReleaseNotesModal 
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
      clearUpdateInfo,
      checkForUpdates
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
