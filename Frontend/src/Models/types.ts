export type ContentType = 'Session' | 'Buffer' | 'Clip' | 'Highlight' | 'PendingEdit';

export type RecordingMode = 'Session' | 'Buffer' | 'Hybrid';

export type DisplayCaptureMethod = 'Auto' | 'DXGI' | 'WGC';

export type AudioOutputMode = 'All' | 'GameOnly' | 'GameAndDiscord';

export interface Content {
  type: ContentType;
  title: string;
  game: string;
  bookmarks: Bookmark[];
  fileName: string;
  filePath: string;
  fileSize: string;
  fileSizeKb: number;
  duration: string;
  createdAt: string;
  uploadId?: string;
  igdbId?: number;
  isImported: boolean;
  audioTrackNames?: string[];
}

export interface OBSVersion {
  version: string;
  isBeta: boolean;
  availableSince?: string;
  supportsFrom?: string;
  supportsTo?: string;
  url: string;
}

export interface State {
  gpuVendor: GpuVendor;
  preRecording?: PreRecording;
  recording?: Recording;
  hasLoadedObs: boolean;
  content: Content[];
  inputDevices: AudioDevice[];
  outputDevices: AudioDevice[];
  displays: Display[];
  codecs: Codec[];
  availableOBSVersions: OBSVersion[];
  isCheckingForUpdates: boolean;
  gameList: GameListEntry[];
  maxDisplayHeight: number;
  currentFolderSizeGb: number;
  cacheFolder: string;
}

export enum GpuVendor {
  Unknown = 'Unknown',
  Nvidia = 'Nvidia',
  AMD = 'AMD',
  Intel = 'Intel',
}

export enum BookmarkType {
  Manual = 'Manual',
  Kill = 'Kill',
  Goal = 'Goal',
  Assist = 'Assist',
  Death = 'Death',
}

export const includeInHighlight = (type: BookmarkType): boolean =>
  type === BookmarkType.Kill || type === BookmarkType.Goal;

export enum BookmarkSubtype {
  Headshot = 'Headshot',
}

export enum KeybindAction {
  CreateBookmark = 'CreateBookmark',
  SaveReplayBuffer = 'SaveReplayBuffer',
  ToggleRecording = 'ToggleRecording',
  TogglePreview = 'TogglePreview',
}

export interface Keybind {
  keys: number[];
  action: KeybindAction;
  enabled: boolean;
}

export interface Bookmark {
  id: number;
  type: BookmarkType;
  subtype?: BookmarkSubtype;
  time: string;
}

export interface Recording {
  startTime: Date;
  endTime: Date;
  game: string;
  isUsingGameHook: boolean;
  coverImageId?: string;
  gameImage?: string; // Base64 encoded image of the game executable icon
  /** 0 = primary output, 1 = secondary (dual session). */
  slot?: number;
}

export interface PreRecording {
  game: string;
  status: string;
  coverImageId?: string;
  slot?: number;
}

export interface AudioDevice {
  id: string;
  name: string;
  isDefault?: boolean;
}

export interface DeviceSetting {
  id: string;
  name: string;
  volume: number; // Multiplier 0–3 (shown as 0–300%); applied to OBS source volume
  /** OBS mixer bitmask: bits 0–5 = tracks 1–6. Default 1 = track 1 only. */
  audioTrackMask?: number;
}

export const MAX_RECORDING_AUDIO_TRACKS = 6;
export const DEFAULT_AUDIO_TRACK_MASK = 1;

export interface Display {
  deviceId: string;
  deviceName: string;
  isPrimary: boolean;
}

export interface Codec {
  friendlyName: string;
  internalEncoderId: string;
  isHardwareEncoder: boolean;
}

export interface Game {
  name: string;
  paths?: string[];
}

export interface GameListEntry {
  name: string;
  executables: string[];
}

export interface GameIntegrationSettings {
  enabled: boolean;
}

export interface GameIntegrations {
  counterStrike2: GameIntegrationSettings;
  leagueOfLegends: GameIntegrationSettings;
  pubg: GameIntegrationSettings;
  rocketLeague: GameIntegrationSettings;
  vrChat: GameIntegrationSettings;
}

export type ClipEncoder = 'gpu' | 'cpu';
export type ClipCodec = 'h264' | 'h265' | 'av1';
export type ClipFPS = 0 | 24 | 30 | 60 | 120 | 144;
export type ClipAudioQuality = '96k' | '128k' | '192k' | '256k' | '320k';
/** Session / replay-buffer recording AAC bitrate (same options as clip audio). */
export type RecordingAudioBitrate = ClipAudioQuality;
export type CpuClipPreset =
  | 'ultrafast'
  | 'superfast'
  | 'veryfast'
  | 'faster'
  | 'fast'
  | 'medium'
  | 'slow'
  | 'slower'
  | 'veryslow';
