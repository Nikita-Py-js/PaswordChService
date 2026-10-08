using System;
using System.Diagnostics;
using System.IO;

namespace PaswordChService
{
    /// <summary>Логирование: текстовый файл + журнал событий Windows.</summary>
    public static class Logger
    {
        private static readonly object _lock = new object();
        private const string EventSource = "PaswordChService";
        private const string EventLogName = "Application";

        private static string _logDir = @"C:\PaswordCh\Logs";
        private static string _logFile;

        /// <summary>Если true — сообщения также выводятся в Console.Out.</summary>
        public static bool ConsoleEcho { get; set; } = false;

        public static void Initialize(string logDir)
        {
            _logDir = logDir;
            Directory.CreateDirectory(_logDir);
            _logFile = Path.Combine(_logDir, "service.log");
            EnsureEventSource();
        }

        private static void EnsureEventSource()
        {
            try
            {
                if (!EventLog.SourceExists(EventSource))
                    EventLog.CreateEventSource(EventSource, EventLogName);
            }
            catch { /* нет прав — пишем только в файл */ }
        }

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg) => Write("ERROR", msg);
        public static void Error(string msg, Exception ex) => Write("ERROR", msg + " :: " + ex);

        private static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(_logDir);
                    File.AppendAllText(_logFile, line + Environment.NewLine);
                }
                catch { }
            }

            if (ConsoleEcho)
            {
                try { Console.WriteLine(line); } catch { }
            }

            try
            {
                var type = level == "ERROR" ? EventLogEntryType.Error
                         : level == "WARN" ? EventLogEntryType.Warning
                         : EventLogEntryType.Information;
                EventLog.WriteEntry(EventSource, message, type);
            }
            catch { }
        }

        public static void ArchiveOldLogs(string logDir, int olderThanHours = 24)
        {
            try
            {
                if (!Directory.Exists(logDir)) return;
                foreach (var f in Directory.GetFiles(logDir, "*.log"))
                {
                    if (File.GetLastWriteTime(f) < DateTime.Now.AddHours(-olderThanHours))
                    {
                        var newName = Path.Combine(logDir,
                            Path.GetFileNameWithoutExtension(f) + "_" +
                            DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log.bak");
                        File.Move(f, newName);
                    }
                }
                Info("Ротация логов выполнена.");
            }
            catch (Exception ex) { Error("Ошибка ротации логов", ex); }
        }
    }
}