import { sendMessageToBackend } from './MessageUtils';

export type RecordingSourceType = 'Session' | 'Buffer';

export function isRecordingSourceType(
  value: string | undefined | null,
): value is RecordingSourceType {
  return value === 'Session' || value === 'Buffer';
}

export function resolveRecordingSourceType(
  value: string | undefined | null,
  fallback: RecordingSourceType = 'Session',
): RecordingSourceType {
  return isRecordingSourceType(value) ? value : fallback;
}

export function sendMoveOutOfReadyToDelete(
  items: { fileName: string; targetType: RecordingSourceType }[],
): void {
  sendMessageToBackend('MoveOutOfReadyToDelete', {
    Items: items.map((item) => ({ FileName: item.fileName, TargetType: item.targetType })),
  });
}
