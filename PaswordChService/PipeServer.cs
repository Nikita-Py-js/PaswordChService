using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace PaswordChService
{
    /// <summary>
    /// Сервер именованного канала. Работает под LocalSystem и выполняет
    /// запросы на смену пароля и массовую смену (run) от клиентов.
    /// </summary>
    public class PipeServer
    {
        public const string PipeName = "PaswordChService";
        private const int MaxInstances = 4;

        private readonly ServiceConfig _cfg;
        private Thread _thread;
        private volatile bool _stop;

        public PipeServer(ServiceConfig cfg) { _cfg = cfg; }

        public void Start()
        {
            _stop = false;
            _thread = new Thread(Loop) { IsBackground = true, Name = "PaswordChPipe" };
            _thread.Start();
            Logger.Info(@"Pipe-сервер запущен: \\.\pipe\" + PipeName);
        }

        public void Stop()
        {
            _stop = true;
            // Разбудить Accept, отправив пустые подключения.
            for (int i = 0; i < MaxInstances; i++)
            {
                try
                {
                    using (var c = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                        c.Connect(200);
                }
                catch { }
            }
            try { _thread?.Join(3000); } catch { }
            Logger.Info("Pipe-сервер остановлен.");
        }

        // ------------------------------------------------------------
        // Приём подключений
        // ------------------------------------------------------------

        private void Loop()
        {
            while (!_stop)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = CreateServer();
                    server.WaitForConnection();
                    HandleClient(server);
                }
                catch (IOException ex) when (IsBrokenPipe(ex))
                {
                    // Нормальная ситуация: клиент отключился раньше, чем сервер закрыл поток.
                    if (!_stop) Logger.Warn("Клиент разорвал соединение с pipe (это нормально): " + ex.Message);
                }
                catch (Exception ex)
                {
                    if (!_stop) Logger.Error("Ошибка pipe-сервера", ex);
                    Thread.Sleep(500);
                }
                finally
                {
                    try { server?.Dispose(); } catch { }
                }
            }
        }

        private NamedPipeServerStream CreateServer()
        {
            var ps = new PipeSecurity();

            ps.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl, AccessControlType.Allow));

            ps.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite, AccessControlType.Allow));

            return new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                MaxInstances,
                PipeTransmissionMode.Message,
                PipeOptions.None,
                4096, 4096,
                ps);
        }

        private void HandleClient(NamedPipeServerStream pipe)
        {
            // Имя клиента, полученное через сам pipe (может быть пустым,
            // если сервер запущен под той же учёткой, что и клиент).
            string impersonated = "";
            try { impersonated = pipe.GetImpersonationUserName(); }
            catch { }

            // Не используем using для writer/reader: при разрыве канала Dispose
            // выбросит IOException — гасим её через флаг broken.
            var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };

            bool broken = false;

            try
            {
                // Первая строка клиента — его идентификация: "ID|<DOMAIN\user>".
                var firstLine = SafeReadLine(reader);
                if (firstLine == null || !firstLine.StartsWith("ID|", StringComparison.OrdinalIgnoreCase))
                {
                    SafeWriteLine(writer, "ERR|Ожидается идентификация клиента", ref broken);
                    return;
                }

                var claimedId = firstLine.Substring(3).Trim();

                // Если система отдала имя — оно приоритетно и должно совпасть.
                if (!string.IsNullOrEmpty(impersonated) &&
                    !string.Equals(impersonated, claimedId, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn($"Несоответствие ID: pipe='{impersonated}', client='{claimedId}'");
                    SafeWriteLine(writer, "ERR|Несоответствие идентификации", ref broken);
                    return;
                }

                string clientFull;
                if (!string.IsNullOrEmpty(impersonated))
                {
                    clientFull = impersonated;
                }
                else
                {
                    clientFull = claimedId;
                    Logger.Warn($"GetImpersonationUserName пуст — используем ID клиента: {clientFull}");
                }

                // Цикл команд.
                string line;
                while ((line = SafeReadLine(reader)) != null)
                {
                    if (line == "PING")
                    {
                        SafeWriteLine(writer, "PONG", ref broken);
                        if (broken) break;
                        continue;
                    }

                    if (line == "QUIT") break;

                    if (line.StartsWith("CHANGE", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { '|' }, 2);
                        var target = parts.Length > 1 ? parts[1].Trim() : "";
                        var response = ProcessChange(clientFull, target);
                        SafeWriteLine(writer, response, ref broken);
                        if (broken) break;
                    }
                    else if (line.StartsWith("RUN", StringComparison.OrdinalIgnoreCase))
                    {
                        // RUN|<user1;user2;...>  или  RUN|  (все)
                        var parts = line.Split(new[] { '|' }, 2);
                        var payload = parts.Length > 1 ? parts[1].Trim() : "";
                        ProcessRun(clientFull, payload, writer, ref broken);
                        if (broken) break;
                    }
                    else
                    {
                        SafeWriteLine(writer, "ERR|Неизвестная команда", ref broken);
                        if (broken) break;
                    }
                }
            }
            catch (IOException ex) when (IsBrokenPipe(ex))
            {
                // Клиент отвалился — это норма.
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка обработки клиента pipe", ex);
            }
            finally
            {
                try { writer.Dispose(); } catch { }
                try { reader.Dispose(); } catch { }
                try { pipe.Dispose(); } catch { }
            }
        }

        // ------------------------------------------------------------
        // Обработка команд
        // ------------------------------------------------------------

        /// <summary>
        /// Смена пароля с учётом ролей.
        ///   1) защищённые логины — отказ всем (в т.ч. владельцу);
        ///   2) обычный пользователь — только свой;
        ///   3) менеджер — любой незащищённый;
        ///   4) менеджер + защищённый (efsol_dc) — меняет чужие, свой не может.
        /// </summary>
        private string ProcessChange(string clientFull, string targetUser)
        {
            try
            {
                if (string.IsNullOrEmpty(clientFull))
                    return "ERR|Не удалось определить пользователя-клиента";

                var clientShort = ServiceConfig.ShortName(clientFull);

                if (string.IsNullOrEmpty(targetUser))
                    targetUser = clientShort;

                var targetShort = ServiceConfig.ShortName(targetUser);

                // 1. Цель защищена политикой — отказ всем.
                if (_cfg.IsProtectedUser(targetShort))
                {
                    bool sameUser = string.Equals(clientShort, targetShort,
                                                  StringComparison.OrdinalIgnoreCase);

                    if (sameUser)
                    {
                        Logger.Warn($"ОТКАЗ [protected-self]: {clientFull}");
                        return "ERR|Ваш пароль защищён политикой безопасности и не может быть изменён " +
                               "через службу. Смените его средствами Windows или обратитесь к администратору.";
                    }

                    Logger.Warn($"ОТКАЗ [protected]: {clientFull} → '{targetShort}'");
                    return "ERR|Логин '" + targetShort + "' защищён политикой безопасности. " +
                           "Смена пароля запрещена.";
                }

                // 2. Проверка роли.
                bool isSelf = string.Equals(clientShort, targetShort, StringComparison.OrdinalIgnoreCase);
                bool isManager = _cfg.IsManager(clientShort);

                if (!isSelf && !isManager)
                {
                    Logger.Warn($"ОТКАЗ [no-role]: {clientFull} → '{targetShort}'");
                    return "ERR|Разрешена смена только своего пароля. Для смены чужих обратитесь к менеджеру.";
                }

                // 3. Выполняем.
                var processor = new EventProcessor(_cfg);
                var results = processor.RunOnce(new[] { targetShort });

                if (results == null || results.Count == 0)
                    return "ERR|Пользователь не найден или цикл не выполнен";

                var r = results[0];
                if (!r.Success)
                    return "ERR|" + (r.Error ?? "неизвестная ошибка");

                if (isSelf)
                    Logger.Info($"По запросу {clientFull} сменён собственный пароль");
                else
                    Logger.Info($"Менеджер {clientFull} сменил пароль для '{targetShort}'");

                return "OK|" + r.NewPassword;
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка обработки pipe-запроса (change)", ex);
                return "ERR|" + ex.Message;
            }
        }

        /// <summary>
        /// Массовая смена паролей по запросу менеджера.
        /// Отвечает несколькими строками:
        ///   OK|user|email|password     — успех по пользователю
        ///   ERR|user|email|error       — ошибка по пользователю
        ///   FATAL|error text           — глобальная ошибка (нет прав, внутренняя ошибка)
        ///   DONE                       — конец передачи
        /// </summary>
        private void ProcessRun(string clientFull, string payload,
                                StreamWriter writer, ref bool broken)
        {
            try
            {
                var clientShort = ServiceConfig.ShortName(clientFull);

                // Только менеджеры могут запускать общий цикл.
                if (!_cfg.IsManager(clientShort))
                {
                    Logger.Warn($"ОТКАЗ [run-no-role]: {clientFull} пытался запустить run");
                    SafeWriteLine(writer,
                        "FATAL|Запуск общего цикла разрешён только менеджерам. " +
                        "Обратитесь к администратору, чтобы добавить ваш логин в список Managers в my.ini.",
                        ref broken);
                    SafeWriteLine(writer, "DONE", ref broken);
                    return;
                }

                // Разбираем список логинов (если пустой — все из CSV).
                List<string> filter = null;
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    var list = payload
                        .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0)
                        .ToList();
                    if (list.Count > 0) filter = list;
                }

                Logger.Info($"Менеджер {clientFull} запросил run: " +
                            (filter == null ? "ВСЕ из emails.csv" : string.Join(", ", filter)));

                var processor = new EventProcessor(_cfg);
                var results = processor.RunOnce(filter);

                foreach (var r in results)
                {
                    if (r.Success)
                        SafeWriteLine(writer, $"OK|{r.Username}|{r.Email}|{r.NewPassword}", ref broken);
                    else
                        SafeWriteLine(writer, $"ERR|{r.Username}|{r.Email}|{r.Error}", ref broken);
                    if (broken) return;
                }

                SafeWriteLine(writer, "DONE", ref broken);
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка выполнения run через pipe", ex);
                SafeWriteLine(writer, "FATAL|" + ex.Message, ref broken);
                SafeWriteLine(writer, "DONE", ref broken);
            }
        }

        // ------------------------------------------------------------
        // Утилиты
        // ------------------------------------------------------------

        private static bool IsBrokenPipe(IOException ex)
        {
            var msg = ex.Message ?? "";
            return msg.IndexOf("нарушен", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("broken", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("pipe", StringComparison.OrdinalIgnoreCase) >= 0
                || ex.HResult == unchecked((int)0x8007006D)   // ERROR_BROKEN_PIPE
                || ex.HResult == unchecked((int)0x800700E8);  // ERROR_NO_DATA
        }

        private static string SafeReadLine(StreamReader reader)
        {
            try { return reader.ReadLine(); }
            catch (IOException) { return null; }
            catch (ObjectDisposedException) { return null; }
        }

        private static void SafeWriteLine(StreamWriter writer, string text, ref bool broken)
        {
            if (broken) return;
            try
            {
                writer.WriteLine(text);
            }
            catch (IOException)
            {
                broken = true;
            }
            catch (ObjectDisposedException)
            {
                broken = true;
            }
        }
    }
}