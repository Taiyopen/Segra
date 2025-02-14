import { useUploads } from '../Context/UploadContext';

interface UploadCardProps {
  fileName: string;
}

export default function UploadCard({ fileName }: UploadCardProps) {
  const { uploads } = useUploads();
  const upload = uploads[fileName];

  if (!upload) return null;

  const getStatusText = () => {
    switch (upload.status) {
      case 'uploading':
        return 'Uploading';
      case 'processing':
        return 'Processing...';
      case 'done':
        return 'Upload Complete';
      case 'error':
        return upload.message || 'Upload Failed';
      default:
        return 'Uploading...';
    }
  };

  return (
    <div className="w-full px-2">
      <div className="bg-neutral border border-secondary border-opacity-75 rounded-md p-3">
        <div className="flex items-center gap-3 w-full">
          {/* Progress */}
          <span className="loading loading-spinner text-primary"></span>

          {/* Upload Details */}
          <div className="min-w-0 flex-1">
            <div className="text-gray-200 text-sm font-medium truncate">{getStatusText()}</div>
            <div className="text-gray-400 text-xs truncate">{fileName}</div>
          </div>
        </div>
      </div>
    </div>
  );
}
