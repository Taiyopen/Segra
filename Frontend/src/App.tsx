import { useEffect, useState, createContext, useRef } from 'react';
import Settings from './Pages/settings';
import Menu from './menu';
import Sessions from './Pages/sessions';
import Clips from './Pages/clips';
import ReplayBuffer from './Pages/replay-buffer';
import PendingEdit from './Pages/pending-edit';
import Highlights from './Pages/highlights';
import { SettingsProvider } from './Context/SettingsContext';
import { AppStateProvider } from './Context/AppStateContext';
import Video from './Pages/video';
import { useSelectedVideo } from './Context/SelectedVideoContext';
import { useSelectedMenu } from './Context/SelectedMenuContext';
import { themeChange } from 'theme-change';
import { HTML5Backend } from 'react-dnd-html5-backend';
import { DndProvider } from 'react-dnd';
import { SegmentsProvider, useSegments } from './Context/SegmentsContext';
import { Content, MenuItemId, menuItemHasContent, normalizeMenuItems } from './Models/types';
import { useSettings } from './Context/SettingsContext';
import { useAppState } from './Context/AppStateContext';
import { UploadProvider } from './Context/UploadContext';
import { ImportProvider } from './Context/ImportContext';
import { ContentMigrationProvider } from './Context/ContentMigrationContext';
import { WebSocketProvider } from './Context/WebSocketContext';
import { ClippingProvider } from './Context/ClippingContext';
import { AiHighlightsProvider } from './Context/AiHighlightsContext';
import { CompressionProvider } from './Context/CompressionContext';
import { UpdateProvider } from './Context/UpdateContext';
import { ObsDownloadProvider } from './Context/ObsDownloadContext';
import { ReleaseNote } from './Models/WebSocketMessages';
import { ScrollProvider } from './Context/ScrollContext';
import { ModalProvider } from './Context/ModalContext';
import { GeneralMessagesProvider } from './Context/GeneralMessagesContext';
import MigrationOverlay from './Components/MigrationOverlay';
import SetupProfileModal from './Components/SetupProfileModal';
import { useAuth } from './Hooks/useAuth';
import { useProfile } from './Hooks/useUserProfile';
import { MonitoringLayoutProvider } from './Context/MonitoringLayoutContext';

// Create a context for release notes that can be accessed globally
export const ReleaseNotesContext = createContext<{
  releaseNotes: ReleaseNote[];
  setReleaseNotes: (notes: ReleaseNote[]) => void;
}>({
  releaseNotes: [],
  setReleaseNotes: () => {},
});

