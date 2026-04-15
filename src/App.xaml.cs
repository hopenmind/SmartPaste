using System;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Interop;

namespace SmartPaste;

public partial class App : Application
{
    private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? notifyIcon;
    public SmartPasteManager pasteManager { get; private set; } = null!;
    public CaseConverterManager caseConverterManager { get; private set; } = null!;
    public AlwaysOnTopManager alwaysOnTopManager { get; private set; } = null!;
    public SmartCopyManager smartCopyManager { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;

    private MainWindow? _mainWindow;
    private IntPtr _hwnd;
    private GlobalHotkey? teleworkHotkey;
    private PasteInterceptor? pasteInterceptor;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        Settings = SettingsManager.Load();

        notifyIcon = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon();
        notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
        notifyIcon.ToolTipText = "SmartPaste - Right click for options";

        var contextMenu = new System.Windows.Controls.ContextMenu();
        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (s, args) => ShowMainWindow();
        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (s, args) => Application.Current.Shutdown();
        
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(exitItem);

        notifyIcon.ContextMenu = contextMenu;
        notifyIcon.TrayMouseDoubleClick += (s, args) => ShowMainWindow();

        // Create hidden window to get hwnd
        var hiddenWindow = new Window();
        _hwnd = new WindowInteropHelper(hiddenWindow).EnsureHandle();

        pasteManager = new SmartPasteManager();
        caseConverterManager = new CaseConverterManager();
        alwaysOnTopManager = new AlwaysOnTopManager();
        smartCopyManager = new SmartCopyManager();

        // Load language
        SetLanguage(Settings.Language);

        // Keyboard interceptor — Ctrl+V (SmartInject) + Ctrl+C (auto-enhance)
        pasteInterceptor = new PasteInterceptor(_hwnd, OnSmartPasteIntercept, OnSmartCopyIntercept);
        pasteInterceptor.Enabled = Settings.EnablePasteIntercept;
        pasteInterceptor.OverrideCtrlC = Settings.OverrideCtrlC;

        RegisterAllHotkeys();

        _mainWindow = new MainWindow();

        if (Settings == null || !Settings.StartMinimized)
        {
            ShowMainWindow();
        }
    }

    private void RegisterAllHotkeys()
    {
        // Smart Paste (only if enabled)
        if (Settings.EnableSmartPaste)
        {
            pasteManager.RegisterHotkeys(_hwnd,
                Settings.SmartPasteShortcut1,
                Settings.SmartPasteShortcut2,
                Settings.SmartPasteShortcut3);
        }
        else
        {
            pasteManager.UnregisterHotkeys();
        }

        // Smart Copy
        if (Settings.EnableSmartCopy)
        {
            smartCopyManager.RegisterHotkey(_hwnd, Settings.SmartCopyShortcut);
        }
        else
        {
            smartCopyManager.UnregisterHotkey();
        }

        // Case Converter
        if (Settings.EnableCaseConverter)
        {
            caseConverterManager.RegisterHotkey(_hwnd, Settings.CaseConverterShortcut);
        }
        else
        {
            caseConverterManager.UnregisterHotkey();
        }

        // Always On Top
        if (Settings.EnableAlwaysOnTop)
        {
            alwaysOnTopManager.RegisterHotkey(_hwnd, Settings.AlwaysOnTopShortcut);
        }
        else
        {
            alwaysOnTopManager.UnregisterHotkey();
        }

        // Telework shortcut (opens settings to Telework tab)
        teleworkHotkey?.Dispose();
        if (ShortcutParser.TryParse(Settings.TeleworkShortcut, out uint teleMods, out var teleKeyCode))
        {
            teleworkHotkey = new GlobalHotkey(teleMods, (uint)teleKeyCode, _hwnd, 99);
            teleworkHotkey.HotkeyPressed += (s, e) => TeleworkShortcutPressed();
        }

        // Intercept toggles
        if (pasteInterceptor != null)
        {
            pasteInterceptor.Enabled = Settings.EnablePasteIntercept;
            pasteInterceptor.OverrideCtrlC = Settings.OverrideCtrlC;
        }

        // Apply telework settings
        pasteManager.DelayMilliseconds = Settings.DelayMilliseconds;
        pasteManager.TeleVariableRhythm = Settings.TeleVariableRhythm;
        pasteManager.TeleMicroPauses = Settings.TeleMicroPauses;
        pasteManager.TeleFlowBursts = Settings.TeleFlowBursts;
        pasteManager.TeleRealisticTypos = Settings.TeleRealisticTypos;
        pasteManager.TeleRandomCapsErrors = Settings.TeleRandomCapsErrors;
        pasteManager.TeleDoubleKeyStrokes = Settings.TeleDoubleKeyStrokes;
        pasteManager.TeleCursorNavigation = Settings.TeleCursorNavigation;
        pasteManager.TeleAutoCorrectMistakes = Settings.TeleAutoCorrectMistakes;
        pasteManager.TeleBreathingPauses = Settings.TeleBreathingPauses;
        pasteManager.TeleEndOfLinePause = Settings.TeleEndOfLinePause;
        pasteManager.TelePasteDelay = Settings.TelePasteDelay;
        pasteManager.TeleWordChunkSize = Settings.TeleWordChunkSize;
        pasteManager.TeleBreathingInterval = Settings.TeleBreathingInterval;
    }

