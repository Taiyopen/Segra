import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { sendMessageToBackend } from '../Utils/MessageUtils';

type MonitoringLayoutContextValue = {
  compactMonitoringLayout: boolean;
  enterMonitoringLayout: () => void;
  exitMonitoringLayout: () => void;
};

const MonitoringLayoutContext = createContext<MonitoringLayoutContextValue | null>(null);

export function MonitoringLayoutProvider({ children }: { children: ReactNode }) {
  const [compactMonitoringLayout, setCompactMonitoringLayout] = useState(false);

  const enterMonitoringLayout = useCallback(() => {
    setCompactMonitoringLayout(true);
    sendMessageToBackend('SetMonitoringWindowLayout', { enabled: true });
  }, []);

  const exitMonitoringLayout = useCallback(() => {
    sendMessageToBackend('SetMonitoringWindowLayout', { enabled: false });
    setCompactMonitoringLayout(false);
  }, []);

  const value = useMemo(
    () => ({
      compactMonitoringLayout,
      enterMonitoringLayout,
      exitMonitoringLayout,
    }),
    [compactMonitoringLayout, enterMonitoringLayout, exitMonitoringLayout],
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
