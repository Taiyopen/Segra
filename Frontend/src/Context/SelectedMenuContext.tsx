import { createContext, useContext, useState, ReactNode, useCallback } from 'react';

interface SelectedMenuContextValue {
  selectedMenu: string;
  setSelectedMenu: (menu: string) => void;
}

const SelectedMenuContext = createContext<SelectedMenuContextValue | undefined>(undefined);

const defaultMenu = 'Full Sessions';

const getInitialMenu = () => {
  if (typeof window === 'undefined') {
    return defaultMenu;
  }

  const stored = (window as typeof window & { __selectedMenu?: string }).__selectedMenu;
  return stored ?? defaultMenu;
};

export const SelectedMenuProvider = ({ children }: { children: ReactNode }) => {
  const [selectedMenuState, setSelectedMenuState] = useState<string>(getInitialMenu);

  const setSelectedMenu = useCallback((menu: string) => {
    setSelectedMenuState(menu);
    if (typeof window !== 'undefined') {
      (window as typeof window & { __selectedMenu?: string }).__selectedMenu = menu;
    }
  }, []);

  return (
    <SelectedMenuContext.Provider value={{ selectedMenu: selectedMenuState, setSelectedMenu }}>
      {children}
    </SelectedMenuContext.Provider>
  );
};

export const useSelectedMenu = () => {
  const context = useContext(SelectedMenuContext);
  if (!context) {
    throw new Error('useSelectedMenu must be used within a SelectedMenuProvider');
  }

  return context;
};
