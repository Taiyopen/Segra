import { useState, useRef } from 'react';
import { Settings as SettingsType, KeybindAction } from '../../Models/types';

interface KeybindingsSectionProps {
  settings: SettingsType;
  updateSettings: (updates: Partial<SettingsType>) => void;
}

export default function KeybindingsSection({ settings, updateSettings }: KeybindingsSectionProps) {
  const [isCapturingKey, setIsCapturingKey] = useState<number | null>(null);
  const activeKeysRef = useRef<number[]>([]);

  // Helper function to get a display name for a key code
  const getKeyDisplayName = (keyCode: number): string => {
    // Function keys
    if (keyCode >= 112 && keyCode <= 123) {
      return `F${keyCode - 111}`;
    }

    // Common keys
    const keyMap: Record<number, string> = {
      8: 'Backspace',
      9: 'Tab',
      13: 'Enter',
      16: 'Shift',
      17: 'Ctrl',
      18: 'Alt',
      19: 'Pause',
      20: 'Caps Lock',
      27: 'Esc',
      32: 'Space',
      33: 'PgUp',
      34: 'PgDn',
      35: 'End',
      36: 'Home',
      37: '←',
      38: '↑',
      39: '→',
      40: '↓',
      45: 'Insert',
      46: 'Delete',
      91: 'Win',
      93: 'Menu',
      124: 'F13',
      125: 'F14',
      126: 'F15',
      127: 'F16',
      128: 'F17',
      129: 'F18',
      130: 'F19',
      131: 'F20',
      132: 'F21',
      133: 'F22',
      134: 'F23',
      135: 'F24',
      144: 'Num Lock',
      186: ';',
      187: '=',
      188: ',',
      189: '-',
      190: '.',
      191: '/',
      192: '`',
      219: '[',
      220: '\\',
      221: ']',
      222: "'",
    };

    // Numbers and letters
    if (keyCode >= 48 && keyCode <= 57) {
      return String.fromCharCode(keyCode); // 0-9
    }
    if (keyCode >= 65 && keyCode <= 90) {
      return String.fromCharCode(keyCode); // A-Z
    }

    return keyMap[keyCode] || `Key(${keyCode})`;
  };
  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <h2 className="text-xl font-semibold mb-4">Keybindings</h2>
      <div className="space-y-2">
        {settings.keybindings.map((keybind, index) => (
          <div key={index} className="flex items-center justify-between">
            <div className="flex items-center justify-between bg-base-200 rounded-lg py-2 px-3 border border-base-400 min-w-[50%]">
              <label className="flex items-center gap-2 cursor-pointer">
                <div className="flex items-center gap-2 mr-2">
                  <input
                    type="checkbox"
                    checked={keybind.enabled}
                    onChange={(e) => {
                      const updatedKeybindings = [...settings.keybindings];
                      updatedKeybindings[index] = {
                        ...updatedKeybindings[index],
                        enabled: e.target.checked,
                      };
                      updateSettings({ keybindings: updatedKeybindings });
                    }}
                    className="checkbox checkbox-primary"
                  />
                </div>
                <span className="font-medium">
                  {keybind.action == KeybindAction.CreateBookmark ? 'Create Bookmark' : 'Save Replay Buffer'}
                </span>
              </label>
              <button
                className={`kbd kbd-md cursor-pointer min-w-[25%] h-8 text-lg ${isCapturingKey === index ? 'animate-pulse' : ''}`}
                style={{ display: 'flex', justifyContent: 'center' }}
                onClick={() => {
                  if (activeKeysRef.current) {
                    activeKeysRef.current = [];
                  }
                  setIsCapturingKey(index);

                  const handleKeyDown = (e: KeyboardEvent) => {
                    e.preventDefault();

                    const newActiveKeys = [...(activeKeysRef.current || [])];

                    if (e.ctrlKey && !newActiveKeys.includes(17)) newActiveKeys.push(17);
                    if (e.altKey && !newActiveKeys.includes(18)) newActiveKeys.push(18);
                    if (e.shiftKey && !newActiveKeys.includes(16)) newActiveKeys.push(16);

                    if (
                      e.keyCode !== 16 &&
                      e.keyCode !== 17 &&
                      e.keyCode !== 18 &&
                      !newActiveKeys.includes(e.keyCode)
                    ) {
                      newActiveKeys.push(e.keyCode);
                    }

                    if (activeKeysRef.current) {
                      activeKeysRef.current = newActiveKeys;
                    }
                  };

                  const handleKeyUp = (e: KeyboardEvent) => {
                    // Cancel if Escape key is pressed
                    if (e.keyCode === 27) {
                      window.removeEventListener('keydown', handleKeyDown);
                      window.removeEventListener('keyup', handleKeyUp);
                      setIsCapturingKey(null);
                      if (activeKeysRef.current) {
                        activeKeysRef.current = [];
                      }
                      return;
                    }

                    if (
                      e.keyCode !== 16 &&
                      e.keyCode !== 17 &&
                      e.keyCode !== 18 &&
                      (activeKeysRef.current?.length || 0) > 0
                    ) {
                      const updatedKeybindings = [...settings.keybindings];
                      updatedKeybindings[index] = {
                        ...updatedKeybindings[index],
                        keys: [...(activeKeysRef.current || [])],
                      };
                      updateSettings({ keybindings: updatedKeybindings });

                      window.removeEventListener('keydown', handleKeyDown);
                      window.removeEventListener('keyup', handleKeyUp);
                      setIsCapturingKey(null);
                      if (activeKeysRef.current) {
                        activeKeysRef.current = [];
                      }
                    }
                  };

                  window.addEventListener('keydown', handleKeyDown);
                  window.addEventListener('keyup', handleKeyUp);
                }}
              >
                {isCapturingKey === index ? (
                  'Press a key combination...'
                ) : (
                  <span>
                    {keybind.keys.map((key, keyIndex) => {
                      const isLastKey = keyIndex === keybind.keys.length - 1;

                      let keyName = '';
                      if (key === 17) keyName = 'CTRL';
                      else if (key === 18) keyName = 'ALT';
                      else if (key === 16) keyName = 'SHIFT';
                      else keyName = getKeyDisplayName(key);

                      return (
                        <span key={keyIndex} className="font-bold">
                          {keyName}
                          {!isLastKey && ' + '}
                        </span>
                      );
                    })}
                  </span>
                )}
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
