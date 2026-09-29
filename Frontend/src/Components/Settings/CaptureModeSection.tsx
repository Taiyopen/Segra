import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Settings as SettingsType } from '../../Models/types';

interface CaptureModeSectionProps {
  settings: SettingsType;
  updateSettings: (updates: Partial<SettingsType>) => void;
}

export default function CaptureModeSection({ settings, updateSettings }: CaptureModeSectionProps) {
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
        <h2 className="text-xl font-semibold">擷取模式</h2>
      </div>
      <div className="mb-6">
        <div
          className={`bg-base-200 p-4 rounded-lg flex flex-col transition-all transition-200 border ${settings.recordingMode == 'Hybrid' ? 'border-primary' : 'border-base-400'} cursor-pointer hover:bg-base-300`}
          onClick={() => updateSettings({ recordingMode: 'Hybrid' })}
        >
          <div className="flex items-center gap-2 mb-3">
            <div className="text-lg font-semibold">混合模式（完整錄影＋重播緩衝）</div>
          </div>
          <div className="text-sm text-left text-base-content">
            <p className="mb-2">
              錄下整場遊戲，同時保留重播緩衝。按快捷鍵就能存下精彩片段，不用停止錄影。
            </p>
            <div className="text-xs text-base-content text-opacity-70">
              • 不必結束錄影就能存片段
              <br />• 完整的遊戲整合功能
              <br />• 可用 AI 自動產生精華
              <br />• 可用標記
            </div>
          </div>
        </div>
      </div>
      <div className="grid grid-cols-2 gap-6">
        <div
          className={`bg-base-200 p-4 rounded-lg flex flex-col transition-all transition-200 border ${settings.recordingMode == 'Session' ? 'border-primary' : 'border-base-400'} cursor-pointer hover:bg-base-300`}
          onClick={() => updateSettings({ recordingMode: 'Session' })}
        >
          <div className="text-lg font-semibold mb-3">完整錄影</div>
          <div className="text-sm text-left text-base-content">
            <p className="mb-2">從頭到尾錄下整場遊戲，適合需要完整遊戲畫面的創作者。</p>
            <div className="text-xs text-base-content text-opacity-70">
              • 比較佔儲存空間
              <br />
              • 完整的遊戲整合功能
              <br />
              • 可用 AI 自動產生精華
              <br />• 可用標記
            </div>
          </div>
        </div>
        <div
          className={`bg-base-200 p-4 rounded-lg flex flex-col transition-all transition-200 border ${settings.recordingMode == 'Buffer' ? 'border-primary' : 'border-base-400'} cursor-pointer hover:bg-base-300`}
          onClick={() => updateSettings({ recordingMode: 'Buffer' })}
        >
          <div className="flex items-center gap-2 mb-3">
            <div className="text-lg font-semibold text-center">重播緩衝</div>
          </div>
          <div className="text-sm text-left text-base-content">
            <p className="mb-2">在背景持續錄影，按一下快捷鍵只存下最精彩的片段。</p>
            <div className="text-xs text-base-content text-opacity-70">
              • 節省儲存空間
              <br />
              • 沒有遊戲整合
              <br />• 沒有標記
            </div>
          </div>
        </div>
      </div>

      <label className="flex items-center gap-3 cursor-pointer p-4 bg-base-200 rounded-lg border border-base-400 mt-6">
        <input
          type="checkbox"
          className="checkbox checkbox-primary checkbox-sm"
          checked={settings.alwaysOnReplayBuffer}
          onChange={(e) => updateSettings({ alwaysOnReplayBuffer: e.target.checked })}
        />
        <div>
          <div className="font-semibold">始終開啟重播緩衝</div>
          <div className="text-xs opacity-70 mt-0.5">
            沒有在錄影時，也持續對螢幕保留重播緩衝，隨時都能存下剛才的畫面。錄遊戲時會暫停，錄完再自動開啟。
          </div>
        </div>
      </label>

      {/* Replay Buffer Settings - Only show when a replay buffer is in use */}
      <AnimatePresence>
        {(settings.recordingMode === 'Buffer' ||
          settings.recordingMode === 'Hybrid' ||
          settings.alwaysOnReplayBuffer) && (
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
                  <span className="label-text">緩衝長度（秒）</span>
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
                  className="input input-bordered bg-base-200 disabled:bg-base-200 disabled:input-bordered disabled:opacity-80 w-full outline-none focus:border-base-400"
                />
              </div>

              <div className="form-control w-full">
                <label
                  htmlFor="replayBufferMaxSize"
                  className="label text-base-content px-0 !block mb-1"
                >
                  <span className="label-text">緩衝大小上限（MB）</span>
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
