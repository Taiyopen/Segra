import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import { isMonitoringWindowLocation } from '../Utils/monitoringWindow';
import { hasLiveRecordingActivity, type State } from '../Models/types';
import { useAppState } from './AppStateContext';

type MonitoringLayoutContextValue = {
  monitoringWindowOpen: boolean;
  enterMonitoringLayout: () => void;
  exitMonitoringLayout: () => void;
};

const MonitoringLayoutContext = createContext<MonitoringLayoutContextValue | null>(null);

function sendMonitoringWindowCommand(enabled: boolean) {
  sendMessageToBackend('SetMonitoringWindowLayout', { enabled });
}

function isRecordingLive(state: State): boolean {
  return hasLiveRecordingActivity(state);
}

export function MonitoringLayoutProvider({ children }: { children: ReactNode }) {
  const appState = useAppState();
  const isMainApp = !isMonitoringWindowLocation();
  const isLive = isRecordingLive(appState);
  const wasLiveRef = useRef(isLive);
  const [monitoringWindowOpen, setMonitoringWindowOpen] = useState(() =>
    isMonitoringWindowLocation(),
  );

  useEffect(() => {
    const handleMessage = (event: CustomEvent) => {
      if (event.detail?.method === 'MonitoringWindowState') {
        setMonitoringWindowOpen(!!event.detail?.content?.open);
      }
    };
    window.addEventListener('websocket-message', handleMessage as EventListener);
    return () => window.removeEventListener('websocket-message', handleMessage as EventListener);
  }, []);

  const enterMonitoringLayout = useCallback(() => {
    sendMonitoringWindowCommand(true);
  }, []);

  const exitMonitoringLayout = useCallback(() => {
    sendMonitoringWindowCommand(false);
  }, []);

  useEffect(() => {
    if (!isMainApp) return;

    const wasLive = wasLiveRef.current;
    if (!wasLive && isLive) {
      enterMonitoringLayout();
    }
    wasLiveRef.current = isLive;
  }, [isLive, isMainApp, enterMonitoringLayout]);

  const value = useMemo(
    () => ({
      monitoringWindowOpen,
      enterMonitoringLayout,
      exitMonitoringLayout,
    }),
    [monitoringWindowOpen, enterMonitoringLayout, exitMonitoringLayout],
  );

  return (
    <MonitoringLayoutContext.Provider value={value}>{children}</MonitoringLayoutContext.Provider>
  );
}

export function useMonitoringLayout(): MonitoringLayoutContextValue {
  const ctx = useContext(MonitoringLayoutContext);
  if (!ctx) {
    throw new Error('useMonitoringLayout must be used within MonitoringLayoutProvider');
  }
  return ctx;
}
