import { useUploads } from '../Context/UploadContext';

interface UploadCardProps {
  fileName: string;
}

export default function UploadCard({ fileName }: UploadCardProps) {
  const { uploads } = useUploads();
  const upload = uploads[fileName];

  if (!upload) return null;

  const getStatusColor = () => {
    switch (upload.status) {
      case 'uploading':
      case 'processing':
        return 'text-primary';
      case 'done':
        return 'text-success';
      case 'error':
        return 'text-error';
      default:
        return 'text-primary';
    }
  };

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
          {/* Radial Progress */}
          <div className={`radial-progress flex-shrink-0 ${getStatusColor()}`} 
          style={{"--value": upload.progress, "--size": "2.5rem"} as React.CSSProperties}>
            <span className="text-xs">{Math.round(upload.progress)}%</span>
          </div>

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