export type NvidiaClipPreset =
  | 'slow'
  | 'medium'
  | 'fast'
  | 'hp'
  | 'hq'
  | 'bd'
  | 'll'
  | 'llhq'
  | 'llhp'
  | 'lossless'
  | 'losslesshp';
export type Av1NvencPreset = 'p1' | 'p2' | 'p3' | 'p4' | 'p5' | 'p6' | 'p7';
export type AmdClipPreset = 'quality' | 'transcoding' | 'lowlatency' | 'ultralowlatency';
export type IntelClipPreset = 'fast' | 'medium' | 'slow';
export type ClipPreset =
  | CpuClipPreset
  | NvidiaClipPreset
  | Av1NvencPreset
  | AmdClipPreset
  | IntelClipPreset;

export type VideoQualityPreset = 'low' | 'standard' | 'high' | 'custom';
export type ClipQualityPreset = 'low' | 'standard' | 'high' | 'custom';

export interface Settings {
  theme:
    | 'segra'
    | 'rich'
    | 'dark'
    | 'night'
    | 'dracula'
    | 'black'
    | 'luxury'
    | 'forest'
    | 'halloween'
    | 'coffee'
    | 'dim'
    | 'sunset';
  resolution: '720p' | '1080p' | '1440p' | '4K';
  frameRate: number;
  stretch4By3: boolean;
  rateControl: string;
  crfValue: number;
  cqLevel: number;
  bitrate: number;
  minBitrate: number; // VBR only (Mbps)
  maxBitrate: number; // VBR / CQVBR peak cap (Mbps)
  encoder: 'gpu' | 'cpu';
  codec: Codec | null;
  storageLimit: number;
  contentFolder: string;
  cacheFolder: string;
  inputDevices: DeviceSetting[];
  outputDevices: DeviceSetting[];
  forceMonoInputSources: boolean;
  inputNoiseSuppression: boolean;
  selectedDisplay: Display | null;
  displayCaptureMethod: DisplayCaptureMethod;
  selectedOBSVersion: string | null; // null means automatic (latest non-beta)
  enableAi: boolean;
  autoGenerateHighlights: boolean;
  runOnStartup: boolean;
  receiveBetaUpdates: boolean;
  recordingMode: RecordingMode;
  replayBufferDuration: number; // in seconds
  replayBufferMaxSize: number; // in MB
  clipClearSegmentsAfterCreatingClip: boolean;
  clipShowInBrowserAfterUpload: boolean; // Open browser after upload
  clipEncoder: ClipEncoder;
  clipRateControl: string;
  clipBitrate: number;
  clipMinBitrate: number;
  clipMaxBitrate: number;
  clipQualityCpu: number; // CPU CRF: 17 (High) to 28 (Low)
  clipQualityGpu: number; // GPU (CQ/QP/ICQ): 0-1 (High) to 51 (Low)
  clipCodec: ClipCodec;
  clipFps: ClipFPS;
  clipAudioQuality: ClipAudioQuality;
  clipPreset: ClipPreset;
  clipKeepSeparateAudioTracks: boolean;
  keybindings: Keybind[];
  whitelist: Game[];
  blacklist: Game[];
  gameIntegrations: GameIntegrations;
  soundEffectsVolume: number; // Volume for UI sound effects (0.0 to 1.0)
  showNewBadgeOnVideos: boolean;
  showGameBackground: boolean; // Show game background while recording
  showAudioWaveformInTimeline: boolean; // Show audio waveform in video timeline
  enableSeparateAudioTracks: boolean; // Advanced: per-source audio tracks
  /** @deprecated Use per-source audioTrackMask instead */
  excludeGameDiscordFromMasterMix: boolean;
  /** OBS mixer bitmask for game capture audio (bits 0–5 = tracks 1–6) */
  gameAudioTrackMask: number;
  /** OBS mixer bitmask for Discord app capture */
  discordAudioTrackMask: number;
  /** Custom names for recording tracks 1–6 (empty string = "Track N") */
  recordingAudioTrackNames: string[];
  /** AAC bitrate for session / replay-buffer recording (OBS). */
  recordingAudioBitrate: RecordingAudioBitrate;
  audioOutputMode: AudioOutputMode;
  videoQualityPreset: VideoQualityPreset;
  clipQualityPreset: ClipQualityPreset;
  removeOriginalAfterCompression: boolean;
  discardSessionsWithoutBookmarks: boolean;
  state: State;
}

