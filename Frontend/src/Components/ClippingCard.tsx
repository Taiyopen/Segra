import React, { useState, useEffect } from "react";
import { useClipping } from "../Hooks/useClipping";
import { MdClose } from "react-icons/md";

import { ClippingProgress } from '../Context/ClippingContext';

interface ClippingCardProps {
    clipping: ClippingProgress;
}

const ClippingCard: React.FC<ClippingCardProps> = ({ clipping }) => {
    const { cancelClip } = useClipping();
    const [displayProgress, setDisplayProgress] = useState(0);

    useEffect(() => {
        if (clipping.progress > 95) {
            setDisplayProgress(clipping.progress);
            return;
        }

        const timer = setInterval(() => {
            setDisplayProgress(prev => {
                const diff = clipping.progress - prev;
                if (Math.abs(diff) < 0.1) return clipping.progress;
                return prev + (diff * 0.15);
            });
        }, 50);

        return () => clearInterval(timer);
    }, [clipping.progress]);

    return (
        <div className="w-full px-2">
            <div className="bg-base-300 border border-primary border-opacity-75 rounded-lg p-3">
                <div className="flex items-center gap-3 w-full relative">
                    {/* Progress */}
                    {clipping.progress < 100 ? (
                        <span className="loading loading-spinner text-primary"></span>
                    ) : (
                        <div className="w-4 h-4 rounded-full bg-success"></div>
                    )}

                    {/* Clipping Details */}
                    <div className="min-w-0 flex-1">
                        {clipping.progress < 100 && (
                            <button
                                onClick={() => cancelClip(clipping.id)}
                                className="absolute right-0 top-1/2 -translate-y-1/2 p-1 text-red-500 hover:text-red-400 transition-colors"
                                aria-label="Cancel clip"
                            >
                                <MdClose size={16} />
                            </button>
                        )}
                        <div className="text-gray-200 text-sm font-medium truncate">
                            Creating Clip
                        </div>
                        <div className="text-gray-400 text-xs truncate">
                            {Math.round(displayProgress)}%
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default ClippingCard;
