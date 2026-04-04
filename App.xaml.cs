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

    private void ShowMainWindow()
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        notifyIcon?.Dispose();
        pasteManager?.Dispose();
        caseConverterManager?.Dispose();
        alwaysOnTopManager?.Dispose();
        smartCopyManager?.Dispose();
        base.OnExit(e);
    }
}
