import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { MdWarning, MdClose } from 'react-icons/md';
import { Settings as SettingsType, AudioDevice } from '../../Models/types';

interface AudioDevicesSectionProps {
  settings: SettingsType;
  updateSettings: (updates: Partial<SettingsType>) => void;
}

export default function AudioDevicesSection({ settings, updateSettings }: AudioDevicesSectionProps) {
  const [draggingVolume, setDraggingVolume] = useState<{
    deviceId: string | null;
    volume: number | null;
  }>({ deviceId: null, volume: null });

  // Helper function to check if the selected device is available
  const isDeviceAvailable = (deviceId: string, devices: AudioDevice[]) => {
    return devices.some((device) => device.id === deviceId);
  };

  // Check if any selected input device is unavailable
  const hasUnavailableInputDevices = settings.inputDevices.some(
    (deviceSetting) => !isDeviceAvailable(deviceSetting.id, settings.state.inputDevices),
  );

  // Check if any selected output device is unavailable
  const hasUnavailableOutputDevices = settings.outputDevices.some(
    (deviceSetting) => !isDeviceAvailable(deviceSetting.id, settings.state.outputDevices),
  );

  // Multi-track audio: first 5 selected sources get isolated tracks (Track 1 is Full Mix)
  const selectedInputIds = settings.inputDevices.map((d) => d.id);
  const selectedOutputIds = settings.outputDevices.map((d) => d.id);
  const combinedSelectedIds = [...selectedInputIds, ...selectedOutputIds];
  const maxIsolatedTracks = 5;
  const hasOverTrackLimit = settings.enableSeparateAudioTracks && combinedSelectedIds.length > maxIsolatedTracks;
  const selectionSig = combinedSelectedIds.join(',');

  // Dismissible warning for track limit exceeded
  const [trackLimitWarnDismissed, setTrackLimitWarnDismissed] = useState<boolean>(false);

  useEffect(() => {
    const storedSig = localStorage.getItem('segra.trackLimitWarnDismissedSig');
    if (hasOverTrackLimit) {
      setTrackLimitWarnDismissed(storedSig === selectionSig);
    } else {
      setTrackLimitWarnDismissed(false);
    }
  }, [selectionSig, hasOverTrackLimit]);

  // Function to toggle input device selection
  const toggleInputDevice = (deviceId: string) => {
    const isSelected = settings.inputDevices.some((d) => d.id === deviceId);
    let updatedDevices;

    if (isSelected) {
      updatedDevices = settings.inputDevices.filter((d) => d.id !== deviceId);
    } else {
      const deviceToAdd = settings.state.inputDevices.find((d) => d.id === deviceId);
      if (deviceToAdd) {
        updatedDevices = [...settings.inputDevices, { id: deviceId, name: deviceToAdd.name, volume: 1.0 }];
      }
    }
    updateSettings({ inputDevices: updatedDevices });
  };

  // Function to toggle output device selection
  const toggleOutputDevice = (deviceId: string) => {
    const isSelected = settings.outputDevices.some((d) => d.id === deviceId);
    let updatedDevices;

    if (isSelected) {
      updatedDevices = settings.outputDevices.filter((d) => d.id !== deviceId);
    } else {
      const deviceToAdd = settings.state.outputDevices.find((d) => d.id === deviceId);
      if (deviceToAdd) {
        updatedDevices = [...settings.outputDevices, { id: deviceId, name: deviceToAdd.name, volume: 1.0 }];
      }
    }
    updateSettings({ outputDevices: updatedDevices });
  };

  // Function to handle input device volume change
  const handleInputVolumeChange = (deviceId: string, volume: number) => {
    const updatedDevices = settings.inputDevices.map((device) =>
      device.id === deviceId ? { ...device, volume: volume } : device,
    );
    updateSettings({ inputDevices: updatedDevices });
  };
  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <h2 className="text-xl font-semibold mb-4">Input/Output Devices</h2>
      <div className="grid grid-cols-2 gap-4">
        {/* Input Devices (Multiple Selection) */}
        <div className="form-control">
          <label className="label">
            <span className="label-text text-base-content">Input Devices</span>
          </label>
          <div className="bg-base-200 rounded-lg p-2 max-h-48 overflow-y-visible overflow-x-hidden border border-base-400">
            {/* Warning for unavailable devices */}
            {hasUnavailableInputDevices && (
              <div className="text-warning text-xs mb-2 flex items-center">
                <span className="mr-1">⚠️</span> Some selected devices are unavailable
              </div>
            )}

            {/* List available input devices as checkboxes */}
            {settings.state.inputDevices.map((device) => (
              <div key={device.id} className="form-control mb-1 last:mb-0">
                <label className="cursor-pointer flex items-center gap-2 p-1 hover:bg-base-200 rounded">
                  <input
                    type="checkbox"
                    className="checkbox checkbox-sm checkbox-accent"
                    checked={settings.inputDevices.some((d) => d.id === device.id)}
                    onChange={() => toggleInputDevice(device.id)}
                  />
                  <span className="label-text flex-1 mr-2 flex items-center">
                    {device.name}
                    {(() => {
                      const selectedIndex = combinedSelectedIds.indexOf(device.id);
                      const showLimitIcon =
                        settings.enableSeparateAudioTracks &&
                        settings.inputDevices.some((d) => d.id === device.id) &&
                        selectedIndex >= maxIsolatedTracks;
                      return showLimitIcon ? (
                        <div
                          className="tooltip tooltip-bottom tooltip-warning ml-1 inline-flex"
                          data-tip="This source will be included in the Full Mix only"
                        >
                          <MdWarning className="h-4 w-4 text-warning" />
                        </div>
                      ) : null;
                    })()}
                  </span>
                  {/* Volume slider */}
                  {settings.inputDevices.some((d) => d.id === device.id) && (
                    <div className="flex items-center gap-1 w-32">
                      <input
                        type="range"
                        min="0"
                        max="2"
                        step="0.01"
                        value={
                          draggingVolume.deviceId === device.id
                            ? (draggingVolume.volume ?? 0)
                            : (settings.inputDevices.find((d) => d.id === device.id)?.volume ?? 1.0)
                        }
                        className="range range-xs range-primary"
                        onChange={(e) => {
                          if (draggingVolume.deviceId === device.id) {
                            setDraggingVolume({
                              ...draggingVolume,
                              volume: parseFloat(e.target.value),
                            });
                          }
                        }}
                        onMouseDown={(e) =>
                          setDraggingVolume({
                            deviceId: device.id,
                            volume: parseFloat(e.currentTarget.value),
                          })
                        }
                        onMouseUp={(e) => {
                          if (draggingVolume.deviceId === device.id) {
                            handleInputVolumeChange(device.id, parseFloat(e.currentTarget.value));
                            setDraggingVolume({ deviceId: null, volume: null });
                          }
                        }}
                      />
                      <span className="text-xs w-8 text-right">
                        {Math.round(
                          (draggingVolume.deviceId === device.id
                            ? (draggingVolume.volume ?? 0)
                            : (settings.inputDevices.find((d) => d.id === device.id)?.volume ?? 1.0)) * 100,
                        )}
                        %
                      </span>
                    </div>
                  )}
                </label>
              </div>
            ))}

            {/* Show unavailable devices that are still selected */}
            {settings.inputDevices
              .filter(
                (deviceSetting) =>
                  !isDeviceAvailable(deviceSetting.id, settings.state.inputDevices) && deviceSetting.id,
              )
              .map((deviceSetting) => (
                <div key={deviceSetting.id} className="form-control mb-1 last:mb-0">
                  <label className="cursor-pointer flex items-center gap-2 p-1 hover:bg-base-200 rounded">
                    <input
                      type="checkbox"
                      className="checkbox checkbox-sm checkbox-warning"
                      checked={true}
                      onChange={() => toggleInputDevice(deviceSetting.id)}
                    />
                    <span className="label-text text-warning flex items-center flex-1 mr-2">
                      <span className="mr-1">⚠️</span>
                      {deviceSetting.name} (Unavailable)
                    </span>
                    <div className="flex items-center gap-1 w-32">
                      <input
                        type="range"
                        min="0"
                        max="1"
                        step="0.01"
                        value={
                          draggingVolume.deviceId === deviceSetting.id
                            ? (draggingVolume.volume ?? 0)
                            : deviceSetting.volume
                        }
                        className="range range-xs range-warning"
                        onChange={(e) => {
                          if (draggingVolume.deviceId === deviceSetting.id) {
                            setDraggingVolume({
                              ...draggingVolume,
                              volume: parseFloat(e.target.value),
                            });
                          }
                        }}
                        onMouseDown={(e) =>
                          setDraggingVolume({
                            deviceId: deviceSetting.id,
                            volume: parseFloat(e.currentTarget.value),
                          })
                        }
                        onMouseUp={(e) => {
                          if (draggingVolume.deviceId === deviceSetting.id) {
                            handleInputVolumeChange(deviceSetting.id, parseFloat(e.currentTarget.value));
                            setDraggingVolume({ deviceId: null, volume: null });
                          }
                        }}
                      />
                      <span className="text-xs w-8 text-right">
                        {Math.round(
                          (draggingVolume.deviceId === deviceSetting.id
                            ? (draggingVolume.volume ?? 0)
                            : deviceSetting.volume) * 100,
                        )}
                        %
                      </span>
                    </div>
                  </label>
                </div>
              ))}

            {settings.state.inputDevices.length === 0 && (
              <div className="text-center py-2 text-gray-500">No input devices available</div>
            )}
          </div>
        </div>

        {/* Output Devices (Multiple Selection) */}
        <div className="form-control">
          <label className="label">
            <span className="label-text text-base-content">Output Devices</span>
          </label>
          <div className="bg-base-200 rounded-lg p-2 max-h-48 overflow-y-visible overflow-x-hidden border border-base-400">
            {hasUnavailableOutputDevices && (
              <div className="text-warning text-xs mb-2 flex items-center">
                <span className="mr-1">⚠️</span> Some selected devices are unavailable
              </div>
            )}

            {settings.state.outputDevices.map((device) => (
              <div key={device.id} className="form-control mb-1 last:mb-0">
                <label className="cursor-pointer flex items-center gap-2 p-1 hover:bg-base-200 rounded">
                  <input
                    type="checkbox"
                    className="checkbox checkbox-sm checkbox-accent"
                    checked={settings.outputDevices.some((d) => d.id === device.id)}
                    onChange={() => toggleOutputDevice(device.id)}
                  />
                  <span className="label-text flex items-center">
                    {device.name}
                    {(() => {
                      const selectedIndex = combinedSelectedIds.indexOf(device.id);
                      const showLimitIcon =
                        settings.enableSeparateAudioTracks &&
                        settings.outputDevices.some((d) => d.id === device.id) &&
                        selectedIndex >= maxIsolatedTracks;
                      return showLimitIcon ? (
                        <div
                          className="tooltip tooltip-bottom tooltip-warning ml-1 inline-flex"
                          data-tip="This source will be included in the Full Mix only"
                        >
                          <MdWarning className="h-4 w-4 text-warning" />
                        </div>
                      ) : null;
                    })()}
                  </span>
                </label>
              </div>
            ))}

            {settings.outputDevices
              .filter(
                (deviceSetting) =>
                  !isDeviceAvailable(deviceSetting.id, settings.state.outputDevices) && deviceSetting.id,
              )
              .map((deviceSetting) => (
                <div key={deviceSetting.id} className="form-control mb-1 last:mb-0">
                  <label className="cursor-pointer flex items-center gap-2 p-1 hover:bg-base-200 rounded">
                    <input
                      type="checkbox"
                      className="checkbox checkbox-sm checkbox-warning"
                      checked={true}
                      onChange={() => toggleOutputDevice(deviceSetting.id)}
                    />
                    <span className="label-text text-warning flex items-center">
                      <span className="mr-1">⚠️</span>
                      {deviceSetting.name} (Unavailable)
                    </span>
                  </label>
                </div>
              ))}

            {settings.state.outputDevices.length === 0 && (
              <div className="text-center py-2 text-gray-500">No output devices available</div>
            )}
          </div>
        </div>
      </div>

      <div className="mt-4">
        <label className="cursor-pointer flex items-center">
          <input
            type="checkbox"
            name="forceMonoInputSources"
            checked={settings.forceMonoInputSources}
            onChange={(e) => updateSettings({ forceMonoInputSources: e.target.checked })}
            className="checkbox checkbox-sm checkbox-accent"
          />
          <span className="ml-2">Mono Input Devices</span>
        </label>

        <AnimatePresence>
          {hasOverTrackLimit && !trackLimitWarnDismissed && (
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
              exit={{ opacity: 0, height: 0, transition: { duration: 0.2 } }}
              className="mt-3 bg-amber-900 bg-opacity-30 border border-amber-500 rounded px-3 text-amber-400 text-sm flex items-center"
            >
              <div className="py-2 flex items-center w-full">
                <MdWarning className="h-5 w-5 mr-2 shrink-0" />
                <motion.span className="flex-1">
                  You have selected more than 5 audio sources. Only the first 5 will be saved as separate audio
                  tracks. Any additional sources will be recorded in the Full Mix only.
                </motion.span>
                <button
                  aria-label="Dismiss track limit warning"
                  className="btn btn-ghost btn-xs text-amber-300 hover:text-amber-100"
                  onClick={() => {
                    setTrackLimitWarnDismissed(true);
                    localStorage.setItem('segra.trackLimitWarnDismissedSig', selectionSig);
                  }}
                >
                  <MdClose className="h-4 w-4" />
                </button>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}
