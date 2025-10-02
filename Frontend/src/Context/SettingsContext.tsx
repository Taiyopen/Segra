import { createContext, useContext, useState, ReactNode, useEffect, useCallback, useRef } from 'react';
import { Settings, initialSettings, initialState } from '../Models/types';
import { useWebSocketContext } from './WebSocketContext';
import { sendMessageToBackend } from '../Utils/MessageUtils';

type SettingsContextType = Settings;
type SettingsUpdateContextType = (newSettings: Partial<Settings>, fromBackend?: boolean) => void;

const SettingsContext = createContext<SettingsContextType>(initialSettings);
const SettingsUpdateContext = createContext<SettingsUpdateContextType>(() => {});

export function useSettings(): SettingsContextType {
  return useContext(SettingsContext);
}

export function useSettingsUpdater(): SettingsUpdateContextType {
  return useContext(SettingsUpdateContext);
}

interface SettingsProviderProps {
  children: ReactNode;
}

export function SettingsProvider({ children }: SettingsProviderProps) {
  const STORAGE_KEY = 'segra.settings.v1';

  const loadCachedSettings = (): Settings | null => {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const cached = JSON.parse(raw);

      // Merge cached settings with defaults
      const revived: Settings = {
        ...initialSettings,
        ...cached,
        state: {
          ...initialState,
          ...cached.state,
        },
      };

      // Do not restore ongoing recording/preRecording or hasLoadedObs from cache
      revived.state.recording = undefined;
      revived.state.preRecording = undefined;
      revived.state.hasLoadedObs = false;

      return revived;
    } catch {
      return null;
    }
  };

  const saveCachedSettings = (value: Settings) => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
    } catch {
      // ignore caching errors
    }
  };

  const [settings, setSettings] = useState<Settings>(() => loadCachedSettings() ?? initialSettings);
  useWebSocketContext();

  const shouldSendToBackendRef = useRef(false);
  const pendingSettingsRef = useRef<Settings | null>(null);

  const updateSettings = useCallback<SettingsUpdateContextType>(
    (newSettings, fromBackend = false) => {
      shouldSendToBackendRef.current = !fromBackend;
      
      setSettings((prev) => {
        const updatedSettings: Settings = {
          ...prev,
          ...newSettings,
          state: {
            ...prev.state,
            ...newSettings.state,
          },
        };

        // Persist stable settings for faster startup rendering before backend connects
        saveCachedSettings(updatedSettings);
        
        // Store for sending to backend after state update
        if (shouldSendToBackendRef.current) {
          pendingSettingsRef.current = updatedSettings;
        }

        return updatedSettings;
      });
      
      // Send to backend OUTSIDE of setSettings callback to avoid React Strict Mode double-invocation
      if (shouldSendToBackendRef.current && pendingSettingsRef.current) {
        sendMessageToBackend('UpdateSettings', pendingSettingsRef.current);
        pendingSettingsRef.current = null;
        shouldSendToBackendRef.current = false;
      }
    },
    [],
  );

  useEffect(() => {
    const handleWebSocketMessage = (event: CustomEvent<any>) => {
      const data = event.detail;

      if (data.method === 'Settings') {
        updateSettings(data.content, true);
      }
    };

    window.addEventListener('websocket-message', handleWebSocketMessage as EventListener);

    return () => {
      window.removeEventListener('websocket-message', handleWebSocketMessage as EventListener);
    };
  }, [updateSettings]);

  return (
    <SettingsContext.Provider value={settings}>
      <SettingsUpdateContext.Provider value={updateSettings}>
        {children}
      </SettingsUpdateContext.Provider>
    </SettingsContext.Provider>
  );
}
