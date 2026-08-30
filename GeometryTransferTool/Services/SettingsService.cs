using System;
using System.IO;
using System.Text.Json;
using GeometryTransferTool.Helpers;
using GeometryTransferTool.Models;

namespace GeometryTransferTool.Services
{
    /// <summary>
    /// Persists tool settings between sessions in %LOCALAPPDATA%\GeometryTransferTool\settings.json.
    /// </summary>
    public static class SettingsService
    {
        private static readonly string _settingsFilePath;

        static SettingsService()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(localAppData, "GeometryTransferTool");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                _settingsFilePath = Path.Combine(dir, "settings.json");
            }
            catch
            {
                _settingsFilePath = Path.Combine(Path.GetTempPath(), "GeometryTransferTool_settings.json");
            }
        }

        public static TransferSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<TransferSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load settings from {_settingsFilePath}: {ex.Message}");
            }

            return new TransferSettings();
        }

        public static void SaveSettings(TransferSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to save settings to {_settingsFilePath}: {ex.Message}");
            }
        }
    }
}
