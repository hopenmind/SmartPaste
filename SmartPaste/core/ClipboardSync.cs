using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SmartPaste
{
    /// <summary>
    /// Synchronization helpers for clipboard-driven copy/paste sequences.
    ///
    /// Fixed delays ("send Ctrl+C, sleep 350 ms, read") are fragile: slow apps
    /// miss the window while fast ones waste time. These helpers poll the Win32
    /// clipboard sequence number and the physical keyboard state instead, and
    /// always bound the wait with an explicit timeout.
    /// </summary>
    internal static class ClipboardSync
    {
        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private const int PollIntervalMs = 15;

        /// <summary>
        /// Current clipboard sequence number. Returns 0 when the calling process
        /// cannot access the clipboard (no WINSTA_ACCESSCLIPBOARD right).
        /// </summary>
        public static uint GetSequenceNumber() => GetClipboardSequenceNumber();

        /// <summary>
        /// Waits until the clipboard sequence number differs from <paramref name="baseline"/>,
        /// i.e. until some process has changed or emptied the clipboard.
        /// Returns false when nothing changed before <paramref name="timeoutMs"/> elapsed.
        /// When the sequence API is unavailable (baseline == 0) a fixed
        /// <paramref name="fallbackDelayMs"/> is awaited and true is returned.
        /// </summary>
        public static async Task<bool> WaitForChangeAsync(uint baseline, int timeoutMs, int fallbackDelayMs = 350)
        {
            if (baseline == 0)
            {
                await Task.Delay(fallbackDelayMs);
                return true;
            }

            var watch = Stopwatch.StartNew();
            while (GetClipboardSequenceNumber() == baseline)
            {
                if (watch.ElapsedMilliseconds >= timeoutMs) return false;
                await Task.Delay(PollIntervalMs);
            }
            return true;
        }

        /// <summary>
        /// Waits (bounded) until Shift, Alt, Win and every key in <paramref name="extraKeys"/>
        /// are physically released. Used before simulating Ctrl+C / Ctrl+V so that a
        /// modifier still held from the triggering hotkey (e.g. Shift in Ctrl+Shift+V)
        /// does not turn the simulated chord into a different command
        /// (Ctrl+Shift+V = "paste as plain text", Ctrl+Shift+C = DevTools in Chromium).
        /// Returns false if keys were still down when the timeout elapsed.
        /// </summary>
        public static async Task<bool> WaitForChordReleaseAsync(int timeoutMs, params int[] extraKeys)
        {
            var watch = Stopwatch.StartNew();
            while (IsAnyDown(VK_SHIFT, VK_MENU, VK_LWIN, VK_RWIN) || IsAnyDown(extraKeys))
            {
                if (watch.ElapsedMilliseconds >= timeoutMs) return false;
                await Task.Delay(PollIntervalMs);
            }
            return true;
        }

        private static bool IsAnyDown(params int[] virtualKeys)
        {
            foreach (int vk in virtualKeys)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true;
            }
            return false;
        }
    }
}
