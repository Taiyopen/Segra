export type ContentType = 'Session' | 'Buffer' | 'Clip' | 'Highlight' | 'PendingEdit' | 'External';

export type RecordingMode = 'Session' | 'Buffer' | 'Hybrid';

export type DisplayCaptureMethod = 'Auto' | 'DXGI' | 'WGC';

export type AudioOutputMode = 'All' | 'GameOnly' | 'GameAndDiscord';

export type StartupWindowMode = 'Normal' | 'Minimized';
export type CloseButtonAction = 'Minimize' | 'Exit';

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
  /** Original Session/Buffer type when this item lives in 待剪輯. */
  pendingEditSourceType?: 'Session' | 'Buffer';
}

export interface OBSVersion {
  version: string;
  isBeta: boolean;
  url: string;
}

export interface State {
  gpuVendor: GpuVendor;
  /** @deprecated Use preRecordings — kept for backward compat with older backend payloads. */
  preRecording?: PreRecording;
  /** @deprecated Use recordings — kept for backward compat with older backend payloads. */
  recording?: Recording;
  preRecordings?: PreRecording[];
  recordings?: Recording[];
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
  recordingDriveUsedGb: number | null;
  recordingDriveFreeGb: number | null;
  cacheFolder: string;
}

/** Active session recordings from state (dual-slot aware). */
export function getActiveRecordings(state: State): Recording[] {
  if (state.recordings && state.recordings.length > 0) return state.recordings;
  return state.recording ? [state.recording] : [];
}

export function getActivePreRecordings(state: State): PreRecording[] {
  if (state.preRecordings && state.preRecordings.length > 0) return state.preRecordings;
  return state.preRecording ? [state.preRecording] : [];
}

export function hasLiveRecordingActivity(state: State): boolean {
  const recordings = getActiveRecordings(state);
  const preRecordings = getActivePreRecordings(state);
  if (preRecordings.length > 0) return true;
  return recordings.some((r) => r.endTime == null || r.endTime === undefined);
}

export function isRecordingFinishing(state: State): boolean {
  const recordings = getActiveRecordings(state);
  return recordings.some((r) => r.endTime != null && r.endTime !== undefined);
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
  volume: number; // Multiplier 0–2 (shown as 0–200%); applied to OBS source volume
  /** OBS mixer bitmask: bits 0–5 = tracks 1–6. Default 1 = track 1 only. */
  audioTrackMask?: number;
}

export const MAX_RECORDING_AUDIO_TRACKS = 6;
export const DEFAULT_AUDIO_TRACK_MASK = 1;

export interface Display {
  deviceId: string;
  deviceName: string;
  isPrimary: boolean;
  isHdr: boolean; // Display is currently in HDR mode (Windows "Use HDR" enabled)
  width: number; // Current resolution width
  height: number; // Current resolution height
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
  icon?: string; // CDN icon id (https://segra.tv/api/games/icon/{icon})
  igdbId?: number;
}

// Per-game recording quality override. When preset is a named preset (low/standard/high) the
// backend resolves concrete values at record time; when 'custom' the explicit fields below are used.
export interface GameQualityOverride {
  preset: VideoQualityPreset;
  resolution: '720p' | '1080p' | '1440p' | '4K';
  frameRate: number;
  rateControl: string;
  crfValue: number;
  cqLevel: number;
  bitrate: number;
  minBitrate: number;
  maxBitrate: number;
  encoder: 'gpu' | 'cpu';
  codec: Codec | null;
}

export interface GameRecordingModeOverride {
  recordingMode: RecordingMode;
  replayBufferDuration: number;
  replayBufferMaxSize: number;
}

// A single entry in the unified per-game settings list (replaces whitelist/blacklist).
// record === true means "always record this game", false means "never record it".
// Each *Override is null when the game inherits the corresponding global setting.
export interface GameSetting {
  name: string;
  paths: string[];
  igdbId: number | null; // catalog link; keeps name/icon in sync on startup
  icon?: string; // CDN icon id resolved from the catalog (known games)
  customIcon: string | null; // base64 PNG extracted from the exe (custom games)
  record: boolean;
  qualityOverride: GameQualityOverride | null;
  recordingModeOverride: GameRecordingModeOverride | null;
  discardSessionsWithoutBookmarksOverride: boolean | null;
  enableHdrOverride: boolean | null;
  volumeOverride: number | null; // Multiplier on top of the configured device volume (0-2)
}

export interface GameIntegrationSettings {
  enabled: boolean;
}

