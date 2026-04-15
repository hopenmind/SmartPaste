using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using WindowsInput;
using WindowsInput.Native;

namespace SmartPaste
{
    public class SmartPasteManager : IDisposable
    {
        private GlobalHotkey? _hotkeyMode1;
        private GlobalHotkey? _hotkeyMode2;
        private GlobalHotkey? _hotkeyMode3;
        private InputSimulator _simulator = new InputSimulator();
        private static readonly Random _random = new Random();

        public int DelayMilliseconds { get; set; } = 30;
        public bool HumanSimulation { get; set; } = false;
        public bool HumanTypos { get; set; } = false;

        // Telework options
        public bool TeleVariableRhythm { get; set; } = true;
        public bool TeleMicroPauses { get; set; } = true;
        public bool TeleFlowBursts { get; set; } = true;
        public bool TeleRealisticTypos { get; set; } = false;
        public bool TeleRandomCapsErrors { get; set; } = false;
        public bool TeleDoubleKeyStrokes { get; set; } = false;
        public bool TeleCursorNavigation { get; set; } = false;
        public bool TeleAutoCorrectMistakes { get; set; } = false;
        public bool TeleBreathingPauses { get; set; } = true;
        public bool TeleEndOfLinePause { get; set; } = true;
        public int TelePasteDelay { get; set; } = 100;
        public int TeleWordChunkSize { get; set; } = 5;
        public int TeleBreathingInterval { get; set; } = 15;

        // ── Dashboard state ──────────────────────────────────────────
        private volatile bool _cancelRequested;
        private volatile bool _pauseRequested;
        public int DashTotal { get; private set; }
        public int DashProgress { get; private set; }
        public bool DashActive { get; private set; }
        public bool DashPaused => _pauseRequested;
        public string DashLastChar { get; private set; } = "";

        /// <summary>Multiplier applied to all delays. Set by WorkScheduler energy curve.</summary>
        public double EnergyMultiplier { get; set; } = 1.0;

        public void DashCancel() => _cancelRequested = true;
        public void DashPause() => _pauseRequested = true;
        public void DashResume() => _pauseRequested = false;

        /// <summary>
        /// Types text from the dashboard with full simulation, progress tracking,
        /// and pause/cancel support. The user must focus the target app first.
        /// </summary>
        public async Task DashTypeAsync(string text, int focusDelayMs = 3000)
        {
            _cancelRequested = false;
            _pauseRequested = false;
            DashTotal = text.Length;
            DashProgress = 0;
            DashActive = true;
            DashLastChar = "";

            // Give user time to click into target app
            await Task.Delay(focusDelayMs);

            await Task.Run(() => SimulateTyping(text, false));

            DashActive = false;
        }

        public void RegisterHotkeys(IntPtr hwnd, string shortcut1, string shortcut2, string shortcut3)
        {
            UnregisterHotkeys();

            if (ShortcutParser.TryParse(shortcut1, out uint m1, out VirtualKeyCode k1))
            {
                _hotkeyMode1 = new GlobalHotkey(m1, (uint)k1, hwnd, 9001);
                _hotkeyMode1.HotkeyPressed += (s, e) => Paste(1);
            }
            if (ShortcutParser.TryParse(shortcut2, out uint m2, out VirtualKeyCode k2))
            {
                _hotkeyMode2 = new GlobalHotkey(m2, (uint)k2, hwnd, 9002);
                _hotkeyMode2.HotkeyPressed += (s, e) => Paste(2);
            }
            if (ShortcutParser.TryParse(shortcut3, out uint m3, out VirtualKeyCode k3))
            {
                _hotkeyMode3 = new GlobalHotkey(m3, (uint)k3, hwnd, 9003);
                _hotkeyMode3.HotkeyPressed += (s, e) => Paste(3);
            }
        }

        public void UnregisterHotkeys()
        {
            _hotkeyMode1?.Dispose(); _hotkeyMode1 = null;
            _hotkeyMode2?.Dispose(); _hotkeyMode2 = null;
            _hotkeyMode3?.Dispose(); _hotkeyMode3 = null;
        }

        // ── Entry point ──────────────────────────────────────────────

        private async void Paste(int mode)
        {
            // ── SmartInject path ──
            // If the clipboard was tagged by SmartCopy, read the ContentPackage
            // and inject the optimal format for the target application.
            try
            {
                IDataObject? clip = Clipboard.GetDataObject();
                if (clip?.GetDataPresent(FormatCache.CopyIdFormat) == true)
                {
                    string? clipId = clip.GetData(FormatCache.CopyIdFormat) as string;
                    if (!string.IsNullOrEmpty(clipId))
                    {
                        var package = FormatCache.Load();
                        if (package != null && package.Id == clipId && package.HasRichContent)
                        {
                            await SmartInject(package);
                            return;
                        }
                    }
                }
            }
            catch { /* fall through to normal paste */ }

            // ── Normal SmartPaste path (typing simulation) ──
            if (!Clipboard.ContainsText()) return;
            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            await Task.Delay(100);

            await Task.Run(() =>
            {
                bool useSim = HumanSimulation;
                switch (mode)
                {
                    case 1:
                        if (useSim) PasteMode3Sim(text); else PasteMode3(text);
                        break;
                    case 2:
                        if (useSim) PasteMode2Sim(text); else PasteMode2(text);
                        break;
                    case 3:
                        if (useSim) PasteMode1Sim(text); else PasteMode1(text);
                        break;
                }
            });
        }

