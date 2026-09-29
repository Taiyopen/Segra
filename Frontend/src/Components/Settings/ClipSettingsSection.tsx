import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import DropdownSelect from '../DropdownSelect';
import {
  Settings as SettingsType,
  GpuVendor,
  ClipFPS,
  ClipPreset,
  ClipQualityPreset,
  ClipCodec,
  ClipEncoder,
  Codec,
} from '../../Models/types';
import { sendMessageToBackend } from '../../Utils/MessageUtils';
import { useAppState } from '../../Context/AppStateContext';

interface ClipSettingsSectionProps {
  settings: SettingsType;
  updateSettings: (updates: Partial<SettingsType>) => void;
}

function obsCodecMatchesClip(codec: Codec, clipEncoder: string, clipCodec: string): boolean {
  const id = codec.internalEncoderId.toLowerCase();
  if (clipEncoder === 'gpu' ? !codec.isHardwareEncoder : codec.isHardwareEncoder) return false;
  if (clipCodec === 'av1') return id.includes('av1');
  if (clipCodec === 'h265') return id.includes('hevc') || id.includes('h265');
  return id.includes('h264') || id.includes('avc') || id.includes('264');
}

function obsCodecToClipSettings(
  codec: Codec,
  appStateGpuVendor: GpuVendor,
  currentPreset: string,
): Partial<SettingsType> {
  const id = codec.internalEncoderId.toLowerCase();
  let clipCodec: ClipCodec = 'h264';
  if (id.includes('av1')) clipCodec = 'av1';
  else if (id.includes('hevc') || id.includes('h265')) clipCodec = 'h265';

  const clipEncoder: ClipEncoder = codec.isHardwareEncoder ? 'gpu' : 'cpu';
  const updates: Partial<SettingsType> = { clipCodec, clipEncoder };

  if (clipEncoder === 'cpu') {
    updates.clipPreset = 'veryfast' as ClipPreset;
  } else {
    switch (appStateGpuVendor) {
      case GpuVendor.AMD:
        updates.clipPreset = 'transcoding' as ClipPreset;
        break;
      case GpuVendor.Intel:
        updates.clipPreset = 'medium' as ClipPreset;
        break;
      default:
        updates.clipPreset = 'medium' as ClipPreset;
        break;
    }
  }

  if (appStateGpuVendor === GpuVendor.Nvidia) {
    if (
      clipCodec === 'av1' &&
      !['p1', 'p2', 'p3', 'p4', 'p5', 'p6', 'p7'].includes(currentPreset)
    ) {
      updates.clipPreset = 'p4' as ClipPreset;
    } else if (
      clipCodec !== 'av1' &&
      ['p1', 'p2', 'p3', 'p4', 'p5', 'p6', 'p7'].includes(currentPreset)
    ) {
      updates.clipPreset = 'hq' as ClipPreset;
    }
  }

  return updates;
}

