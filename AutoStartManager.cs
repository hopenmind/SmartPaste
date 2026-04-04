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
            string? currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            
            if (enable)
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                if (!string.Equals(currentExe, TargetExePath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (currentExe != null) File.Copy(currentExe, TargetExePath, true);
                        
                        string? currentDir = Path.GetDirectoryName(currentExe);
                        if (currentDir != null)
                        {
                            foreach (var file in Directory.GetFiles(currentDir))
                            {
                                string destFile = Path.Combine(AppDataFolder, Path.GetFileName(file));
                                try { File.Copy(file, destFile, true); } catch { }
                            }
                            
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
                        Console.WriteLine("Could not copy files: " + ex.Message);
                    }
                }

                // Add to registry
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key?.SetValue(AppName, $"\"{TargetExePath}\"");
                }
            }
            else
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key?.DeleteValue(AppName, false);
                }
                
                // Note: we don't delete the executable because it might be currently running.
                // We just remove it from startup.
            }
        }
    }
}