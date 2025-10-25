import { useState, useRef, useEffect } from 'react';
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
  const { contentFolder, clipShowInBrowserAfterUpload } = useSettings();
  const updateSettings = useSettingsUpdater();
  const { session } = useAuth();
  const [title, setTitle] = useState(video.title || '');
  const [visibility] = useState<'Public' | 'Unlisted'>('Public');
  const [titleError, setTitleError] = useState(false);
  const titleInputRef = useRef<HTMLInputElement>(null);

  // Focus on title input when modal opens (hacky but works)
  useEffect(() => {
    const timer = setTimeout(() => {
      const el = titleInputRef.current;
      if (!el) return;
      el.focus();
      el.select();
    }, 100);
    return () => clearTimeout(timer);
  }, [video.fileName]);

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
          <button
            className="btn btn-circle btn-ghost absolute right-4 top-1 z-10 text-lg hover:bg-white/10"
            onClick={onClose}
          >
            ✕
          </button>
        </div>
        <div className="modal-body pt-8">
          <div className="w-full aspect-video mb-4">
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
              <span className="label-text text-base-content">Title</span>
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
              className={`input input-bordered bg-base-300 w-full focus:outline-none ${titleError ? 'input-error' : ''}`}
            />
            {titleError && (
              <label className="label mt-1">
                <span className="label-text-alt text-error">Title is required</span>
              </label>
            )}
          </div>

          <div className="form-control mt-4">
            <label className="label cursor-pointer justify-start gap-2">
              <input
                type="checkbox"
                className="checkbox checkbox-primary"
                checked={clipShowInBrowserAfterUpload}
                onChange={(e) => updateSettings({ clipShowInBrowserAfterUpload: e.target.checked })}
              />
              <span className="label-text text-base-content">Open in Browser After Upload</span>
            </label>
          </div>
        </div>
        <div className="modal-action mt-6">
          <button
            className="btn btn-secondary bg-base-300 h-10 text-gray-400 border-base-400 hover:text-primary hover:border-base-400 hover:bg-base-300 flex items-center gap-1 w-full"
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
