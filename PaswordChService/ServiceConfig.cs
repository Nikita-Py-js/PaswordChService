using System;
using System.IO;
using System.Linq;

namespace PaswordChService
{
    /// <summary>Конфигурация службы, загружаемая из my.ini.</summary>
    public class ServiceConfig
    {
        // ================================================================
        //  [Branding]
        // ================================================================
        public string CompanyBrandHtml { get; set; } = "<span>EFSOL</span>";
        public string CompanyName { get; set; } = "EFSOL";
        public string ClientCompanyName { get; set; } = "";
        public string CompanyTagline { get; set; } = "";
        public string CompanySite { get; set; } = "";
        public string CompanyEmail { get; set; } = "";
        public string CompanyPhone { get; set; } = "";

        public string VendorLine { get; set; } = "Решение компании ООО «Реумент»";
        public string VendorUrl { get; set; } = "https://reument.ru";

        public string EmailUserSubject { get; set; } = "Смена пароля";
        public string EmailAdminSubject { get; set; } = "PaswordCh: отчёт о смене паролей";

        // ================================================================
        //  [Service]
        // ================================================================
        public int IntervalDays { get; set; } = 30;
        public int CheckIntervalMinutes { get; set; } = 15;
        public int LogArchiveHours { get; set; } = 24;

        // ================================================================
        //  [Users]
        // ================================================================
        public string Mode { get; set; } = "Local";
        public string Domain { get; set; } = "";
        public string AdAdminUser { get; set; } = "";
        public string AdAdminPassword { get; set; } = "";
        public int PasswordLength { get; set; } = 12;

        // ================================================================
        //  [Paths] — опционально. Пустое значение = взять из Paths.cs
        // ================================================================
        public string CsvPath { get; set; } = "";
        public string ArchivePath { get; set; } = "";
        public string LogPath { get; set; } = "";
        public string ErrorUsersPath { get; set; } = "";

        // ================================================================
        //  [Archive]
        // ================================================================
        public string ZipPassword { get; set; } = "ChangeMe";

        // ================================================================
        //  [Mail]
        // ================================================================
        public string[] AdminEmails { get; set; } = new string[0];
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 25;
        public bool SmtpUseSsl { get; set; } = false;
        public string SmtpUser { get; set; } = "";
        public string SmtpPassword { get; set; } = "";
        public string SmtpFrom { get; set; } = "";
        public bool SendEmailsToUsers { get; set; } = false;
        public int SmtpTimeoutMs { get; set; } = 30000;

        // ================================================================
        //  [Access]
        // ================================================================
        public string[] Managers { get; set; } = new string[0];
        public string[] ProtectedUsers { get; set; } = new string[0];

        // ================================================================
        //  Роли
        // ================================================================

