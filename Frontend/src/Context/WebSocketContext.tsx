import { createContext, useContext, ReactNode, useCallback } from 'react';
import useWebSocket from 'react-use-websocket';
import { sendMessageToBackend } from '../Utils/MessageUtils';

interface WebSocketContextType {
  sendMessage: (message: string) => void;
}

const WebSocketContext = createContext<WebSocketContextType | undefined>(undefined);

interface WebSocketMessage {
  method: string;
  parameters: any;
}

export function WebSocketProvider({ children }: { children: ReactNode }) {
  useWebSocket('ws://localhost:5000/', {
    onOpen: () => {
      console.log('Connected to WebSocket server');
      // Request initial settings when connection is established
      sendMessageToBackend("NewConnection");
    },
    onMessage: (event) => {
      try {
        const data: WebSocketMessage = JSON.parse(event.data);
        console.log(data);
        // Dispatch the message to all listeners
        window.dispatchEvent(new CustomEvent('websocket-message', {
          detail: data
        }));
      } catch (error) {
        console.error('Error parsing WebSocket message:', error);
      }
    },
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
