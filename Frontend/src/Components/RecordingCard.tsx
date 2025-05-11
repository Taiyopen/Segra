import {useState, useEffect} from "react";
import {Recording} from "../Models/types";
import { MdWarning } from "react-icons/md";

interface RecordingCardProps {
	recording: Recording;
}

const RecordingCard: React.FC<RecordingCardProps> = ({recording}) => {
	const [elapsedTime, setElapsedTime] = useState({hours: 0, minutes: 0, seconds: 0});

	useEffect(() => {
		if (!recording?.startTime) return;

		const startTime = new Date(recording.startTime).getTime(); // Get the timestamp in milliseconds

		const updateElapsedTime = () => {
			const now = Date.now(); // Current time in milliseconds
			const secondsElapsed = Math.max(0, Math.floor((now - startTime) / 1000));

			const hours = Math.floor(secondsElapsed / 3600);
			const minutes = Math.floor((secondsElapsed % 3600) / 60);
			const seconds = secondsElapsed % 60;

			setElapsedTime({hours, minutes, seconds});
		};

		// Update the timer every second
		const intervalId = setInterval(updateElapsedTime, 1000);

		// Clean up the interval when the component unmounts
		return () => clearInterval(intervalId);
	}, [recording?.startTime]);

	return (
		recording && (
			<div className="mb-4 px-2">
				<div className="bg-neutral border border-secondary border-opacity-75 rounded-md px-3 py-3 cursor-default relative overflow-hidden">
					{/* Background image with fade effect */}
					<div className="absolute top-1/2 right-0 w-[25%] h-[80%] -translate-y-1/2 z-0 opacity-20">
						<div className="absolute inset-0" style={{
							backgroundImage: `url(data:image/png;base64,${recording.gameImage})`,
							backgroundSize: "contain",
							backgroundPosition: "right center",
							backgroundRepeat: "no-repeat",
							maskImage: "linear-gradient(to left, rgba(0,0,0,1), rgba(0,0,0,0))",
							WebkitMaskImage: "linear-gradient(to left, rgba(0,0,0,1), rgba(0,0,0,0))"
						}}></div>
					</div>
					{/* Recording Indicator */}
					<div className="flex items-center mb-1 relative z-10">
						<span className="w-3 h-3 rounded-full mr-2 bg-red-500 animate-pulse"></span>
						<span className="text-gray-200 text-sm font-medium">
							Recording
						</span>
					</div>

					{/* Recording Details */}
					<div className="flex items-center text-gray-400 text-sm relative z-10">
						<div className="flex items-center">
							<span className="countdown">
								{elapsedTime.hours > 0 && (
									<>
										<span style={{"--value": elapsedTime.hours} as React.CSSProperties}></span>:
									</>
								)}
								<span style={{"--value": elapsedTime.minutes} as React.CSSProperties}></span>:
								<span style={{"--value": elapsedTime.seconds} as React.CSSProperties}></span>
							</span>
							<p className="truncate mx-2">{recording.game}</p>
						</div>
					</div>

					{/* Display Recording is in Alpha Warning */}
					{!recording.isUsingGameHook && (
						<div className="bg-amber-900 bg-opacity-30 border border-amber-500 rounded px-2 py-1 mt-2 text-amber-400 text-xs flex items-center relative z-10">
							<MdWarning className="h-6 w-6 mr-1" />
							<span>Display Recording is in alpha and will cause lag</span>
						</div>
					)}
				</div>
			</div>
		)
	);
};

export default RecordingCard;
