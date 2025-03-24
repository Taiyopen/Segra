import { useUpdate } from '../Context/UpdateContext';
import { FaDownload, FaCheck, FaExclamationTriangle } from 'react-icons/fa';
import { SiGithub } from 'react-icons/si';
import { sendMessageToBackend } from '../Utils/MessageUtils';

export default function UpdateCard() {
  const { updateInfo, releaseNotes, openReleaseNotesModal, clearUpdateInfo } = useUpdate();

  if (!updateInfo) return null;

  const getStatusIcon = () => {
    switch (updateInfo.status) {
      case 'downloading':
        return <span className="loading loading-spinner loading-md text-primary w-8 h-8"></span>;
      case 'downloaded':
      case 'ready':
        return <FaCheck className="text-success text-xl" />;
      case 'error':
        return <FaExclamationTriangle className="text-error text-xl" />;
      default:
        return <span className="loading loading-spinner loading-md text-primary"></span>;
    }
  };

  const handleInstallClick = () => {
    // Send a message to the backend to restart the application and install the update
    sendMessageToBackend('ApplyUpdate');
    clearUpdateInfo();
  };

  // Compact version for the sidebar
  return (
    <div className="w-full px-2 py-1">
      <div className="bg-neutral shadow-lg rounded-md p-3">
        {/* Header with status and version */}
        <div className="flex items-center justify-between mb-2">
          <div className="flex items-center gap-2">
            <div className="flex items-center justify-center w-8 h-8 bg-base-300 rounded-full">
              {getStatusIcon()}
            </div>
            <div>
              <h3 className="text-sm font-bold text-white">
                {updateInfo.status === 'downloading' 
                  ? 'Update in Progress' 
                  : 'Update Available'}
              </h3>
              <p className="text-xs text-gray-400">
                Version {updateInfo.version}
              </p>
            </div>
          </div>
          
          {updateInfo.status === 'downloading' && (
            <div className="text-primary text-sm font-medium">
              {updateInfo.progress}%
            </div>
          )}
        </div>

        {/* Progress Bar (only show when downloading) */}
        {updateInfo.status === 'downloading' && (
          <div className="w-full mb-3">
            <div className="w-full rounded-full h-2 overflow-hidden">
              <div 
                className="bg-primary h-full rounded-full transition-all duration-300 ease-in-out"
                style={{ width: `${updateInfo.progress}%` }}
              ></div>
            </div>
          </div>
        )}

        {/* Action Buttons */}
        <div className="flex flex-col gap-2">
          {updateInfo.progress === 100 && (
            <button 
              className="btn btn-primary no-animation btn-sm w-full normal-case font-medium"
              onClick={handleInstallClick}
            >
              <FaDownload className="mr-2" /> Install Now
            </button>
          )}

          {/* Release Notes Button (only when there are notes) */}
          {releaseNotes.length > 0 && (
            <button 
              className="btn btn-sm no-animation text-xs text-gray-300 bg-base-300 hover:bg-base-300/70 w-full normal-case flex items-center justify-center gap-2"
              onClick={() => openReleaseNotesModal(__APP_VERSION__)}
            >
              <SiGithub /> View Release Notes
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
