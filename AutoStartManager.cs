using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace SmartPaste
{
    public static class AutoStartManager
    {
        private const string AppName = "SmartPaste";
        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
        private static readonly string TargetExePath = Path.Combine(AppDataFolder, "SmartPaste.exe");

        public static void SetAutoStart(bool enable)
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            
            if (enable)
            {
                // Ensure the directory exists
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                // If we are not already running from AppData, copy there
                if (!string.Equals(currentExe, TargetExePath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // Note: If updating, the file might be locked if another instance is running from there.
                        // But usually, they run from download folder and copy here.
                        File.Copy(currentExe, TargetExePath, true);
                        
                        // We also need to copy the associated .dlls and files if it's not a single file app.
                        // However, since .NET 8 WPF apps usually have multiple files unless published as single file,
                        // we should copy all files in the current exe directory to TargetExePath directory.
                        string currentDir = Path.GetDirectoryName(currentExe);
                        if (currentDir != null)
                        {
                            foreach (var file in Directory.GetFiles(currentDir))
                            {
                                string destFile = Path.Combine(AppDataFolder, Path.GetFileName(file));
                                try { File.Copy(file, destFile, true); } catch { } // Ignore locked files
                            }
                            
                            // Also copy assets folder
                            string assetsSource = Path.Combine(currentDir, "assets");
                            string assetsDest = Path.Combine(AppDataFolder, "assets");
                            if (Directory.Exists(assetsSource))
                            {
                                if (!Directory.Exists(assetsDest)) Directory.CreateDirectory(assetsDest);
                                foreach (var file in Directory.GetFiles(assetsSource))
                                {
                                    string destFile = Path.Combine(assetsDest, Path.GetFileName(file));
                                    try { File.Copy(file, destFile, true); } catch { }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle failure (e.g. access denied)
                        Console.WriteLine("Could not copy files: " + ex.Message);
                    }
                }

                // Add to registry
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key?.SetValue(AppName, $"\"{TargetExePath}\"");
                }
            }
            else
            {
                // Remove from registry
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key?.DeleteValue(AppName, false);
                }
                
                // Note: we don't delete the executable because it might be currently running.
                // We just remove it from startup.
            }
        }
    }
}