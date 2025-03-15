export interface Content {
	type: 'Session' | 'Buffer' | 'Clip' | 'Highlight';
	title: string;
	game: string;
	bookmarks: Bookmark[];
	fileName: string;
	filePath: string;
	fileSize: string;
	duration: string;
	createdAt: string;
}

export interface State {
	recording?: Recording;
	hasLoadedObs: boolean;
	content: Content[];
	inputDevices: AudioDevice[];
	outputDevices: AudioDevice[];
}

export enum BookmarkType {
	Manual = 'Manual',
	Kill = 'Kill',
	Assist = 'Assist',
	Death = 'Death'
}

export enum BookmarkSubtype {
	Headshot = 'Headshot'
}

export interface Bookmark {
	id: number;
	type: BookmarkType;
	subtype: BookmarkSubtype;
	time: string;
}

export interface Recording {
	startTime: Date;
	endTime: Date;
	game: string;
	isUsingGameHook: boolean;
}

export interface Settings {
	theme: 'segra' | 'rich' | 'dark' | 'night' | 'dracula' | 'black' | 'luxury' | 'forest' | 'halloween' | 'coffee' | 'dim' | 'sunset';
	resolution: '720p' | '1080p' | '1440p' | '4K';
	frameRate: number;
	rateControl: string;
	crfValue: number;
	cqLevel: number;
	bitrate: number;
	encoder: 'gpu' | 'cpu';
	codec: 'h264' | 'h265';
	storageLimit: number;
	contentFolder: string;
	inputDevice: string;
	outputDevice: string;
	enableDisplayRecording: boolean;
	enableAi: boolean;
	state: State;
}

export const initialState: State = {
	recording: undefined,
	hasLoadedObs: true,
	content: [],
	inputDevices: [],
	outputDevices: [],
};

export const initialSettings: Settings = {
	theme: 'segra',
	resolution: '720p',
	frameRate: 30,
	rateControl: 'CBR',
	crfValue: 23,
	cqLevel: 20,
	bitrate: 10,
	encoder: 'gpu',
	codec: 'h264',
	storageLimit: 100,
	contentFolder: '',
	inputDevice: '',
	outputDevice: '',
	enableDisplayRecording: false,
	enableAi: true,
	state: initialState,
};

export interface Selection {
    id: number;
    type: Content['type'];
    startTime: number;
    endTime: number;
    thumbnailDataUrl?: string;
    isLoading: boolean;
    fileName: string;
    game?: string;
}

export interface SelectionCardProps {
    selection: Selection;
    index: number;
    moveCard: (dragIndex: number, hoverIndex: number) => void;
    formatTime: (time: number) => string;
    isHovered: boolean;
    setHoveredSelectionId: (id: number | null) => void;
    removeSelection: (id: number) => void;
}

export interface AudioDevice {
	id: string;
	name: string;
}

export interface AiProgress {
    id: string;
    progress: number;
    status: 'processing' | 'done';
    message: string;
    content: Content;
}