export const initialState: State = {
  gpuVendor: GpuVendor.Unknown,
  recording: undefined,
  hasLoadedObs: false,
  content: [],
  inputDevices: [],
  outputDevices: [],
  displays: [],
  codecs: [],
  availableOBSVersions: [],
  isCheckingForUpdates: false,
  gameList: [],
  maxDisplayHeight: 1080,
  currentFolderSizeGb: 0,
  cacheFolder: '',
};

export const initialSettings: Settings = {
  theme: 'segra',
  resolution: '720p',
  frameRate: 30,
  stretch4By3: true,
  rateControl: 'VBR',
  crfValue: 23,
  cqLevel: 20,
  bitrate: 50,
  minBitrate: 35,
  maxBitrate: 70,
  encoder: 'gpu',
  codec: null,
  storageLimit: 100,
  contentFolder: '',
  cacheFolder: '',
  inputDevices: [],
  outputDevices: [],
  forceMonoInputSources: false,
  inputNoiseSuppression: true,
  selectedDisplay: null, // Default to null (auto-select)
  displayCaptureMethod: 'Auto',
  selectedOBSVersion: null, // null means automatic (latest non-beta)
  enableAi: true,
  autoGenerateHighlights: true,
  runOnStartup: false,
  receiveBetaUpdates: false,
  recordingMode: 'Hybrid',
  replayBufferDuration: 30,
  replayBufferMaxSize: 1000,
  clipClearSegmentsAfterCreatingClip: false,
  clipShowInBrowserAfterUpload: false,
  clipEncoder: 'cpu',
  clipRateControl: 'CRF',
  clipBitrate: 35,
  clipMinBitrate: 25,
  clipMaxBitrate: 55,
  clipQualityCpu: 23,
  clipQualityGpu: 23,
  clipCodec: 'h264',
  clipFps: 60,
  clipAudioQuality: '128k',
  clipPreset: 'veryfast',
  clipKeepSeparateAudioTracks: false,
  soundEffectsVolume: 1,
  showNewBadgeOnVideos: false,
  showGameBackground: true,
  showAudioWaveformInTimeline: true,
  enableSeparateAudioTracks: false,
  excludeGameDiscordFromMasterMix: false,
  gameAudioTrackMask: 1,
  discordAudioTrackMask: 1,
  recordingAudioTrackNames: ['', '', '', '', '', ''],
  recordingAudioBitrate: '128k',
  audioOutputMode: 'All',
  videoQualityPreset: 'high',
  clipQualityPreset: 'standard',
  removeOriginalAfterCompression: false,
  discardSessionsWithoutBookmarks: false,
  keybindings: [
    { keys: [119], action: KeybindAction.CreateBookmark, enabled: true }, // 119 is F8
    { keys: [121], action: KeybindAction.SaveReplayBuffer, enabled: true }, // 121 is F10
  ],
  whitelist: [],
  blacklist: [],
  gameIntegrations: {
    counterStrike2: { enabled: true },
    leagueOfLegends: { enabled: true },
    pubg: { enabled: true },
    rocketLeague: { enabled: false },
    vrChat: { enabled: true },
  },
  state: initialState,
};

export interface Segment {
  id: number;
  type: ContentType;
  startTime: number;
  endTime: number;
  thumbnailDataUrl?: string;
  isLoading: boolean;
  fileName: string;
  filePath: string;
  game?: string;
  title?: string;
  igdbId?: number;
  mutedAudioTracks?: number[];
  audioTrackVolumes?: Record<number, number>;
}

export interface SegmentCardProps {
  segment: Segment;
  index: number;
  moveCard: (dragIndex: number, hoverIndex: number) => void;
  formatTime: (time: number) => string;
  isHovered: boolean;
  setHoveredSegmentId: (id: number | null) => void;
  removeSegment: (id: number) => void;
  audioTrackNames?: string[];
  onMutedAudioTracksChange?: (id: number, mutedTracks: number[]) => void;
  onAudioTrackVolumesChange?: (id: number, volumes: Record<number, number>) => void;
  /** 標記此片段為剪輯編輯目標（例如設定起／終點） */
  onClipEditTarget?: (id: number) => void;
  /** 點擊卡片（非按鈕／輸入）時跳轉至該片段起點 */
  onSidebarSegmentClick?: (segment: Segment) => void;
  /** 側欄卡片開始拖曳排序前（用於單次復原步驟） */
  onSegmentCardDragBegin?: () => void;
}

export interface AiProgress {
  id: string;
  progress: number;
  status: 'processing' | 'done';
  message: string;
  content: Content;
}

export interface MigrationStatus {
  isRunning: boolean;
  currentMigration: string | null;
}

export interface GameResponse {
  game: {
    id: number;
    name: string;
    cover?: {
      id: number;
      image_id: string;
    };
  };
}
