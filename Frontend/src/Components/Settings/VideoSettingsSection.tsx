import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { MdWarning } from 'react-icons/md';
import DropdownSelect from '../DropdownSelect';
import { Settings as SettingsType } from '../../Models/types';

interface VideoSettingsSectionProps {
  settings: SettingsType;
  updateSettings: (updates: Partial<SettingsType>) => void;
}

export default function VideoSettingsSection({ settings, updateSettings }: VideoSettingsSectionProps) {
  const [localReplayBufferDuration, setLocalReplayBufferDuration] = useState<number>(
    settings.replayBufferDuration,
  );
  const [localReplayBufferMaxSize, setLocalReplayBufferMaxSize] = useState<number>(settings.replayBufferMaxSize);

  useEffect(() => {
    setLocalReplayBufferDuration(settings.replayBufferDuration);
  }, [settings.replayBufferDuration]);

  useEffect(() => {
    setLocalReplayBufferMaxSize(settings.replayBufferMaxSize);
  }, [settings.replayBufferMaxSize]);
  const isRecording = settings.state.recording != null || settings.state.preRecording != null;

  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <h2 className="text-xl font-semibold mb-4">Video Settings</h2>

      {/* Replay Buffer Settings - Only show when Replay Buffer mode is selected */}
      <AnimatePresence>
        {(settings.recordingMode === 'Buffer' || settings.recordingMode === 'Hybrid') && (
          <motion.div
            className="bg-base-300 rounded-lg border border-custom mb-4"
            initial={{ opacity: 0, height: 0, overflow: 'hidden' }}
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
          >
            <div className="content-wrapper mb-4 p-3 pb-0">
              <motion.h3
                className="text-md font-medium mb-3"
                initial={{ opacity: 0 }}
                animate={{ opacity: 1, transition: { delay: 0.1 } }}
              >
                Replay Buffer Settings
              </motion.h3>
              <motion.div
                className="grid grid-cols-2 gap-4"
                initial={{ opacity: 0 }}
                animate={{ opacity: 1, transition: { delay: 0.2 } }}
              >
                {/* Buffer Duration */}
                <div className="form-control w-full">
                  <label htmlFor="replayBufferDuration" className="label text-base-content px-0 !block mb-1">
                    <span className="label-text">Buffer Duration (seconds)</span>
                  </label>
                  <input
                    id="replayBufferDuration"
                    type="number"
                    name="replayBufferDuration"
                    value={localReplayBufferDuration}
                    onChange={(e) => setLocalReplayBufferDuration(Number(e.target.value))}
                    onBlur={() => updateSettings({ replayBufferDuration: localReplayBufferDuration })}
                    min="5"
                    max="600"
                    disabled={isRecording}
                    className={`input input-bordered bg-base-200 disabled:bg-base-200 disabled:input-bordered disabled:opacity-80 w-full`}
                  />
                  <div className="help-text-container">
                    <span className="text-xs text-base-content/60 mt-1">
                      How many seconds of gameplay to keep in memory
                    </span>
                  </div>
                </div>

                {/* Buffer Max Size */}
                <div className="form-control w-full">
                  <label htmlFor="replayBufferMaxSize" className="label text-base-content px-0 !block mb-1">
                    <span className="label-text">Maximum Size (MB)</span>
                  </label>
                  <input
                    id="replayBufferMaxSize"
                    type="number"
                    name="replayBufferMaxSize"
                    value={localReplayBufferMaxSize}
                    onChange={(e) => setLocalReplayBufferMaxSize(Number(e.target.value))}
                    onBlur={() => updateSettings({ replayBufferMaxSize: localReplayBufferMaxSize })}
                    min="100"
                    max="5000"
                    disabled={isRecording}
                    className="input input-bordered bg-base-200 disabled:bg-base-200 disabled:input-bordered disabled:opacity-80 w-full"
                  />
                  <div className="help-text-container">
                    <span className="text-xs text-base-content/60 mt-1">
                      Maximum buffer size in megabytes
                    </span>
                  </div>
                </div>
              </motion.div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <div className="grid grid-cols-2 gap-4">
        {/* Resolution */}
        <div className="form-control">
          <label className="label">
            <span className="label-text text-base-content">Resolution</span>
          </label>
          <DropdownSelect
            items={[
              { value: '720p', label: '720p' },
              { value: '1080p', label: '1080p' },
              { value: '1440p', label: '1440p' },
              { value: '4K', label: '4K' },
            ]}
            value={settings.resolution}
            onChange={(val) => updateSettings({ resolution: val as '720p' | '1080p' | '1440p' | '4K' })}
          />
        </div>

        {/* Frame Rate */}
        <div className="form-control">
          <label className="label">
            <span className="label-text text-base-content">Frame Rate (FPS)</span>
          </label>
          <DropdownSelect
            items={[24, 30, 60, 120, 144].map((v) => ({ value: String(v), label: String(v) }))}
            value={String(settings.frameRate)}
            onChange={(val) => updateSettings({ frameRate: Number(val) })}
          />
        </div>

        {/* Rate Control */}
        <div className="form-control">
          <label className="label">
            <span className="label-text text-base-content">Rate Control</span>
          </label>
          <DropdownSelect
            items={[
              { value: 'CBR', label: 'CBR (Constant Bitrate)' },
              { value: 'VBR', label: 'VBR (Variable Bitrate)' },
              ...(settings.encoder === 'cpu' ? [{ value: 'CRF', label: 'CRF (Constant Rate Factor)' }] : []),
              ...(settings.encoder !== 'cpu' ? [{ value: 'CQP', label: 'CQP (Constant Quantization Parameter)' }] : []),
            ]}
            value={settings.rateControl}
            onChange={(val) => updateSettings({ rateControl: val })}
          />
        </div>

        {/* Bitrate (for CBR) */}
        {settings.rateControl === 'CBR' && (
          <div className="form-control">
            <label className="label">
              <span className="label-text text-base-content">Bitrate (Mbps)</span>
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
                <span className="label-text text-base-content">Minimum Bitrate (Mbps)</span>
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
                <span className="label-text text-base-content">Maximum Bitrate (Mbps)</span>
              </label>
              <DropdownSelect
                items={Array.from({ length: 19 }, (_, i) => (i + 2) * 5).map((v) => ({
                  value: String(v),
                  label: `${v} Mbps`,
                }))}
                value={String(
                  settings.maxBitrate ??
                    Math.max(settings.minBitrate ?? settings.bitrate, Math.round((settings.bitrate || 10) * 1.5)),
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
              <span className="label-text text-base-content">CRF Value (0-51)</span>
            </label>
            <input
              type="number"
              name="crfValue"
              value={settings.crfValue}
              onChange={(e) => updateSettings({ crfValue: Number(e.target.value) })}
              min="0"
              max="51"
              className="input input-bordered bg-base-200 w-full"
            />
          </div>
        )}

        {/* CQ Level (for CQP) */}
        {settings.rateControl === 'CQP' && (
          <div className="form-control">
            <label className="label">
              <span className="label-text text-base-content">CQ Level (0-30)</span>
            </label>
            <input
              type="number"
              name="cqLevel"
              value={settings.cqLevel}
              onChange={(e) => updateSettings({ cqLevel: Number(e.target.value) })}
              min="0"
              max="30"
              className="input input-bordered bg-base-200 w-full"
            />
          </div>
        )}

        {/* Encoder */}
        <div className="form-control">
          <label className="label">
            <span className="label-text text-base-content">Video Encoder</span>
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
            <span className="label-text text-base-content">Codec</span>
          </label>
          <DropdownSelect
            items={settings.state.codecs
              .filter((codec) => (settings.encoder === 'gpu' ? codec.isHardwareEncoder : !codec.isHardwareEncoder))
              .sort((a, b) => {
                const priorityOrder = ['jim_nvenc', 'h264_texture_amf', 'obs_x264'];
                const aIndex = priorityOrder.indexOf(a.internalEncoderId);
                const bIndex = priorityOrder.indexOf(b.internalEncoderId);
                if (aIndex !== -1 && bIndex !== -1) return aIndex - bIndex;
                if (aIndex !== -1) return -1;
                if (bIndex !== -1) return 1;
                return 0;
              })
              .map((codec) => ({ value: codec.internalEncoderId, label: codec.friendlyName }))}
            value={
              settings.state.codecs.find((c) => c.internalEncoderId === settings.codec?.internalEncoderId)
                ?.internalEncoderId
            }
            onChange={(val) =>
              updateSettings({
                codec: settings.state.codecs.find((c) => c.internalEncoderId === val),
              })
            }
            disabled={settings.state.codecs.length === 0}
          />
        </div>
      </div>

      <div className="form-control mt-3">
        <label className="label cursor-pointer justify-start gap-2 px-0">
          <input
            type="checkbox"
            name="enableSeparateAudioTracks"
            checked={settings.enableSeparateAudioTracks}
            onChange={(e) => updateSettings({ enableSeparateAudioTracks: e.target.checked })}
            className="checkbox checkbox-sm checkbox-primary"
          />
          <span className="flex items-center gap-1 text-base-content">Separate audio tracks</span>
        </label>
      </div>

      <div className="flex items-center justify-between mt-3">
        <label className="flex items-center gap-2 cursor-pointer">
          <input
            type="checkbox"
            name="enableDisplayRecording"
            checked={settings.enableDisplayRecording}
            onChange={(e) => updateSettings({ enableDisplayRecording: e.target.checked })}
            className="checkbox checkbox-primary checkbox-sm"
          />
          <span className="font-medium cursor-pointer">Enable Display Recording</span>
          <span className="badge badge-primary badge-sm text-base-content text-base-300!">Beta</span>
        </label>
      </div>

      <AnimatePresence>
        {settings.enableDisplayRecording && (
          <motion.div
            initial={{ opacity: 0, height: 0, overflow: 'hidden' }}
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
            className="mt-3 bg-amber-900 bg-opacity-30 border border-amber-500 rounded px-3 text-amber-400 text-sm flex items-center"
            key="display-recording-warning"
          >
            <div className="py-2 flex items-center w-full">
              <MdWarning className="h-5 w-5 mr-2 shrink-0" />
              <motion.span>
                This feature enables recording of games that do not support game hook. This could cause lag during
                gameplay as it uses display capture instead of game capture. For more details, see{' '}
                <a
                  href="https://github.com/Segergren/Segra/issues/1"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-amber-300 hover:text-amber-200 underline"
                >
                  GitHub Issue #1
                </a>
                .
              </motion.span>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <AnimatePresence>
        {settings.enableDisplayRecording && (
          <motion.div
            initial={{ opacity: 0, height: 0, overflow: 'hidden' }}
            animate={{
              opacity: 1,
              height: 'fit-content',
              overflow: 'visible',
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
            key="display-selection"
          >
            <div className="flex flex-col gap-1 mt-2">
              <span className="font-medium">Monitor Selection</span>
              <DropdownSelect
                items={[
                  { value: 'Automatic', label: 'Automatic' },
                  ...settings.state.displays.map((d) => ({
                    value: d.deviceName,
                    label: `${d.deviceName}${d.isPrimary ? ' (Primary)' : ''}`,
                  })),
                ]}
                value={settings.selectedDisplay?.deviceName || 'Automatic'}
                onChange={(val) =>
                  updateSettings({
                    selectedDisplay:
                      val === 'Automatic' ? undefined : settings.state.displays.find((d) => d.deviceName === val),
                  })
                }
              />
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
