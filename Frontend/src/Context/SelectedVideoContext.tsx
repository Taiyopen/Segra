import { createContext, useContext, useState, ReactNode } from 'react';
import { Content } from '../Models/types';

interface SelectedVideoContextProps {
  selectedVideo: Content | null;
  setSelectedVideo: (video: Content | null) => void;
}

const SelectedVideoContext = createContext<SelectedVideoContextProps | undefined>(undefined);

export const SelectedVideoProvider = ({ children }: { children: ReactNode }) => {
  const [selectedVideo, setSelectedVideo] = useState<Content | null>(null);

  return (
    <SelectedVideoContext.Provider value={{ selectedVideo, setSelectedVideo }}>
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
