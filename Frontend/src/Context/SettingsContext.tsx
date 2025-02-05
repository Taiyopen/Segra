import {createContext, useContext, useState, ReactNode, useEffect} from 'react';
import {Settings, initialSettings} from '../Models/types';
import {useWebSocketContext} from './WebSocketContext';
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

export function SettingsProvider({children}: SettingsProviderProps) {
	const [settings, setSettings] = useState<Settings>(initialSettings);
	useWebSocketContext();

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
	}, []);

	const updateSettings: SettingsUpdateContextType = (newSettings, fromBackend = false) => {
		const updatedSettings = {
			...settings,
			...newSettings,
			state: {
				...settings.state,
				...newSettings.state,
			},
		};

		setSettings(updatedSettings);

		// Only send UpdateSettings to backend if the change is from the frontend
		if (!fromBackend) {
			sendMessageToBackend('UpdateSettings', updatedSettings);
		}
	};

	return (
		<SettingsContext.Provider value={settings}>
			<SettingsUpdateContext.Provider value={updateSettings}>
				{children}
			</SettingsUpdateContext.Provider>
		</SettingsContext.Provider>
	);
}
