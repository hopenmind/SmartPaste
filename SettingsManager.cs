using System;
using System.IO;
using System.Text.Json;

namespace SmartPaste
{
    public class AppSettings
    {
        // Startup
        public bool StartMinimized { get; set; } = false;
        public bool AutoStart { get; set; } = false;

        // Function Toggles
        public bool EnableSmartPaste { get; set; } = true;
        public bool EnableSmartCopy { get; set; } = true;
        public bool EnableCaseConverter { get; set; } = true;
        public bool EnableAlwaysOnTop { get; set; } = true;

        // Typing
        public int DelayMilliseconds { get; set; } = 30;

        // Custom Shortcuts (format: "Modifier1+Modifier2+Key", e.g. "Ctrl+Shift+V")
        public string SmartPasteShortcut1 { get; set; } = "Ctrl+Shift+V";
        public string SmartPasteShortcut2 { get; set; } = "Ctrl+Alt+V";
        public string SmartPasteShortcut3 { get; set; } = "Ctrl+Win+V";
        public string SmartCopyShortcut { get; set; } = "Ctrl+Shift+C";
        public string CaseConverterShortcut { get; set; } = "Ctrl+Win+C";
        public string AlwaysOnTopShortcut { get; set; } = "Ctrl+Alt+T";
        public string TeleworkShortcut { get; set; } = "Ctrl+Shift+T";

        // Telework: Core
        public bool TeleVariableRhythm { get; set; } = true;
        public bool TeleMicroPauses { get; set; } = true;
        public bool TeleFlowBursts { get; set; } = true;
        public bool TeleRealisticTypos { get; set; } = false;

        // Telework: Advanced
        public bool TeleRandomCapsErrors { get; set; } = false;
        public bool TeleDoubleKeyStrokes { get; set; } = false;
        public bool TeleCursorNavigation { get; set; } = false;
        public bool TeleAutoCorrectMistakes { get; set; } = false;
        public bool TeleBreathingPauses { get; set; } = true;
        public bool TeleEndOfLinePause { get; set; } = true;

        // Telework: Timing
        public int TelePasteDelay { get; set; } = 100;
        public int TeleWordChunkSize { get; set; } = 5;
        public int TeleBreathingInterval { get; set; } = 15;
    }

    public static class SettingsManager
    {
        private static readonly string SettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartPaste");
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        public static AppSettings Load()
        {
            if (File.Exists(SettingsFile))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFile);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch
                {
                    return new AppSettings();
                }
            }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }
}
