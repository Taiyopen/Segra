import React from "react";

interface ClippingProgress {
    id: number;
    progress: number;
}

interface ClippingCardProps {
    clipping: ClippingProgress;
}

const ClippingCard: React.FC<ClippingCardProps> = ({ clipping }) => {
    const getStatusText = () => {
        if (clipping.progress === 100) {
            return 'Clip Complete';
        }
        return 'Creating Clip...';
    };

    return (
        <div className="w-full px-2">
            <div className="bg-neutral border border-secondary border-opacity-75 rounded-md p-3">
                <div className="flex items-center gap-3 w-full">
                    {/* Progress */}
                    {clipping.progress < 100 ? (
                        <span className="loading loading-spinner text-primary"></span>
                    ) : (
                        <div className="w-4 h-4 rounded-full bg-success"></div>
                    )}

                    {/* Clipping Details */}
                    <div className="min-w-0 flex-1">
                        <div className="text-gray-200 text-sm font-medium truncate">
                            {getStatusText()}
                        </div>
                        <div className="text-gray-400 text-xs truncate">
                            {Math.round(clipping.progress)}%
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default ClippingCard;
