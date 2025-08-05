import React, { createContext, useContext, useState } from 'react';

type ScrollPositions = {
  sessions: number;
  clips: number;
  highlights: number;
  replayBuffer: number;
};

interface ScrollContextType {
  scrollPositions: ScrollPositions;
  setScrollPosition: (page: keyof ScrollPositions, position: number) => void;
}

const ScrollContext = createContext<ScrollContextType | undefined>(undefined);

export function ScrollProvider({ children }: { children: React.ReactNode }) {
  const [scrollPositions, setScrollPositions] = useState<ScrollPositions>({
    sessions: 0,
    clips: 0,
    highlights: 0,
    replayBuffer: 0,
  });

  const setScrollPosition = (page: keyof ScrollPositions, position: number) => {
    setScrollPositions(prev => ({
      ...prev,
      [page]: position,
    }));
  };

  return (
    <ScrollContext.Provider value={{ scrollPositions, setScrollPosition }}>
      {children}
    </ScrollContext.Provider>
  );
}

export function useScroll() {
  const context = useContext(ScrollContext);
  if (context === undefined) {
    throw new Error('useScroll must be used within a ScrollProvider');
  }
  return context;
}
