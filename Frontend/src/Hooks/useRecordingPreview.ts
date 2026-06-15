import { useState, useEffect, useMemo } from 'react';

/** Live JPEG preview frames while recording (same WebSocket events as RecordingCard). */
export function useRecordingPreview(
  recordingOngoing: boolean,
  hasRecordingObject: boolean,
  recording?: { startTime: Date },
) {
  /**
   * When false: stream is assumed ON (covers delay before first RecordingPreviewState; default session behavior).
   * When true: user or backend explicitly turned preview OFF (e.g. F11; subscription failure).
   */
  const [previewExplicitDisabled, setPreviewExplicitDisabled] = useState(false);
  const [hasPreviewFrame, setHasPreviewFrame] = useState(false);
  const [previewFrameSrc, setPreviewFrameSrc] = useState<string | null>(null);

  const recordingStartKey = useMemo(() => {
    if (!recording?.startTime) return null;
    const t = new Date(recording.startTime).getTime();
    return Number.isFinite(t) ? t : null;
  }, [recording?.startTime]);

  // Assume preview ON again for each new recording until RecordingPreviewState says otherwise.
  useEffect(() => {
    setPreviewExplicitDisabled(false);
  }, [recordingStartKey]);

  useEffect(() => {
    const handleMessage = (event: CustomEvent) => {
      const method = event.detail?.method;
      if (method === 'RecordingPreviewState') {
        const enabled = !!event.detail?.content?.enabled;
        setPreviewExplicitDisabled((prevExplicitDisabled) => {
          if (enabled && prevExplicitDisabled) {
            setHasPreviewFrame(false);
          }
          return !enabled;
        });
      } else if (method === 'RecordingPreviewFrame') {
        const b64 = event.detail?.content?.jpegBase64;
        if (typeof b64 === 'string' && b64.length > 0) {
          setPreviewFrameSrc(`data:image/jpeg;base64,${b64}`);
          setHasPreviewFrame(true);
        }
      }
    };

    window.addEventListener('websocket-message', handleMessage as EventListener);
    return () => {
      window.removeEventListener('websocket-message', handleMessage as EventListener);
    };
  }, []);

  useEffect(() => {
    if (!recordingOngoing) {
      setPreviewFrameSrc(null);
      setHasPreviewFrame(false);
    }
  }, [recordingOngoing]);

  useEffect(() => {
    if (!hasRecordingObject) {
      setPreviewExplicitDisabled(false);
      setPreviewFrameSrc(null);
      setHasPreviewFrame(false);
    }
  }, [hasRecordingObject]);

  const previewEnabled = recordingOngoing && hasRecordingObject && !previewExplicitDisabled;

  return {
    previewEnabled,
    previewFrameSrc,
    hasPreviewFrame,
  };
}
