using System;
using System.IO;
using System.Text.Json;

namespace SmartPaste
{
    public class AppSettings
    {
        public bool StartMinimized { get; set; } = false;
        public bool AutoStart { get; set; } = false;
        public int DelayMilliseconds { get; set; } = 30;
        public bool HumanSimulation { get; set; } = false;
        public bool HumanTypos { get; set; } = false;
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