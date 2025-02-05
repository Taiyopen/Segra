import { Settings } from './types';

export interface UploadProgressMessage {
  fileName: string;
  progress: number;
  status: 'uploading' | 'processing' | 'done' | 'error';
  message?: string;
}

export interface SettingsMessage {
  settings: Settings;
}

export interface WebSocketMessage<T = any> {
  method: string;
  parameters: T;
}

export type WebSocketMessageType = 'uploadProgress' | 'settings';

export function isUploadProgressMessage(message: WebSocketMessage<any>): boolean {
  return message.method === 'uploadProgress' && 
    typeof message.parameters === 'object' && 
    'fileName' in message.parameters && 
    'progress' in message.parameters && 
    'status' in message.parameters;
}

export function isSettingsMessage(message: WebSocketMessage<any>): boolean {
  return message.method === 'settings' && 
    typeof message.parameters === 'object' && 
    'contentFolder' in message.parameters;
}
