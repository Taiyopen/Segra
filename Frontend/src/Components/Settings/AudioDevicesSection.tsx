import { useState } from 'react';
import { CircleAlert, Volume2, Gamepad2 } from 'lucide-react';
import { DiscordIcon } from '../icons/BrandIcons';
import DropdownSelect from '../DropdownSelect';
import {
  Settings as SettingsType,
  AudioDevice,
  AudioOutputMode,
  RecordingAudioBitrate,
  DEFAULT_AUDIO_TRACK_MASK,
  MAX_RECORDING_AUDIO_TRACKS,
} from '../../Models/types';
import { useAppState } from '../../Context/AppStateContext';

interface AudioDevicesSectionProps {
  settings: SettingsType;
  updateSettings: (updates: Partial<SettingsType>) => void;
}

function normalizeTrackMask(mask: number | undefined): number {
  return (mask ?? DEFAULT_AUDIO_TRACK_MASK) & 0x3f;
}

function toggleTrackBit(mask: number, trackNumber: number): number {
  const bit = 1 << (trackNumber - 1);
  return mask & bit ? mask & ~bit : mask | bit;
}

function AudioTrackPicker({
  mask,
  onChange,
  disabled,
}: {
  mask: number;
  onChange: (mask: number) => void;
  disabled?: boolean;
}) {
  const normalized = normalizeTrackMask(mask);

  return (
    <div className="flex gap-0.5 shrink-0">
      {Array.from({ length: MAX_RECORDING_AUDIO_TRACKS }, (_, i) => i + 1).map((trackNumber) => {
        const bit = 1 << (trackNumber - 1);
        const active = (normalized & bit) !== 0;
        return (
          <button
            key={trackNumber}
            type="button"
            disabled={disabled}
            title={`音軌 ${trackNumber}`}
            aria-label={`音軌 ${trackNumber}`}
            aria-pressed={active}
            onClick={() => onChange(toggleTrackBit(normalized, trackNumber))}
            className={`w-6 h-6 text-[10px] font-medium rounded border transition-colors ${
              disabled
                ? 'opacity-40 cursor-not-allowed border-base-400 text-base-content/40'
                : active
                  ? 'bg-primary border-primary text-primary-content cursor-pointer'
                  : 'border-base-400 text-base-content/60 hover:border-primary hover:text-primary cursor-pointer'
            }`}
          >
            {trackNumber}
          </button>
        );
      })}
    </div>
  );
}

