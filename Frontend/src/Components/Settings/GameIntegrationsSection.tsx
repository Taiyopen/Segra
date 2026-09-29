import { useSettings } from '../../Context/SettingsContext';
import { sendMessageToBackend } from '../../Utils/MessageUtils';
import { GameIntegrations } from '../../Models/types';

interface GameIntegration {
  id: string;
  name: string;
  settingsKey: keyof GameIntegrations;
  bookmarks: string[];
  backgroundImage: string;
  coverOpacity?: number;
  isBeta?: boolean;
  warningText?: string;
}

const GAME_INTEGRATIONS: GameIntegration[] = [
  {
    id: 'cs2',
    name: 'Counter-Strike 2',
    settingsKey: 'counterStrike2',
    bookmarks: ['Kills', 'Deaths'],
    backgroundImage: 'https://segra.tv/api/games/cover/coaczd',
  },
  {
    id: 'lol',
    name: 'League of Legends',
    settingsKey: 'leagueOfLegends',
    bookmarks: ['Kills', 'Assists', 'Deaths'],
    backgroundImage: 'https://segra.tv/api/games/cover/ar57ot',
  },
  {
    id: 'pubg',
    name: 'PUBG: Battlegrounds',
    settingsKey: 'pubg',
    bookmarks: ['Kills', 'Knocks', 'Deaths'],
    backgroundImage: 'https://segra.tv/api/games/cover/sc87ll',
  },
  {
    id: 'rocket-league',
    name: 'Rocket League',
    settingsKey: 'rocketLeague',
    bookmarks: ['Goals', 'Assists'],
    backgroundImage: 'https://segra.tv/api/games/cover/ar5u6d',
  },
  {
    id: 'gta',
    name: 'Grand Theft Auto',
    settingsKey: 'gta',
    bookmarks: ['Deaths'],
    backgroundImage: 'https://segra.tv/api/games/cover/ar4pi5',
  },
  {
    id: 'minecraft',
    name: 'Minecraft',
    settingsKey: 'minecraft',
    bookmarks: ['Deaths'],
    backgroundImage: 'https://segra.tv/api/games/cover/co8fu7',
  },
  {
    id: 'rust',
    name: 'Rust',
    settingsKey: 'rust',
    bookmarks: ['Deaths'],
    backgroundImage: 'https://segra.tv/api/games/cover/coajjj',
    coverOpacity: 45,
  },
  {
    id: 'dota2',
    name: 'Dota 2',
    settingsKey: 'dota2',
    bookmarks: ['Kills', 'Assists', 'Deaths'],
    backgroundImage: 'https://segra.tv/api/games/cover/q6dxlfgq7e01ktv2zejz',
  },
  {
    id: 'war-thunder',
    name: 'War Thunder',
    settingsKey: 'warThunder',
    bookmarks: ['Kills', 'Deaths'],
    backgroundImage: 'https://segra.tv/api/games/cover/co1p78',
  },
  {
    id: 'runescape-dragonwilds',
    name: 'RuneScape: Dragonwilds',
    settingsKey: 'runescapeDragonwilds',
    bookmarks: ['Deaths'],
    backgroundImage: 'https://segra.tv/api/games/cover/ar3en0',
  },
  {
    id: 'vrchat',
    name: 'VRChat',
    settingsKey: 'vrChat',
    bookmarks: ['World video', 'Auto clips'],
    backgroundImage: 'https://segra.tv/api/games/cover/cobuhu',
    isBeta: true,
    warningText:
      '從 VRChat 的輸出日誌偵測 VVMW 世界影片播放。PlaybackStart 和 PlaybackEnded 都要帶 Url，而且要是同一支影片（正規化後比對）。PlaybackEnded 之後會清掉暫存狀態，所以下一次 PlaybackStart（就算是同一個 Url）會重新計算。結束前重複收到 PlaybackStart 會保留第一次的開始時間。片段優先從重播緩衝的尾端擷取；有完整錄影時會用完整錄影。',
  },
];

const BOOKMARK_BADGE_LABELS: Record<string, string> = {
  Kills: '擊殺',
  Knocks: '擊倒',
  Assists: '助攻',
  Goals: '進球',
  Deaths: '死亡',
  'World video': '世界影片',
  'Auto clips': '自動片段',
};

const getBookmarkBadgeClass = (bookmark: string): string => {
  switch (bookmark) {
    case 'Kills':
    case 'Knocks':
    case 'Assists':
    case 'Goals':
    case 'World video':
    case 'Auto clips':
      return 'bg-success/15 text-success';
    case 'Deaths':
      return 'bg-error/15 text-error';
    default:
      return 'bg-base-300';
  }
};

interface GameIntegrationCardProps {
  integration: GameIntegration;
  enabled: boolean;
  showBackground: boolean;
  onToggle: (enabled: boolean) => void;
}

function GameIntegrationCard({
  integration,
  enabled,
  showBackground,
  onToggle,
}: GameIntegrationCardProps) {
  return (
    <div className="relative bg-base-200 p-4 rounded-lg border border-custom overflow-hidden">
      {/* Background image */}
      {showBackground && (
        <div
          className="absolute inset-0 bg-cover bg-center pointer-events-none"
          style={{
            backgroundImage: `url(${integration.backgroundImage})`,
            opacity: (integration.coverOpacity ?? 25) / 100,
          }}
        />
      )}
      <div className="relative z-10 flex flex-col h-full">
        <div className="flex items-center gap-2 mb-2">
          <h3 className="text-lg font-semibold">{integration.name}</h3>
          {integration.isBeta && (
            <span className="badge badge-primary badge-sm drop-shadow-md">測試版</span>
          )}
        </div>
        <div className="flex flex-wrap gap-1 mb-4">
          {integration.bookmarks.map((bookmark) => (
            <span
              key={bookmark}
              className={`badge badge-sm border-0 drop-shadow-md ${getBookmarkBadgeClass(bookmark)}`}
            >
              {BOOKMARK_BADGE_LABELS[bookmark] ?? bookmark}
            </span>
          ))}
        </div>
        {integration.warningText && (
          <p className="text-xs text-warning mb-3">{integration.warningText}</p>
        )}
        <div className="mt-auto">
          <label className="flex items-center gap-3 cursor-pointer">
            <input
              type="checkbox"
              className="toggle toggle-primary"
              checked={enabled}
              onChange={(e) => onToggle(e.target.checked)}
            />
            <span className="text-sm">{enabled ? '已啟用' : '未啟用'}</span>
          </label>
        </div>
      </div>
    </div>
  );
}

export default function GameIntegrationsSection() {
  const settings = useSettings();

  const handleToggle = (settingsKey: GameIntegration['settingsKey'], enabled: boolean) => {
    sendMessageToBackend('UpdateSettings', {
      ...settings,
      gameIntegrations: {
        ...settings.gameIntegrations,
        [settingsKey]: {
          ...settings.gameIntegrations[settingsKey],
          enabled,
        },
      },
    });
  };

  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <h2 className="text-xl font-semibold mb-2">遊戲整合</h2>
      <p className="text-sm opacity-80 mb-4">
        讓支援的遊戲自動偵測事件。開啟後，Segra 會在遊戲中自動標記擊殺、進球等事件。
      </p>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {GAME_INTEGRATIONS.map((integration) => (
          <GameIntegrationCard
            key={integration.id}
            integration={integration}
            enabled={settings.gameIntegrations[integration.settingsKey].enabled}
            showBackground={settings.showGameBackground}
            onToggle={(enabled) => handleToggle(integration.settingsKey, enabled)}
          />
        ))}
      </div>
    </div>
  );
}
