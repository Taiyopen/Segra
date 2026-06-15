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
import { useSettings } from './SettingsContext';
import { useWebSocketContext } from './WebSocketContext';

type MonitoringLayoutContextValue = {
  monitoringWindowOpen: boolean;
  enterMonitoringLayout: () => void;
  exitMonitoringLayout: () => void;
};

const MonitoringLayoutContext = createContext<MonitoringLayoutContextValue | null>(null);

function sendMonitoringWindowCommand(enabled: boolean, sendRawMessage: (message: string) => void) {
  const payload = { Method: 'SetMonitoringWindowLayout', Parameters: { enabled } };
  sendMessageToBackend('SetMonitoringWindowLayout', { enabled });
  sendRawMessage(JSON.stringify(payload));
}

function isRecordingLive(recording?: { endTime?: Date | null }, preRecording?: unknown): boolean {
  return !!(
    preRecording ||
    (recording && (recording.endTime == null || recording.endTime === undefined))
  );
}

export function MonitoringLayoutProvider({ children }: { children: ReactNode }) {
  const { sendRawMessage } = useWebSocketContext();
  const { recording, preRecording } = useSettings().state;
  const isMainApp = !isMonitoringWindowLocation();
  const isLive = isRecordingLive(recording, preRecording);
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
    sendMonitoringWindowCommand(true, sendRawMessage);
  }, [sendRawMessage]);

  const exitMonitoringLayout = useCallback(() => {
    sendMonitoringWindowCommand(false, sendRawMessage);
  }, [sendRawMessage]);

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
