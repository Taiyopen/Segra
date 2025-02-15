import {useState, useEffect} from "react";
import {Recording} from "../Models/types";

interface RecordingCardProps {
	recording: Recording;
}

const RecordingCard: React.FC<RecordingCardProps> = ({recording}) => {
	const [elapsedTime, setElapsedTime] = useState({minutes: 0, seconds: 0});

	useEffect(() => {
		if (!recording?.startTime) return;

		const startTime = new Date(recording.startTime).getTime(); // Get the timestamp in milliseconds

		const updateElapsedTime = () => {
			const now = Date.now(); // Current time in milliseconds
			const secondsElapsed = Math.max(0, Math.floor((now - startTime) / 1000));

			const minutes = Math.floor(secondsElapsed / 60);
			const seconds = secondsElapsed % 60;

			setElapsedTime({minutes, seconds});
		};

		// Update the timer every second
		const intervalId = setInterval(updateElapsedTime, 1000);

		// Clean up the interval when the component unmounts
		return () => clearInterval(intervalId);
	}, [recording?.startTime]);

	return (
		recording && (
			<div className="mb-4 px-2">
				<div className="bg-neutral border border-secondary border-opacity-75 rounded-md px-3 py-3">
					{/* Recording Indicator */}
					<div className="flex items-center mb-1">
						<span className={`w-3 h-3 rounded-full mr-2 ${elapsedTime.minutes === 0 && elapsedTime.seconds === 0
							? 'bg-orange-500'
							: 'bg-red-500 animate-pulse'
							}`}></span>
						<span className="text-gray-200 text-sm font-medium">
							{elapsedTime.minutes === 0 && elapsedTime.seconds === 0
								? 'Initializing'
								: 'Recording'
							}
						</span>
					</div>

					{/* Recording Details */}
					<div className="flex justify-between items-center text-gray-400 text-sm">
						<span className="countdown">
							<span style={{"--value": elapsedTime.minutes} as React.CSSProperties}></span>:
							<span style={{"--value": elapsedTime.seconds} as React.CSSProperties}></span>
						</span>
						<p className="ps-2 pe-2 truncate whitespace-nowrap">{recording.game}</p>
					</div>
				</div>
			</div>
		)
	);
};

export default RecordingCard;
