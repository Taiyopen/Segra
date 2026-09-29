import { useState, useRef, useEffect } from 'react';
import { Content } from '../Models/types';
import { useSettings, useSettingsUpdater } from '../Context/SettingsContext';
import { useAuth } from '../Hooks/useAuth.tsx';
import { Upload, Globe, EyeOff } from 'lucide-react';
import Button from './Button';
import DropdownSelect from './DropdownSelect';

interface UploadModalProps {
  video: Content;
  onUpload: (title: string, description: string, visibility: 'Public' | 'Unlisted') => void;
  onClose: () => void;
}

export default function UploadModal({ video, onUpload, onClose }: UploadModalProps) {
  const { clipShowInBrowserAfterUpload } = useSettings();
  const updateSettings = useSettingsUpdater();
  const { session } = useAuth();
  const [title, setTitle] = useState(video.title || '');
  const [description, setDescription] = useState('');
  const [visibility, setVisibility] = useState<'Public' | 'Unlisted'>(() =>
    localStorage.getItem('uploadVisibility') === 'Unlisted' ? 'Unlisted' : 'Public',
  );

  const handleVisibilityChange = (value: string) => {
    const next = value === 'Unlisted' ? 'Unlisted' : 'Public';
    setVisibility(next);
    localStorage.setItem('uploadVisibility', next);
  };
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
    onUpload(title, description, visibility);
    onClose();
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      handleUpload();
    }
  };

  return (
    <>
      <div className="bg-base-300">
        <div className="modal-header">
          <Button variant="ghost" icon className="absolute right-2 top-1 z-10" onClick={onClose}>
            ✕
          </Button>
        </div>
        <div className="modal-body pt-3">
          <div className="form-control w-full">
            <label className="label">
              <span className="label-text text-base-content">
                標題 <span className="text-error">*</span>
              </span>
            </label>
            <input
              ref={titleInputRef}
              type="text"
              tabIndex={1}
              placeholder="輸入標題"
              value={title}
              onChange={(e) => {
                setTitle(e.target.value);
                setTitleError(false);
              }}
              onKeyDown={handleKeyPress}
              className={`input input-bordered bg-base-300 w-full focus:outline focus:outline-1 focus:outline-white focus:outline-offset-0 ${titleError ? 'input-error' : ''}`}
            />
            {titleError && (
              <label className="label mt-1">
                <span className="label-text-alt text-error">請輸入標題</span>
              </label>
            )}
          </div>

          <div className="form-control w-full mt-4">
            <label className="label">
              <span className="label-text text-base-content">說明</span>
            </label>
            <textarea
              tabIndex={2}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={4}
              className="textarea textarea-bordered bg-base-300 w-full focus:outline focus:outline-1 focus:outline-white focus:outline-offset-0 resize-none"
              placeholder="加上說明（選填）"
            />
          </div>

          <div className="form-control w-full mt-4">
            <label className="label">
              <span className="label-text text-base-content">公開範圍</span>
            </label>
            <DropdownSelect
              items={[
                {
                  value: 'Public',
                  label: (
                    <span className="flex items-center gap-2">
                      <Globe size={16} />
                      公開
                    </span>
                  ),
                },
                {
                  value: 'Unlisted',
                  label: (
                    <span className="flex items-center gap-2">
                      <EyeOff size={16} />
                      不公開列出
                    </span>
                  ),
                },
              ]}
              value={visibility}
              onChange={handleVisibilityChange}
              align="start"
            />
          </div>

          <div className="form-control mt-4">
            <label className="label cursor-pointer justify-start gap-2">
              <input
                type="checkbox"
                tabIndex={4}
                className="checkbox checkbox-primary focus:!outline focus:!outline-1 focus:!outline-white focus:!outline-offset-2"
                checked={clipShowInBrowserAfterUpload}
                onChange={(e) => updateSettings({ clipShowInBrowserAfterUpload: e.target.checked })}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    updateSettings({ clipShowInBrowserAfterUpload: !clipShowInBrowserAfterUpload });
                  }
                }}
              />
              <span className="label-text text-base-content">上傳後在瀏覽器開啟</span>
            </label>
          </div>
        </div>
        <div className="modal-action mt-6">
          <Button
            variant="primary"
            tabIndex={5}
            className="w-full focus:!outline focus:!outline-1 focus:!outline-white focus:!outline-offset-2"
            onClick={handleUpload}
            disabled={session === null}
          >
            <Upload className="w-5 h-5" />
            {session === null ? '登入後才能上傳' : '上傳'}
          </Button>
        </div>
      </div>
    </>
  );
}