        // ── SmartInject — target-aware paste from ContentPackage ─────
        //
        // Instead of putting ALL formats on the clipboard and hoping
        // the target picks the right one, we put ONLY the optimal
        // format → the app has no choice but to use it.

        /// <summary>
        /// Target-aware paste from ContentPackage.
        /// Called from SmartPaste hotkeys AND from PasteInterceptor (Ctrl+V override).
        /// </summary>
        public async Task SmartInject(ContentPackage package)
        {
            var (target, _) = TargetDetector.Detect();

            // Brief delay to let key release propagate
            await Task.Delay(120);

            var data = new DataObject();

            // Always include text (universal fallback)
            if (!string.IsNullOrEmpty(package.PlainText))
                data.SetText(package.PlainText, TextDataFormat.UnicodeText);

            switch (target)
            {
                case TargetType.Office:
                case TargetType.RichText:
                    if (!string.IsNullOrEmpty(package.RtfContent))
                        data.SetText(package.RtfContent, TextDataFormat.Rtf);
                    else if (!string.IsNullOrEmpty(package.HtmlFragment))
                        data.SetText(FormatCache.BuildCFHtml(package.HtmlFragment, package.SourceUrl), TextDataFormat.Html);
                    break;

                case TargetType.Browser:
                case TargetType.Electron:
                    if (!string.IsNullOrEmpty(package.HtmlFragment))
                        data.SetText(FormatCache.BuildCFHtml(package.HtmlFragment, package.SourceUrl), TextDataFormat.Html);
                    break;

                case TargetType.PlainText:
                    // Text already set above
                    break;

                case TargetType.ImageEditor:
                    if (FormatCache.HasSelectionBitmap)
                    {
                        try
                        {
                            using var stream = File.OpenRead(FormatCache.SelectionBitmapPath);
                            var decoder = new PngBitmapDecoder(stream,
                                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                            data.SetImage(decoder.Frames[0]);
                        }
                        catch { }
                    }
                    break;

                default:
                    // Unknown → all formats
                    if (!string.IsNullOrEmpty(package.RtfContent))
                        data.SetText(package.RtfContent, TextDataFormat.Rtf);
                    if (!string.IsNullOrEmpty(package.HtmlFragment))
                        data.SetText(FormatCache.BuildCFHtml(package.HtmlFragment, package.SourceUrl), TextDataFormat.Html);
                    if (FormatCache.HasSelectionBitmap)
                    {
                        try
                        {
                            using var stream = File.OpenRead(FormatCache.SelectionBitmapPath);
                            var dec = new PngBitmapDecoder(stream,
                                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                            data.SetImage(dec.Frames[0]);
                        }
                        catch { }
                    }
                    break;
            }

            // Tag for SmartPaste recognition
            data.SetData(FormatCache.CopyIdFormat, package.Id);

            // Set targeted clipboard
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { Clipboard.SetDataObject(data, true); break; }
                catch (System.Runtime.InteropServices.COMException)
                { await Task.Delay(100); }
            }

            // Inject via Ctrl+V
            await Task.Delay(50);
            _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);
        }

        // ── Normal paste modes (unchanged) ───────────────────────────

        private void PasteMode1(string text) { _simulator.Keyboard.TextEntry(text); }

        private void PasteMode2(string text)
        {
            string[] items = SplitText(text);
            for (int i = 0; i < items.Length; i++)
            {
                _simulator.Keyboard.TextEntry(items[i]);
                if (i < items.Length - 1)
                {
                    Thread.Sleep(DelayMilliseconds);
                    _simulator.Keyboard.KeyPress(VirtualKeyCode.SPACE);
                    Thread.Sleep(DelayMilliseconds);
                }
            }
        }

        private void PasteMode3(string text)
        {
            string[] items = SplitText(text);
            for (int i = 0; i < items.Length; i++)
            {
                _simulator.Keyboard.TextEntry(items[i]);
                Thread.Sleep(DelayMilliseconds);
                _simulator.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                Thread.Sleep(DelayMilliseconds);
            }
        }

        // ── Simulation modes (unchanged) ─────────────────────────────

        private void PasteMode1Sim(string text) { SimulateTyping(text, false); }

        private void PasteMode2Sim(string text)
        {
            string[] items = SplitText(text);
            for (int i = 0; i < items.Length; i++)
            {
                SimulateTyping(items[i], false);
                if (i < items.Length - 1)
                {
                    SleepHumanDelay();
                    _simulator.Keyboard.KeyPress(VirtualKeyCode.SPACE);
                    SleepHumanDelay();
                }
            }
        }

