using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;

namespace PaswordChService
{
    public static class Host
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        public static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "install": InstallService(); return;
                    case "uninstall": UninstallService(); return;
                    case "start": StartService(); return;
                    case "stop": StopService(); return;
                    case "console": RunAsConsole(); return;
                    case "run": RunManual(args.Skip(1)); return;
                    case "runall": RunManual(Enumerable.Empty<string>()); return;
                    case "change": ChangeViaPipe(args.Length > 1 ? args[1] : null); return;
                    case "ping": PingService(); return;
                    case "whoami": WhoAmI(); return;
                    case "nextrun": NextRunInfo(); return;
                    case "testmail": TestMail(args.Length > 1 ? args[1] : null); return;
                    case "seal": SealIni(); return;
                    case "unseal": UnsealIni(); return;
                    case "encrypt": EncryptValue(args.Length > 1 ? args[1] : null); return;
                    case "decrypt": DecryptValue(args.Length > 1 ? args[1] : null); return;
                    default:
                        TrySetConsoleEncoding();
                        PrintHelp();
                        return;
                }
            }

            // Без аргументов: если консоли нет — нас запустил SCM.
            if (GetConsoleWindow() == IntPtr.Zero)
            {
                ServiceBase.Run(new PaswordChService());
                return;
            }

            TrySetConsoleEncoding();
            PrintHelp();
        }

        private static void TrySetConsoleEncoding()
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch { }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("PaswordCh Service Host");
            Console.WriteLine("Использование: PaswordChService.exe <команда> [аргументы]");
            Console.WriteLine();
            Console.WriteLine("  whoami         - показать текущего пользователя и его роль");
            Console.WriteLine("  change [user]  - сменить свой пароль (или пароль user — если вы менеджер)");
            Console.WriteLine("                   без прав администратора — через службу");
            Console.WriteLine("  ping           - проверить связь со службой");
            Console.WriteLine("  nextrun        - показать, когда будет следующая смена паролей");
            Console.WriteLine("  testmail [to]  - отправить тестовое письмо через SMTP");
            Console.WriteLine();
            Console.WriteLine("  run [users...] - массовая смена (требует роль МЕНЕДЖЕР или админ)");
            Console.WriteLine("  runall         - сменить пароли всем из emails.csv");
            Console.WriteLine();
            Console.WriteLine("  ---- работа с секретами my.ini ----");
            Console.WriteLine("  seal             - зашифровать открытые пароли в my.ini");
            Console.WriteLine("  unseal           - расшифровать ENC: обратно");
            Console.WriteLine("  encrypt <text>   - получить ENC: строку для my.ini");
            Console.WriteLine("  decrypt <ENC:..> - расшифровать одно значение");
            Console.WriteLine();
            Console.WriteLine("  ---- только для администраторов Windows ----");
            Console.WriteLine("  install        - установить службу");
            Console.WriteLine("  uninstall      - удалить службу");
            Console.WriteLine("  start / stop   - запустить/остановить службу");
            Console.WriteLine("  console        - отладка службы в консольном режиме");
        }

        // ================================================================
        //  Проверка прав администратора
        // ================================================================

        private static bool IsCurrentUserAdmin()
        {
            try
            {
                var wp = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                return wp.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static bool RequireAdmin(string action)
        {
            if (IsCurrentUserAdmin()) return true;

            TrySetConsoleEncoding();
            Console.WriteLine();
            Console.WriteLine("ОШИБКА: команда '" + action + "' требует прав администратора Windows.");
            Console.WriteLine("Запустите PaswordChService.exe от имени администратора");
            Console.WriteLine("(правый клик → «Запуск от имени администратора»).");
            Console.WriteLine();
            Environment.ExitCode = 5;
            return false;
        }

        // ================================================================
        //  Service control (только для админа)
        // ================================================================

        private static void InstallService()
        {
            if (!RequireAdmin("install")) return;

            try
            {
                var exePath = Assembly.GetExecutingAssembly().Location;
                ManagedInstallerClass.InstallHelper(new[] { exePath });
                Console.WriteLine("Служба успешно установлена.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка установки: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void UninstallService()
        {
            if (!RequireAdmin("uninstall")) return;

            try
            {
                var exePath = Assembly.GetExecutingAssembly().Location;
                ManagedInstallerClass.InstallHelper(new[] { "/u", exePath });
                Console.WriteLine("Служба успешно удалена.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка удаления: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void StartService()
        {
            if (!RequireAdmin("start")) return;

            try
            {
                using (var sc = new ServiceController(PaswordChService.ServiceNameConst))
                {
                    if (sc.Status != ServiceControllerStatus.Running)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    }
                    Console.WriteLine("Служба запущена.");
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("Не удалось запустить службу: " + ex.Message);
                Console.WriteLine("Убедитесь, что служба PaswordChService установлена.");
                Environment.ExitCode = 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка запуска службы: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void StopService()
        {
            if (!RequireAdmin("stop")) return;

            try
            {
                using (var sc = new ServiceController(PaswordChService.ServiceNameConst))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
                    Console.WriteLine("Служба остановлена.");
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("Не удалось остановить службу: " + ex.Message);
                Console.WriteLine("Убедитесь, что служба PaswordChService установлена.");
                Environment.ExitCode = 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка остановки службы: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void RunAsConsole()
        {
            if (!RequireAdmin("console")) return;

            TrySetConsoleEncoding();
            Logger.ConsoleEcho = true;

            var service = new PaswordChService();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;

            typeof(ServiceBase).GetMethod("OnStart", flags)
                .Invoke(service, new object[] { new string[0] });

            Console.WriteLine("Служба работает в консольном режиме. Нажмите Enter для остановки...");
            Console.ReadLine();

            typeof(ServiceBase).GetMethod("OnStop", flags).Invoke(service, null);
        }

        // ================================================================
        //  Работа с секретами my.ini
        // ================================================================

        private static void SealIni()
        {
            TrySetConsoleEncoding();
            try
            {
                Paths.EnsureDataDirs();
                var count = ServiceConfig.SealSecrets(Paths.IniPath);

                if (count == 0)
                    Console.WriteLine("В my.ini нет открытых секретов — всё уже зашифровано.");
                else
                    Console.WriteLine($"Зашифровано полей в my.ini: {count}. Файл: {Paths.IniPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void UnsealIni()
        {
            TrySetConsoleEncoding();

            Console.WriteLine("ВНИМАНИЕ: секреты в my.ini будут расшифрованы и сохранены открытым текстом.");
            Console.Write("Продолжить? (y/N): ");
            var answer = Console.ReadLine();
            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Отменено.");
                return;
            }

            try
            {
                var count = ServiceConfig.UnsealSecrets(Paths.IniPath);
                if (count == 0)
                    Console.WriteLine("В my.ini нет зашифрованных секретов.");
                else
                    Console.WriteLine($"Расшифровано полей в my.ini: {count}. " +
                                      "Не забудьте зашифровать обратно: PaswordChService.exe seal");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void EncryptValue(string plain)
        {
            TrySetConsoleEncoding();

            if (plain == null)
            {
                Console.WriteLine("Использование: PaswordChService.exe encrypt <значение>");
                Console.WriteLine("Возвращает строку ENC:..., которую можно вставить в my.ini.");
                return;
            }

            try { Console.WriteLine(CryptoHelper.Encrypt(plain)); }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void DecryptValue(string value)
        {
            TrySetConsoleEncoding();

            if (string.IsNullOrEmpty(value))
            {
                Console.WriteLine("Использование: PaswordChService.exe decrypt <ENC:...>");
                return;
            }

            try { Console.WriteLine(CryptoHelper.Decrypt(value)); }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка расшифровки: " + ex.Message);
                Console.WriteLine("Возможно, значение зашифровано на другом компьютере.");
                Environment.ExitCode = 1;
            }
        }

        // ================================================================
        //  run / runall
        // ================================================================

        private static void RunManual(IEnumerable<string> rawArgs)
        {
            TrySetConsoleEncoding();

            var filter = rawArgs
                .SelectMany(a => (a ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            // Локальный админ — делаем напрямую.
            // Иначе — через службу (named pipe), там проверится роль менеджера.
            if (IsCurrentUserAdmin())
                RunManualLocal(filter);
            else
                RunManualViaPipe(filter);
        }

        private static void RunManualLocal(List<string> filter)
        {
            Logger.ConsoleEcho = true;
            Paths.EnsureDataDirs();
            Logger.Initialize(Paths.LogsDir);

            ServiceConfig cfg;
            try { cfg = ServiceConfig.Load(Paths.IniPath); }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка конфигурации: " + ex.Message);
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine(filter.Count == 0
                ? "Режим: смена паролей ВСЕМ пользователям из emails.csv (локально, под админом)"
                : "Режим: смена паролей для " + string.Join(", ", filter) + " (локально, под админом)");

            List<PasswordChangeResult> results;
            try
            {
                var processor = new EventProcessor(cfg);
                results = processor.RunOnce(filter.Count == 0 ? null : (IEnumerable<string>)filter);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Критическая ошибка выполнения: " + ex.Message);
                Environment.ExitCode = 1;
                return;
            }

            PrintResults(results, cfg.ArchivePath);
        }

        private static void RunManualViaPipe(List<string> filter)
        {
            var me = WindowsIdentity.GetCurrent().Name;
            var payload = filter.Count == 0 ? "" : string.Join(";", filter);

            Console.WriteLine($"Пользователь: {me}");
            Console.WriteLine(filter.Count == 0
                ? "Запрос: сменить пароли ВСЕМ из emails.csv (через службу)"
                : "Запрос: сменить пароли для " + string.Join(", ", filter) + " (через службу)");
            Console.WriteLine();

            var results = new List<PasswordChangeResult>();

            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeServer.PipeName, PipeDirection.InOut))
                {
                    pipe.Connect(5000);

                    using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                    using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true))
                    {
                        writer.WriteLine("ID|" + me);
                        writer.WriteLine("RUN|" + payload);

                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line == "DONE") break;
                            if (string.IsNullOrEmpty(line)) continue;

                            var idx = line.IndexOf('|');
                            if (idx < 0) continue;

                            var tag = line.Substring(0, idx);
                            var rest = line.Substring(idx + 1);

                            // Глобальная ошибка — печатаем и выходим.
                            if (tag == "FATAL")
                            {
                                Console.WriteLine();
                                Console.WriteLine("ОШИБКА: " + rest);
                                Environment.ExitCode = 4;
                                return;
                            }

                            var p = rest.Split(new[] { '|' }, 3);

                            var r = new PasswordChangeResult
                            {
                                Username = p.Length > 0 ? p[0] : "",
                                Email = p.Length > 1 ? p[1] : "",
                            };

                            if (tag == "OK")
                            {
                                r.Success = true;
                                r.NewPassword = p.Length > 2 ? p[2] : "";
                            }
                            else
                            {
                                r.Success = false;
                                r.Error = p.Length > 2 ? p[2] : "неизвестная ошибка";
                            }

                            results.Add(r);
                        }
                    }
                }
            }
            catch (System.TimeoutException)
            {
                Console.WriteLine("Служба PaswordChService не отвечает. Убедитесь, что она установлена и запущена.");
                Environment.ExitCode = 2;
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка связи со службой: " + ex.Message);
                Environment.ExitCode = 2;
                return;
            }

            string archivePath = "";
            try { archivePath = ServiceConfig.Load(Paths.IniPath).ArchivePath; } catch { }

            PrintResults(results, archivePath);
        }

        private static void PrintResults(List<PasswordChangeResult> results, string archivePath)
        {
            Console.WriteLine();
            Console.WriteLine("================ РЕЗУЛЬТАТЫ ================");
            foreach (var r in results)
            {
                if (r.Success)
                    Console.WriteLine($"[OK]     {r.Username,-20} email={r.Email,-25} пароль: {r.NewPassword}");
                else
                    Console.WriteLine($"[ОШИБКА] {r.Username,-20} email={r.Email,-25} {r.Error}");
            }
            Console.WriteLine("============================================");
            Console.WriteLine($"Успешно: {results.Count(r => r.Success)}   Ошибок: {results.Count(r => !r.Success)}");
            if (!string.IsNullOrEmpty(archivePath))
                Console.WriteLine("Новые пароли также сохранены в зашифрованный ZIP в: " + archivePath);
        }

        // ================================================================
        //  Клиентские команды через pipe
        // ================================================================

        private static void ChangeViaPipe(string targetUser)
        {
            TrySetConsoleEncoding();

            var me = WindowsIdentity.GetCurrent().Name;
            var meShort = ServiceConfig.ShortName(me);

            if (string.IsNullOrEmpty(targetUser))
                targetUser = meShort;

            Console.WriteLine($"Пользователь: {me}");
            Console.WriteLine($"Запрос на смену пароля для: {targetUser}");
            Console.Write("Выполняется... ");

            string reply;
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeServer.PipeName, PipeDirection.InOut))
                {
                    pipe.Connect(5000);

                    using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                    using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true))
                    {
                        writer.WriteLine("ID|" + me);
                        writer.WriteLine("CHANGE|" + targetUser);
                        reply = reader.ReadLine();
                    }
                }
            }
            catch (System.TimeoutException)
            {
                Console.WriteLine("не удалось.");
                Console.WriteLine("Служба PaswordChService не отвечает. Убедитесь, что она установлена и запущена.");
                Environment.ExitCode = 2;
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine("не удалось.");
                Console.WriteLine("Ошибка связи со службой: " + ex.Message);
                Environment.ExitCode = 2;
                return;
            }

            Console.WriteLine("готово.");
            Console.WriteLine();

            if (reply == null) { Console.WriteLine("Пустой ответ от службы."); Environment.ExitCode = 3; return; }

            var idx = reply.IndexOf('|');
            var status = idx >= 0 ? reply.Substring(0, idx) : reply;
            var payload = idx >= 0 ? reply.Substring(idx + 1) : "";

            if (status == "OK")
            {
                Console.WriteLine("========================================");
                Console.WriteLine("  Новый пароль: " + payload);
                Console.WriteLine("========================================");
                Console.WriteLine("Смените его при первом входе, если требуется.");
            }
            else
            {
                Console.WriteLine("ОШИБКА: " + payload);
                Environment.ExitCode = 4;
            }
        }

        private static void PingService()
        {
            TrySetConsoleEncoding();

            var me = WindowsIdentity.GetCurrent().Name;

            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeServer.PipeName, PipeDirection.InOut))
                {
                    pipe.Connect(3000);

                    using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                    using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true))
                    {
                        writer.WriteLine("ID|" + me);
                        writer.WriteLine("PING");
                        var reply = reader.ReadLine();
                        Console.WriteLine("Ответ службы: " + reply);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Служба недоступна: " + ex.Message);
                Environment.ExitCode = 2;
            }
        }

        private static void WhoAmI()
        {
            TrySetConsoleEncoding();

            var me = WindowsIdentity.GetCurrent().Name;
            var meShort = ServiceConfig.ShortName(me);

            Console.WriteLine("Текущий пользователь: " + me);

            try
            {
                var cfg = ServiceConfig.Load(Paths.IniPath);

                bool isManager = cfg.IsManager(meShort);
                bool isProtected = cfg.IsProtectedUser(meShort);

                if (isManager && isProtected)
                {
                    Console.WriteLine("Роль: МЕНЕДЖЕР — может менять пароли других пользователей.");
                    Console.WriteLine("      Свой пароль защищён политикой безопасности и не может быть изменён.");
                }
                else if (isManager)
                {
                    Console.WriteLine("Роль: МЕНЕДЖЕР — может менять пароли любых незащищённых логинов.");
                }
                else if (isProtected)
                {
                    Console.WriteLine("Роль: ЗАЩИЩЁННЫЙ — смена пароля запрещена политикой безопасности.");
                }
                else
                {
                    Console.WriteLine("Роль: ОБЫЧНЫЙ — может менять только свой пароль.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Не удалось прочитать my.ini: " + ex.Message);
            }
        }

        private static void NextRunInfo()
        {
            TrySetConsoleEncoding();

            try
            {
                var cfg = ServiceConfig.Load(Paths.IniPath);
                var path = Paths.LastRunFile;

                if (!File.Exists(path))
                {
                    Console.WriteLine("Файл lastrun.txt отсутствует — цикл будет выполнен при первой проверке.");
                    return;
                }

                if (!DateTime.TryParse(File.ReadAllText(path).Trim(), out var last))
                {
                    Console.WriteLine("Не удалось разобрать дату в lastrun.txt.");
                    return;
                }

                var next = last.AddDays(cfg.IntervalDays);
                var left = next - DateTime.Now;

                Console.WriteLine("Последняя смена:      " + last.ToString("yyyy-MM-dd HH:mm"));
                Console.WriteLine("Интервал (дней):      " + cfg.IntervalDays);
                Console.WriteLine("Следующая смена:      " + next.ToString("yyyy-MM-dd HH:mm"));
                Console.WriteLine(left.TotalSeconds > 0
                    ? $"Осталось:             {left.Days} дн. {left.Hours} ч. {left.Minutes} мин."
                    : "Срок уже наступил — цикл выполнится при следующей проверке службой.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка: " + ex.Message);
            }
        }

        private static void TestMail(string to)
        {
            TrySetConsoleEncoding();

            ServiceConfig cfg;
            try { cfg = ServiceConfig.Load(Paths.IniPath); }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка конфигурации: " + ex.Message);
                return;
            }

            if (string.IsNullOrEmpty(to))
                to = cfg.AdminEmails.FirstOrDefault() ?? "";

            if (string.IsNullOrEmpty(to))
            {
                Console.WriteLine("Не указан получатель и AdminEmails пуст.");
                return;
            }

            Console.WriteLine($"SMTP: {cfg.SmtpHost}:{cfg.SmtpPort}, SSL={cfg.SmtpUseSsl}, user={cfg.SmtpUser}");
            Console.WriteLine($"Отправка тестового письма на {to}...");

            try
            {
                var m = new EmailSender(cfg);
                m.Send(to, "PaswordCh test", "Это тестовое письмо от PaswordChService.");
                Console.WriteLine("OK. Проверьте почту (и папку «Спам»).");
            }
            catch (Exception ex)
            {
                Console.WriteLine("ОШИБКА: " + ex.Message);
                if (ex.InnerException != null)
                    Console.WriteLine("Внутренняя: " + ex.InnerException.Message);
            }
        }
    }
}