import React from 'react';
import { MdError } from 'react-icons/md';

const UnavailableDeviceCard: React.FC = () => {

  return (
    <div className="mb-4 px-2">
      <div className="bg-error bg-opacity-20 border border-secondary border-opacity-75 rounded-md px-3 py-3 cursor-default">
        <div className="flex items-center gap-2">
          <MdError className="text-error w-5 h-5 flex-shrink-0" />
          <div>
            <h2 className="text-gray-200 text-sm font-medium">Unavailable Audio Devices</h2>
            <p className="text-gray-400 text-xs">Some selected audio devices are unavailable</p>
          </div>
        </div>
      </div>
    </div>
  );
};

export default UnavailableDeviceCard;
