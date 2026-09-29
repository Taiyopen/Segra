import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import DropdownSelect from '../DropdownSelect';
import {
  Settings as SettingsType,
  VideoQualityPreset,
  DisplayCaptureMethod,
} from '../../Models/types';
import { sendMessageToBackend } from '../../Utils/MessageUtils';
import { useAppState } from '../../Context/AppStateContext';

interface VideoSettingsSectionProps {
  settings: SettingsType;
  updateSettings: (updates: Partial<SettingsType>) => void;
}

export default function VideoSettingsSection({
  settings,
  updateSettings,
}: VideoSettingsSectionProps) {
  const appState = useAppState();
  const [localCrfValue, setLocalCrfValue] = useState<string>(String(settings.crfValue));
  const [localCqLevel, setLocalCqLevel] = useState<string>(String(settings.cqLevel));

  useEffect(() => {
    setLocalCrfValue(String(settings.crfValue));
  }, [settings.crfValue]);

  useEffect(() => {
    setLocalCqLevel(String(settings.cqLevel));
  }, [settings.cqLevel]);
  const codecIdLower = (settings.codec?.internalEncoderId ?? '').toLowerCase();
  const isNvencCodec = codecIdLower.includes('nvenc');

  // CQVBR is OBS 31+ NVENC-only; switch away if user picks a non-NVENC codec
  useEffect(() => {
    const id = (settings.codec?.internalEncoderId ?? '').toLowerCase();
    if (settings.rateControl !== 'CQVBR' || settings.encoder !== 'gpu') return;
    if (!id || id.includes('nvenc')) return;
    updateSettings({ rateControl: 'CQP' });
  }, [settings.codec?.internalEncoderId, settings.rateControl, settings.encoder, updateSettings]);

  const handlePresetChange = (preset: VideoQualityPreset) => {
    sendMessageToBackend('ApplyVideoPreset', { preset });
  };

  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <div className="flex items-center gap-2 mb-4">
        <h2 className="text-xl font-semibold">影像設定</h2>
      </div>

      {/* Quality Preset Selector */}
      <div className="mb-4">
        <div className="grid grid-cols-4 gap-3">
          <div
            className={`bg-base-200 p-3 rounded-lg flex flex-col items-center justify-center transition-all transition-200 border ${
              settings.videoQualityPreset === 'low' ? 'border-primary' : 'border-base-400'
            } cursor-pointer hover:bg-base-300`}
            onClick={() => handlePresetChange('low')}
          >
            <div className="text-sm font-semibold">低畫質</div>
            <div className="text-xs text-base-content text-opacity-70 mt-1">720p • 30fps</div>
          </div>
          <div
            className={`bg-base-200 p-3 rounded-lg flex flex-col items-center justify-center transition-all transition-200 border ${
              settings.videoQualityPreset === 'standard' ? 'border-primary' : 'border-base-400'
            } cursor-pointer hover:bg-base-300`}
            onClick={() => handlePresetChange('standard')}
          >
            <div className="text-sm font-semibold">標準</div>
            <div className="text-xs text-base-content text-opacity-70 mt-1">1080p • 60fps</div>
          </div>
          <div
            className={`bg-base-200 p-3 rounded-lg flex flex-col items-center justify-center transition-all transition-200 border ${
              settings.videoQualityPreset === 'high' ? 'border-primary' : 'border-base-400'
            } cursor-pointer hover:bg-base-300`}
            onClick={() => handlePresetChange('high')}
          >
            <div className="text-sm font-semibold">高畫質</div>
            <div className="text-xs text-base-content text-opacity-70 mt-1">
              {appState.maxDisplayHeight >= 1440 ? '1440p' : '1080p'} • 60fps
            </div>
          </div>
          <div
            className={`bg-base-200 p-3 rounded-lg flex flex-col items-center justify-center transition-all transition-200 border ${
              settings.videoQualityPreset === 'custom' ? 'border-primary' : 'border-base-400'
            } cursor-pointer hover:bg-base-300`}
            onClick={() => handlePresetChange('custom')}
          >
            <div className="text-sm font-semibold">自訂</div>
            <div className="text-xs text-base-content text-opacity-70 mt-1">手動設定</div>
          </div>
        </div>
      </div>

      {/* Advanced Settings - Only show when Custom preset is selected */}
      <AnimatePresence>
        {settings.videoQualityPreset === 'custom' && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{
              opacity: 1,
              height: 'fit-content',
              transition: {
                duration: 0.3,
                height: { type: 'spring', stiffness: 300, damping: 30 },
              },
            }}
            exit={{
              opacity: 0,
              height: 0,
              transition: {
                duration: 0.2,
              },
            }}
            style={{ overflow: 'visible' }}
          >
            <div className="grid grid-cols-2 gap-4 pb-1 mt-4">
              {/* Resolution */}
              <div className="form-control">
                <label className="label">
                  <span className="label-text text-base-content">解析度</span>
                </label>
                <DropdownSelect
                  items={[
                    { value: '720p', label: '720p' },
                    { value: '1080p', label: '1080p' },
                    ...(appState.maxDisplayHeight >= 1440
                      ? [{ value: '1440p', label: '1440p' }]
                      : []),
                    ...(appState.maxDisplayHeight >= 2160 ? [{ value: '4K', label: '4K' }] : []),
                  ]}
                  value={settings.resolution}
                  onChange={(val) =>
                    updateSettings({ resolution: val as '720p' | '1080p' | '1440p' | '4K' })
                  }
                />
              </div>

              {/* Frame Rate */}
              <div className="form-control">
                <label className="label">
                  <span className="label-text text-base-content">影格率（FPS）</span>
                </label>
                <DropdownSelect
                  items={[24, 30, 60, 120, 144].map((v) => ({
                    value: String(v),
                    label: String(v),
                  }))}
                  value={String(settings.frameRate)}
                  onChange={(val) => updateSettings({ frameRate: Number(val) })}
                />
              </div>

              {/* Rate Control */}
              <div className="form-control">
                <label className="label">
                  <span className="label-text text-base-content">位元率控制</span>
                </label>
                <DropdownSelect
                  items={[
                    { value: 'CBR', label: 'CBR（固定位元率）' },
                    { value: 'VBR', label: 'VBR（變動位元率）' },
                    ...(settings.encoder === 'cpu'
                      ? [{ value: 'CRF', label: 'CRF（固定畫質係數）' }]
                      : []),
                    ...(settings.encoder !== 'cpu'
                      ? [{ value: 'CQP', label: 'CQP（固定量化參數）' }]
                      : []),
                    ...(settings.encoder === 'gpu' && isNvencCodec
                      ? [
                          {
                            value: 'CQVBR',
                            label: 'CQVBR（目標畫質＋最高位元率，需 OBS 31 以上的 NVENC）',
                          },
                        ]
                      : []),
                  ]}
                  value={settings.rateControl}
                  onChange={(val) => updateSettings({ rateControl: val })}
                />
              </div>

              {/* Bitrate (for CBR) */}
              {settings.rateControl === 'CBR' && (
                <div className="form-control">
                  <label className="label">
                    <span className="label-text text-base-content">位元率</span>
                  </label>
                  <DropdownSelect
                    items={Array.from({ length: 19 }, (_, i) => (i + 2) * 5).map((v) => ({
                      value: String(v),
                      label: `${v} Mbps`,
                    }))}
                    value={String(settings.bitrate)}
                    onChange={(val) => updateSettings({ bitrate: Number(val) })}
                  />
                </div>
              )}

              {/* VBR Min/Max Bitrate */}
              {settings.rateControl === 'VBR' && (
                <>
                  <div className="form-control">
                    <label className="label">
                      <span className="label-text text-base-content">最低位元率</span>
                    </label>
                    <DropdownSelect
                      items={Array.from({ length: 19 }, (_, i) => (i + 2) * 5).map((v) => ({
                        value: String(v),
                        label: `${v} Mbps`,
                      }))}
                      value={String(settings.minBitrate ?? settings.bitrate)}
                      onChange={(val) => {
                        const min = Number(val);
                        const max = Math.max(min, settings.maxBitrate ?? min);
                        updateSettings({ minBitrate: min, maxBitrate: max });
                      }}
                    />
                  </div>
                  <div className="form-control">
                    <label className="label">
                      <span className="label-text text-base-content">最高位元率</span>
                    </label>
                    <DropdownSelect
                      items={Array.from({ length: 19 }, (_, i) => (i + 2) * 5).map((v) => ({
                        value: String(v),
                        label: `${v} Mbps`,
                      }))}
                      value={String(
                        settings.maxBitrate ??
                          Math.max(
                            settings.minBitrate ?? settings.bitrate,
                            Math.round((settings.bitrate || 10) * 1.5),
                          ),
                      )}
                      onChange={(val) => {
                        const max = Number(val);
                        const min = Math.min(max, settings.minBitrate ?? settings.bitrate);
                        updateSettings({ maxBitrate: max, minBitrate: min });
                      }}
                    />
                  </div>
                </>
              )}

              {/* CRF Value (for CRF) */}
              {settings.rateControl === 'CRF' && (
                <div className="form-control">
                  <label className="label">
                    <span className="label-text text-base-content">CRF 值（0–51）</span>
                  </label>
                  <input
                    type="number"
                    name="crfValue"
                    value={localCrfValue}
                    onChange={(e) => setLocalCrfValue(e.target.value)}
                    onBlur={() => {
                      const val = Number(localCrfValue) || 23;
                      if (!localCrfValue) setLocalCrfValue('23');
                      updateSettings({ crfValue: val });
                    }}
                    min="0"
                    max="51"
                    className="input input-bordered bg-base-200 disabled:bg-base-200 disabled:opacity-80 w-full outline-none focus:border-base-400"
                  />
                </div>
              )}

              {/* CQ Level (for CQP) */}
              {settings.rateControl === 'CQP' && (
                <div className="form-control">
                  <label className="label">
                    <span className="label-text text-base-content">CQ 等級（0–30）</span>
                  </label>
                  <input
                    type="number"
                    name="cqLevel"
                    value={localCqLevel}
                    onChange={(e) => setLocalCqLevel(e.target.value)}
                    onBlur={() => {
                      const val = Number(localCqLevel) || 20;
                      if (!localCqLevel) setLocalCqLevel('20');
                      updateSettings({ cqLevel: val });
                    }}
                    min="0"
                    max="30"
                    className="input input-bordered bg-base-200 disabled:bg-base-200 disabled:opacity-80 w-full outline-none focus:border-base-400"
                  />
                </div>
              )}

              {/* CQVBR: max bitrate + target quality (OBS NVENC target_quality) */}
              {settings.rateControl === 'CQVBR' && (
                <>
                  <div className="form-control">
                    <label className="label">
                      <span className="label-text text-base-content">最高位元率（Mbps）</span>
                    </label>
                    <DropdownSelect
                      items={Array.from({ length: 19 }, (_, i) => (i + 2) * 5).map((v) => ({
                        value: String(v),
                        label: `${v} Mbps`,
                      }))}
                      value={String(
                        settings.maxBitrate ??
                          Math.max(
                            settings.minBitrate ?? settings.bitrate,
                            Math.round((settings.bitrate || 10) * 1.5),
                          ),
                      )}
                      onChange={(val) => {
                        const max = Number(val);
                        updateSettings({ maxBitrate: max });
                      }}
                    />
                  </div>
                  <div className="form-control">
                    <label className="label">
                      <span className="label-text text-base-content">
                        目標畫質（CQ）{' '}
                        <span className="text-base-content/60 text-sm">
                          （{codecIdLower.includes('av1') ? '1–63' : '1–51'}，數字越小畫質越好）
                        </span>
                      </span>
                    </label>
                    <input
                      type="number"
                      name="cqvbrTargetQuality"
                      value={localCqLevel}
                      onChange={(e) => setLocalCqLevel(e.target.value)}
                      onBlur={() => {
                        const maxTq = codecIdLower.includes('av1') ? 63 : 51;
                        let val = Number(localCqLevel) || 20;
                        val = Math.min(maxTq, Math.max(1, val));
                        if (!localCqLevel) setLocalCqLevel(String(val));
                        setLocalCqLevel(String(val));
                        updateSettings({ cqLevel: val });
                      }}
                      min="1"
                      max={codecIdLower.includes('av1') ? '63' : '51'}
                      className="input input-bordered bg-base-200 w-full outline-none focus:border-base-400"
                    />
                  </div>
                </>
              )}

              {/* Encoder */}
              <div className="form-control">
                <label className="label">
                  <span className="label-text text-base-content">影像編碼器</span>
                </label>
                <DropdownSelect
                  items={[
                    { value: 'gpu', label: 'GPU' },
                    { value: 'cpu', label: 'CPU' },
                  ]}
                  value={settings.encoder}
                  onChange={(val) => updateSettings({ encoder: val as 'gpu' | 'cpu' })}
                />
              </div>

              {/* Codec */}
              <div className="form-control">
                <label className="label">
                  <span className="label-text text-base-content">編碼格式</span>
                </label>
                <DropdownSelect
                  items={appState.codecs
                    .filter((codec) =>
                      settings.encoder === 'gpu'
                        ? codec.isHardwareEncoder
                        : !codec.isHardwareEncoder,
                    )
                    .sort((a, b) => {
                      const priorityOrder = ['jim_nvenc', 'h264_texture_amf', 'obs_x264'];
                      const aIndex = priorityOrder.indexOf(a.internalEncoderId);
                      const bIndex = priorityOrder.indexOf(b.internalEncoderId);
                      if (aIndex !== -1 && bIndex !== -1) return aIndex - bIndex;
                      if (aIndex !== -1) return -1;
                      if (bIndex !== -1) return 1;
                      return 0;
                    })
                    .map((codec) => ({
                      value: codec.internalEncoderId,
                      label: codec.friendlyName,
                    }))}
                  value={
                    appState.codecs.find(
                      (c) => c.internalEncoderId === settings.codec?.internalEncoderId,
                    )?.internalEncoderId
                  }
                  onChange={(val) =>
                    updateSettings({
                      codec: appState.codecs.find((c) => c.internalEncoderId === val),
                    })
                  }
                  disabled={appState.codecs.length === 0}
                />
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <div className="grid grid-cols-2 gap-4 mt-3">
        <div className="flex flex-col">
          <span className="font-medium">錄影螢幕</span>
          <DropdownSelect
            items={[
              { value: 'Automatic', label: '自動' },
              ...appState.displays.map((d, i) => {
                const hasDuplicateName = appState.displays.some(
                  (other, j) => j !== i && other.deviceName === d.deviceName,
                );
                const label = hasDuplicateName
                  ? `${d.deviceName} (${i + 1})${d.isPrimary ? '（主螢幕）' : ''}`
                  : `${d.deviceName}${d.isPrimary ? '（主螢幕）' : ''}`;
                return { value: d.deviceId, label };
              }),
            ]}
            value={settings.selectedDisplay?.deviceId || 'Automatic'}
            onChange={(val) =>
              updateSettings({
                selectedDisplay:
                  val === 'Automatic'
                    ? undefined
                    : appState.displays.find((d) => d.deviceId === val),
              })
            }
          />
        </div>
        <div className="flex flex-col">
          <span className="font-medium">擷取方式</span>
          <DropdownSelect
            items={[
              { value: 'Auto', label: '自動' },
              { value: 'DXGI', label: 'DXGI（桌面複製）' },
              { value: 'WGC', label: 'WGC（Windows 圖形擷取）' },
            ]}
            value={settings.displayCaptureMethod}
            onChange={(val) =>
              updateSettings({ displayCaptureMethod: val as DisplayCaptureMethod })
            }
          />
        </div>
      </div>

      {/* 4:3 Stretch Option */}
      <div className="mt-3">
        <label className={`flex items-center gap-2 cursor-pointer`}>
          <input
            type="checkbox"
            checked={settings.stretch4By3}
            onChange={(e) => updateSettings({ stretch4By3: e.target.checked })}
            className="checkbox checkbox-primary checkbox-sm"
          />
          <span>把 4:3 畫面拉伸成 16:9</span>
        </label>
      </div>

      {/* HDR Option - only shown when at least one display is in HDR mode */}
      {appState.displays.some((d) => d.isHdr) && (
        <div className="mt-3">
          <label className={`flex items-center gap-2 cursor-pointer`}>
            <input
              type="checkbox"
              checked={settings.enableHdr}
              onChange={(e) => updateSettings({ enableHdr: e.target.checked })}
              className="checkbox checkbox-primary checkbox-sm"
            />
            <span>以 HDR 錄影</span>
          </label>
        </div>
      )}
    </div>
  );
}
