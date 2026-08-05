import { createContext, useContext, useState, ReactNode, useEffect } from 'react';
import { State, initialState, GameListEntry } from '../Models/types';
import { SETTINGS_STORAGE_KEY } from './SettingsContext';

const AppStateContext = createContext<State>(initialState);

export function useAppState(): State {
  return useContext(AppStateContext);
}

interface AppStateProviderProps {
  children: ReactNode;
}

export function AppStateProvider({ children }: AppStateProviderProps) {
  const STORAGE_KEY = 'segra.appstate.v1';

  const loadCachedState = (): State => {
    const reviveState = (raw: Record<string, unknown>): State => {
      const revived: State = { ...initialState, ...raw };
      // Do not restore live recording info from cache
      revived.recording = undefined;
      revived.preRecording = undefined;
      revived.recordings = undefined;
      revived.preRecordings = undefined;
      revived.hasLoadedObs = false;
      return revived;
    };

    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        return reviveState(JSON.parse(raw));
      }

      // Migrate state embedded in the legacy settings blob (pre AppState split).
      const settingsRaw = localStorage.getItem(SETTINGS_STORAGE_KEY);
      if (settingsRaw) {
        const settings = JSON.parse(settingsRaw) as { state?: Record<string, unknown> };
        if (settings.state && typeof settings.state === 'object') {
          return reviveState(settings.state);
        }
      }
    } catch {
      /* fall through */
    }

    return initialState;
  };

  const saveCachedState = (value: State) => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
    } catch {
      // ignore caching errors
    }
  };

  const [appState, setAppState] = useState<State>(() => loadCachedState());

  useEffect(() => {
    const handleWebSocketMessage = (event: CustomEvent<any>) => {
      const data = event.detail;

      if (data.method === 'State') {
        setAppState((prev) => {
          const next: State = { ...prev, ...data.content };
          saveCachedState(next);
          return next;
        });
      } else if (data.method === 'GameList') {
        setAppState((prev) => {
          const next: State = { ...prev, gameList: data.content as GameListEntry[] };
          saveCachedState(next);
          return next;
        });
      }
    };

    window.addEventListener('websocket-message', handleWebSocketMessage as EventListener);
    return () => {
      window.removeEventListener('websocket-message', handleWebSocketMessage as EventListener);
    };
  }, []);

  return <AppStateContext.Provider value={appState}>{children}</AppStateContext.Provider>;
}
