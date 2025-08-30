import { createContext, useContext, ReactNode, useCallback, useEffect, useRef } from 'react';
import useWebSocket from 'react-use-websocket';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import { useAuth } from '../Hooks/useAuth.tsx';

interface WebSocketContextType {
  sendMessage: (message: string) => void;
}

const WebSocketContext = createContext<WebSocketContextType | undefined>(undefined);

interface WebSocketMessage {
  method: string;
  content: any;
}

export function WebSocketProvider({ children }: { children: ReactNode }) {
  // Get the auth session to properly handle authentication
  const { session } = useAuth();
  // Ref to track if we've already handled a version mismatch (prevent multiple reloads)
  const versionCheckHandled = useRef(false);

  // Check for stored version info on mount
  useEffect(() => {
    const storedOldVersion = localStorage.getItem('oldAppVersion');
    if (storedOldVersion) {
      // Clear the flag so it only runs once
      localStorage.removeItem('oldAppVersion');

      // Import and use the update context to show release notes
      import('../Context/UpdateContext').then(({ useUpdate }) => {
        const { openReleaseNotesModal } = useUpdate();
        openReleaseNotesModal(storedOldVersion);
      });
    }
  }, []);

  // Log when the WebSocket provider mounts or session changes
  useEffect(() => {
    console.log("WebSocketProvider: Session state changed:", !!session);
  }, [session]);

  // We only need the onOpen and onMessage callbacks, not the returned functions
  useWebSocket('ws://localhost:5000/', {
    onOpen: () => {
      console.log('Connected to WebSocket server');
      sendMessageToBackend("NewConnection");

      // If we already have a session when connecting, ensure we're logged in
      if (session) {
        console.log("WebSocket connected with active session, ensuring login state");
        sendMessageToBackend("Login", {
          accessToken: session.access_token,
          refreshToken: session.refresh_token
        });
      }
    },
    onMessage: (event) => {
      try {
        const data: WebSocketMessage = JSON.parse(event.data);
        console.log("WebSocket message received:", data);

        // Handle version check
        if (data.method === "AppVersion" && !versionCheckHandled.current) {
          versionCheckHandled.current = true;
          const backendVersion = data.content?.version;

          if (backendVersion && backendVersion !== __APP_VERSION__) {
            console.log(`Version mismatch: Backend ${backendVersion}, Frontend ${__APP_VERSION__}. Reloading...`);
            // Store the old version before reloading
            localStorage.setItem('oldAppVersion', __APP_VERSION__);
            window.location.reload();
            return;
          }
        }

        // Dispatch the message to all listeners
        window.dispatchEvent(new CustomEvent('websocket-message', {
          detail: data
        }));
      } catch (error) {
        console.error('Failed to parse WebSocket message:', error);
      }
    }
  });

  const contextValue = {
    sendMessage: useCallback((message: string) => {
      sendMessageToBackend(message);
    }, []),
  };

  return (
    <WebSocketContext.Provider value={contextValue}>
      {children}
    </WebSocketContext.Provider>
  );
}

export function useWebSocketContext() {
  const context = useContext(WebSocketContext);
  if (!context) {
    throw new Error('useWebSocketContext must be used within a WebSocketProvider');
  }
  return context;
}
