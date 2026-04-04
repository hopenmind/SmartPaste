using System.Configuration;
using System.Data;
using System.Windows;

namespace SmartPaste;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon notifyIcon;
    public SmartPasteManager pasteManager { get; private set; }
    public CaseConverterManager caseConverterManager { get; private set; }
    public AlwaysOnTopManager alwaysOnTopManager { get; private set; }
    public SmartCopyManager smartCopyManager { get; private set; }
    public AppSettings Settings { get; private set; }

    private MainWindow _mainWindow;

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

        pasteManager = new SmartPasteManager();
        if (Settings != null)
        {
            pasteManager.DelayMilliseconds = Settings.DelayMilliseconds;
            pasteManager.HumanSimulation = Settings.HumanSimulation;
            pasteManager.HumanTypos = Settings.HumanTypos;
        }

        caseConverterManager = new CaseConverterManager();
        alwaysOnTopManager = new AlwaysOnTopManager();
        smartCopyManager = new SmartCopyManager();

        _mainWindow = new MainWindow();

        if (Settings == null || !Settings.StartMinimized)
        {
            ShowMainWindow();
        }
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

