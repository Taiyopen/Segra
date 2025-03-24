import { ReleaseNote } from '../Models/WebSocketMessages';
import Markdown from 'markdown-to-jsx';
import { useContext, useEffect, useState } from 'react';
import { ReleaseNotesContext } from '../App';

interface ReleaseNotesModalProps {
  onClose: () => void;
  filterVersion: string | null;
}

// Custom CSS for markdown content
const markdownStyles = `
  .markdown-content ul {
    list-style-type: disc;
    padding-top: 0.75rem;
    padding-left: 2rem;
    margin-bottom: 1.5rem;
  }
  
  .markdown-content ul li {
    display: list-item;
    margin-bottom: 0.75rem;
    line-height: 1.75rem;
  }
  
  .markdown-content ul li::marker {
    color: hsl(var(--p));
    font-size: 1.5em;
  }
  
  .markdown-content ol {
    list-style-type: decimal;
    padding-left: 2rem;
    margin-bottom: 1.5rem;
  }
  
  .markdown-content ol li {
    display: list-item;
    margin-bottom: 0.75rem;
    line-height: 1.75rem;
  }
  
  .markdown-content img {
    border-radius: 0.5rem;
    pointer-events: none;
    max-width: 100%;
    margin: 1.5rem 0;
    border: 1px solid rgba(255, 255, 255, 0.1);
  }
`;

// Custom components for markdown rendering with larger text
const MarkdownComponents = {
  p: (props: any) => <p className="mb-4 text-lg leading-relaxed" {...props} />,
  h1: (props: any) => <h1 className="text-3xl font-bold mb-6 text-white" {...props} />,
  h2: (props: any) => <h2 className="text-2xl font-bold mb-4 text-white" {...props} />,
  h3: (props: any) => <h3 className="text-xl font-bold mb-3 text-white" {...props} />,
  ul: (props: any) => <ul className="markdown-content-ul" {...props} />,
  ol: (props: any) => <ol className="markdown-content-ol" {...props} />,
  li: (props: any) => <li className="markdown-content-li" {...props} />,
  a: (props: any) => <a className="text-primary hover:underline" target="_blank" rel="noopener noreferrer" {...props} />,
  code: (props: any) => <code className="bg-base-200 px-1 py-0.5 rounded text-sm" {...props} />,
  pre: (props: any) => <pre className="bg-base-200 p-4 rounded-lg mb-6 overflow-x-auto" {...props} />,
  blockquote: (props: any) => <blockquote className="border-l-4 border-primary pl-4 italic my-6" {...props} />,
  hr: (props: any) => <hr className="my-8 border-gray-700" {...props} />,
  img: (props: any) => <img className="markdown-content-img" {...props} />,
};

/**
 * Compares two semantic version strings to determine if version2 is newer than version1.
 * @param version1 - First version string (e.g., "1.2.3")
 * @param version2 - Second version string (e.g., "1.3.0")
 * @returns true if version2 is newer than version1, false otherwise
 */
function isVersionNewer(version1: string, version2: string): boolean {
  // Split versions into components
  const v1Parts = version1.split('.').map(part => parseInt(part, 10));
  const v2Parts = version2.split('.').map(part => parseInt(part, 10));
  
  // Ensure both arrays have the same length (typically 3 for semantic versioning)
  while (v1Parts.length < 3) v1Parts.push(0);
  while (v2Parts.length < 3) v2Parts.push(0);
  
  // Compare major version
  if (v2Parts[0] > v1Parts[0]) return true;
  if (v2Parts[0] < v1Parts[0]) return false;
  
  // Compare minor version
  if (v2Parts[1] > v1Parts[1]) return true;
  if (v2Parts[1] < v1Parts[1]) return false;
  
  // Compare patch version
  if (v2Parts[2] > v1Parts[2]) return true;
  
  // If we get here, versions are equal or v2 is older
  return false;
}