    public void RefreshHotkeys()
    {
        RegisterAllHotkeys();
    }

    public void SetLanguage(string lang)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/src/Lang/{lang}.xaml")
        };

        // Remove previous language dictionary if any
        var existing = Resources.MergedDictionaries;
        for (int i = existing.Count - 1; i >= 0; i--)
        {
            var src = existing[i].Source?.ToString() ?? "";
            if (src.Contains("Lang/")) existing.RemoveAt(i);
        }
        existing.Add(dict);

        Settings.Language = lang;
        SettingsManager.Save(Settings);
    }

    /// <summary>
    /// Called by PasteInterceptor when Ctrl+C is pressed with OverrideCtrlC enabled.
    /// Normal Ctrl+C already happened — we just enhance the clipboard.
    /// </summary>
    private void OnSmartCopyIntercept()
    {
        smartCopyManager.EnhanceClipboard();
    }

    /// <summary>
    /// Called by PasteInterceptor when Ctrl+V is pressed and SmartCopy content exists.
    /// </summary>
    private async void OnSmartPasteIntercept()
    {
        try
        {
            IDataObject? clip = System.Windows.Clipboard.GetDataObject();
            string? clipId = clip?.GetData(FormatCache.CopyIdFormat) as string;
            if (string.IsNullOrEmpty(clipId)) return;

            var package = FormatCache.Load();
            if (package == null || package.Id != clipId) return;

            pasteInterceptor?.BeginInject();
            try
            {
                await pasteManager.SmartInject(package);
            }
            finally
            {
                // Small delay so the injected Ctrl+V isn't re-intercepted
                await System.Threading.Tasks.Task.Delay(200);
                pasteInterceptor?.EndInject();
            }
        }
        catch { }
    }

    private void ShowMainWindow()
    {
        ShowMainWindowToTab(0);
    }

    private void ShowMainWindowToTab(int tabIndex)
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow();
        }
        _mainWindow.Show();
        _mainWindow.Activate();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.SwitchToTab(tabIndex);
    }

    private void TeleworkShortcutPressed()
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() => ShowMainWindowToTab(1)));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        pasteInterceptor?.Dispose();
        notifyIcon?.Dispose();
        pasteManager?.Dispose();
        caseConverterManager?.Dispose();
        alwaysOnTopManager?.Dispose();
        smartCopyManager?.Dispose();
        teleworkHotkey?.Dispose();
        base.OnExit(e);
    }
}
