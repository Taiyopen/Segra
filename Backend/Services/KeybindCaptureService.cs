using NAudio.Wave;
using Segra.Models;
using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Segra.Backend.Services
{
    internal class KeybindCaptureService
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        public static void Start()
        {
            _hookID = SetHook(_proc);
            Application.Run();
        }

        public static void Stop()
        {
            UnhookWindowsHookEx(_hookID);
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    proc,
                    GetModuleHandle(curModule.ModuleName),
                    0
                );
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(
            int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Check for F8 key
                if ((Keys)vkCode == Keys.F8)
                {
                    Log.Information("F8 Pressed");
                    Settings.Instance.State.Recording.Bookmarks.Add(new Bookmark
                    {
                        Type = BookmarkType.Manual,
                        Time = DateTime.Now - Settings.Instance.State.Recording.StartTime
                    });
                    PlayBookmarkSound();
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static void PlayBookmarkSound()
        {
            using (var audioStream = new MemoryStream(Properties.Resources.bookmark))
            using (var audioReader = new WaveFileReader(audioStream))
            using (var waveOut = new WaveOutEvent())
            {
                var volumeStream = new VolumeWaveProvider16(audioReader)
                {
                    Volume = 0.5f
                };

                waveOut.Init(volumeStream);
                waveOut.Play();

                while (waveOut.PlaybackState == PlaybackState.Playing)
                    Thread.Sleep(100);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk,
            int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
