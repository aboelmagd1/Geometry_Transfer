using System;
using System.IO;

namespace GeometryTransferTool.Helpers
{
    /// <summary>
    /// File logger writing to %LOCALAPPDATA%\GeometryTransferTool\logs\ (rolling daily file).
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new();
        private static readonly string _logDirectory;

        static Logger()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _logDirectory = Path.Combine(localAppData, "GeometryTransferTool", "logs");
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
            }
            catch
            {
                _logDirectory = Path.GetTempPath();
            }
        }

        public static void Info(string message) => Log("INFO", message);
        public static void Warn(string message) => Log("WARN", message);
        public static void Error(string message, Exception? ex = null)
        {
            string fullMessage = ex != null
                ? $"{message} | Exception: {ex.GetType().Name} - {ex.Message}\nStackTrace: {ex.StackTrace}"
                : message;
            Log("ERROR", fullMessage);
        }

        private static void Log(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                    string logFile = Path.Combine(_logDirectory, $"GeometryTransfer_{dateStr}.log");
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string line = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(logFile, line);
                }
            }
            catch
            {
                // Silently ignore logging failures in production
            }
        }
    }
}
