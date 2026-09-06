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
    private TrayMenu? _trayMenu;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        Settings = SettingsManager.Load();
        ThemeManager.Apply(Settings.ThemeBrand, Settings.ThemeMode);

        notifyIcon = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon();
        notifyIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(ThemeManager.EmblemUri);
        notifyIcon.ToolTipText = SmartPaste.Localization.Strings.TrayTooltip;
        ThemeManager.ThemeChanged += (s, args) =>
        {
            if (notifyIcon != null)
                notifyIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(ThemeManager.EmblemUri);
        };

        // Full settings surface lives in the tray menu itself, so a minimized app is
        // never forced to re-open just to change a setting. This same menu is the whole
        // UI of the tray-only "lite" edition.
        _trayMenu = new TrayMenu(Settings, OnTraySettingsChanged, ShowMainWindow,
            () => ShowMainWindowToTab(2),          // Telework panel (sliders + timing)
            OpenShortcutEditorFromTray,            // rebind editor for a shortcut tag
            () => Application.Current.Shutdown());
        notifyIcon.ContextMenu = _trayMenu.Menu;
        notifyIcon.TrayMouseDoubleClick += (s, args) => ShowMainWindow();

        // Create hidden window to get hwnd
        var hiddenWindow = new Window();
        _hwnd = new WindowInteropHelper(hiddenWindow).EnsureHandle();

        pasteManager = new SmartPasteManager();
        caseConverterManager = new CaseConverterManager();
        alwaysOnTopManager = new AlwaysOnTopManager();
        smartCopyManager = new SmartCopyManager();

        // Ctrl+V interceptor - replaces native paste when SmartCopy content is available
        pasteInterceptor = new PasteInterceptor(_hwnd, OnSmartPasteIntercept);
        pasteInterceptor.Enabled = Settings.EnablePasteIntercept;

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

        // Paste intercept toggle
        if (pasteInterceptor != null)
            pasteInterceptor.Enabled = Settings.EnablePasteIntercept;

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

    /// <summary>
    /// Invoked by the tray menu after any setting is toggled: persist to disk, re-apply
    /// hotkeys/interception, and refresh the main window's wheel if it happens to be open.
    /// The main window shares the same AppSettings instance, so no data is copied.
    /// </summary>
    private void OnTraySettingsChanged()
    {
        SettingsManager.Save(Settings);
        RefreshHotkeys();
        _mainWindow?.RefreshFromSettings();
    }

    /// <summary>
    /// Opens the rebind editor for a shortcut tag in a small dedicated capture window owned
    /// by the tray subsystem. The main application window is never opened for this - the tray
    /// stays fully autonomous (this is also how the future lite edition edits shortcuts).
    /// </summary>
    private void OpenShortcutEditorFromTray(string tag)
    {
        var win = new ShortcutCaptureWindow(Settings, tag, OnTraySettingsChanged);
        win.Show();
    }

    /// <summary>
    /// Registers a temporary global Escape hotkey so Telework typing can be stopped with the
    /// keyboard even though the typed-into target holds the foreground focus. Dispose to stop.
    /// </summary>
    public IDisposable BeginTypingEscapeWatch(Action onEscape)
    {
        var hk = new GlobalHotkey(GlobalHotkey.MOD_NONE, 0x1B /* VK_ESCAPE */, _hwnd, 9100);
        hk.HotkeyPressed += (s, e) => onEscape();
        return hk;
    }

    /// <summary>
    /// Called by PasteInterceptor when a physical Ctrl+V is pressed while SmartCopy
    /// content is tagged on the clipboard. The interceptor has already suppressed
    /// the keystroke, so a paste MUST be delivered in every case: SmartInject when
    /// the tagged ContentPackage is available, otherwise a plain re-sent Ctrl+V.
    /// EndInject() then re-evaluates the clipboard so the untagged injected content
    /// does not leave a stale "smart content" flag behind (which would swallow the
    /// next plain Ctrl+V).
    /// </summary>
    private async void OnSmartPasteIntercept()
    {
        pasteInterceptor?.BeginInject();
        try
        {
            ContentPackage? package = TryLoadTaggedPackage();
            if (package != null)
                await pasteManager.SmartInject(package);
            else
                await pasteManager.SendNativePaste();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SmartPaste intercept failed: {ex.Message}");
        }
        finally
        {
            // Let the injected Ctrl+V drain through the keyboard hook before re-arming
            await System.Threading.Tasks.Task.Delay(200);
            pasteInterceptor?.EndInject();
        }
    }

    /// <summary>Returns the cached ContentPackage matching the clipboard CopyId tag, or null.</summary>
    private static ContentPackage? TryLoadTaggedPackage()
    {
        try
        {
            IDataObject? clip = System.Windows.Clipboard.GetDataObject();
            if (clip?.GetDataPresent(FormatCache.CopyIdFormat) != true) return null;

            string? clipId = clip.GetData(FormatCache.CopyIdFormat) as string;
            if (string.IsNullOrEmpty(clipId)) return null;

            var package = FormatCache.Load();
            return package != null && package.Id == clipId && package.HasRichContent
                ? package
                : null;
        }
        catch
        {
            return null;
        }
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
        if (tabIndex == 2) _mainWindow.OpenTeleworkOverlay();
    }

        private void TeleworkShortcutPressed()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => ShowMainWindowToTab(2)));
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