function App() {
  useEffect(() => {
    themeChange(false);
  }, []);

  const { session } = useAuth();
  const { data: profile } = useProfile();
  const needsUsername = session && profile?.username?.startsWith('user_');

  const settings = useSettings();
  const appState = useAppState();
  const { selectedVideo, setSelectedVideo } = useSelectedVideo();
  const { selectedMenu, setSelectedMenu } = useSelectedMenu();
  const { renameSegmentsForVideo } = useSegments();
  const selectedVideoRef = useRef<Content | null>(null);
  useEffect(() => {
    selectedVideoRef.current = selectedVideo;
  }, [selectedVideo]);

  // If the current menu becomes hidden (and has no content keeping it visible),
  // fall back to the default (or first reachable item).
  useEffect(() => {
    const items = normalizeMenuItems(settings.menuItems);

    const isReachable = (id: MenuItemId) =>
      id === 'Settings' ||
      items.find((m) => m.id === id)?.visible === true ||
      menuItemHasContent(id, appState.content);

    if (isReachable(selectedMenu as MenuItemId)) return;

    const defaultId = (settings.defaultMenuItem ?? 'Full Sessions') as MenuItemId;
    const fallback =
      (isReachable(defaultId) ? defaultId : null) ?? items.find((m) => isReachable(m.id))?.id;
    if (fallback) {
      setSelectedMenu(fallback);
    }
  }, [
    settings.menuItems,
    settings.defaultMenuItem,
    selectedMenu,
    setSelectedMenu,
    appState.content,
  ]);

  useEffect(() => {
    const handleContentRenamed = (event: CustomEvent<any>) => {
      const data = event.detail;
      if (data.method !== 'ContentRenamed') return;

      const { OldFileName, Content: renamedContent } = data.content as {
        OldFileName: string;
        Content?: Content;
      };

      if (!renamedContent) return;

      if (selectedVideoRef.current?.fileName === OldFileName) {
        setSelectedVideo(renamedContent);
      }

      renameSegmentsForVideo(OldFileName, renamedContent.fileName, renamedContent.filePath);

      try {
        const viewedContent = localStorage.getItem('viewed-content') || '{}';
        const viewedContentObj = JSON.parse(viewedContent);
        if (viewedContentObj[OldFileName]) {
          viewedContentObj[renamedContent.fileName] = true;
          delete viewedContentObj[OldFileName];
          localStorage.setItem('viewed-content', JSON.stringify(viewedContentObj));
        }
      } catch {
        /* no-op */
      }
    };

    window.addEventListener('websocket-message', handleContentRenamed as EventListener);
    return () =>
      window.removeEventListener('websocket-message', handleContentRenamed as EventListener);
  }, [setSelectedVideo, renameSegmentsForVideo]);

  const handleMenuSelection = (menu: MenuItemId | string) => {
    setSelectedVideo(null);
    setSelectedMenu(menu);
  };

  const renderContent = () => {
    if (selectedVideo) {
      return (
        <DndProvider backend={HTML5Backend}>
          <Video video={selectedVideo} />
        </DndProvider>
      );
    }

    switch (selectedMenu) {
      case 'Full Sessions':
        return <Sessions />;
      case 'Replay Buffer':
        return <ReplayBuffer />;
      case '待剪輯':
        return <PendingEdit />;
      case 'Clips':
        return <Clips />;
      case 'Highlights':
        return <Highlights />;
      case 'Settings':
        return <Settings />;
      default:
        return <Sessions />;
    }
  };

  return (
    <div className="flex h-screen w-screen">
      {needsUsername && <SetupProfileModal />}
      <div className="h-full">
        <Menu selectedMenu={selectedMenu} onSelectMenu={handleMenuSelection} />
      </div>
      <div className="flex-1 max-h-full overflow-auto">{renderContent()}</div>
    </div>
  );
}

export default function AppWrapper() {
  const [releaseNotes, setReleaseNotes] = useState<ReleaseNote[]>([]);

  return (
    <WebSocketProvider>
      <MigrationOverlay />
      <ScrollProvider>
        <SettingsProvider>
          <AppStateProvider>
            <MonitoringLayoutProvider>
              <ReleaseNotesContext.Provider value={{ releaseNotes, setReleaseNotes }}>
                <ModalProvider>
                  <GeneralMessagesProvider>
                    <SegmentsProvider>
                      <DndProvider backend={HTML5Backend}>
                        <UploadProvider>
                          <ImportProvider>
                            <ContentMigrationProvider>
                              <ClippingProvider>
                                <AiHighlightsProvider>
                                  <CompressionProvider>
                                    <UpdateProvider>
                                      <ObsDownloadProvider>
                                        <App />
                                      </ObsDownloadProvider>
                                    </UpdateProvider>
                                  </CompressionProvider>
                                </AiHighlightsProvider>
                              </ClippingProvider>
                            </ContentMigrationProvider>
                          </ImportProvider>
                        </UploadProvider>
                      </DndProvider>
                    </SegmentsProvider>
                  </GeneralMessagesProvider>
                </ModalProvider>
              </ReleaseNotesContext.Provider>
            </MonitoringLayoutProvider>
          </AppStateProvider>
        </SettingsProvider>
      </ScrollProvider>
    </WebSocketProvider>
  );
}
