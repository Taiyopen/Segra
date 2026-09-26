import { sendMessageToBackend } from './MessageUtils';
import type { ContentType } from '../Models/types';

export type PendingEditSourceType = 'Session' | 'Buffer';

export function isPendingEditSourceType(
  value: string | undefined | null,
): value is PendingEditSourceType {
  return value === 'Session' || value === 'Buffer';
}

export function resolvePendingEditSourceType(
  value: string | undefined | null,
  fallback: PendingEditSourceType = 'Session',
): PendingEditSourceType {
  return isPendingEditSourceType(value) ? value : fallback;
}

export function sendMoveToPendingEdit(
  items: { fileName: string; contentType: ContentType | PendingEditSourceType }[],
) {
  if (items.length === 0) return;
  sendMessageToBackend('MoveToPendingEdit', {
    Items: items.map((item) => ({ FileName: item.fileName, ContentType: item.contentType })),
  });
}

export function sendMoveOutOfPendingEdit(
  items: { fileName: string; targetType: PendingEditSourceType }[],
) {
  if (items.length === 0) return;
  sendMessageToBackend('MoveOutOfPendingEdit', {
    Items: items.map((item) => ({ FileName: item.fileName, TargetType: item.targetType })),
  });
}
