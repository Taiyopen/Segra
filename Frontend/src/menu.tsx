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
		<div className="bg-base-300 w-56 h-screen flex flex-col border-r border-custom">
			{/* Menu Items */}
			<div className="flex flex-col space-y-2 px-4 text-left py-2 relative">  {/* Added relative positioning */}
				{/* Selection indicator rectangle */}
				<div 
					className="absolute w-1.5 bg-primary rounded-r transition-all duration-200 ease-in-out" 
					style={{
						left: 0,
						top: selectedMenu === 'Full Sessions' ? '20px' : 
							selectedMenu === 'Replay Buffer' ? '76px' : 
							selectedMenu === 'Clips' ? '132px' : 
							selectedMenu === 'Highlights' ? '188px' : 
							selectedMenu === 'Settings' ? '244px' : '20px',
						height: '40px',
					}}
				/>
				<button
					className={`btn btn-secondary ${selectedMenu === 'Full Sessions' ? 'bg-base-300 text-primary' : ''} w-full justify-start border-primary hover:border-primary hover:border-opacity-75 py-3`}
					onMouseDown={() => onSelectMenu('Full Sessions')}
				>
					<MdOutlinePlayCircleOutline className="w-6 h-6" />
					Full Sessions
				</button>
				<button
					className={`btn btn-secondary ${selectedMenu === 'Replay Buffer' ? 'bg-base-300 text-primary' : ''} w-full justify-start border-primary hover:border-primary hover:border-opacity-75 py-3`}
					onMouseDown={() => onSelectMenu('Replay Buffer')}
				>
					<MdReplay30 className="w-6 h-6" />
					Replay Buffer
				</button>
				<button
					className={`btn btn-secondary ${selectedMenu === 'Clips' ? 'bg-base-300 text-primary' : ''} w-full justify-start border-primary hover:border-primary hover:border-opacity-75 py-3`}
					onMouseDown={() => onSelectMenu('Clips')}
				>
					<MdOutlineContentCut className="w-6 h-6" />
					Clips
				</button>
				<button
					className={`btn btn-secondary ${selectedMenu === 'Highlights' ? 'bg-base-300 text-primary' : ''} w-full justify-start border-primary hover:border-primary hover:border-opacity-75 py-3`}
					onMouseDown={() => onSelectMenu('Highlights')}
				>
					<div className="relative w-6 h-6 flex items-center justify-center">
						<HiOutlineSparkles 
							className={`w-6 h-6 ${hasActiveAiHighlights ? 'text-purple-400 animate-pulse' : ''}`} 
						/>
					</div>
					<span className={hasActiveAiHighlights ? 'text-purple-400 animate-pulse' : ''}>Highlights</span>
				</button>
				<button
					className={`btn btn-secondary ${selectedMenu === 'Settings' ? 'bg-base-300 text-primary' : ''} w-full justify-start border-primary hover:border-primary hover:border-opacity-75 py-3`}
					onMouseDown={() => onSelectMenu('Settings')}
				>
					<MdOutlineSettings className="w-6 h-6" />
					Settings
				</button>
			</div>

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
				<div className="flex flex-col items-center">
					{settings.state.recording ? (
						<button
							className="btn btn-secondary border-primary border-opacity-75 hover:border-primary hover:border-opacity-75 w-full"
							disabled={!settings.state.hasLoadedObs}
							onClick={() => sendMessageToBackend('StopRecording')}
						>
							Stop Recording
						</button>
					) : (
						<button
							className="btn btn-secondary border-primary border-opacity-75 hover:border-primary hover:border-opacity-75 w-full"
							disabled={!settings.state.hasLoadedObs || settings.state.preRecording != null}
							onClick={() => sendMessageToBackend('StartRecording')}
						>
							Start Recording
						</button>
					)}
				</div>
			</div>
		</div>
	);
}
