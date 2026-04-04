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
        private GlobalHotkey _hotkeyMode1;
        private GlobalHotkey _hotkeyMode2;
        private GlobalHotkey _hotkeyMode3;
        private GlobalHotkey _hotkeyMode4; // Ctrl + Shift + Alt + V (Human Sim)
        private InputSimulator _simulator;
        private static readonly Random _random = new Random();

        public int DelayMilliseconds { get; set; } = 30;
        public bool HumanSimulation { get; set; } = false;
        public bool HumanTypos { get; set; } = false;

        public SmartPasteManager()
        {
            _simulator = new InputSimulator();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? new Window()).EnsureHandle();

            // Mode 1: Ctrl + Win + V (Normal)
            _hotkeyMode1 = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_WIN, (uint)VirtualKeyCode.VK_V, hwnd, 9001);
            _hotkeyMode1.HotkeyPressed += (s, e) => Paste(1);

            // Mode 2: Ctrl + Alt + V (Word by Word + Space)
            _hotkeyMode2 = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT, (uint)VirtualKeyCode.VK_V, hwnd, 9002);
            _hotkeyMode2.HotkeyPressed += (s, e) => Paste(2);

            // Mode 3: Ctrl + Shift + V (Word by Word + Enter)
            _hotkeyMode3 = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_SHIFT, (uint)VirtualKeyCode.VK_V, hwnd, 9003);
            _hotkeyMode3.HotkeyPressed += (s, e) => Paste(3);

            // Mode 4: Ctrl + Shift + Alt + V (Human Simulation - forces sim on any mode)
            _hotkeyMode4 = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_SHIFT | GlobalHotkey.MOD_ALT, (uint)VirtualKeyCode.VK_V, hwnd, 9007);
            _hotkeyMode4.HotkeyPressed += (s, e) => PasteWithSim(1);
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
                        if (useSim) PasteMode1Sim(text); else PasteMode1(text);
                        break;
                    case 2:
                        if (useSim) PasteMode2Sim(text); else PasteMode2(text);
                        break;
                    case 3:
                        if (useSim) PasteMode3Sim(text); else PasteMode3(text);
                        break;
                }
            });
        }

        private async void PasteWithSim(int mode)
        {
            if (!Clipboard.ContainsText()) return;
            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            await Task.Delay(100);

            await Task.Run(() =>
            {
                switch (mode)
                {
                    case 1: PasteMode1Sim(text); break;
                    case 2: PasteMode2Sim(text); break;
                    case 3: PasteMode3Sim(text); break;
                }
            });
        }

        // --- Normal Modes ---

        private void PasteMode1(string text)
        {
            _simulator.Keyboard.TextEntry(text);
        }

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

        // --- Human Simulation Modes ---

        private void PasteMode1Sim(string text)
        {
            foreach (char c in text)
            {
                if (HumanTypos && ShouldMakeTypo())
                {
                    TypeWithTypo(c);
                }
                else
                {
                    _simulator.Keyboard.TextEntry(c.ToString());
                }
                SleepHumanDelay();
            }
        }

        private void PasteMode2Sim(string text)
        {
            string[] items = SplitText(text);
            for (int i = 0; i < items.Length; i++)
            {
                foreach (char c in items[i])
                {
                    if (HumanTypos && ShouldMakeTypo())
                    {
                        TypeWithTypo(c);
                    }
                    else
                    {
                        _simulator.Keyboard.TextEntry(c.ToString());
                    }
                    SleepHumanDelay();
                }
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
                foreach (char c in items[i])
                {
                    if (HumanTypos && ShouldMakeTypo())
                    {
                        TypeWithTypo(c);
                    }
                    else
                    {
                        _simulator.Keyboard.TextEntry(c.ToString());
                    }
                    SleepHumanDelay();
                }
                SleepHumanDelay();
                _simulator.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                SleepHumanDelay();
            }
        }

        // --- Human Simulation Engine ---

        private int _flowCounter = 0;
        private bool _inFlow = false;

        private void SleepHumanDelay()
        {
            int delay;

            // Flow mode: rapid typing burst
            if (_inFlow && _flowCounter > 0)
            {
                delay = _random.Next(10, 40);
                _flowCounter--;
                if (_flowCounter <= 0) _inFlow = false;
            }
            // 10% chance to enter flow mode
            else if (_random.Next(100) < 10)
            {
                _inFlow = true;
                _flowCounter = _random.Next(5, 20);
                delay = _random.Next(10, 40);
            }
            // 5% chance of a "thinking" pause
            else if (_random.Next(100) < 5)
            {
                delay = _random.Next(300, 800);
            }
            // Normal variable rhythm
            else
            {
                int baseDelay = Math.Max(DelayMilliseconds, 10);
                delay = _random.Next(baseDelay / 2, baseDelay * 2 + 1);
            }

            Thread.Sleep(delay);
        }

        private bool ShouldMakeTypo()
        {
            // 1.5% chance of a typo
            return _random.Next(1000) < 15;
        }

        private void TypeWithTypo(char correctChar)
        {
            // Type a random wrong letter
            char wrongChar = GetRandomWrongChar(correctChar);
            _simulator.Keyboard.TextEntry(wrongChar.ToString());
            Thread.Sleep(_random.Next(80, 200));

            // Backspace
            _simulator.Keyboard.KeyPress(VirtualKeyCode.BACK);
            Thread.Sleep(_random.Next(60, 150));

            // Type the correct character
            _simulator.Keyboard.TextEntry(correctChar.ToString());
        }

        private char GetRandomWrongChar(char correct)
        {
            // Common adjacent-key mistakes on QWERTY
            string lower = "abcdefghijklmnopqrstuvwxyz";
            int idx = lower.IndexOf(char.ToLower(correct));
            if (idx < 0) return 'a';

            int offset = _random.Next(-2, 3);
            if (offset == 0) offset = 1;
            int newIdx = (idx + offset + lower.Length) % lower.Length;
            char wrong = lower[newIdx];

            // Preserve case
            return char.IsUpper(correct) ? char.ToUpper(wrong) : wrong;
        }

        // --- Smart Splitting ---

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
            _hotkeyMode1?.Dispose();
            _hotkeyMode2?.Dispose();
            _hotkeyMode3?.Dispose();
            _hotkeyMode4?.Dispose();
        }
    }
}
