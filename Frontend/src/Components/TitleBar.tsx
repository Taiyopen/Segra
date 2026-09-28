import { useEffect, useRef, useState, type MouseEvent } from 'react';
import { Copy, Download, Minus, Square, TriangleAlert, X } from 'lucide-react';
import { useUpdate } from '../Context/UpdateContext';
import { useWebSocketContext } from '../Context/WebSocketContext';
import { sendMessageToBackend } from '../Utils/MessageUtils';

// Two presses on the drag area within this window count as a double-click. The native move loop that a
// drag starts swallows the mouse-up, so the browser never fires its own dblclick there.
const DOUBLE_CLICK_MS = 400;

function windowCommand(action: string) {
  sendMessageToBackend('MainWindowCommand', { action });
}

function UpdateStatus() {
  const { updateInfo, openReleaseNotesModal, clearUpdateInfo } = useUpdate();
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menuOpen) return;
    const closeOnOutsideClick = (event: globalThis.MouseEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) setMenuOpen(false);
    };
    window.addEventListener('mousedown', closeOnOutsideClick);
    return () => window.removeEventListener('mousedown', closeOnOutsideClick);
  }, [menuOpen]);

  if (!updateInfo) return null;

  if (updateInfo.status === 'error') {
    return (
      <span className="flex items-center gap-1 px-2 text-xs text-error" title={updateInfo.message}>
        <TriangleAlert className="h-3.5 w-3.5" />
        更新失敗
      </span>
    );
  }

  if (updateInfo.status === 'downloading') {
    return (
      <span
        className="flex items-center gap-1.5 px-2 text-xs text-gray-400"
        title={`正在下載 ${updateInfo.version}`}
      >
        <span className="loading loading-spinner loading-xs text-primary" />
        下載更新 {Math.round(updateInfo.progress)}%
      </span>
    );
  }

  const install = () => {
    setMenuOpen(false);
    sendMessageToBackend('ApplyUpdate');
    clearUpdateInfo();
  };

  return (
    <div ref={menuRef} className="relative">
      <button
        type="button"
        onClick={() => setMenuOpen((open) => !open)}
        className="flex h-6 cursor-pointer items-center gap-1.5 rounded-full bg-success/15 px-2.5 text-xs font-medium text-success transition-colors hover:bg-success/25"
      >
        <Download className="h-3.5 w-3.5" />
        新版本 {updateInfo.version}
      </button>
      {menuOpen && (
        <div className="absolute right-0 top-full z-[100] mt-1.5 w-44 rounded-lg border border-base-400 bg-base-300 p-1 shadow-xl">
          <button
            type="button"
            disabled={updateInfo.progress !== 100}
            onClick={install}
            className="w-full cursor-pointer rounded-md px-3 py-1.5 text-left text-sm hover:bg-base-100 disabled:cursor-not-allowed disabled:opacity-50"
          >
            立即更新並重啟
          </button>
          <button
            type="button"
            onClick={() => {
              setMenuOpen(false);
              openReleaseNotesModal(__APP_VERSION__);
            }}
            className="w-full cursor-pointer rounded-md px-3 py-1.5 text-left text-sm hover:bg-base-100"
          >
            查看更新內容
          </button>
        </div>
      )}
    </div>
  );
}

function WindowButton({
  title,
  onClick,
  danger,
  children,
}: {
  title: string;
  onClick: () => void;
  danger?: boolean;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={`flex h-full w-11 items-center justify-center text-gray-400 transition-colors ${
        danger ? 'hover:bg-[#c42b1c] hover:text-white' : 'hover:bg-white/10 hover:text-white'
      }`}
    >
      {children}
    </button>
  );
}

/**
 * Main window title bar: drag area, update status and window buttons. Hidden until the backend reports it removed
 * the native caption (Windows only), so the page never shows a second bar.
 */
export default function TitleBar() {
  const { isConnected } = useWebSocketContext();
  const [enabled, setEnabled] = useState(false);
  const [maximized, setMaximized] = useState(false);
  const lastPressRef = useRef(0);

  useEffect(() => {
    const handleMessage = (event: CustomEvent) => {
      if (event.detail?.method === 'MainWindowState') {
        setEnabled(!!event.detail.content?.customTitleBar);
        setMaximized(!!event.detail.content?.maximized);
      }
    };
    window.addEventListener('websocket-message', handleMessage as EventListener);
    return () => window.removeEventListener('websocket-message', handleMessage as EventListener);
  }, []);

  // The state reply comes over the WebSocket, so ask again whenever it (re)connects
  useEffect(() => {
    if (isConnected) windowCommand('sync');
  }, [isConnected]);

  const onDragAreaMouseDown = (event: MouseEvent) => {
    if (event.button !== 0) return;
    const now = Date.now();
    if (now - lastPressRef.current < DOUBLE_CLICK_MS) {
      lastPressRef.current = 0;
      windowCommand('toggleMaximize');
      return;
    }
    lastPressRef.current = now;
    windowCommand('drag');
  };

  const startTopResize = (action: string) => (event: MouseEvent) => {
    if (event.button !== 0) return;
    event.stopPropagation();
    windowCommand(action);
  };

  if (!enabled) return null;

  return (
    <div className="relative flex h-8 shrink-0 select-none items-center border-b border-base-400 bg-base-300">
      {/* The native top resize border is gone, so resizing from the top edge starts here */}
      {!maximized && (
        <>
          <div
            className="absolute inset-x-2 top-0 z-10 h-1 cursor-ns-resize"
            onMouseDown={startTopResize('resizeTop')}
          />
          <div
            className="absolute left-0 top-0 z-10 h-1 w-2 cursor-nwse-resize"
            onMouseDown={startTopResize('resizeTopLeft')}
          />
          <div
            className="absolute right-0 top-0 z-10 h-1 w-2 cursor-nesw-resize"
            onMouseDown={startTopResize('resizeTopRight')}
          />
        </>
      )}

      <div className="flex h-full min-w-0 flex-1 items-center" onMouseDown={onDragAreaMouseDown}>
        <span className="px-3 text-sm font-semibold tracking-wide text-gray-300">Segra</span>
      </div>

      <div className="flex h-full items-center gap-1 pr-2">
        <UpdateStatus />
      </div>

      <div className="flex h-full">
        <WindowButton title="最小化" onClick={() => windowCommand('minimize')}>
          <Minus className="h-4 w-4" strokeWidth={1.5} />
        </WindowButton>
        <WindowButton
          title={maximized ? '還原' : '最大化'}
          onClick={() => windowCommand('toggleMaximize')}
        >
          {maximized ? (
            <Copy className="h-3.5 w-3.5 -scale-x-100" strokeWidth={1.5} />
          ) : (
            <Square className="h-3.5 w-3.5" strokeWidth={1.5} />
          )}
        </WindowButton>
        <WindowButton title="關閉" onClick={() => windowCommand('close')} danger>
          <X className="h-4 w-4" strokeWidth={1.5} />
        </WindowButton>
      </div>
    </div>
  );
}
