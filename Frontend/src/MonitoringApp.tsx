import { useEffect } from 'react';
import { themeChange } from 'theme-change';
import MonitoringCompactShell from './Components/MonitoringCompactShell';
import { SettingsProvider } from './Context/SettingsContext';
import { AppStateProvider } from './Context/AppStateContext';
import { WebSocketProvider } from './Context/WebSocketContext';
import { MonitoringLayoutProvider } from './Context/MonitoringLayoutContext';

export default function MonitoringApp() {
  useEffect(() => {
    themeChange(false);
    document.title = 'Segra 監控';
    document.documentElement.classList.add('monitoring-window');
    document.documentElement.setAttribute('data-theme', 'segra');

    const applyZoom = () => {
      const width = window.outerWidth || window.innerWidth || 336;
      document.documentElement.style.zoom = String(width / 336);
    };
    applyZoom();
    window.addEventListener('resize', applyZoom);

    return () => {
      window.removeEventListener('resize', applyZoom);
      document.documentElement.style.zoom = '';
      document.documentElement.classList.remove('monitoring-window');
    };
  }, []);

  return (
    <WebSocketProvider>
      <SettingsProvider>
        <AppStateProvider>
          <MonitoringLayoutProvider>
            <MonitoringCompactShell />
          </MonitoringLayoutProvider>
        </AppStateProvider>
      </SettingsProvider>
    </WebSocketProvider>
  );
}
