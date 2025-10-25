import { MdLock } from 'react-icons/md';
import { Session } from '@supabase/supabase-js';
import CloudBadge from '../CloudBadge';
import { Settings as SettingsType } from '../../Models/types';

interface SegraAISectionProps {
  session: Session | null;
  settings: SettingsType;
  updateSettings: (updates: Partial<SettingsType>) => void;
}

export default function SegraAISection({ session, settings, updateSettings }: SegraAISectionProps) {
  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <h2 className="text-xl font-semibold mb-4">Segra AI</h2>
      <div className="bg-base-200 p-4 rounded-lg border border-custom space-y-4">
        {!session && (
          <div className="flex items-center gap-2 mb-3 text-sm text-warning">
            <MdLock className="w-4 h-4" />
            <span>Sign in to access AI features</span>
          </div>
        )}
        <div className="flex items-center justify-between">
          <label className="cursor-pointer flex items-center">
            <input
              type="checkbox"
              name="enableAI"
              checked={settings.enableAi}
              onChange={(e) => updateSettings({ enableAi: e.target.checked })}
              className="checkbox checkbox-primary"
              disabled={!session}
            />
            <span className="text-sm ml-2 flex items-center gap-1">
              Enable Segra AI <CloudBadge side="right" />
            </span>
          </label>
        </div>
        <div className="flex items-center justify-between">
          <label className="cursor-pointer flex items-center">
            <input
              type="checkbox"
              name="autoGenerateHighlights"
              checked={settings.autoGenerateHighlights}
              onChange={(e) => updateSettings({ autoGenerateHighlights: e.target.checked })}
              className="checkbox checkbox-primary"
              disabled={!session || !settings.enableAi}
            />
            <span className="text-sm ml-2 flex items-center gap-1">
              Auto-Generate Highlights <CloudBadge side="right" />
            </span>
          </label>
        </div>
      </div>
    </div>
  );
}
