import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Settings as SettingsType, hasLiveRecordingActivity } from '../../Models/types';
import { useAppState } from '../../Context/AppStateContext';

interface CaptureModeSectionProps {
  settings: SettingsType;
  updateSettings: (updates: Partial<SettingsType>) => void;
}

export default function CaptureModeSection({ settings, updateSettings }: CaptureModeSectionProps) {
  const appState = useAppState();
  const isRecording = hasLiveRecordingActivity(appState);
  const [localReplayBufferDuration, setLocalReplayBufferDuration] = useState<string>(
    String(settings.replayBufferDuration),
  );
  const [localReplayBufferMaxSize, setLocalReplayBufferMaxSize] = useState<string>(
    String(settings.replayBufferMaxSize),
  );

  useEffect(() => {
    setLocalReplayBufferDuration(String(settings.replayBufferDuration));
  }, [settings.replayBufferDuration]);

  useEffect(() => {
    setLocalReplayBufferMaxSize(String(settings.replayBufferMaxSize));
  }, [settings.replayBufferMaxSize]);

  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <div className="flex items-center gap-2 mb-4">
        <h2 className="text-xl font-semibold">Capture Mode</h2>
        {isRecording && <span className="text-xs text-warning">(locked while recording)</span>}
      </div>
      <div className="mb-6">
        <div
          className={`bg-base-200 p-4 rounded-lg flex flex-col transition-all transition-200 border ${settings.recordingMode == 'Hybrid' ? 'border-primary' : 'border-base-400'} ${isRecording ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer hover:bg-base-300'}`}
          onClick={() => !isRecording && updateSettings({ recordingMode: 'Hybrid' })}
        >
          <div className="flex items-center gap-2 mb-3">
            <div className="text-lg font-semibold">Hybrid (Session + Buffer)</div>
          </div>
          <div className="text-sm text-left text-base-content">
            <p className="mb-2">
              Record the full session while keeping a replay buffer. Save short highlights with a
              hotkey without stopping the session.
            </p>
            <div className="text-xs text-base-content text-opacity-70">
              • Clip without ending the session recording
              <br />• Full game integration features
              <br />• Access to AI-generated highlights
              <br />• Access to Bookmarks
            </div>
          </div>
        </div>
      </div>
      <div className="grid grid-cols-2 gap-6">
        <div
          className={`bg-base-200 p-4 rounded-lg flex flex-col transition-all transition-200 border ${settings.recordingMode == 'Session' ? 'border-primary' : 'border-base-400'} ${isRecording ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer hover:bg-base-300'}`}
          onClick={() => !isRecording && updateSettings({ recordingMode: 'Session' })}
        >
          <div className="text-lg font-semibold mb-3">Session Recording</div>
          <div className="text-sm text-left text-base-content">
            <p className="mb-2">
              Records your entire gaming session from start to finish. Ideal for content creators
              who want complete gameplay recordings.
            </p>
            <div className="text-xs text-base-content text-opacity-70">
              • Uses more storage space
              <br />
              • Full game integration features
              <br />
              • Access to AI-generated highlights
              <br />• Access to Bookmarks
            </div>
          </div>
        </div>
        <div
          className={`bg-base-200 p-4 rounded-lg flex flex-col transition-all transition-200 border ${settings.recordingMode == 'Buffer' ? 'border-primary' : 'border-base-400'} ${isRecording ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer hover:bg-base-300'}`}
          onClick={() => !isRecording && updateSettings({ recordingMode: 'Buffer' })}
        >
          <div className="flex items-center gap-2 mb-3">
            <div className="text-lg font-semibold text-center">Replay Buffer</div>
          </div>
          <div className="text-sm text-left text-base-content">
            <p className="mb-2">
              Continuously records in the background. Save only your best moments with a hotkey
              press.
            </p>
            <div className="text-xs text-base-content text-opacity-70">
              • Efficient storage usage
              <br />
              • No game integration
              <br />• No bookmarks
            </div>
          </div>
        </div>
      </div>

      {/* Replay Buffer Settings - Only show when Replay Buffer mode is selected */}
      <AnimatePresence>
        {(settings.recordingMode === 'Buffer' || settings.recordingMode === 'Hybrid') && (
          <motion.div
            className="mt-6"
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
            <motion.div
              className="grid grid-cols-2 gap-4"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1, transition: { delay: 0.2 } }}
            >
              <div className="form-control w-full">
                <label
                  htmlFor="replayBufferDuration"
                  className="label text-base-content px-0 !block mb-1"
                >
                  <span className="label-text">Buffer Duration (seconds)</span>
                </label>
                <input
                  id="replayBufferDuration"
                  type="number"
                  name="replayBufferDuration"
                  value={localReplayBufferDuration}
                  onChange={(e) => setLocalReplayBufferDuration(e.target.value)}
                  onBlur={() => {
                    const val = Number(localReplayBufferDuration) || 30;
                    if (!localReplayBufferDuration) setLocalReplayBufferDuration('30');
                    updateSettings({ replayBufferDuration: val });
                  }}
                  min="5"
                  max="600"
                  disabled={isRecording}
                  className="input input-bordered bg-base-200 disabled:bg-base-200 disabled:input-bordered disabled:opacity-80 w-full outline-none focus:border-base-400"
                />
              </div>

              <div className="form-control w-full">
                <label
                  htmlFor="replayBufferMaxSize"
                  className="label text-base-content px-0 !block mb-1"
                >
                  <span className="label-text">Buffer Maximum Size (MB)</span>
                </label>
                <input
                  id="replayBufferMaxSize"
                  type="number"
                  name="replayBufferMaxSize"
                  value={localReplayBufferMaxSize}
                  onChange={(e) => setLocalReplayBufferMaxSize(e.target.value)}
                  onBlur={() => {
                    const val = Number(localReplayBufferMaxSize) || 1000;
                    if (!localReplayBufferMaxSize) setLocalReplayBufferMaxSize('1000');
                    updateSettings({ replayBufferMaxSize: val });
                  }}
                  min="100"
                  max="5000"
                  disabled={isRecording}
                  className="input input-bordered bg-base-200 disabled:bg-base-200 disabled:input-bordered disabled:opacity-80 w-full outline-none focus:border-base-400"
                />
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
