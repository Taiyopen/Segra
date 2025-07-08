import { useState, useEffect, useRef } from 'react';
import { Content } from '../Models/types';
import { useSettings, useSettingsUpdater } from '../Context/SettingsContext';
import { useAuth } from '../Hooks/useAuth.tsx';
import { MdOutlineFileUpload } from 'react-icons/md';

interface UploadModalProps {
  video: Content;
  onUpload: (title: string, visibility: 'Public' | 'Unlisted') => void;
  onClose: () => void;
}

export default function UploadModal({ video, onUpload, onClose }: UploadModalProps) {
  const {contentFolder, clipShowInBrowserAfterUpload} = useSettings();
  const updateSettings = useSettingsUpdater();
  const { session } = useAuth();
  const [title, setTitle] = useState(video.title || '');
  const [visibility] = useState<'Public' | 'Unlisted'>('Public');
  const [titleError, setTitleError] = useState(false);
  const titleInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    titleInputRef.current?.focus();
  }, []);

  const handleUpload = () => {
    if (!title.trim()) {
      setTitleError(true);
      titleInputRef.current?.focus();
      return;
    }
    setTitleError(false);
    onUpload(title, visibility);
    onClose();
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      handleUpload();
    }
  };

  const getVideoPath = (): string => {
    const contentFileName = `${contentFolder}/${video.type.toLowerCase()}s/${video.fileName}.mp4`;
    return `http://localhost:2222/api/content?input=${encodeURIComponent(contentFileName)}&type=${video.type.toLowerCase()}s`;
  };

  return (
    <>
      <div className="bg-base-300">
        <div className="modal-header">
          <button className="btn btn-sm btn-circle btn-ghost absolute right-4 top-2" onClick={onClose}>✕</button>
        </div>
        <div className="modal-body">
          <div className="w-full aspect-video mb-4 mt-4">
            <video 
              src={getVideoPath()}
              autoPlay
              muted
              loop
              className="w-full h-full object-contain bg-base-300 rounded-lg"
            />
          </div>
          
          <div className="form-control w-full">
            <label className="label">
              <span className="label-text">Title</span>
            </label>
            <input 
              ref={titleInputRef}
              type="text" 
              value={title}
              onChange={(e) => {
                setTitle(e.target.value);
                setTitleError(false);
              }}
              onKeyDown={handleKeyPress}
              className={`input input-bordered bg-base-300 w-full ${titleError ? 'input-error' : ''}`}
            />
            {titleError && (
              <label className="label">
                <span className="label-text-alt text-error">Title is required</span>
              </label>
            )}
          </div>
          
          <div className="form-control mt-2">
            <label className="label cursor-pointer justify-start gap-2">
              <input 
                type="checkbox" 
                className="checkbox checkbox-primary"
                checked={clipShowInBrowserAfterUpload} 
                onChange={(e) => updateSettings({ clipShowInBrowserAfterUpload: e.target.checked })}
              />
              <span className="label-text">Open in browser after upload</span>
            </label>
          </div>

        </div>
        <div className="modal-action mt-6">
          <button 
            className="btn btn-secondary bg-base-300 h-10 text-gray-400 border-primary hover:text-accent hover:border-primary flex items-center gap-1 w-full"
            onClick={handleUpload}
            disabled={session === null}
          >
            <MdOutlineFileUpload className="w-5 h-5" />
            {session === null ? 'Login to upload' : 'Upload'}
          </button>
        </div>
      </div>
    </>
  );
}
