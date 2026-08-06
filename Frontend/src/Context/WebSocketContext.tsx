import { createContext, useContext, ReactNode, useCallback, useEffect, useRef } from 'react';
import * as ReactUseWebSocket from 'react-use-websocket';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import { AuthContext } from '../Hooks/useAuth.tsx';

type UseWebSocketFn = typeof import('react-use-websocket').default;

function resolveUseWebSocket(): UseWebSocketFn {
  const mod = ReactUseWebSocket as unknown as Record<string, unknown>;
  const candidates = [
    mod,
    mod.default,
    (mod.default as Record<string, unknown> | undefined)?.default,
  ];
  for (const candidate of candidates) {
    if (typeof candidate === 'function') {
      return candidate as UseWebSocketFn;
    }
  }
  throw new Error('react-use-websocket default export is unavailable (CJS interop failed)');
}

const useWebSocket = resolveUseWebSocket();
const ReadyState =
  (ReactUseWebSocket as unknown as { ReadyState: typeof import('react-use-websocket').ReadyState })
    .ReadyState ??
  (
    ReactUseWebSocket as unknown as {
      default: { ReadyState: typeof import('react-use-websocket').ReadyState };
    }
  ).default.ReadyState;

interface WebSocketContextType {
  sendMessage: (message: string) => void;
  /** Sends a JSON command directly over the WebSocket (bypasses Photino bridge). */
  sendRawMessage: (message: string) => void;
  isConnected: boolean;
  connectionState: (typeof ReadyState)[keyof typeof ReadyState];
}

const WebSocketContext = createContext<WebSocketContextType | undefined>(undefined);

interface WebSocketMessage {
  method: string;
  content: any;
}

export function WebSocketProvider({ children }: { children: ReactNode }) {
  // Optional: monitoring window may mount without AuthProvider.
  const auth = useContext(AuthContext);
  const session = auth?.session ?? null;
  // Ref to track if this is a reconnection (not initial connection)
  const hasConnectedBefore = useRef(false);

  // Log when the WebSocket provider mounts or session changes
  useEffect(() => {
    console.log('WebSocketProvider: Session state changed:', !!session);
  }, [session]);

  // Configure WebSocket with reconnection and heartbeat
  const { readyState, sendMessage: wsSend } = useWebSocket('ws://localhost:44030/', {
    onOpen: () => {
      // Check if this is a reconnection
      if (hasConnectedBefore.current) {
        console.log('WebSocket reconnected after disconnect - resyncing state');
      } else {
        console.log('WebSocket connected for the first time');
        hasConnectedBefore.current = true;
      }

      sendMessageToBackend('NewConnection');

      // If we already have a session when connecting, ensure we're logged in
      if (session) {
        console.log('WebSocket connected with active session, ensuring login state');
        sendMessageToBackend('Login', {
          accessToken: session.access_token,
          refreshToken: session.refresh_token,
        });
      }
    },
    onClose: (event) => {
      console.warn('WebSocket closed:', event.code, event.reason);
    },
    onError: (event) => {
      console.error('WebSocket error:', event);
    },
    onMessage: (event) => {
      try {
        const data: WebSocketMessage = JSON.parse(event.data);
        if (data.method !== 'RecordingPreviewFrame') {
          console.log('WebSocket message received:', data);
        }

        // Dispatch the message to all listeners
        window.dispatchEvent(
          new CustomEvent('websocket-message', {
            detail: data,
          }),
        );
      } catch (error) {
        console.error('Failed to parse WebSocket message:', error);
      }
    },
    shouldReconnect: () => {
      console.log('WebSocket closed, will attempt to reconnect');
      return true;
    },
    reconnectAttempts: Infinity,
    reconnectInterval: 3000,
    // The heartbeat closes the socket if no message arrives within `timeout`, and otherwise
    // sends `message` every `interval`. Both run off a single setInterval. While the Segra
    // window is backgrounded during gameplay, Chromium/WebView2 throttles timers to fire at
    // most about once every 60 seconds. `interval` must stay below that floor so each throttled
    // tick still emits a ping (which the backend answers, resetting the timeout), and `timeout`
    // must stay well above it so one slow tick can't trip the close.
    heartbeat: {
      message: 'ping',
      timeout: 120000,
      interval: 30000,
    },
  });

  const sendRawMessage = useCallback(
    (message: string) => {
      if (readyState === ReadyState.OPEN) {
        wsSend(message);
      }
    },
    [readyState, wsSend],
  );

  const contextValue = {
    sendMessage: useCallback((message: string) => {
      sendMessageToBackend(message);
    }, []),
    sendRawMessage,
    isConnected: readyState === ReadyState.OPEN,
    connectionState: readyState,
  };

  return <WebSocketContext.Provider value={contextValue}>{children}</WebSocketContext.Provider>;
}

export function useWebSocketContext() {
  const context = useContext(WebSocketContext);
  if (!context) {
    throw new Error('useWebSocketContext must be used within a WebSocketProvider');
  }
  return context;
}
