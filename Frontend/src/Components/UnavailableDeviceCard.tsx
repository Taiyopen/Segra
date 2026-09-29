import React from 'react';
import { CircleAlert } from 'lucide-react';

const UnavailableDeviceCard: React.FC = () => {
  return (
    <div className="mb-4 px-2">
      <div className="bg-error/20 bg-opacity-20 border border-base-400 border-opacity-75 rounded-lg px-3 py-3 cursor-default">
        <div className="flex items-center gap-2">
          <CircleAlert className="text-error w-5 h-5 shrink-0" />
          <p className="text-error text-xs">有些選取的音訊裝置目前無法使用</p>
        </div>
      </div>
    </div>
  );
};

export default UnavailableDeviceCard;