export interface GameIntegrations {
  counterStrike2: GameIntegrationSettings;
  leagueOfLegends: GameIntegrationSettings;
  pubg: GameIntegrationSettings;
  rocketLeague: GameIntegrationSettings;
  dota2: GameIntegrationSettings;
  rust: GameIntegrationSettings;
  minecraft: GameIntegrationSettings;
  runescapeDragonwilds: GameIntegrationSettings;
  warThunder: GameIntegrationSettings;
  gta: GameIntegrationSettings;
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
  CpuClipPreset | NvidiaClipPreset | Av1NvencPreset | AmdClipPreset | IntelClipPreset;

export type VideoQualityPreset = 'low' | 'standard' | 'high' | 'custom';
export type ClipQualityPreset = 'low' | 'standard' | 'high' | 'custom';

export type MenuItemId =
  'Full Sessions' | 'Replay Buffer' | '待剪輯' | '瀏覽影片' | 'Clips' | 'Highlights' | 'Settings';

export interface MenuItemPreference {
  id: MenuItemId;
  visible: boolean;
}

export const ALL_MENU_ITEM_IDS: MenuItemId[] = [
  'Full Sessions',
  'Replay Buffer',
  '待剪輯',
  '瀏覽影片',
  'Clips',
  'Highlights',
  'Settings',
];

export const DEFAULT_MENU_ITEMS: MenuItemPreference[] = ALL_MENU_ITEM_IDS.map((id) => ({
  id,
  visible: true,
}));

/** Ensures every known menu item exists (e.g. after adding 待剪輯 in a later version). */
export function normalizeMenuItems(
  items: MenuItemPreference[] | undefined | null,
): MenuItemPreference[] {
  const source = items && items.length > 0 ? items : DEFAULT_MENU_ITEMS;
  const byId = new Map(source.map((item) => [item.id, item]));
  return ALL_MENU_ITEM_IDS.map((id) => {
    const existing = byId.get(id);
    if (existing) return existing;
    return { id, visible: true };
  });
}

export const MENU_ITEM_CONTENT_TYPES: Record<MenuItemId, ContentType[]> = {
  'Full Sessions': ['Session'],
  'Replay Buffer': ['Buffer'],
  待剪輯: ['PendingEdit'],
  瀏覽影片: [],
  Clips: ['Clip'],
  Highlights: ['Highlight'],
  Settings: [],
};

export const menuItemHasContent = (id: MenuItemId, content: Content[]): boolean => {
  const types = MENU_ITEM_CONTENT_TYPES[id];
  if (types.length === 0) return false;
  return content.some((c) => types.includes(c.type));
};

export interface Settings {
  resolution: '720p' | '1080p' | '1440p' | '4K';
  frameRate: number;
  stretch4By3: boolean;
  enableHdr: boolean; // When false, recordings are always SDR even on an HDR display
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
  startupWindowMode: StartupWindowMode; // Window state when launched from startup
  closeButtonAction: CloseButtonAction;
  receiveBetaUpdates: boolean;
  airplaneMode: boolean; // Hides cloud account/login/upload features and signs the user out
  recordingMode: RecordingMode;
  replayBufferDuration: number; // in seconds
  replayBufferMaxSize: number; // in MB
  highlightPaddingBefore: number; // Seconds before a highlight moment
  highlightPaddingAfter: number; // Seconds after a highlight moment
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
  games: GameSetting[];
  autoRecordGames: boolean; // When false, don't auto-start recording when a game launches
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
  confirmBeforeDeleting: boolean;
  removeOriginalAfterCompression: boolean;
  discardSessionsWithoutBookmarks: boolean;
  disableWindowsGameMode: boolean; // When true, ensures Windows Game Mode stays off on startup
  menuItems: MenuItemPreference[];
  defaultMenuItem: MenuItemId;
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
  recordingDriveUsedGb: null,
  recordingDriveFreeGb: null,
  cacheFolder: '',
};

export const initialSettings: Settings = {
  resolution: '720p',
  frameRate: 30,
  stretch4By3: true,
  enableHdr: true,
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
  startupWindowMode: 'Minimized',
  closeButtonAction: 'Minimize',
  receiveBetaUpdates: false,
  airplaneMode: false,
  recordingMode: 'Hybrid',
  replayBufferDuration: 30,
  replayBufferMaxSize: 1000,
  highlightPaddingBefore: 4,
  highlightPaddingAfter: 4,
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
  confirmBeforeDeleting: false,
  removeOriginalAfterCompression: false,
  discardSessionsWithoutBookmarks: false,
  disableWindowsGameMode: false,
  menuItems: DEFAULT_MENU_ITEMS,
  defaultMenuItem: 'Full Sessions',
  keybindings: [
    { keys: [119], action: KeybindAction.CreateBookmark, enabled: true }, // 119 is F8
    { keys: [120], action: KeybindAction.ToggleRecording, enabled: true }, // 120 is F9
    { keys: [121], action: KeybindAction.SaveReplayBuffer, enabled: true }, // 121 is F10
    { keys: [122], action: KeybindAction.TogglePreview, enabled: true }, // 122 is F11
  ],
  games: [],
  autoRecordGames: true,
  gameIntegrations: {
    counterStrike2: { enabled: true },
    leagueOfLegends: { enabled: true },
    pubg: { enabled: true },
    rocketLeague: { enabled: false },
    dota2: { enabled: true },
    rust: { enabled: true },
    minecraft: { enabled: true },
    runescapeDragonwilds: { enabled: true },
    warThunder: { enabled: true },
    gta: { enabled: true },
    vrChat: { enabled: true },
  },
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
  /** Sidebar clip-edit target helper. */
  onClipEditTarget?: (id: number) => void;
  /** Sidebar segment click: select and seek to start. */
  onSidebarSegmentClick?: (segment: Segment) => void;
  /** Called when sidebar segment drag begins. */
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
