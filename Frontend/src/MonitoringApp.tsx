import { useEffect } from 'react';
import { themeChange } from 'theme-change';
import MonitoringCompactShell from './Components/MonitoringCompactShell';
import { SettingsProvider } from './Context/SettingsContext';
import { WebSocketProvider } from './Context/WebSocketContext';
import { MonitoringLayoutProvider } from './Context/MonitoringLayoutContext';

export default function MonitoringApp() {
  useEffect(() => {
    themeChange(false);
    document.documentElement.classList.add('monitoring-window');
    document.documentElement.setAttribute('data-theme', 'segra');
    return () => document.documentElement.classList.remove('monitoring-window');
  }, []);

  return (
    <WebSocketProvider>
      <SettingsProvider>
        <MonitoringLayoutProvider>
          <MonitoringCompactShell />
        </MonitoringLayoutProvider>
      </SettingsProvider>
    </WebSocketProvider>
  );
}
