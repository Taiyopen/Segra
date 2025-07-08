import {useState, useEffect} from "react";
import {PreRecording, Recording} from "../Models/types";
import { LuGamepad2 } from "react-icons/lu";
import { BsDisplay } from "react-icons/bs";

interface RecordingCardProps {
	recording?: Recording;
	preRecording?: PreRecording;
}

const RecordingCard: React.FC<RecordingCardProps> = ({recording, preRecording}) => {
	const [elapsedTime, setElapsedTime] = useState({hours: 0, minutes: 0, seconds: 0});

	useEffect(() => {
		if(preRecording) {
			setElapsedTime({hours: 0, minutes: 0, seconds: 0});
			return;
		}

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
	}, [recording?.startTime, preRecording]);

	return (
		<div className="mb-2 px-2">
			<div className="bg-base-300 border border-primary border-opacity-75 rounded-md px-3 py-3 cursor-default relative">
				{/* Background image with fade effect */}
				<div className="absolute top-1/2 right-0.5 w-[25%] h-[80%] -translate-y-1/2 z-0 opacity-20">
					<div className="absolute inset-0" style={{
						backgroundImage: `url(data:image/png;base64,${recording?.gameImage})`,
						backgroundSize: "contain",
						backgroundPosition: "right center",
						backgroundRepeat: "no-repeat",
						maskImage: "linear-gradient(to left, rgba(0,0,0,1), rgba(0,0,0,0))",
						WebkitMaskImage: "linear-gradient(to left, rgba(0,0,0,1), rgba(0,0,0,0))"
					}}></div>
				</div>

				{/* Recording Indicator */}
				<div className="flex items-center mb-1 relative z-10">
					<div className="flex items-center">
						<span className={`w-3 h-3 rounded-full mr-2 ${preRecording ? 'bg-orange-500' : 'bg-red-500 animate-pulse'}`}></span>
						<span className="text-gray-200 text-sm font-medium">
							{preRecording ? preRecording.status : 'Recording'}
						</span>
					</div>
					{!preRecording && (
						<div className={`tooltip tooltip-right ${recording?.isUsingGameHook ? 'tooltip-success' : 'tooltip-warning'} flex items-center ml-1.5`} data-tip={`${recording?.isUsingGameHook ? 'Game capture (using game hook)' : 'Display capture (not using game hook)'}`}>
							<div className={`swap swap-flip cursor-default overflow-hidden justify-center`}>
								<input type="checkbox" checked={recording?.isUsingGameHook} />
								<div className={`swap-on`}>
									<LuGamepad2 className="h-5 w-5 text-gray-300" />
								</div>	
								<div className={`swap-off`}>
									<BsDisplay className="h-5 w-5 text-gray-300 scale-90" />
								</div>
							</div>
						</div>
					)
				}
				</div>

				{/* Recording Details */}
				<div className="flex items-center text-gray-400 text-sm relative z-10">
					<div className="flex items-center max-w-[105%]">
						<span className="countdown">
							{elapsedTime.hours > 0 && (
								<>
									<span style={{"--value": elapsedTime.hours} as React.CSSProperties}></span>:
								</>
							)}
							<span style={{"--value": elapsedTime.minutes} as React.CSSProperties}></span>:
							<span style={{"--value": elapsedTime.seconds} as React.CSSProperties}></span>
						</span>
						<p className="truncate ml-2">{preRecording ? preRecording.game : recording?.game}</p>
					</div>
				</div>
			</div>
		</div>
	)
};

export default RecordingCard;