export default function ClipSettingsSection({
  settings,
  updateSettings,
}: ClipSettingsSectionProps) {
  const appState = useAppState();
  const [localCrfValue, setLocalCrfValue] = useState<string>(String(settings.clipQualityCpu));
  const [localCqLevel, setLocalCqLevel] = useState<string>(String(settings.clipQualityGpu));

  useEffect(() => {
    setLocalCrfValue(String(settings.clipQualityCpu));
  }, [settings.clipQualityCpu]);

  useEffect(() => {
    setLocalCqLevel(String(settings.clipQualityGpu));
  }, [settings.clipQualityGpu]);

  const recordingCodecId = (settings.codec?.internalEncoderId ?? '').toLowerCase();
  const recordingUsesNvenc = settings.encoder === 'gpu' && recordingCodecId.includes('nvenc');
  const gpuVendorKnown = appState.gpuVendor !== GpuVendor.Unknown;
  const isClipNvenc =
    settings.clipEncoder === 'gpu' &&
    (appState.gpuVendor === GpuVendor.Nvidia || (!gpuVendorKnown && recordingUsesNvenc));

  const clipMbpsItems = Array.from({ length: 19 }, (_, i) => (i + 2) * 5).map((v) => ({
    value: String(v),
    label: `${v} Mbps`,
  }));

  const handlePresetChange = (preset: ClipQualityPreset) => {
    sendMessageToBackend('ApplyClipPreset', { preset });
  };

  // CQVBR is NVENC-only; switch away if clip encoder is not NVENC-capable GPU
  useEffect(() => {
    if (settings.clipRateControl !== 'CQVBR' || settings.clipEncoder !== 'gpu') return;
    if (isClipNvenc) return;
    updateSettings({ clipRateControl: 'CQP' });
  }, [settings.clipRateControl, settings.clipEncoder, isClipNvenc, updateSettings]);

  const clipRc = settings.clipRateControl ?? 'CRF';
  const isAv1Clip = settings.clipCodec === 'av1';

  const selectedClipObsCodec = appState.codecs.find((c) =>
    obsCodecMatchesClip(c, settings.clipEncoder, settings.clipCodec),
  );

  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <h2 className="text-xl font-semibold mb-4">剪輯片段設定</h2>

      {/* Quality Preset Selector */}
      <div className="mb-4">
        <div className="grid grid-cols-4 gap-3">
          <div
            className={`bg-base-200 p-3 rounded-lg flex flex-col items-center justify-center transition-all transition-200 border cursor-pointer hover:bg-base-300 ${
              settings.clipQualityPreset === 'low' ? 'border-primary' : 'border-base-400'
            }`}
            onClick={() => handlePresetChange('low')}
          >
            <div className="text-sm font-semibold">低畫質</div>
            <div className="text-xs text-base-content text-opacity-70 mt-1">720p • 30fps</div>
          </div>
          <div
            className={`bg-base-200 p-3 rounded-lg flex flex-col items-center justify-center transition-all transition-200 border cursor-pointer hover:bg-base-300 ${
              settings.clipQualityPreset === 'standard' ? 'border-primary' : 'border-base-400'
            }`}
            onClick={() => handlePresetChange('standard')}
          >
            <div className="text-sm font-semibold">標準</div>
            <div className="text-xs text-base-content text-opacity-70 mt-1">1080p • 60fps</div>
          </div>
          <div
            className={`bg-base-200 p-3 rounded-lg flex flex-col items-center justify-center transition-all transition-200 border cursor-pointer hover:bg-base-300 ${
              settings.clipQualityPreset === 'high' ? 'border-primary' : 'border-base-400'
            }`}
            onClick={() => handlePresetChange('high')}
          >
            <div className="text-sm font-semibold">高畫質</div>
            <div className="text-xs text-base-content text-opacity-70 mt-1">
              {appState.maxDisplayHeight >= 1440 ? '1440p' : '1080p'} • 60fps
            </div>
          </div>
          <div
            className={`bg-base-200 p-3 rounded-lg flex flex-col items-center justify-center transition-all transition-200 border cursor-pointer hover:bg-base-300 ${
              settings.clipQualityPreset === 'custom' ? 'border-primary' : 'border-base-400'
            }`}
            onClick={() => handlePresetChange('custom')}
          >
            <div className="text-sm font-semibold">自訂</div>
            <div className="text-xs text-base-content text-opacity-70 mt-1">手動設定</div>
          </div>
        </div>
      </div>

      {/* Advanced Settings - Only show when Custom preset is selected */}
      <AnimatePresence>
        {settings.clipQualityPreset === 'custom' && (
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
              {/* Frame Rate */}
              <div className="form-control">
                <label className="label">
                  <span className="label-text text-base-content">影格率（FPS）</span>
                </label>
                <DropdownSelect
                  items={[
                    { value: '0', label: '跟原始影片相同' },
                    ...[24, 30, 60, 120, 144].map((v) => ({
                      value: String(v),
                      label: String(v),
                    })),
                  ]}
                  value={String(settings.clipFps)}
                  onChange={(val) => updateSettings({ clipFps: Number(val) as ClipFPS })}
                />
              </div>

              {/* Audio Quality (clip export only) */}
              <div className="form-control">
                <label className="label">
                  <span className="label-text text-base-content">音訊品質</span>
                </label>
                <DropdownSelect
                  items={[
                    { value: '96k', label: '96 kbps（低）' },
                    { value: '128k', label: '128 kbps（中）' },
                    { value: '192k', label: '192 kbps（高）' },
                    { value: '256k', label: '256 kbps（很高）' },
                    { value: '320k', label: '320 kbps（極高）' },
                  ]}
                  value={settings.clipAudioQuality}
                  onChange={(val) =>
                    updateSettings({
                      clipAudioQuality: val as '96k' | '128k' | '192k' | '256k' | '320k',
                    })
                  }
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
                    ...(settings.clipEncoder === 'cpu'
                      ? [{ value: 'CRF', label: 'CRF（固定畫質係數）' }]
                      : []),
                    ...(settings.clipEncoder !== 'cpu'
                      ? [{ value: 'CQP', label: 'CQP（固定量化參數）' }]
                      : []),
                    ...(settings.clipEncoder === 'gpu' && isClipNvenc
                      ? [
                          {
                            value: 'CQVBR',
                            label: 'CQVBR（目標畫質＋最高位元率，需 OBS 31 以上的 NVENC）',
                          },
                        ]
                      : []),
                  ]}
                  value={clipRc}
                  onChange={(val) => updateSettings({ clipRateControl: val })}
                />
              </div>

              {/* Bitrate (for CBR) */}
              {clipRc === 'CBR' && (
                <div className="form-control">
                  <label className="label">
                    <span className="label-text text-base-content">位元率</span>
                  </label>
                  <DropdownSelect
                    items={clipMbpsItems}
                    value={String(settings.clipBitrate)}
                    onChange={(val) => updateSettings({ clipBitrate: Number(val) })}
                  />
                </div>
              )}

              {/* VBR Min/Max Bitrate */}
              {clipRc === 'VBR' && (
                <>
                  <div className="form-control">
                    <label className="label">
                      <span className="label-text text-base-content">最低位元率</span>
                    </label>
                    <DropdownSelect
                      items={clipMbpsItems}
                      value={String(settings.clipMinBitrate ?? settings.clipBitrate)}
                      onChange={(val) => {
                        const min = Number(val);
                        const max = Math.max(min, settings.clipMaxBitrate ?? min);
                        updateSettings({ clipMinBitrate: min, clipMaxBitrate: max });
                      }}
                    />
                  </div>
                  <div className="form-control">
                    <label className="label">
                      <span className="label-text text-base-content">最高位元率</span>
                    </label>
                    <DropdownSelect
                      items={clipMbpsItems}
                      value={String(
                        settings.clipMaxBitrate ??
                          Math.max(
                            settings.clipMinBitrate ?? settings.clipBitrate,
                            Math.round((settings.clipBitrate || 10) * 1.5),
                          ),
                      )}
                      onChange={(val) => {
                        const max = Number(val);
                        const min = Math.min(max, settings.clipMinBitrate ?? settings.clipBitrate);
                        updateSettings({ clipMaxBitrate: max, clipMinBitrate: min });
                      }}
                    />
                  </div>
                </>
              )}

              {/* CRF Value (for CRF) */}
              {clipRc === 'CRF' && (
                <div className="form-control">
                  <label className="label">
                    <span className="label-text text-base-content">CRF 值（0–51）</span>
                  </label>
                  <input
                    type="number"
                    name="clipCrfValue"
                    value={localCrfValue}
                    onChange={(e) => setLocalCrfValue(e.target.value)}
                    onBlur={() => {
                      const val = Number(localCrfValue) || 23;
                      if (!localCrfValue) setLocalCrfValue('23');
                      updateSettings({ clipQualityCpu: val });
                    }}
                    min="0"
                    max="51"
                    className="input input-bordered bg-base-200 disabled:bg-base-200 disabled:opacity-80 w-full outline-none focus:border-base-400"
                  />
                </div>
              )}

              {/* CQ Level (for CQP) */}
              {clipRc === 'CQP' && (
                <div className="form-control">
                  <label className="label">
                    <span className="label-text text-base-content">CQ 等級（0–30）</span>
                  </label>
                  <input
                    type="number"
                    name="clipCqLevel"
                    value={localCqLevel}
                    onChange={(e) => setLocalCqLevel(e.target.value)}
                    onBlur={() => {
                      const val = Number(localCqLevel) || 20;
                      if (!localCqLevel) setLocalCqLevel('20');
                      updateSettings({ clipQualityGpu: val });
                    }}
                    min="0"
                    max="30"
                    className="input input-bordered bg-base-200 disabled:bg-base-200 disabled:opacity-80 w-full outline-none focus:border-base-400"
                  />
                </div>
              )}

              {/* CQVBR: max bitrate + target quality */}
              {clipRc === 'CQVBR' && (
                <>
                  <div className="form-control">
                    <label className="label">
                      <span className="label-text text-base-content">最高位元率（Mbps）</span>
                    </label>
                    <DropdownSelect
                      items={clipMbpsItems}
                      value={String(
                        settings.clipMaxBitrate ??
                          Math.max(
                            settings.clipMinBitrate ?? settings.clipBitrate,
                            Math.round((settings.clipBitrate || 10) * 1.5),
                          ),
                      )}
                      onChange={(val) => updateSettings({ clipMaxBitrate: Number(val) })}
                    />
                  </div>
                  <div className="form-control">
                    <label className="label">
                      <span className="label-text text-base-content">
                        目標畫質（CQ）{' '}
                        <span className="text-base-content/60 text-sm">
                          （{isAv1Clip ? '1–63' : '1–51'}，數字越小畫質越好）
                        </span>
                      </span>
                    </label>
                    <input
                      type="number"
                      name="clipCqvbrTargetQuality"
                      value={localCqLevel}
                      onChange={(e) => setLocalCqLevel(e.target.value)}
                      onBlur={() => {
                        const maxTq = isAv1Clip ? 63 : 51;
                        let val = Number(localCqLevel) || 20;
                        val = Math.min(maxTq, Math.max(1, val));
                        if (!localCqLevel) setLocalCqLevel(String(val));
                        setLocalCqLevel(String(val));
                        updateSettings({ clipQualityGpu: val });
                      }}
                      min="1"
                      max={isAv1Clip ? '63' : '51'}
                      className="input input-bordered bg-base-200 w-full outline-none focus:border-base-400"
                    />
                  </div>
                </>
              )}

              {/* Video Encoder */}
              <div className="form-control">
                <label className="label">
                  <span className="label-text text-base-content">影像編碼器</span>
                </label>
                <DropdownSelect
                  items={[
                    { value: 'gpu', label: 'GPU' },
                    { value: 'cpu', label: 'CPU' },
                  ]}
                  value={settings.clipEncoder}
                  onChange={(val) => {
                    const newEncoder = val as ClipEncoder;
                    const newSettings: Partial<SettingsType> = { clipEncoder: newEncoder };

                    if (newEncoder === 'cpu' && settings.clipEncoder !== 'cpu') {
                      newSettings.clipPreset = 'veryfast' as ClipPreset;
                      if (
                        settings.clipRateControl === 'CQP' ||
                        settings.clipRateControl === 'CQVBR'
                      ) {
                        newSettings.clipRateControl = 'CRF';
                      }
                      if (settings.clipCodec === 'av1') {
                        newSettings.clipCodec = 'h264';
                      }
                    } else if (newEncoder === 'gpu' && settings.clipEncoder !== 'gpu') {
                      if (settings.clipRateControl === 'CRF') {
                        newSettings.clipRateControl = 'CQP';
                      }
                      switch (appState.gpuVendor) {
                        case GpuVendor.AMD:
                          newSettings.clipPreset = 'transcoding' as ClipPreset;
                          break;
                        case GpuVendor.Intel:
                          newSettings.clipPreset = 'medium' as ClipPreset;
                          break;
                        case GpuVendor.Nvidia:
                        default:
                          newSettings.clipPreset = 'medium' as ClipPreset;
                          break;
                      }
                    }
                    updateSettings(newSettings);
                  }}
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
                      settings.clipEncoder === 'gpu'
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
                  value={selectedClipObsCodec?.internalEncoderId}
                  onChange={(val) => {
                    const codec = appState.codecs.find((c) => c.internalEncoderId === val);
                    if (!codec) return;
                    updateSettings(
                      obsCodecToClipSettings(codec, appState.gpuVendor, settings.clipPreset),
                    );
                  }}
                  disabled={!appState.hasLoadedObs || appState.codecs.length === 0}
                />
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Keep Separate Audio Tracks */}
      {settings.enableSeparateAudioTracks && (
        <div className="flex items-center mt-4">
          <label className="flex items-center gap-2">
            <input
              type="checkbox"
              name="clipKeepSeparateAudioTracks"
              checked={settings.clipKeepSeparateAudioTracks}
              onChange={(e) => updateSettings({ clipKeepSeparateAudioTracks: e.target.checked })}
              className="checkbox checkbox-primary checkbox-sm"
            />
            <span className="cursor-pointer">保留分開的音軌</span>
          </label>
        </div>
      )}
    </div>
  );
}
