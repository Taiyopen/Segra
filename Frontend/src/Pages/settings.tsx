import { useState } from 'react';
import { useSettings, useSettingsUpdater } from '../Context/SettingsContext';
import { useUpdate } from '../Context/UpdateContext';
import AccountSection from '../Components/Settings/AccountSection';
import CaptureModeSection from '../Components/Settings/CaptureModeSection';
import VideoSettingsSection from '../Components/Settings/VideoSettingsSection';
import StorageSettingsSection from '../Components/Settings/StorageSettingsSection';
import ClipSettingsSection from '../Components/Settings/ClipSettingsSection';
import AudioDevicesSection from '../Components/Settings/AudioDevicesSection';
import KeybindingsSection from '../Components/Settings/KeybindingsSection';
import GameDetectionSection from '../Components/Settings/GameDetectionSection';
import GameIntegrationsSection from '../Components/Settings/GameIntegrationsSection';
import HighlightsSection from '../Components/Settings/HighlightsSection';
import PreferencesSection from '../Components/Settings/PreferencesSection';
import AdvancedSection from '../Components/Settings/AdvancedSection';

type SectionId =
  | 'account'
  | 'recording'
  | 'clips'
  | 'games'
  | 'storage'
  | 'preferences'
  | 'advanced';

const NAV_ITEMS: { id: SectionId; label: string }[] = [
  { id: 'account', label: 'Account' },
  { id: 'recording', label: 'Recording' },
  { id: 'clips', label: 'Clips' },
  { id: 'storage', label: 'Storage' },
  { id: 'games', label: 'Games' },
  { id: 'preferences', label: 'Preferences' },
  { id: 'advanced', label: 'Advanced' },
];

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <h2 className="text-sm font-semibold text-primary uppercase tracking-wider mb-4">{children}</h2>
  );
}

export default function Settings() {
  const { openReleaseNotesModal, checkForUpdates } = useUpdate();
  const settings = useSettings();
  const updateSettings = useSettingsUpdater();
  const [activeSection, setActiveSection] = useState<SectionId>('account');

  const activeLabel = NAV_ITEMS.find((item) => item.id === activeSection)?.label ?? 'Settings';

  return (
    <div className="min-h-full bg-base-200 dark:bg-base-300">
      <div className="sticky top-0 z-50 bg-base-200 dark:bg-base-300 border-b border-base-400 px-5 py-3">
        <div className="flex items-center gap-6">
          <h1 className="text-2xl font-bold">Settings</h1>
          <nav className="flex gap-1 flex-wrap">
            {NAV_ITEMS.map((item) => (
              <button
                key={item.id}
                type="button"
                onClick={() => setActiveSection(item.id)}
                className={`px-3 py-1.5 text-sm rounded transition-colors cursor-pointer ${
                  activeSection === item.id
                    ? 'text-primary bg-base-300'
                    : 'text-gray-400 hover:text-primary hover:bg-base-300'
                }`}
              >
                {item.label}
              </button>
            ))}
          </nav>
        </div>
      </div>

      <div className="p-5 space-y-6">
        <SectionTitle>{activeLabel}</SectionTitle>

        {activeSection === 'account' && <AccountSection />}

        {activeSection === 'recording' && (
          <>
            <CaptureModeSection settings={settings} updateSettings={updateSettings} />
            <VideoSettingsSection settings={settings} updateSettings={updateSettings} />
            <AudioDevicesSection settings={settings} updateSettings={updateSettings} />
            <KeybindingsSection settings={settings} updateSettings={updateSettings} />
          </>
        )}

        {activeSection === 'clips' && (
          <>
            <ClipSettingsSection settings={settings} updateSettings={updateSettings} />
            <HighlightsSection settings={settings} updateSettings={updateSettings} />
          </>
        )}

        {activeSection === 'storage' && (
          <StorageSettingsSection settings={settings} updateSettings={updateSettings} />
        )}

        {activeSection === 'games' && (
          <>
            <GameDetectionSection />
            <GameIntegrationsSection />
          </>
        )}

        {activeSection === 'preferences' && (
          <PreferencesSection settings={settings} updateSettings={updateSettings} />
        )}

        {activeSection === 'advanced' && (
          <AdvancedSection
            settings={settings}
            updateSettings={updateSettings}
            openReleaseNotesModal={openReleaseNotesModal}
            checkForUpdates={checkForUpdates}
          />
        )}
      </div>
    </div>
  );
}
