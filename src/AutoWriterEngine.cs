using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmartPaste
{
    /// <summary>
    /// Automated writing engine for the Telework Command Center.
    ///
    /// Cycle:
    ///   1. Pick a source (txt, html, URL) → load text
    ///   2. Pick a target app → launch or focus
    ///   3. Type with full human simulation
    ///   4. Wait random interval
    ///   5. Repeat (if loop enabled)
    ///
    /// Sources can be: .txt, .html, .htm, .md, .log, or any URL.
    /// Targets are executable paths (notepad, word, etc.)
    /// </summary>
    public class AutoWriterEngine
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private readonly SmartPasteManager _pasteManager;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly Random _random = new();

        private volatile bool _cancelRequested;

        // ── Configuration ────────────────────────────────────────────
        public List<string> Sources { get; set; } = new();
        public List<string> Targets { get; set; } = new();
        public int MinIntervalSec { get; set; } = 30;
        public int MaxIntervalSec { get; set; } = 120;
        public bool Loop { get; set; } = true;
        public bool ClearBeforeTyping { get; set; } = false;

        // ── Live state (read by dashboard timer) ─────────────────────
        public bool IsRunning { get; private set; }
        public string CurrentSource { get; private set; } = "";
        public string CurrentTarget { get; private set; } = "";
        public int CycleCount { get; private set; }
        public string StatusMessage { get; private set; } = "";

        public AutoWriterEngine(SmartPasteManager pasteManager)
        {
            _pasteManager = pasteManager;
        }

        // ── Main loop ────────────────────────────────────────────────

        public async Task RunAsync()
        {
            if (Sources.Count == 0) return;

            IsRunning = true;
            _cancelRequested = false;
            CycleCount = 0;

            try
            {
                do
                {
                    // Shuffle sources each round for variety
                    var shuffled = new List<string>(Sources);
                    Shuffle(shuffled);

                    foreach (string source in shuffled)
                    {
                        if (_cancelRequested) break;

                        // 1. Load text from source
                        StatusMessage = $"Loading: {Path.GetFileName(source)}";
                        string? text = await LoadSourceAsync(source);
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        CurrentSource = source;

                        // 2. Launch or focus target
                        if (Targets.Count > 0)
                        {
                            string target = Targets[_random.Next(Targets.Count)];
                            CurrentTarget = Path.GetFileNameWithoutExtension(target);
                            StatusMessage = $"Opening: {CurrentTarget}";
                            LaunchOrFocus(target);
                            await Task.Delay(2500);
                            if (_cancelRequested) break;
                        }

                        // 3. Clear if requested (Ctrl+A → Delete)
                        if (ClearBeforeTyping)
                        {
                            var sim = new WindowsInput.InputSimulator();
                            sim.Keyboard.ModifiedKeyStroke(
                                WindowsInput.Native.VirtualKeyCode.CONTROL,
                                WindowsInput.Native.VirtualKeyCode.VK_A);
                            await Task.Delay(100);
                            sim.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.DELETE);
                            await Task.Delay(300);
                        }

                        // 4. Type with simulation
                        StatusMessage = $"Typing: {Path.GetFileName(source)}";
                        await _pasteManager.DashTypeAsync(text, 500);

                        if (_cancelRequested) break;
                        CycleCount++;

                        // 5. Wait random interval
                        int waitSec = _random.Next(
                            Math.Min(MinIntervalSec, MaxIntervalSec),
                            Math.Max(MinIntervalSec, MaxIntervalSec) + 1);

                        StatusMessage = $"Waiting {waitSec}s...";
                        for (int i = 0; i < waitSec * 10 && !_cancelRequested; i++)
                            await Task.Delay(100);
                    }
                }
                while (Loop && !_cancelRequested);
            }
            finally
            {
                IsRunning = false;
                StatusMessage = _cancelRequested ? "Stopped" : "Complete";
            }
        }

        public void Stop()
        {
            _cancelRequested = true;
            _pasteManager.DashCancel();
        }

        // ── Source loading ────────────────────────────────────────────

        private async Task<string?> LoadSourceAsync(string source)
        {
            try
            {
                // URL
                if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    string html = await _http.GetStringAsync(source);
                    return StripHtml(html);
                }

                // Local file
                if (!File.Exists(source)) return null;
                string content = await Task.Run(() => File.ReadAllText(source));

                string ext = Path.GetExtension(source).ToLowerInvariant();
                return ext is ".html" or ".htm"
                    ? StripHtml(content)
                    : content;
            }
            catch
            {
                return null;
            }
        }

        private static string StripHtml(string html)
        {
            // Remove script/style blocks
            html = Regex.Replace(html, @"<script[^>]*>.*?</script>", "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<style[^>]*>.*?</style>", "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            // Convert structural tags to newlines
            html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</(?:p|div|tr|li|h[1-6])>", "\n", RegexOptions.IgnoreCase);
            // Strip remaining tags
            html = Regex.Replace(html, @"<[^>]+>", "");
            html = System.Net.WebUtility.HtmlDecode(html);
            // Normalize whitespace
            html = Regex.Replace(html, @"\n{3,}", "\n\n");
            return html.Trim();
        }

        // ── Target app management ────────────────────────────────────

        private static void LaunchOrFocus(string exePath)
        {
            try
            {
                string procName = Path.GetFileNameWithoutExtension(exePath);
                var existing = Process.GetProcessesByName(procName);

                if (existing.Length > 0 && existing[0].MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(existing[0].MainWindowHandle, 9); // SW_RESTORE
                    SetForegroundWindow(existing[0].MainWindowHandle);
                }
                else
                {
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
            }
            catch { }
        }

        // ── Utilities ────────────────────────────────────────────────

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
