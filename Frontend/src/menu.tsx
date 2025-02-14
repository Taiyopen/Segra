import {useSettings} from './Context/SettingsContext';
import RecordingCard from './Components/RecordingCard';
import {sendMessageToBackend} from './Utils/MessageUtils';
import {useUploads} from './Context/UploadContext';
import UploadCard from './Components/UploadCard';
import {MdOutlineContentCut, MdOutlinePlayCircleOutline, MdOutlineSettings, MdReplay30} from 'react-icons/md';
import {HiOutlineSparkles} from 'react-icons/hi';

interface MenuProps {
	selectedMenu: string;
	onSelectMenu: (menu: string) => void;
}

export default function Menu({selectedMenu, onSelectMenu}: MenuProps) {
	const {state} = useSettings();
	const {hasLoadedObs, recording} = state;
	const {uploads} = useUploads();
	const uploadFiles = Object.keys(uploads);

	return (
		<div className="bg-base-300 w-56 h-screen flex flex-col">
			{/* Menu Items */}
			<ul className="menu">
				<li>
					<a
						className={selectedMenu === 'Full Sessions' ? 'active py-3' : 'py-3'}
						onClick={() => onSelectMenu('Full Sessions')}
					>
						<MdOutlinePlayCircleOutline className="w-6 h-6" />
						Full Sessions
					</a>
				</li>
				<li>
					<a
						className={selectedMenu === 'Replay Buffer' ? 'active py-3' : 'py-3'}
						onClick={() => onSelectMenu('Replay Buffer')}
					>
						<MdReplay30 className="w-6 h-6" />
						Replay Buffer
					</a>
				</li>
				<li>
					<a
						className={selectedMenu === 'Clips' ? 'active py-3' : 'py-3'}
						onClick={() => onSelectMenu('Clips')}
					>
						<MdOutlineContentCut className="w-6 h-6" />
						Clips
					</a>
				</li>
				<li>
					<a
						className={selectedMenu === 'Highlights' ? 'active py-3' : 'py-3'}
						onClick={() => onSelectMenu('Highlights')}
					>
						<HiOutlineSparkles className="w-6 h-6" />
						Highlights
					</a>
				</li>
				<li>
					<a
						className={selectedMenu === 'Settings' ? 'active py-3' : 'py-3'}
						onClick={() => onSelectMenu('Settings')}
					>
						<MdOutlineSettings className="w-6 h-6" />
						Settings
					</a>
				</li>
			</ul>

			{/* Spacer to push content to the bottom */}
			<div className="flex-grow"></div>

			{/* Status Cards */}
			<div className="mt-auto p-2 space-y-2">
				{uploadFiles.map((fileName) => (
					<UploadCard key={fileName} fileName={fileName} />
				))}
				{recording && recording.endTime == null && <RecordingCard recording={recording} />}
			</div>

			{/* Start and Stop Buttons */}
			<div className="mb-4 px-4">
				<div className="flex flex-col items-center space-y-2">
					<button
						className="btn btn-primary w-full font-semibold text-white"
						disabled={state.recording != null}
						onClick={() => sendMessageToBackend('StartRecording')}
					>
						Start
					</button>
					<button
						className="btn btn-secondary w-full"
						disabled={!state.recording}
						onClick={() => sendMessageToBackend('StopRecording')}
					>
						Stop
					</button>
				</div>
			</div>

			{/* OBS Loading Section */}
			{!hasLoadedObs && (
				<div className="mb-4 flex flex-col items-center">
					<div
						style={{
							width: '3.5rem',
							height: '2rem',
						}}
						className="loading loading-infinity"
					></div>
					<p className="text-center mt-2 disabled">Starting OBS</p>
				</div>
			)}
		</div>
	);
}
