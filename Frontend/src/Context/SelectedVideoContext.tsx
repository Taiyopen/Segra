import { createContext, useContext, useState, useCallback, ReactNode } from 'react';
import { Content, ContentType } from '../Models/types';

export type StickySourceCategory = {
  fileName: string;
  sourceType: Extract<ContentType, 'Session' | 'Buffer'>;
};

interface SelectedVideoContextProps {
  selectedVideo: Content | null;
  setSelectedVideo: (video: Content | null) => void;
  stickySourceCategory: StickySourceCategory | null;
  pinStickySourceCategory: (
    fileName: string,
    sourceType: Extract<ContentType, 'Session' | 'Buffer'>,
  ) => void;
  clearStickySourceCategory: () => void;
}

const SelectedVideoContext = createContext<SelectedVideoContextProps | undefined>(undefined);

export const SelectedVideoProvider = ({ children }: { children: ReactNode }) => {
  const [selectedVideo, setSelectedVideoState] = useState<Content | null>(null);
  const [stickySourceCategory, setStickySourceCategory] = useState<StickySourceCategory | null>(
    null,
  );

  const setSelectedVideo = useCallback((video: Content | null) => {
    setSelectedVideoState(video);
    // Keep sticky only while continuing to watch the same video.
    // Clear when leaving the player or switching to another video.
    setStickySourceCategory((prev) => {
      if (!prev) return null;
      if (!video || video.fileName !== prev.fileName) return null;
      return prev;
    });
  }, []);

  const pinStickySourceCategory = useCallback(
    (fileName: string, sourceType: Extract<ContentType, 'Session' | 'Buffer'>) => {
      setStickySourceCategory({ fileName, sourceType });
    },
    [],
  );

  const clearStickySourceCategory = useCallback(() => {
    setStickySourceCategory(null);
  }, []);

  return (
    <SelectedVideoContext.Provider
      value={{
        selectedVideo,
        setSelectedVideo,
        stickySourceCategory,
        pinStickySourceCategory,
        clearStickySourceCategory,
      }}
    >
      {children}
    </SelectedVideoContext.Provider>
  );
};

export const useSelectedVideo = () => {
  const context = useContext(SelectedVideoContext);
  if (!context) {
    throw new Error('useSelectedVideo must be used within a SelectedVideoProvider');
  }
  return context;
};

/** Include a PendingEdit item in Session/Buffer lists while sticky pin is active. */
export function matchesContentCategory(
  item: Content,
  contentType: ContentType,
  sticky: StickySourceCategory | null,
): boolean {
  if (item.type === contentType) return true;
  return (
    sticky != null &&
    sticky.sourceType === contentType &&
    sticky.fileName === item.fileName &&
    item.type === 'PendingEdit'
  );
}
