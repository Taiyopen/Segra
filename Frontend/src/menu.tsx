import {useSettings} from './Context/SettingsContext';
import RecordingCard from './Components/RecordingCard';
import {sendMessageToBackend} from './Utils/MessageUtils';
import {useUploads} from './Context/UploadContext';
import {useClipping} from './Context/ClippingContext';
import {useUpdate} from './Context/UpdateContext';
import {useAiHighlights} from './Context/AiHighlightsContext';
import UploadCard from './Components/UploadCard';
import ClippingCard from './Components/ClippingCard';
import UpdateCard from './Components/UpdateCard';
import UnavailableDeviceCard from './Components/UnavailableDeviceCard';
import AnimatedCard from './Components/AnimatedCard';
import {MdOutlineContentCut, MdOutlinePlayCircleOutline, MdOutlineSettings, MdReplay30} from 'react-icons/md';
import {HiOutlineSparkles} from 'react-icons/hi';
import { AnimatePresence } from 'framer-motion';

interface MenuProps {
	selectedMenu: string;
	onSelectMenu: (menu: string) => void;
}

export default function Menu({selectedMenu, onSelectMenu}: MenuProps) {
	const settings = useSettings();
	const {hasLoadedObs, recording, preRecording} = settings.state;
	const {updateInfo} = useUpdate();
	const {aiProgress} = useAiHighlights();
	
	// Check if there are any active AI highlight generations
	const hasActiveAiHighlights = Object.values(aiProgress).length > 0;
	
	const hasUnavailableDevices = () => {
		const unavailableInput = settings.inputDevices.some(
			(deviceSetting: { id: string }) => !settings.state.inputDevices.some(d => d.id === deviceSetting.id)
		);
		const unavailableOutput = settings.outputDevices.some(
			(deviceSetting: { id: string }) => !settings.state.outputDevices.some(d => d.id === deviceSetting.id)
		);
		return unavailableInput || unavailableOutput;
	};

	return (
		<div className="bg-base-300 w-56 h-screen flex flex-col">
			{/* Menu Items */}
			<ul className="menu">
				<li>
					<a
						className={selectedMenu === 'Full Sessions' ? 'py-3 focus' : 'py-3 link-base-content'}
						onMouseDown={() => onSelectMenu('Full Sessions')}
					>
						<MdOutlinePlayCircleOutline className="w-6 h-6" />
						Full Sessions
					</a>
				</li>
				<li>
					<a
						className={selectedMenu === 'Replay Buffer' ? 'py-3 focus' : 'py-3 link-base-content'}
						onMouseDown={() => onSelectMenu('Replay Buffer')}
					>
						<MdReplay30 className="w-6 h-6" />
						Replay Buffer
					</a>
				</li>
				<li>
					<a
						className={selectedMenu === 'Clips' ? 'py-3 focus' : 'py-3 link-base-content'}
						onMouseDown={() => onSelectMenu('Clips')}
					>
						<MdOutlineContentCut className="w-6 h-6" />
						Clips
					</a>
				</li>
				<li>
					<a
						className={selectedMenu === 'Highlights' ? 'py-3 focus' : 'py-3 link-base-content'}
						onMouseDown={() => onSelectMenu('Highlights')}
					>
						<div className="relative w-6 h-6 flex items-center justify-center">
							{/* Single icon with transition for color and pulsing animation */}
							<HiOutlineSparkles 
								className={`w-6 h-6 ${hasActiveAiHighlights ? 'text-purple-400 animate-pulse' : ''}`} 
							/>
						</div>
						<span className={hasActiveAiHighlights ? 'text-purple-400 animate-pulse' : ''}>Highlights</span>
					</a>
				</li>
				<li>
					<a
						className={selectedMenu === 'Settings' ? 'py-3 focus' : 'py-3 link-base-content'}
						onMouseDown={() => onSelectMenu('Settings')}
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
				<AnimatePresence>
					{updateInfo && (
						<AnimatedCard key="update-card">
							<UpdateCard />
						</AnimatedCard>
					)}
				</AnimatePresence>

				<AnimatePresence>
					{Object.values(useUploads().uploads).map((file) => (
						<AnimatedCard key={file.fileName}>
							<UploadCard upload={file} />
						</AnimatedCard>
					))}
				</AnimatePresence>

				{/* Show warning if there are unavailable audio devices */}
				<AnimatePresence>
					{hasUnavailableDevices() && (
						<AnimatedCard key="unavailable-device-card">
							<UnavailableDeviceCard />
						</AnimatedCard>
					)}
				</AnimatePresence>

				<AnimatePresence>
					{ (preRecording || (recording && recording.endTime == null)) && (
						<AnimatedCard key="recording-card">
							<RecordingCard recording={recording} preRecording={preRecording} />
						</AnimatedCard>
					)}
				</AnimatePresence>

				<AnimatePresence>
					{Object.values(useClipping().clippingProgress).map((clipping) => (
						<AnimatedCard key={clipping.id}>
							<ClippingCard clipping={clipping} />
						</AnimatedCard>
					))}
				</AnimatePresence>
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

			{/* Start and Stop Buttons */}
			<div className="mb-4 px-4">
				<div className="flex flex-col items-center space-y-2">
					<button
						className="btn btn-neutral w-full"
						disabled={settings.state.recording != null || !settings.state.hasLoadedObs || settings.state.preRecording != null}
						onClick={() => sendMessageToBackend('StartRecording')}
					>
						Start
					</button>
					<button
						className="btn btn-neutral w-full"
						disabled={!settings.state.recording || !settings.state.hasLoadedObs}
						onClick={() => sendMessageToBackend('StopRecording')}
					>
						Stop
					</button>
				</div>
			</div>
		</div>
	);
}
