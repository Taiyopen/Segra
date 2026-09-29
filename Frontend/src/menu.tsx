import { useSettings } from './Context/SettingsContext';
import { useAppState } from './Context/AppStateContext';
import RecordingCard from './Components/RecordingCard';
import CircularProgress from './Components/CircularProgress';
import { sendMessageToBackend } from './Utils/MessageUtils';
import { useUploads } from './Context/UploadContext';
import { useImports } from './Context/ImportContext';
import { useContentMigration } from './Context/ContentMigrationContext';
import { useClipping } from './Context/ClippingContext';
import { useObsDownload } from './Context/ObsDownloadContext';
import { useAiHighlights } from './Context/AiHighlightsContext';
import UploadCard from './Components/UploadCard';
import ImportCard from './Components/ImportCard';
import ContentMigrationCard from './Components/ContentMigrationCard';
import ClippingCard from './Components/ClippingCard';
import UnavailableDeviceCard from './Components/UnavailableDeviceCard';
import AnimatedCard from './Components/AnimatedCard';
import {
  Clapperboard,
  OctagonX,
  Settings,
  History,
  Crown,
  Monitor,
  Play,
  PictureInPicture2,
  Inbox,
  FolderOpen,
  Trash2,
  PanelLeftClose,
  PanelLeftOpen,
  LucideIcon,
} from 'lucide-react';
import { AnimatePresence, motion } from 'framer-motion';
import { useRef, useEffect, useLayoutEffect, useState, useMemo, useCallback } from 'react';
import Button from './Components/Button';
import { useMonitoringLayout } from './Context/MonitoringLayoutContext';
import {
  MenuItemId,
  MENU_ITEM,
  MENU_ITEM_LABELS,
  menuItemHasContent,
  normalizeMenuItems,
  getActiveRecordings,
  getActivePreRecordings,
  hasLiveRecordingActivity,
  isRecordingFinishing,
} from './Models/types';

interface MenuProps {
  selectedMenu: string;
  onSelectMenu: (menu: string) => void;
}

const MENU_ICONS: Record<MenuItemId, LucideIcon> = {
  [MENU_ITEM.Sessions]: Play,
  [MENU_ITEM.ReplayBuffer]: History,
  [MENU_ITEM.PendingEdit]: Inbox,
  [MENU_ITEM.ReadyToDelete]: Trash2,
  [MENU_ITEM.BrowseVideos]: FolderOpen,
  [MENU_ITEM.Clips]: Clapperboard,
  [MENU_ITEM.Highlights]: Crown,
  [MENU_ITEM.Settings]: Settings,
};

const SIDEBAR_MODE_KEY = 'segra-sidebar-mode';
type SidebarMode = 'expanded' | 'icons' | 'hidden';

const readSidebarMode = (): SidebarMode => {
  try {
    const value = localStorage.getItem(SIDEBAR_MODE_KEY);
    if (value === 'icons' || value === 'hidden' || value === 'expanded') return value;
  } catch {
    /* private mode / quota */
  }
  return 'expanded';
};

