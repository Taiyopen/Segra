import {useEffect, useState} from 'react';
import Settings from "./Pages/settings";
import Menu from "./menu";
import Videos from './Pages/videos';
import Clips from './Pages/clips';
import ReplayBuffer from './Pages/replay-buffer';
import Highlights from './Pages/highlights';
import {SettingsProvider} from './Context/SettingsContext';
import Video from './Pages/video';
import {ModalProvider} from './Context/ModalContext';
import {useSelectedVideo} from './Context/SelectedVideoContext';
import {themeChange} from 'theme-change';
import {HTML5Backend} from 'react-dnd-html5-backend';
import {DndProvider} from 'react-dnd';
import {SelectionsProvider} from './Context/SelectionsContext';
import {UploadProvider} from './Context/UploadContext';
import {WebSocketProvider} from './Context/WebSocketContext';
import {ClippingProvider} from './Context/ClippingContext';

function App() {
	useEffect(() => {
		themeChange(false);
	}, []);

	const {selectedVideo, setSelectedVideo} = useSelectedVideo();
	const [selectedMenu, setSelectedMenu] = useState('Full Sessions');

	const handleMenuSelection = (menu: any) => {
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
				return <Videos />;
			case 'Replay Buffer':
				return <ReplayBuffer />;
			case 'Clips':
				return <Clips />;
			case 'Highlights':
				return <Highlights />;
			case 'Settings':
				return <Settings />;
			default:
				return <Videos />;
		}
	};

	return (
		<div className="flex h-screen w-screen">
			<div className="h-full">
				<Menu selectedMenu={selectedMenu} onSelectMenu={handleMenuSelection} />
			</div>
			<div className="flex-1 max-h-full overflow-auto">
				{renderContent()}
			</div>
		</div>
	);
}

export default function AppWrapper() {
  return (
    <WebSocketProvider>
      <SettingsProvider>
        <ModalProvider>
          <SelectionsProvider>
            <DndProvider backend={HTML5Backend}>
              <UploadProvider>
                <ClippingProvider>
                  <App />
                </ClippingProvider>
              </UploadProvider>
            </DndProvider>
          </SelectionsProvider>
        </ModalProvider>
      </SettingsProvider>
    </WebSocketProvider>
  );
}
