import { CONTENT_TYPE_FOLDER, Content } from '../Models/types';

// Converts time string in format "HH:MM:SS.mmm" to seconds
export const timeStringToSeconds = (timeStr: string): number => {
  const [time, milliseconds] = timeStr.split('.');
  const [hours, minutes, seconds] = time.split(':').map(Number);
  return hours * 3600 + minutes * 60 + seconds + (milliseconds ? Number(`0.${milliseconds}`) : 0);
};

export const PLAYBACK_RATE_MIN = 0.1;
export const PLAYBACK_RATE_MAX = 5;
export const PLAYBACK_RATE_STEP = 0.1;

export const clampPlaybackRate = (rate: number) =>
  Math.round(Math.max(PLAYBACK_RATE_MIN, Math.min(PLAYBACK_RATE_MAX, rate)) * 10) / 10;

export const formatPlaybackRateLabel = (rate: number) => {
  const rounded = clampPlaybackRate(rate);
  return `${Number.isInteger(rounded) ? rounded : rounded.toFixed(1)}x`;
};

export const playbackRateSliderPercent = (rate: number) =>
  ((clampPlaybackRate(rate) - PLAYBACK_RATE_MIN) / (PLAYBACK_RATE_MAX - PLAYBACK_RATE_MIN)) * 100;

// Fetches a video thumbnail from the backend for a specific timestamp
export const fetchThumbnailAtTime = async (
  videoPath: string,
  timeInSeconds: number,
): Promise<string> => {
  const url = `http://localhost:2222/api/thumbnail?input=${encodeURIComponent(videoPath)}&time=${timeInSeconds}`;
  const response = await fetch(url);

  if (!response.ok) {
    throw new Error(`Failed to fetch thumbnail: ${response.statusText}`);
  }

  const blob = await response.blob();
  return URL.createObjectURL(blob);
};

// Get audio waveform URL - waveforms are stored in AppData
export const getWaveformUrl = (
  cacheFolder: string,
  type: Content['type'],
  fileName: string,
): string => {
  const folderName = CONTENT_TYPE_FOLDER[type];
  const waveformPath = `${cacheFolder}/waveforms/${folderName}/${fileName}.peaks.json`;
  return `http://localhost:2222/api/content?input=${encodeURIComponent(waveformPath)}&type=${type.toLowerCase()}`;
};
