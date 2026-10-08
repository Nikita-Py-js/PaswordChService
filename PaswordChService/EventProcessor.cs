using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PaswordChService
{
    public class PasswordChangeResult
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string NewPassword { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class EventProcessor
    {
        private readonly ServiceConfig _cfg;
        private readonly PasswordManager _pwdMgr;
        private readonly EmailSender _mailer;
        private readonly string _csvPath;

        public EventProcessor(ServiceConfig cfg)
        {
            _cfg = cfg;
            _pwdMgr = new PasswordManager(cfg);
            _mailer = new EmailSender(cfg);
            _csvPath = cfg.CsvPath;
        }

        public List<PasswordChangeResult> RunOnce(IEnumerable<string> onlyUsernames = null)
        {
            var results = new List<PasswordChangeResult>();
            var sync = new object();

            var mode = onlyUsernames == null ? "все пользователи" : "выборочно";
            Logger.Info($"=== Начало цикла обработки ({mode}) ===");

            var csvUsers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(_csvPath))
            {
                foreach (var raw in File.ReadAllLines(_csvPath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    var parts = line.Split(new[] { ';', ',' }, StringSplitOptions.None);
                    if (parts.Length < 2) { Logger.Warn("Пропущена строка: " + raw); continue; }

                    var email = parts[0].Trim();
                    var user = parts[1].Trim();
                    if (user.Length > 0) csvUsers[user] = email;
                }
            }
            else
            {
                Logger.Warn("Файл emails.csv не найден: " + _csvPath);
            }

            var targets = new List<Tuple<string, string>>();

            if (onlyUsernames == null)
            {
                foreach (var kv in csvUsers)
                {
                    if (_cfg.IsProtectedUser(kv.Key))
                    {
                        Logger.Warn($"Пропущен защищённый логин (автоцикл): {kv.Key}");
                        continue;
                    }
                    targets.Add(Tuple.Create(kv.Value, kv.Key));
                }
            }
            else
            {
                foreach (var u in onlyUsernames)
                {
                    if (string.IsNullOrWhiteSpace(u)) continue;
                    var user = u.Trim();

                    if (_cfg.IsProtectedUser(user))
                    {
                        lock (sync)
                            results.Add(new PasswordChangeResult
                            {
                                Username = user,
                                Email = "",
                                Success = false,
                                Error = "Защищённый логин — смена пароля запрещена"
                            });
                        Logger.Warn($"Отказ (защищённый логин): {user}");
                        continue;
                    }

                    if (csvUsers.TryGetValue(user, out var email))
                        targets.Add(Tuple.Create(email, user));
                    else
                    {
                        Logger.Warn("Пользователь '" + user + "' не найден в emails.csv — обрабатываю без email.");
                        targets.Add(Tuple.Create("", user));
                    }
                }
            }

            if (targets.Count == 0)
            {
                Logger.Warn("Список пользователей пуст — нечего обрабатывать.");
                return results;
            }

            var tasks = new List<Task>();
            foreach (var t in targets)
            {
                var email = t.Item1;
                var username = t.Item2;

                tasks.Add(Task.Run(() =>
                {
                    var res = new PasswordChangeResult { Username = username, Email = email };
                    try
                    {
                        var newPassword = GeneratePassword(_cfg.PasswordLength);
                        res.NewPassword = newPassword;

                        if (_pwdMgr.ChangePassword(username, newPassword, out var err))
                        {
                            res.Success = true;
                            Logger.Info($"Пароль изменён: {username}");

                            if (_cfg.SendEmailsToUsers && !string.IsNullOrEmpty(email))
                            {
                                try
                                {
                                    // HTML-письмо пользователю
                                    _mailer.SendUserPassword(email, username, newPassword);
                                    Thread.Sleep(1500);
                                }
                                catch (Exception ex) { Logger.Error("Ошибка уведомления " + email, ex); }
                            }
                        }
                        else
                        {
                            res.Success = false;
                            res.Error = err;
                            Logger.Warn($"Не удалось сменить пароль для {username}: {err}");
                        }
                    }
                    catch (Exception ex)
                    {
                        res.Success = false;
                        res.Error = ex.Message;
                        Logger.Error("Ошибка обработки " + username, ex);
                    }

                    lock (sync) results.Add(res);
                }));
            }

            try { Task.WaitAll(tasks.ToArray(), TimeSpan.FromMinutes(30)); }
            catch (Exception ex) { Logger.Error("Ожидание задач", ex); }

            var successes = results.Where(r => r.Success).ToList();
            var errors = results.Where(r => !r.Success).ToList();

            if (successes.Count > 0)
            {
                Directory.CreateDirectory(_cfg.ArchivePath);
                var resultFile = Path.Combine(_cfg.ArchivePath,
                    "newpasswords_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");

                File.WriteAllLines(resultFile,
                    successes.Select(r => $"{r.Username};{r.Email};{r.NewPassword}"),
                    Encoding.UTF8);
                Logger.Info("Новые пароли сохранены: " + resultFile);

                try
                {
                    var zip = ZipArchiver.CreateEncryptedZip(resultFile, _cfg.ArchivePath, _cfg.ZipPassword);
                    Logger.Info("Шифрованный ZIP создан: " + zip);
                }
                catch (Exception ex) { Logger.Error("Ошибка архивации", ex); }
            }

            if (errors.Count > 0)
            {
                Directory.CreateDirectory(_cfg.ErrorUsersPath);
                var errFile = Path.Combine(_cfg.ErrorUsersPath,
                    "errors_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                File.WriteAllLines(errFile,
                    errors.Select(r => $"{r.Username};{r.Email};{r.Error}"),
                    Encoding.UTF8);
                Logger.Warn("Ошибки сохранены: " + errFile);
            }

            // HTML-отчёт админам — только при полном цикле.
            if (onlyUsernames == null)
            {
                try
                {
                    var nextRun = DateTime.Now.AddDays(_cfg.IntervalDays);
                    _mailer.SendAdminReport(successes, errors, nextRun);
                }
                catch (Exception ex) { Logger.Error("Ошибка отправки HTML-отчёта админам", ex); }
            }

            Logger.Info($"=== Цикл завершён. Успешно: {successes.Count}, ошибок: {errors.Count} ===");
            return results.OrderBy(r => r.Username).ToList();
        }

        private static string GeneratePassword(int length)
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnpqrstuvwxyz";
            const string digits = "23456789";
            const string special = "!@#$%^&*";
            const string all = upper + lower + digits + special;

            if (length < 8) length = 8;

            var bytes = new byte[length + 8];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(bytes);

            var sb = new StringBuilder();
            sb.Append(upper[bytes[0] % upper.Length]);
            sb.Append(lower[bytes[1] % lower.Length]);
            sb.Append(digits[bytes[2] % digits.Length]);
            sb.Append(special[bytes[3] % special.Length]);
            for (int i = 4; i < length; i++) sb.Append(all[bytes[i] % all.Length]);

            var chars = sb.ToString().ToCharArray();
            for (int i = chars.Length - 1; i > 0; i--)
            {
                int j = bytes[length + (i % 8)] % (i + 1);
                var t = chars[i]; chars[i] = chars[j]; chars[j] = t;
            }
            return new string(chars);
        }
    }
}