export default function AudioDevicesSection({
  settings,
  updateSettings,
}: AudioDevicesSectionProps) {
  const appState = useAppState();
  // The slider being dragged (keyed per source); its value is only saved on release
  const [draggingVolume, setDraggingVolume] = useState<{
    key: string | null;
    volume: number | null;
  }>({ key: null, volume: null });

  const isDeviceAvailable = (deviceId: string, devices: AudioDevice[]) => {
    if (deviceId === 'default') return true;
    return devices.some((device) => device.id === deviceId);
  };

  const showTrackControls = settings.enableSeparateAudioTracks;
  const showGameSources =
    settings.audioOutputMode === 'GameOnly' || settings.audioOutputMode === 'GameAndDiscord';

  const trackNames = Array.from({ length: MAX_RECORDING_AUDIO_TRACKS }, (_, i) => {
    const names = settings.recordingAudioTrackNames ?? [];
    return names[i] ?? '';
  });

  const updateTrackName = (index: number, value: string) => {
    const next = [...trackNames];
    next[index] = value;
    updateSettings({ recordingAudioTrackNames: next });
  };

  const toggleDevice = (deviceId: string, deviceType: 'input' | 'output') => {
    const isInput = deviceType === 'input';
    const selectedDevices = isInput ? settings.inputDevices : settings.outputDevices;
    const availableDevices = isInput ? appState.inputDevices : appState.outputDevices;

    const isSelected = selectedDevices.some((d) => d.id === deviceId);
    let updatedDevices;

    if (isSelected) {
      updatedDevices = selectedDevices.filter((d) => d.id !== deviceId);
    } else if (deviceId === 'default') {
      updatedDevices = [
        ...selectedDevices,
        {
          id: 'default',
          name: 'Default Device',
          volume: 1.0,
          audioTrackMask: DEFAULT_AUDIO_TRACK_MASK,
        },
      ];
    } else {
      const deviceToAdd = availableDevices.find((d) => d.id === deviceId);
      if (deviceToAdd) {
        updatedDevices = [
          ...selectedDevices,
          {
            id: deviceId,
            name: deviceToAdd.name,
            volume: 1.0,
            audioTrackMask: DEFAULT_AUDIO_TRACK_MASK,
          },
        ];
      }
    }

    if (updatedDevices) {
      updateSettings(
        isInput ? { inputDevices: updatedDevices } : { outputDevices: updatedDevices },
      );
    }
  };

  const handleVolumeChange = (deviceId: string, volume: number, deviceType: 'input' | 'output') => {
    const isInput = deviceType === 'input';
    const selectedDevices = isInput ? settings.inputDevices : settings.outputDevices;
    const updatedDevices = selectedDevices.map((device) =>
      device.id === deviceId ? { ...device, volume } : device,
    );
    updateSettings(isInput ? { inputDevices: updatedDevices } : { outputDevices: updatedDevices });
  };

  const handleDeviceTrackMaskChange = (
    deviceId: string,
    mask: number,
    deviceType: 'input' | 'output',
  ) => {
    const isInput = deviceType === 'input';
    const selectedDevices = isInput ? settings.inputDevices : settings.outputDevices;
    const updatedDevices = selectedDevices.map((device) =>
      device.id === deviceId ? { ...device, audioTrackMask: mask } : device,
    );
    updateSettings(isInput ? { inputDevices: updatedDevices } : { outputDevices: updatedDevices });
  };

  const renderVolumeSlider = (key: string, volume: number, onCommit: (volume: number) => void) => {
    const isDragging = draggingVolume.key === key;

    return (
      <div className="flex items-center gap-1 w-32 shrink-0">
        <input
          type="range"
          min="0"
          max="5"
          step="0.02"
          value={isDragging ? (draggingVolume.volume ?? 0) : volume}
          className="range range-xs range-primary [--range-fill:0]"
          onChange={(e) => {
            if (isDragging) {
              setDraggingVolume({
                ...draggingVolume,
                volume: parseFloat(e.target.value),
              });
            }
          }}
          onMouseDown={(e) => setDraggingVolume({ key, volume: parseFloat(e.currentTarget.value) })}
          onMouseUp={(e) => {
            if (isDragging) {
              onCommit(parseFloat(e.currentTarget.value));
              setDraggingVolume({ key: null, volume: null });
            }
          }}
        />
        <span className="text-xs w-8 text-right">
          {Math.round((isDragging ? (draggingVolume.volume ?? 0) : volume) * 100)}%
        </span>
      </div>
    );
  };

  const renderDeviceRow = (
    deviceId: string,
    deviceName: React.ReactNode,
    deviceType: 'input' | 'output',
    isSelected: boolean,
    volume: number,
    trackMask: number,
    onToggle: () => void,
  ) => (
    <div key={deviceId} className="form-control mb-1 last:mb-0">
      <label className="cursor-pointer flex items-center gap-2 p-1 hover:bg-base-200 rounded">
        <input
          type="checkbox"
          className="checkbox checkbox-sm checkbox-primary shrink-0"
          checked={isSelected}
          onChange={onToggle}
        />
        <span className="label-text flex-1 min-w-0 mr-2 flex items-center truncate">
          {deviceName}
        </span>
        {isSelected &&
          renderVolumeSlider(`${deviceType}:${deviceId}`, volume, (v) =>
            handleVolumeChange(deviceId, v, deviceType),
          )}
        {isSelected && showTrackControls && (
          <AudioTrackPicker
            mask={trackMask}
            onChange={(mask) => handleDeviceTrackMaskChange(deviceId, mask, deviceType)}
          />
        )}
      </label>
    </div>
  );

  const renderDeviceList = (deviceType: 'input' | 'output') => {
    const isInput = deviceType === 'input';
    const selectedDevices = isInput ? settings.inputDevices : settings.outputDevices;
    const availableDevices = isInput ? appState.inputDevices : appState.outputDevices;
    const defaultDevice: AudioDevice = { id: 'default', name: '預設裝置', isDefault: false };
    const allDevices = [defaultDevice, ...availableDevices];

    return (
      <>
        {allDevices.map((device) => {
          const selected = selectedDevices.find((d) => d.id === device.id);
          return renderDeviceRow(
            device.id,
            device.name,
            deviceType,
            !!selected,
            selected?.volume ?? 1.0,
            normalizeTrackMask(selected?.audioTrackMask),
            () => toggleDevice(device.id, deviceType),
          );
        })}

        {selectedDevices
          .filter(
            (deviceSetting) =>
              deviceSetting.id !== 'default' &&
              !isDeviceAvailable(deviceSetting.id, availableDevices),
          )
          .map((deviceSetting) =>
            renderDeviceRow(
              deviceSetting.id,
              <span className="text-error flex items-center flex-1 mr-2 relative pl-6 leading-none">
                <div
                  className="tooltip tooltip-right tooltip-error absolute left-0 inline-flex"
                  data-tip="這個裝置目前無法使用"
                >
                  <CircleAlert size={18} />
                </div>
                {deviceSetting.name.replace(' (Default)', '')}
              </span>,
              deviceType,
              true,
              deviceSetting.volume,
              normalizeTrackMask(deviceSetting.audioTrackMask),
              () => toggleDevice(deviceSetting.id, deviceType),
            ),
          )}
      </>
    );
  };

  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <h2 className="text-xl font-semibold mb-4">輸入／輸出裝置</h2>

      <div className="mb-4 flex flex-col gap-2">
        <label className="cursor-pointer flex items-center">
          <input
            type="checkbox"
            name="enableSeparateAudioTracks"
            checked={settings.enableSeparateAudioTracks}
            onChange={(e) => updateSettings({ enableSeparateAudioTracks: e.target.checked })}
            className="checkbox checkbox-sm checkbox-accent"
          />
          <span className="ml-2">分軌錄音</span>
        </label>
        {showTrackControls && (
          <p className="text-xs text-base-content/60 leading-snug ml-6">
            把每個音源分配到一條或多條音軌（1–6），跟 OBS
            的進階音訊屬性一樣。只有至少分配到一個音源的音軌才會寫進檔案。
          </p>
        )}
      </div>

      {showTrackControls && (
        <div className="mb-4">
          <label className="label py-0">
            <span className="label-text text-base-content">音軌名稱</span>
          </label>
          <div className="grid grid-cols-2 md:grid-cols-3 gap-2">
            {trackNames.map((name, index) => (
              <label key={index} className="form-control">
                <span className="label-text text-xs text-base-content/60 pb-1">
                  音軌 {index + 1}
                </span>
                <input
                  type="text"
                  className="input input-sm input-bordered bg-base-200"
                  placeholder={`音軌 ${index + 1}`}
                  value={name}
                  onChange={(e) => updateTrackName(index, e.target.value)}
                />
              </label>
            ))}
          </div>
        </div>
      )}

      <div className="grid grid-cols-2 gap-4">
        <div className="form-control">
          <label className="label">
            <span className="label-text text-base-content">輸入裝置</span>
            {showTrackControls && (
              <span className="label-text-alt text-xs text-base-content/50">音軌</span>
            )}
          </label>
          <div className="bg-base-200 rounded-lg p-2 max-h-48 overflow-y-auto overflow-x-hidden border border-base-400 min-h-12.5">
            {renderDeviceList('input')}
          </div>

          <div className="mt-3 flex flex-col gap-2">
            <label className="cursor-pointer flex items-center">
              <input
                type="checkbox"
                name="inputNoiseSuppression"
                checked={settings.inputNoiseSuppression}
                onChange={(e) => updateSettings({ inputNoiseSuppression: e.target.checked })}
                className="checkbox checkbox-sm checkbox-accent"
              />
              <span className="ml-2">降噪</span>
            </label>
            <label className="cursor-pointer flex items-center">
              <input
                type="checkbox"
                name="forceMonoInputSources"
                checked={settings.forceMonoInputSources}
                onChange={(e) => updateSettings({ forceMonoInputSources: e.target.checked })}
                className="checkbox checkbox-sm checkbox-accent"
              />
              <span className="ml-2">強制單聲道</span>
            </label>
          </div>
        </div>

        <div className="form-control">
          <label className="label">
            <span className="label-text text-base-content">輸出裝置</span>
            {showTrackControls && (
              <span className="label-text-alt text-xs text-base-content/50">音軌</span>
            )}
          </label>
          <div className="bg-base-200 rounded-lg p-2 max-h-48 overflow-y-auto overflow-x-hidden border border-base-400 min-h-12.5">
            {renderDeviceList('output')}
          </div>
          {settings.audioOutputMode !== 'All' && settings.outputDevices.length > 0 && (
            <div className="mt-2 text-xs text-base-content/60 leading-snug">
              還沒掛上遊戲時當作備援音源。遊戲擷取開始運作後會自動靜音（開啟分軌錄音時例外）。
            </div>
          )}

          <div className="flex flex-col gap-1 w-70 mt-2">
            {[
              {
                value: 'All' as AudioOutputMode,
                label: '電腦所有聲音',
                icons: <Volume2 className="h-4 w-4" />,
              },
              {
                value: 'GameOnly' as AudioOutputMode,
                label: '只錄遊戲聲音',
                icons: <Gamepad2 className="h-4 w-4" />,
              },
              {
                value: 'GameAndDiscord' as AudioOutputMode,
                label: '只錄遊戲＋Discord 聲音',
                icons: (
                  <span className="flex items-center gap-1.5">
                    <Gamepad2 className="h-4 w-4" />
                    <DiscordIcon className="h-4 w-4" />
                  </span>
                ),
              },
            ].map((option) => (
              <label
                key={option.value}
                className="cursor-pointer flex items-center gap-2 p-1 hover:bg-base-200 rounded"
              >
                <input
                  type="radio"
                  name="audioOutputMode"
                  className="radio radio-sm radio-accent"
                  checked={settings.audioOutputMode === option.value}
                  onChange={() => updateSettings({ audioOutputMode: option.value })}
                />
                <span className="flex items-center gap-1.5 text-sm">
                  {option.label}
                  {option.icons}
                </span>
              </label>
            ))}
          </div>
        </div>
      </div>

      {showGameSources && (
        <div className="mt-4 border-t border-base-400 pt-4">
          <label className="label py-0">
            <span className="label-text text-base-content">遊戲擷取音源</span>
            {showTrackControls && (
              <span className="label-text-alt text-xs text-base-content/50">音軌</span>
            )}
          </label>
          <div className="bg-base-200 rounded-lg p-2 border border-base-400 space-y-1">
            <div className="flex items-center gap-2 p-1">
              <Gamepad2 className="h-4 w-4 shrink-0 text-base-content/70" />
              <span className="flex-1 text-sm">遊戲聲音</span>
              {renderVolumeSlider('game', settings.gameAudioVolume ?? 1, (v) =>
                updateSettings({ gameAudioVolume: v }),
              )}
              {showTrackControls && (
                <AudioTrackPicker
                  mask={settings.gameAudioTrackMask}
                  onChange={(mask) => updateSettings({ gameAudioTrackMask: mask })}
                />
              )}
            </div>
            {settings.audioOutputMode === 'GameAndDiscord' && (
              <div className="flex items-center gap-2 p-1">
                <DiscordIcon className="h-4 w-4 shrink-0" />
                <span className="flex-1 text-sm">Discord</span>
                {renderVolumeSlider('discord', settings.discordAudioVolume ?? 1, (v) =>
                  updateSettings({ discordAudioVolume: v }),
                )}
                {showTrackControls && (
                  <AudioTrackPicker
                    mask={settings.discordAudioTrackMask}
                    onChange={(mask) => updateSettings({ discordAudioTrackMask: mask })}
                  />
                )}
              </div>
            )}
          </div>
        </div>
      )}

      <div className="form-control mt-4 max-w-md">
        <label className="label">
          <span className="label-text text-base-content">錄影音訊位元率</span>
        </label>
        <DropdownSelect
          items={[
            { value: '96k', label: '96 kbps（低）' },
            { value: '128k', label: '128 kbps（中）' },
            { value: '192k', label: '192 kbps（高）' },
            { value: '256k', label: '256 kbps（很高）' },
            { value: '320k', label: '320 kbps（極高）' },
          ]}
          value={settings.recordingAudioBitrate}
          onChange={(val) =>
            updateSettings({ recordingAudioBitrate: val as RecordingAudioBitrate })
          }
        />
        <span className="label-text-alt text-xs text-base-content/60 mt-1 block">
          完整錄影與重播緩衝使用的 AAC 音訊。下一次開始錄影時生效。
        </span>
      </div>
    </div>
  );
}