export default function ReleaseNotesModal({ onClose, filterVersion }: ReleaseNotesModalProps) {
  const [localReleaseNotes, setLocalReleaseNotes] = useState<ReleaseNote[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  
  // Access the global release notes context
  const { releaseNotes: globalReleaseNotes } = useContext(ReleaseNotesContext);
  
  // Update local state when global release notes change
  useEffect(() => {
    if (globalReleaseNotes.length > 0) {
      setLocalReleaseNotes(globalReleaseNotes);
      setIsLoading(false);
    } else {
      // If after 2 seconds we still don't have release notes, stop showing the loader
      const timer = setTimeout(() => {
        setIsLoading(false);
      }, 2000);
      
      return () => clearTimeout(timer);
    }
  }, [globalReleaseNotes]);
  
  // Format date from ISO string (e.g., "2025-02-26T23:37:33Z") to "26 February 2025"
  const formatDate = (isoDate: string): string => {
    try {
      const date = new Date(isoDate);
      return date.toLocaleDateString('en-GB', {
        day: 'numeric',
        month: 'long',
        year: 'numeric'
      });
    } catch (error) {
      console.error('Error formatting date:', error);
      return isoDate; // Return original string if parsing fails
    }
  };

  // Helper function to properly decode base64 strings with UTF-8 characters
  const decodeBase64 = (base64: string): string => {
    try {
      return decodeURIComponent(escape(atob(base64)));
    } catch (error) {
      console.error('Error decoding base64 markdown:', error);
      return 'Error decoding content';
    }
  };

  // Filter notes to only show those newer than the current app version if filterVersion is provided
  const filteredNotes = filterVersion 
    ? localReleaseNotes.filter(note => isVersionNewer(filterVersion, note.version))
    : localReleaseNotes;

  // Decode the base64 content for each release note
  const decodedReleaseNotes = filteredNotes;

  return (
    <>
      <style dangerouslySetInnerHTML={{ __html: markdownStyles }} />
      {/* Header */}
      <div className="modal-header pb-4 border-b border-gray-700">
        <h2 className="font-bold text-3xl mb-2 text-white">Release Notes</h2>
        <p className="text-gray-400 text-lg">
          {filterVersion ? `New updates since v${filterVersion}` : 'See what\'s new in Segra'}
        </p>
        <button 
          className="btn btn-circle btn-ghost absolute right-4 top-4 text-2xl hover:bg-base-100/30" 
          onClick={onClose}
        >
          ✕
        </button>
      </div>
      
      <div className="modal-body pt-4">
        {isLoading ? (
          <div className="flex flex-col items-center justify-center py-10">
            <div className="loading loading-spinner loading-lg text-primary"></div>
          </div>
        ) : decodedReleaseNotes.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-10">
            <p className="text-gray-400 text-xl">
              {filterVersion 
                ? 'You are up to date! No new release notes available.'
                : 'No release notes available.'}
            </p>
          </div>
        ) : (
          decodedReleaseNotes.map((note, index) => (
            <div key={index} className="mb-10 last:mb-0">
              {/* Version badge with date underneath */}
              <div className="mb-6">
                <div className="text-primary rounded-full font-bold text-2xl inline-block">
                  Version {note.version}
                </div>
                <div className="text-gray-400 text-lg">
                  {formatDate(note.releaseDate)}
                </div>
              </div>
              
              {/* Markdown content with custom components for larger text */}
              <div className="text-gray-300 markdown-content text-lg">
                <Markdown options={{ overrides: MarkdownComponents }}>
                  {decodeBase64(note.base64Markdown)}
                </Markdown>
              </div>
              
              {/* Divider except for last item */}
              {index < decodedReleaseNotes.length - 1 && (
                <div className="border-t border-gray-700 mt-8"></div>
              )}
            </div>
          ))
        )}
        
        {/* Show your current version at the bottom */}
        <div className="text-center mt-6 pt-4 border-t border-gray-700">
          <p className="text-gray-400">
            Your current version: <span className="text-primary">{__APP_VERSION__}</span>
          </p>
        </div>
      </div>
    </>
  );
}
