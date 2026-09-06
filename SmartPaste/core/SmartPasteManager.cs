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
            try
            {
                // ── SmartInject path ──
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
                    switch (mode)
                    {
                        case 1: PasteMode3Sim(text); break;
                        case 2: PasteMode2Sim(text); break;
                        case 3: PasteMode1Sim(text); break;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Paste error: {ex.Message}");
            }
        }

        // ── SmartInject - target-aware paste from ContentPackage ─────
        //
        // Instead of putting ALL formats on the clipboard and hoping
        // the target picks the right one, we put ONLY the optimal
        // format → the app has no choice but to use it.
        //
        // Rich targets get the self-contained CF_HTML (data: URI images,
        // SVG/MathJax preserved). RTF is only a fallback: it carries raster
        // images only, and Word/LibreOffice prefer RTF over HTML when both
        // are present - which would silently drop the SVG content.

        private const int VK_V = 0x56;
        private const int KeyReleaseTimeoutMs = 300;
        private const int ClipboardSetTimeoutMs = 300;

        /// <summary>
        /// Target-aware paste from ContentPackage.
        /// Called from SmartPaste hotkeys AND from PasteInterceptor (Ctrl+V override).
        /// Always ends by sending Ctrl+V: if the targeted clipboard cannot be set,
        /// the existing (multi-format) clipboard content is pasted instead so the
        /// user's paste is never swallowed.
        /// </summary>
        public async Task SmartInject(ContentPackage package)
        {
            var (target, processName) = TargetDetector.Detect();

            // Wait (bounded) for Shift/Alt/Win and V from the triggering chord to be
            // released, so the simulated Ctrl+V is not received as Ctrl+Shift+V etc.
            await ClipboardSync.WaitForChordReleaseAsync(KeyReleaseTimeoutMs, VK_V);

            try
            {
                DataObject data = BuildTargetedDataObject(package, target, processName);
                await SetClipboardAsync(data);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SmartInject: clipboard update failed, pasting existing content ({ex.Message})");
            }

            // Inject via Ctrl+V
            _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);
        }

        /// <summary>
        /// Types arbitrary text into the foreground window using the human-rhythm engine
        /// (current Telework settings). A short delay lets the user focus the target field
        /// after the SmartPaste window steps aside.
        /// </summary>
        private CancellationTokenSource? _typingCts;

        /// <summary>True while a Telework file-typing run is in progress (not yet cancelled).</summary>
        public bool IsTyping => _typingCts is { IsCancellationRequested: false };

        /// <summary>Aborts the current Telework typing run (Stop button / Escape).</summary>
        public void CancelTyping() => _typingCts?.Cancel();

        public async Task TypeFileText(string text, int startDelayMs = 2500)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _typingCts?.Cancel();
            using var cts = new CancellationTokenSource();
            _typingCts = cts;
            var token = cts.Token;
            try
            {
                await ClipboardSync.WaitForChordReleaseAsync(KeyReleaseTimeoutMs);
                await Task.Delay(startDelayMs, token);           // countdown; cancellable
                await Task.Run(() => SimulateTyping(text, false, token), token);
            }
            catch (OperationCanceledException) { /* stopped by the user */ }
            finally { _typingCts = null; }
        }

        /// <summary>
        /// Re-sends a plain Ctrl+V. Used by the interceptor when it suppressed the
        /// user's Ctrl+V but no usable ContentPackage is available.
        /// </summary>
        public async Task SendNativePaste()
        {
            await ClipboardSync.WaitForChordReleaseAsync(KeyReleaseTimeoutMs, VK_V);
            _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);
        }

        /// <summary>Builds the clipboard payload carrying only the format(s) the target consumes best.</summary>
        private static DataObject BuildTargetedDataObject(ContentPackage package, TargetType target, string processName)
        {
            string? cfHtml = string.IsNullOrEmpty(package.HtmlFragment)
                ? null
                : FormatCache.BuildCFHtml(package.HtmlFragment, package.SourceUrl);
            string? rtf = string.IsNullOrEmpty(package.RtfContent) ? null : package.RtfContent;

            var data = new DataObject();

            switch (target)
            {
                case TargetType.Office:
                case TargetType.Browser:
                case TargetType.Electron:
                case TargetType.RichText when !TargetDetector.IsRtfOnlyEditor(processName):
                    // Self-contained CF_HTML is the primary rich format; RTF only when no HTML exists
                    if (cfHtml != null)
                        data.SetData(DataFormats.Html, cfHtml);
                    else if (rtf != null)
                        data.SetData(DataFormats.Rtf, rtf);
                    if (package.PlainText != null)
                        data.SetData(DataFormats.UnicodeText, package.PlainText);
                    break;

                case TargetType.RichText:
                    // RichEdit-based editors (WordPad) ignore CF_HTML → RTF first (raster images only)
                    if (rtf != null)
                        data.SetData(DataFormats.Rtf, rtf);
                    else if (cfHtml != null)
                        data.SetData(DataFormats.Html, cfHtml);
                    if (package.PlainText != null)
                        data.SetData(DataFormats.UnicodeText, package.PlainText);
                    break;

                case TargetType.Markdown:
                    // Markdown-first editors: convert the HTML fragment to Markdown text
                    string md = HtmlToMarkdown.Convert(package.HtmlFragment);
                    data.SetData(DataFormats.UnicodeText, string.IsNullOrEmpty(md) ? (package.PlainText ?? "") : md);
                    break;

                case TargetType.PlainText:
                    // Only text - no rich format overhead
                    data.SetData(DataFormats.UnicodeText, package.PlainText ?? "");
                    break;

                case TargetType.ImageEditor:
                    // Vector editors (Inkscape) prefer the captured SVG; all get the bitmap fallback
                    if (processName == "inkscape") TrySetSvg(data, package);
                    TrySetSelectionBitmap(data);
                    if (package.PlainText != null)
                        data.SetData(DataFormats.UnicodeText, package.PlainText);
                    break;

                default:
                    // Unknown app → all formats, let it choose
                    if (cfHtml != null)
                        data.SetData(DataFormats.Html, cfHtml);
                    if (rtf != null)
                        data.SetData(DataFormats.Rtf, rtf);
                    TrySetSelectionBitmap(data);
                    if (package.PlainText != null)
                        data.SetData(DataFormats.UnicodeText, package.PlainText);
                    break;
            }

            return data;
        }

        /// <summary>Sets the captured SVG vector on the clipboard (image/svg+xml) for vector editors.</summary>
        private static void TrySetSvg(DataObject data, ContentPackage package)
        {
            var svg = package.Images.FirstOrDefault(i => i.IsSvg && i.Data != null && i.Data.Length > 0);
            if (svg?.Data == null) return;
            try { data.SetData("image/svg+xml", new MemoryStream(svg.Data)); } catch { }
        }

        private static void TrySetSelectionBitmap(DataObject data)
        {
            if (!FormatCache.HasSelectionBitmap) return;
            try
            {
                using var stream = File.OpenRead(FormatCache.SelectionBitmapPath);
                var decoder = new PngBitmapDecoder(stream,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                data.SetImage(decoder.Frames[0]);
            }
            catch { }
        }

        /// <summary>
        /// Sets the clipboard, then waits until the new content is visible through
        /// the clipboard sequence number instead of sleeping a fixed amount of time.
        /// </summary>
        private static async Task SetClipboardAsync(DataObject data)
        {
            uint sequenceBefore = ClipboardSync.GetSequenceNumber();

            System.Runtime.InteropServices.ExternalException? lastError = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(data, true);
                    lastError = null;
                    break;
                }
                catch (System.Runtime.InteropServices.ExternalException ex)
                {
                    lastError = ex;   // clipboard locked by another app - retry
                    await Task.Delay(50 * (attempt + 1));
                }
            }
            if (lastError != null)
                throw new InvalidOperationException("Clipboard is locked by another application", lastError);

            await ClipboardSync.WaitForChangeAsync(sequenceBefore, ClipboardSetTimeoutMs, fallbackDelayMs: 50);
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

        private void SimulateTyping(string text, bool isItemEnd, CancellationToken token = default)
        {
            _charCountSinceBreath = 0;
            foreach (char c in text)
            {
                if (token.IsCancellationRequested) return;
                if (TeleRealisticTypos && ShouldMakeTypo())
                    TypeWithTypo(c);
                else if (TeleDoubleKeyStrokes && ShouldDoubleKey())
                    TypeWithDoubleKey(c);
                else if (TeleRandomCapsErrors && ShouldCapError(c))
                    TypeWithCapError(c);
                else
                    _simulator.Keyboard.TextEntry(c.ToString());

                _charCountSinceBreath++;

                if (TeleBreathingPauses && _charCountSinceBreath >= TeleBreathingInterval)
                {
                    Thread.Sleep(Random.Shared.Next(400, 1200));
                    _charCountSinceBreath = 0;
                }

                SleepHumanDelay();
            }
        }

        private void SleepHumanDelay()
        {
            int baseDelay = Math.Max(DelayMilliseconds, 5);

            if (TeleFlowBursts && _inFlow && _flowCounter > 0)
            {
                Thread.Sleep(Random.Shared.Next(10, 40));
                _flowCounter--;
                if (_flowCounter <= 0) _inFlow = false;
                return;
            }

            if (TeleFlowBursts && !_inFlow && Random.Shared.Next(100) < 10)
            {
                _inFlow = true;
                _flowCounter = Random.Shared.Next(5, 20);
                Thread.Sleep(Random.Shared.Next(10, 40));
                return;
            }

            if (TeleMicroPauses && Random.Shared.Next(100) < 5)
            {
                Thread.Sleep(Random.Shared.Next(300, 800));
                return;
            }

            if (TeleVariableRhythm)
                Thread.Sleep(Random.Shared.Next(baseDelay / 2, baseDelay * 2 + 1));
            else
                Thread.Sleep(baseDelay);
        }

        private bool ShouldMakeTypo() => Random.Shared.Next(1000) < 15;
        private bool ShouldDoubleKey() => Random.Shared.Next(1000) < 10;
        private bool ShouldCapError(char c) => char.IsLetter(c) && Random.Shared.Next(1000) < 8;

        private void TypeWithTypo(char correctChar)
        {
            char wrongChar = GetRandomWrongChar(correctChar);
            _simulator.Keyboard.TextEntry(wrongChar.ToString());
            Thread.Sleep(Random.Shared.Next(80, 200));
            _simulator.Keyboard.KeyPress(VirtualKeyCode.BACK);
            Thread.Sleep(Random.Shared.Next(60, 150));
            _simulator.Keyboard.TextEntry(correctChar.ToString());
        }

        private void TypeWithDoubleKey(char c)
        {
            _simulator.Keyboard.TextEntry(c.ToString());
            Thread.Sleep(Random.Shared.Next(20, 60));
            _simulator.Keyboard.TextEntry(c.ToString());
            Thread.Sleep(Random.Shared.Next(80, 150));
            _simulator.Keyboard.KeyPress(VirtualKeyCode.BACK);
        }

        private void TypeWithCapError(char c)
        {
            if (char.IsLower(c))
                _simulator.Keyboard.TextEntry(char.ToUpper(c).ToString());
            else
                _simulator.Keyboard.TextEntry(char.ToLower(c).ToString());
            Thread.Sleep(Random.Shared.Next(100, 250));
            _simulator.Keyboard.KeyPress(VirtualKeyCode.BACK);
            Thread.Sleep(Random.Shared.Next(60, 120));
            _simulator.Keyboard.TextEntry(c.ToString());
        }

        private char GetRandomWrongChar(char correct)
        {
            string lower = "abcdefghijklmnopqrstuvwxyz";
            int idx = lower.IndexOf(char.ToLower(correct));
            if (idx < 0) return 'a';
            int offset = Random.Shared.Next(-2, 3);
            if (offset == 0) offset = 1;
            int newIdx = (idx + offset + lower.Length) % lower.Length;
            char wrong = lower[newIdx];
            return char.IsUpper(correct) ? char.ToUpper(wrong) : wrong;
        }

        private static readonly char[] ListPunct =
            { ',', ';', ':', '.', '/', '\\', '|', '•', '·', '、', '，', '；', '：', '。', '｜' };

        // A punctuation mark separates list items ONLY when whitespace sits immediately
        // before or after it, and the punctuation (with its surrounding whitespace) is then
        // removed. Punctuation with no adjacent space is never a cut, so an email's ".com",
        // a URL's "://" or a time's ":" stays intact. A newline always separates. When no
        // punctuation is present at all, a space-separated list falls back to whitespace.
        private static readonly System.Text.RegularExpressions.Regex ListSplitRegex =
            new(@"\s*[\r\n]+\s*|\s+[,;:./\\|•·、，；：。｜]+\s*|[,;:./\\|•·、，；：。｜]+\s+",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private string[] SplitText(string text)
        {
            var items = ListSplitRegex.Split(text)
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToArray();
            if (items.Length > 1) return items;

            // A single chunk survived: only split on whitespace when there is no punctuation
            // to honour, so a space-separated email pair is kept whole rather than shattered.
            if (text.IndexOfAny(ListPunct) < 0)
                return text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim())
                           .Where(s => !string.IsNullOrWhiteSpace(s))
                           .ToArray();
            return items;
        }

        public void Dispose()
        {
            UnregisterHotkeys();
        }
    }
}