export default function Menu({ selectedMenu, onSelectMenu }: MenuProps) {
  const settings = useSettings();
  const appState = useAppState();
  const { enterMonitoringLayout, exitMonitoringLayout, monitoringWindowOpen } =
    useMonitoringLayout();
  const { hasLoadedObs } = appState;
  const activeRecordings = getActiveRecordings(appState);
  const activePreRecordings = getActivePreRecordings(appState);
  const hasLiveActivity = hasLiveRecordingActivity(appState);
  const liveActivityCount =
    activePreRecordings.length +
    activeRecordings.filter((r) => r.endTime == null || r.endTime === undefined).length;
  const useGlobalStop = !hasLiveActivity || liveActivityCount <= 1;
  const { aiProgress } = useAiHighlights();
  const { obsDownloadProgress } = useObsDownload();
  const { migrations: contentMigrations, isMigrating } = useContentMigration();
  const { uploads } = useUploads();
  const { imports } = useImports();
  const { clippingProgress } = useClipping();
  const [buttonCooldown, setButtonCooldown] = useState(false);
  const [sidebarMode, setSidebarModeState] = useState<SidebarMode>(readSidebarMode);

  const setSidebarMode = useCallback((mode: SidebarMode) => {
    setSidebarModeState(mode);
    try {
      localStorage.setItem(SIDEBAR_MODE_KEY, mode);
    } catch {
      /* private mode / quota */
    }
  }, []);

  const isIcons = sidebarMode === 'icons';

  const buttonRefs = useRef<Record<string, HTMLDivElement | null>>({});
  const [indicatorPosition, setIndicatorPosition] = useState({ top: 12 });
  const [indicatorAnimated, setIndicatorAnimated] = useState(false);

  const visibleMenuItems = useMemo(() => {
    const items = normalizeMenuItems(settings.menuItems);
    // Force-show items that contain content so the user always has a way to reach their files.
    return items.filter(
      (item) =>
        item.id === MENU_ITEM.Settings ||
        item.visible ||
        menuItemHasContent(item.id, appState.content),
    );
  }, [settings.menuItems, appState.content]);

  const computeIndicatorPosition = () => {
    if (!visibleMenuItems.some((item) => item.id === selectedMenu)) return;
    const rowEl = buttonRefs.current[selectedMenu];
    if (!rowEl) return;
    const buttonEl = rowEl.firstElementChild as HTMLElement | null;
    const buttonHeight = buttonEl?.offsetHeight || 48;
    const indicatorTop = rowEl.offsetTop + buttonHeight / 2 - 20;
    setIndicatorPosition({ top: indicatorTop });
  };

  useLayoutEffect(() => {
    computeIndicatorPosition();
    const timeoutId = setTimeout(computeIndicatorPosition, 220);
    return () => clearTimeout(timeoutId);
  }, [selectedMenu, visibleMenuItems, sidebarMode]);

  useEffect(() => {
    setIndicatorAnimated(true);
  }, []);

  const aiProgressValues = Object.values(aiProgress);
  const hasActiveAiHighlights = aiProgressValues.length > 0;
  const averageAiProgress = hasActiveAiHighlights
    ? Math.round(aiProgressValues.reduce((sum, p) => sum + p.progress, 0) / aiProgressValues.length)
    : 0;

  const hasUnavailableDevices = () => {
    const unavailableInput = settings.inputDevices.some(
      (deviceSetting: { id: string }) =>
        deviceSetting.id !== 'default' &&
        !appState.inputDevices.some((d) => d.id === deviceSetting.id),
    );
    const unavailableOutput = settings.outputDevices.some(
      (deviceSetting: { id: string }) =>
        deviceSetting.id !== 'default' &&
        !appState.outputDevices.some((d) => d.id === deviceSetting.id),
    );
    return unavailableInput || unavailableOutput;
  };

  if (sidebarMode === 'hidden') {
    return (
      <div className="h-full w-8 bg-frame flex flex-col items-center pt-2 shrink-0">
        <Button
          variant="ghost"
          className="min-h-8 h-8 w-8 p-0"
          title="展開選單"
          onClick={() => setSidebarMode('expanded')}
        >
          <PanelLeftOpen className="w-4 h-4" />
        </Button>
      </div>
    );
  }

  const padX = isIcons ? 'px-1' : 'px-4';

  return (
    <div
      className={`bg-frame h-full flex flex-col overflow-hidden transition-[width] duration-200 ease-in-out ${
        isIcons ? 'w-14' : 'w-56'
      }`}
    >
      {/* Menu Items */}
      <div className={`flex flex-col ${padX} text-left py-2 relative mt-2`}>
        <div
          className={`absolute w-1.5 bg-primary rounded-r ${
            indicatorAnimated ? 'transition-all duration-200 ease-in-out' : ''
          }`}
          style={{
            left: 0,
            top: `${indicatorPosition.top}px`,
            height: '40px',
          }}
        />
        <AnimatePresence initial={false} mode="popLayout">
          {visibleMenuItems.map(({ id }) => {
            const Icon = MENU_ICONS[id];
            const isActive = selectedMenu === id;
            const isDisabled = isMigrating && id !== MENU_ITEM.Settings;
            const iconClass = isIcons
              ? '!justify-center px-0 min-h-10'
              : id === MENU_ITEM.Highlights
                ? 'justify-between'
                : '';

            const buttonNode =
              id === MENU_ITEM.Highlights ? (
                <Button
                  variant="nav"
                  className={`${iconClass} ${isActive ? 'text-primary' : ''}`}
                  disabled={isDisabled}
                  title={MENU_ITEM_LABELS[id]}
                  onMouseDown={() => onSelectMenu(id)}
                >
                  <span className="flex items-center gap-2">
                    {hasActiveAiHighlights && !isActive && isIcons ? (
                      <CircularProgress progress={averageAiProgress} size={20} strokeWidth={2} />
                    ) : (
                      <Icon className="w-5 h-5" />
                    )}
                    {!isIcons && MENU_ITEM_LABELS[id]}
                  </span>
                  {!isIcons && (
                    <div className="ml-auto flex items-center">
                      <AnimatePresence>
                        {hasActiveAiHighlights && !isActive && (
                          <motion.div
                            className="flex items-center justify-center"
                            initial={{ opacity: 0 }}
                            animate={{ opacity: 1 }}
                            exit={{ opacity: 0 }}
                            transition={{ duration: 0.2 }}
                          >
                            <CircularProgress
                              progress={averageAiProgress}
                              size={24}
                              strokeWidth={2}
                            />
                          </motion.div>
                        )}
                      </AnimatePresence>
                    </div>
                  )}
                </Button>
              ) : (
                <Button
                  variant="nav"
                  className={`${iconClass} ${isActive ? 'text-primary' : ''}`}
                  disabled={isDisabled}
                  title={MENU_ITEM_LABELS[id]}
                  onMouseDown={() => onSelectMenu(id)}
                >
                  <Icon className="w-5 h-5" />
                  {!isIcons && MENU_ITEM_LABELS[id]}
                </Button>
              );

            return (
              <motion.div
                key={id}
                ref={(el) => {
                  buttonRefs.current[id] = el;
                }}
                layout
                initial={{ opacity: 0, height: 0 }}
                animate={{ opacity: 1, height: 'auto' }}
                exit={{ opacity: 0, height: 0 }}
                transition={{ duration: 0.2, ease: 'easeInOut' }}
                className="overflow-hidden pb-2 last:pb-0"
              >
                {buttonNode}
              </motion.div>
            );
          })}
        </AnimatePresence>
      </div>

      <div className={`${padX} pb-1`}>
        <Button
          variant="ghost"
          className={`w-full hover:bg-white/5 hover:text-gray-200 ${
            isIcons ? '!justify-center px-0 min-h-10' : 'justify-start gap-2'
          } ${monitoringWindowOpen ? 'text-primary' : 'text-gray-400'}`}
          onClick={() => (monitoringWindowOpen ? exitMonitoringLayout() : enterMonitoringLayout())}
          title={
            monitoringWindowOpen ? '極簡監控視窗已開啟（點擊可關閉）' : '開啟獨立的極簡監控視窗'
          }
        >
          <PictureInPicture2 className="h-5 w-5 shrink-0" />
          {!isIcons && '極簡監控'}
        </Button>
      </div>

      <div className="grow"></div>

      {/* Status Cards */}
      {!isIcons && (
        <div className="mt-auto p-2 space-y-1.5 max-h-[min(52vh,28rem)] overflow-y-auto overflow-x-hidden shrink-0">
          <AnimatePresence>
            {Object.values(uploads).map((file) => (
              <AnimatedCard key={file.fileName}>
                <UploadCard upload={file} />
              </AnimatedCard>
            ))}
          </AnimatePresence>

          <AnimatePresence>
            {Object.values(imports).map((importItem) => (
              <AnimatedCard key={importItem.id}>
                <ImportCard importItem={importItem} />
              </AnimatedCard>
            ))}
          </AnimatePresence>

          <AnimatePresence>
            {Object.values(contentMigrations).map((migration) => (
              <AnimatedCard key={migration.id}>
                <ContentMigrationCard migration={migration} />
              </AnimatedCard>
            ))}
          </AnimatePresence>

          <AnimatePresence>
            {hasUnavailableDevices() && (
              <AnimatedCard key="unavailable-device-card">
                <UnavailableDeviceCard />
              </AnimatedCard>
            )}
          </AnimatePresence>

          <AnimatePresence>
            {appState.alwaysOnBufferActive && !hasLiveActivity && (
              <motion.div
                key="always-on-buffer"
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.2 }}
                className="flex items-center justify-center gap-1.5 py-1 text-xs leading-none text-gray-400"
              >
                <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-success" />
                始終開啟重播緩衝中
              </motion.div>
            )}
          </AnimatePresence>

          <AnimatePresence>
            {activePreRecordings.map((pre) => (
              <AnimatedCard key={`pre-recording-${pre.slot ?? pre.game}`}>
                <RecordingCard preRecording={pre} />
              </AnimatedCard>
            ))}
            {activeRecordings
              .filter((r) => r.endTime == null || r.endTime === undefined)
              .map((rec) => (
                <AnimatedCard key={`recording-${rec.slot ?? rec.game}`}>
                  <RecordingCard recording={rec} />
                </AnimatedCard>
              ))}
          </AnimatePresence>

          <AnimatePresence>
            {Object.values(clippingProgress).map((clipping) => (
              <AnimatedCard key={clipping.id}>
                <ClippingCard clipping={clipping} />
              </AnimatedCard>
            ))}
          </AnimatePresence>
        </div>
      )}

      {!hasLoadedObs && (
        <div className={`mb-4 flex flex-col items-center ${padX}`}>
          {obsDownloadProgress !== null && obsDownloadProgress < 100 ? (
            <>
              {!isIcons && <p className="text-center text-sm text-gray-300 mb-2">正在下載 OBS</p>}
              <div className="w-full bg-base-200 rounded-full h-1.5">
                <div
                  className="h-1.5 rounded-full bg-primary transition-all duration-300"
                  style={{ width: `${obsDownloadProgress}%` }}
                ></div>
              </div>
              {!isIcons && <p className="text-gray-500 text-xs mt-1">{obsDownloadProgress}%</p>}
            </>
          ) : (
            <>
              <div
                style={{
                  width: isIcons ? '1.5rem' : '3.5rem',
                  height: '2rem',
                }}
                className="loading loading-infinity"
              ></div>
              {!isIcons && <p className="text-center mt-2 disabled">OBS 啟動中</p>}
            </>
          )}
        </div>
      )}

      <div className={`${padX} pb-1 flex flex-col gap-1`}>
        {isIcons && (
          <Button
            variant="ghost"
            className="w-full min-h-8 h-8 p-0"
            title="展開選單"
            onClick={() => setSidebarMode('expanded')}
          >
            <PanelLeftOpen className="w-4 h-4" />
          </Button>
        )}
        <Button
          variant="ghost"
          className={isIcons ? 'w-full min-h-8 h-8 p-0' : 'w-full justify-start gap-2'}
          title={isIcons ? '完全收起選單' : '收合成圖示'}
          onClick={() => setSidebarMode(isIcons ? 'hidden' : 'icons')}
        >
          <PanelLeftClose className="w-4 h-4 shrink-0" />
          {!isIcons && '收合側欄'}
        </Button>
      </div>

      <div className={`mb-4 ${padX}`}>
        <div className="flex flex-col items-center z-50">
          <Button
            variant="primary"
            className={`w-full h-12 ${isIcons ? 'min-h-12 px-0' : ''}`}
            disabled={
              buttonCooldown ||
              !appState.hasLoadedObs ||
              isRecordingFinishing(appState) ||
              (hasLiveActivity && !useGlobalStop)
            }
            title={
              hasLiveActivity && !useGlobalStop
                ? isIcons
                  ? '雙路錄影中：請展開側欄，在各錄影卡片上個別停止'
                  : '雙路錄影中：請在各錄影卡片上個別停止'
                : hasLiveActivity
                  ? '停止'
                  : '螢幕擷取'
            }
            onClick={() => {
              setButtonCooldown(true);
              setTimeout(() => setButtonCooldown(false), 1000);
              sendMessageToBackend(hasLiveActivity ? 'StopRecording' : 'StartRecording');
            }}
          >
            {hasLiveActivity ? (
              useGlobalStop ? (
                <>
                  <OctagonX className="w-4 h-4" />
                  {!isIcons && '停止'}
                </>
              ) : (
                <>{isIcons ? <OctagonX className="w-4 h-4" /> : '雙路錄影中'}</>
              )
            ) : (
              <>
                <Monitor className="w-4 h-4" />
                {!isIcons && '螢幕擷取'}
              </>
            )}
          </Button>
        </div>
      </div>
    </div>
  );
}
