using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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

        private async void Paste(int mode)
        {
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

        // --- Normal Modes ---
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

        // --- Simulation Modes ---
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
                if (TeleRealisticTypos && ShouldMakeTypo())
                {
                    TypeWithTypo(c);
                }
                else if (TeleDoubleKeyStrokes && ShouldDoubleKey())
                {
                    TypeWithDoubleKey(c);
                }
                else if (TeleRandomCapsErrors && ShouldCapError(c))
                {
                    TypeWithCapError(c);
                }
                else
                {
                    _simulator.Keyboard.TextEntry(c.ToString());
                }

                _charCountSinceBreath++;

                // Breathing pauses
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
            int baseDelay = Math.Max(TelePasteDelay, 10);

            // Flow mode
            if (TeleFlowBursts && _inFlow && _flowCounter > 0)
            {
                Thread.Sleep(_random.Next(10, 40));
                _flowCounter--;
                if (_flowCounter <= 0) _inFlow = false;
                return;
            }

            // Enter flow
            if (TeleFlowBursts && !_inFlow && _random.Next(100) < 10)
            {
                _inFlow = true;
                _flowCounter = _random.Next(5, 20);
                Thread.Sleep(_random.Next(10, 40));
                return;
            }

            // Micro-pauses
            if (TeleMicroPauses && _random.Next(100) < 5)
            {
                Thread.Sleep(_random.Next(300, 800));
                return;
            }

            // Variable rhythm
            if (TeleVariableRhythm)
            {
                Thread.Sleep(_random.Next(baseDelay / 2, baseDelay * 2 + 1));
            }
            else
            {
                Thread.Sleep(baseDelay);
            }
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
