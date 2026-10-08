using System;
using System.IO;

namespace PaswordChService
{
    /// <summary>
    /// Все пути относительно папки, где лежит EXE (C:\Program Files\PaswordCh).
    /// Единый каталог для программы, конфигов, логов и данных.
    /// </summary>
    public static class Paths
    {
        /// <summary>Папка, где лежит EXE.</summary>
        public static string InstallDir => AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

        /// <summary>Каталог данных = каталог программы.</summary>
        public static string DataDir => InstallDir;

        public static string LogsDir => Path.Combine(InstallDir, "Logs");
        public static string ArchiveDir => Path.Combine(InstallDir, "Archive");
        public static string ErrorsDir => Path.Combine(InstallDir, "ErrorUsers");
        public static string LastRunFile => Path.Combine(LogsDir, "lastrun.txt");

        public static string IniPath => Path.Combine(InstallDir, "my.ini");
        public static string CsvPath => Path.Combine(InstallDir, "emails.csv");

        public static string UserEmailTemplate => Path.Combine(InstallDir, "EmailUser.html");
        public static string AdminEmailTemplate => Path.Combine(InstallDir, "EmailAdmin.html");

        /// <summary>Создаёт все каталоги данных, если их нет.</summary>
        public static void EnsureDataDirs()
        {
            Directory.CreateDirectory(InstallDir);
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(ArchiveDir);
            Directory.CreateDirectory(ErrorsDir);
        }
    }
}