        private void PasteMode3Sim(string text)
        {
            string[] items = SplitText(text);
            for (int i = 0; i < items.Length; i++)
            {
                SimulateTyping(items[i], true);
                SleepHumanDelay();
                _simulator.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                if (TeleEndOfLinePause) SleepHumanDelay();
            }
        }

        private int _flowCounter = 0;
        private bool _inFlow = false;
        private int _charCountSinceBreath = 0;

        private void SimulateTyping(string text, bool isItemEnd)
        {
            _charCountSinceBreath = 0;
            foreach (char c in text)
            {
                // Dashboard: cancel/pause support
                if (_cancelRequested) return;
                while (_pauseRequested && !_cancelRequested)
                    Thread.Sleep(100);
                if (_cancelRequested) return;

                if (TeleRealisticTypos && ShouldMakeTypo())
                    TypeWithTypo(c);
                else if (TeleDoubleKeyStrokes && ShouldDoubleKey())
                    TypeWithDoubleKey(c);
                else if (TeleRandomCapsErrors && ShouldCapError(c))
                    TypeWithCapError(c);
                else
                    _simulator.Keyboard.TextEntry(c.ToString());

                _charCountSinceBreath++;
                DashProgress++;
                DashLastChar = c.ToString();

                if (TeleBreathingPauses && _charCountSinceBreath >= TeleBreathingInterval)
                {
                    Thread.Sleep(_random.Next(400, 1200));
                    _charCountSinceBreath = 0;
                }

                SleepHumanDelay();
            }
        }

        private void SleepHumanDelay()
        {
            int baseDelay = (int)(Math.Max(TelePasteDelay, 10) * EnergyMultiplier);

            if (TeleFlowBursts && _inFlow && _flowCounter > 0)
            {
                Thread.Sleep(_random.Next(10, 40));
                _flowCounter--;
                if (_flowCounter <= 0) _inFlow = false;
                return;
            }

            if (TeleFlowBursts && !_inFlow && _random.Next(100) < 10)
            {
                _inFlow = true;
                _flowCounter = _random.Next(5, 20);
                Thread.Sleep(_random.Next(10, 40));
                return;
            }

            if (TeleMicroPauses && _random.Next(100) < 5)
            {
                Thread.Sleep(_random.Next(300, 800));
                return;
            }

            if (TeleVariableRhythm)
                Thread.Sleep(_random.Next(baseDelay / 2, baseDelay * 2 + 1));
            else
                Thread.Sleep(baseDelay);
        }

        private bool ShouldMakeTypo() => _random.Next(1000) < 15;
        private bool ShouldDoubleKey() => _random.Next(1000) < 10;
        private bool ShouldCapError(char c) => char.IsLetter(c) && _random.Next(1000) < 8;

        private void TypeWithTypo(char correctChar)
        {
            char wrongChar = GetRandomWrongChar(correctChar);
            _simulator.Keyboard.TextEntry(wrongChar.ToString());
            Thread.Sleep(_random.Next(80, 200));
            _simulator.Keyboard.KeyPress(VirtualKeyCode.BACK);
            Thread.Sleep(_random.Next(60, 150));
            _simulator.Keyboard.TextEntry(correctChar.ToString());
        }

        private void TypeWithDoubleKey(char c)
        {
            _simulator.Keyboard.TextEntry(c.ToString());
            Thread.Sleep(_random.Next(20, 60));
            _simulator.Keyboard.TextEntry(c.ToString());
            Thread.Sleep(_random.Next(80, 150));
            _simulator.Keyboard.KeyPress(VirtualKeyCode.BACK);
        }

        private void TypeWithCapError(char c)
        {
            if (char.IsLower(c))
                _simulator.Keyboard.TextEntry(char.ToUpper(c).ToString());
            else
                _simulator.Keyboard.TextEntry(char.ToLower(c).ToString());
            Thread.Sleep(_random.Next(100, 250));
            _simulator.Keyboard.KeyPress(VirtualKeyCode.BACK);
            Thread.Sleep(_random.Next(60, 120));
            _simulator.Keyboard.TextEntry(c.ToString());
        }

        private char GetRandomWrongChar(char correct)
        {
            string lower = "abcdefghijklmnopqrstuvwxyz";
            int idx = lower.IndexOf(char.ToLower(correct));
            if (idx < 0) return 'a';
            int offset = _random.Next(-2, 3);
            if (offset == 0) offset = 1;
            int newIdx = (idx + offset + lower.Length) % lower.Length;
            char wrong = lower[newIdx];
            return char.IsUpper(correct) ? char.ToUpper(wrong) : wrong;
        }

        private string[] SplitText(string text)
        {
            char[] delimiters = new[]
            {
                '\r', '\n', ',', ';', ':', '.', '/', '\\', '|', '•', '·',
                '，', '；', '：', '。', '、', '｜'
            };

            if (text.IndexOfAny(delimiters) >= 0)
            {
                return text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim())
                           .Where(s => !string.IsNullOrWhiteSpace(s))
                           .ToArray();
            }
            return text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => !string.IsNullOrWhiteSpace(s))
                       .ToArray();
        }

        public void Dispose()
        {
            UnregisterHotkeys();
        }
    }
}
