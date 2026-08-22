using System;
using System.IO;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace VoiceDuck
{
    internal static class SettingsStore
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoiceDuck");
        private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

        public static string SettingsPath { get { return FilePath; } }

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return AppSettings.CreateDefault();
                using (FileStream stream = File.OpenRead(FilePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    var settings = serializer.ReadObject(stream) as AppSettings;
                    if (settings == null) return AppSettings.CreateDefault();
                    settings.Normalize();
                    return settings;
                }
            }
            catch
            {
                return AppSettings.CreateDefault();
            }
        }

        public static void Save(AppSettings settings)
        {
            if (settings == null) return;
            settings.Normalize();
            Directory.CreateDirectory(FolderPath);
            string temporaryPath = FilePath + ".tmp";
            using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(stream, settings);
                stream.Flush(true);
            }

            if (File.Exists(FilePath))
            {
                string backupPath = FilePath + ".bak";
                try { File.Replace(temporaryPath, FilePath, backupPath, true); }
                catch
                {
                    File.Copy(temporaryPath, FilePath, true);
                    File.Delete(temporaryPath);
                }
            }
            else
            {
                File.Move(temporaryPath, FilePath);
            }
        }
    }

    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "VoiceDuck";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    string value = key == null ? null : key.GetValue(ValueName) as string;
                    return !String.IsNullOrEmpty(value);
                }
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled)
                {
                    string executable = System.Windows.Forms.Application.ExecutablePath;
                    key.SetValue(ValueName, "\"" + executable + "\" --tray", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