        public bool IsManager(string user)
        {
            if (string.IsNullOrWhiteSpace(user)) return false;
            var u = ShortName(user);
            return Managers.Any(m => string.Equals(ShortName(m), u, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsProtectedUser(string user)
        {
            if (string.IsNullOrWhiteSpace(user)) return false;
            var u = ShortName(user);
            return ProtectedUsers.Any(p => string.Equals(ShortName(p), u, StringComparison.OrdinalIgnoreCase));
        }

        public static string ShortName(string full)
        {
            if (string.IsNullOrEmpty(full)) return full;
            var i = full.IndexOf('\\');
            return i >= 0 ? full.Substring(i + 1) : full;
        }

        // ================================================================
        //  Загрузка
        // ================================================================

        public static ServiceConfig Load(string iniPath)
        {
            if (!File.Exists(iniPath))
                throw new FileNotFoundException("Файл конфигурации не найден: " + iniPath, iniPath);

            var ini = new INIManager(iniPath);

            var cfg = new ServiceConfig
            {
                // [Branding]
                CompanyBrandHtml = ini.ReadString("Branding", "CompanyBrandHtml",
                                                   "<span style=\"color:#ffffff;\">EFSOL</span>"),
                CompanyName = ini.ReadString("Branding", "CompanyName", "EFSOL"),
                ClientCompanyName = ini.ReadString("Branding", "ClientCompanyName", ""),
                CompanyTagline = ini.ReadString("Branding", "CompanyTagline", ""),
                CompanySite = ini.ReadString("Branding", "CompanySite", ""),
                CompanyEmail = ini.ReadString("Branding", "CompanyEmail", ""),
                CompanyPhone = ini.ReadString("Branding", "CompanyPhone", ""),

                VendorLine = ini.ReadString("Branding", "VendorLine",
                                                   "Решение компании ООО «Реумент»"),
                VendorUrl = ini.ReadString("Branding", "VendorUrl",
                                                   "https://reument.ru"),

                EmailUserSubject = ini.ReadString("Branding", "EmailUserSubject",
                                                   "Смена пароля"),
                EmailAdminSubject = ini.ReadString("Branding", "EmailAdminSubject",
                                                   "PaswordCh: отчёт о смене паролей"),

                // [Service]
                IntervalDays = ini.ReadInt("Service", "IntervalDays", 30),
                CheckIntervalMinutes = ini.ReadInt("Service", "CheckIntervalMinutes", 15),
                LogArchiveHours = ini.ReadInt("Service", "LogArchiveHours", 24),

                // [Users]
                Mode = ini.ReadString("Users", "Mode", "Local"),
                Domain = ini.ReadString("Users", "Domain", ""),
                AdAdminUser = ini.ReadString("Users", "AdAdminUser", ""),
                AdAdminPassword = DecryptSafe(ini.ReadString("Users", "AdAdminPassword", "")),
                PasswordLength = ini.ReadInt("Users", "PasswordLength", 12),

                // [Paths]
                CsvPath = ini.ReadString("Paths", "CsvPath", ""),
                ArchivePath = ini.ReadString("Paths", "ArchivePath", ""),
                LogPath = ini.ReadString("Paths", "LogPath", ""),
                ErrorUsersPath = ini.ReadString("Paths", "ErrorUsersPath", ""),

                // [Archive]
                ZipPassword = DecryptSafe(ini.ReadString("Archive", "ZipPassword", "ChangeMe")),

                // [Mail]
                AdminEmails = SplitList(ini.ReadString("Mail", "AdminEmails", "")),
                SmtpHost = ini.ReadString("Mail", "SmtpHost", ""),
                SmtpPort = ini.ReadInt("Mail", "SmtpPort", 25),
                SmtpUseSsl = ini.ReadBool("Mail", "SmtpUseSsl", false),
                SmtpUser = ini.ReadString("Mail", "SmtpUser", ""),
                SmtpPassword = DecryptSafe(ini.ReadString("Mail", "SmtpPassword", "")),
                SmtpFrom = ini.ReadString("Mail", "SmtpFrom", ""),
                SendEmailsToUsers = ini.ReadBool("Mail", "SendEmailsToUsers", false),
                SmtpTimeoutMs = ini.ReadInt("Mail", "SmtpTimeoutMs", 30000),

                // [Access]
                Managers = SplitList(ini.ReadString("Access", "Managers", "")),
                ProtectedUsers = SplitList(ini.ReadString("Access", "ProtectedUsers", "Administrator")),
            };

            // ============================================================
            //  Финальная подстановка путей: если пусто — берём из Paths.cs
            // ============================================================
            cfg.CsvPath = UseOr(cfg.CsvPath, Paths.CsvPath);
            cfg.ArchivePath = UseOr(cfg.ArchivePath, Paths.ArchiveDir);
            cfg.LogPath = UseOr(cfg.LogPath, Paths.LogsDir);
            cfg.ErrorUsersPath = UseOr(cfg.ErrorUsersPath, Paths.ErrorsDir);

            return cfg;
        }

        // ================================================================
        //  Шифрование секретов в my.ini
        // ================================================================

        public static readonly (string Section, string Key)[] SecretKeys = new[]
        {
            ("Users",   "AdAdminPassword"),
            ("Archive", "ZipPassword"),
            ("Mail",    "SmtpPassword"),
        };

        public static int SealSecrets(string iniPath)
        {
            if (!File.Exists(iniPath))
                throw new FileNotFoundException("Файл конфигурации не найден: " + iniPath, iniPath);

            var ini = new INIManager(iniPath);
            int count = 0;

            foreach (var (section, key) in SecretKeys)
            {
                var val = ini.ReadString(section, key, "");
                if (string.IsNullOrEmpty(val)) continue;
                if (CryptoHelper.IsEncrypted(val)) continue;

                var enc = CryptoHelper.Encrypt(val);
                ini.WriteString(section, key, enc);
                count++;
            }

            return count;
        }

        public static int UnsealSecrets(string iniPath)
        {
            if (!File.Exists(iniPath))
                throw new FileNotFoundException("Файл конфигурации не найден: " + iniPath, iniPath);

            var ini = new INIManager(iniPath);
            int count = 0;

            foreach (var (section, key) in SecretKeys)
            {
                var val = ini.ReadString(section, key, "");
                if (string.IsNullOrEmpty(val)) continue;
                if (!CryptoHelper.IsEncrypted(val)) continue;

                var plain = CryptoHelper.Decrypt(val);
                ini.WriteString(section, key, plain);
                count++;
            }

            return count;
        }

        // ================================================================
        //  Внутреннее
        // ================================================================

        /// <summary>Возвращает value, если оно непустое; иначе — fallback.</summary>
        private static string UseOr(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string DecryptSafe(string value)
        {
            if (!CryptoHelper.IsEncrypted(value)) return value;
            try
            {
                return CryptoHelper.Decrypt(value);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось расшифровать секрет из my.ini. Возможно, файл скопирован " +
                    "с другого компьютера. Заново зашифруйте пароль командой: " +
                    "PaswordChService.exe encrypt <пароль>", ex);
            }
        }

        private static string[] SplitList(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new string[0];
            return value
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
        }
    }
}