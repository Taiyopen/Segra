import { useState, useEffect, useRef } from 'react';
import { Content } from '../Models/types';
import { useSettings } from '../Context/SettingsContext';
import { useAuth } from '../Hooks/useAuth';

interface UploadModalProps {
  video: Content;
  onUpload: (title: string, visibility: 'Public' | 'Unlisted') => void;
  onClose: () => void;
}

export default function UploadModal({ video, onUpload, onClose }: UploadModalProps) {
  const {contentFolder} = useSettings();
  const { session } = useAuth();
  const [title, setTitle] = useState(video.title || '');
  const [visibility, setVisibility] = useState<'Public' | 'Unlisted'>('Public');
  const titleInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    titleInputRef.current?.focus();
  }, []);

  const handleUpload = () => {
    onUpload(title, visibility);
    onClose();
  };

  const getVideoPath = (): string => {
    const contentFileName = `${contentFolder}/${video.type.toLowerCase()}s/${video.fileName}.mp4`;
    return `http://localhost:2222/api/content?input=${encodeURIComponent(contentFileName)}&type=${video.type.toLowerCase()}`;
  };

  return (
    <>
      <div className="modal-header">
        <button className="btn btn-sm btn-circle btn-ghost absolute right-4 top-2" onClick={onClose}>✕</button>
      </div>
      <div className="modal-body">
        <div className="w-full aspect-video mb-4 mt-4">
          <video 
            src={getVideoPath()}
            autoPlay
            muted
            className="w-full h-full object-contain bg-base-300 rounded-lg"
            controls
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
            onChange={(e) => setTitle(e.target.value)}
            className="input input-bordered w-full" 
          />
        </div>

        <div className="form-control w-full mt-4">
          <label className="label">
            <span className="label-text">Visibility</span>
          </label>
          <select 
            className="select select-bordered w-full"
            value={visibility}
            onChange={(e) => setVisibility(e.target.value as 'Public' | 'Unlisted')}
          >
            <option value="Public">Public</option>
            <option value="Unlisted">Unlisted</option>
          </select>
        </div>
      </div>
      <div className="modal-action mt-6">
        <button 
          className="btn btn-primary text-white font-semibold w-full"
          onClick={handleUpload}
          disabled={session === null}
        >
          {session === null ? 'Login to upload' : 'Upload'}
        </button>
      </div>
    </>
  );
}
