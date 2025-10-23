using Microsoft.Win32;

namespace Segra.Backend.Utils
{
    public class DisplayWatcher : IDisposable
    {
        public event Action? DisplaysChanged;

        public DisplayWatcher()
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            DisplaysChanged?.Invoke();
        }

        public void Dispose()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        }
    }
}
