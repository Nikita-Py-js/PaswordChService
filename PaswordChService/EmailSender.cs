using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;

namespace PaswordChService
{
    /// <summary>SMTP-уведомления (HTML + plaintext fallback) с retry.</summary>
    public class EmailSender
    {
        private readonly ServiceConfig _cfg;
        private static readonly int[] RetryDelaysSeconds = { 5, 15, 30 };

        public EmailSender(ServiceConfig cfg) { _cfg = cfg; }

        // ================================================================
        //  Публичные API
        // ================================================================

        /// <summary>Простое текстовое письмо (для обратной совместимости).</summary>
        public void Send(string to, string subject, string body)
            => SendMessage(to, subject, body, isHtml: false);

        /// <summary>HTML-письмо пользователю с новым паролем.</summary>
        public void SendUserPassword(string to, string userName, string newPassword)
        {
            string html;
            try
            {
                html = BuildUserEmail(userName, newPassword);
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось построить HTML-письмо пользователю, отправляю текстом", ex);

                var subjFallback = RenderSubject(_cfg.EmailUserSubject, new Dictionary<string, string>
                {
                    { "UserName",          userName },
                    { "ClientCompanyName", EffectiveClientName },
                    { "CompanyName",       _cfg.CompanyName },
                });

                Send(to, subjFallback, $"Ваш новый пароль: {newPassword}");
                return;
            }

            var subject = RenderSubject(_cfg.EmailUserSubject, new Dictionary<string, string>
            {
                { "UserName",          userName },
                { "ClientCompanyName", EffectiveClientName },
                { "CompanyName",       _cfg.CompanyName },
            });

            SendMessage(to, subject, html, isHtml: true);
        }

        /// <summary>HTML-отчёт администраторам о результате цикла.</summary>
        public void SendAdminReport(List<PasswordChangeResult> successes,
                                     List<PasswordChangeResult> errors,
                                     DateTime nextRun)
        {
            string html;
            try
            {
                html = BuildAdminEmail(successes, errors, nextRun);
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось построить HTML-отчёт, отправляю текстом", ex);
                var txt = $"Успешно: {successes.Count}. Ошибок: {errors.Count}.";
                NotifyAdmins("PaswordCh: цикл смены паролей", txt);
                return;
            }

            var subject = RenderSubject(_cfg.EmailAdminSubject, new Dictionary<string, string>
            {
                { "ClientCompanyName", EffectiveClientName },
                { "CompanyName",       _cfg.CompanyName },
                { "TotalSuccess",      successes.Count.ToString() },
                { "TotalErrors",       errors.Count.ToString() },
            });

            foreach (var a in _cfg.AdminEmails)
            {
                var to = a.Trim();
                if (to.Length == 0) continue;
                try
                {
                    SendMessage(to, subject, html, isHtml: true);
                    Logger.Info("HTML-отчёт отправлен: " + to);
                }
                catch (Exception ex)
                {
                    Logger.Error("Ошибка отправки отчёта на " + to, ex);
                }
                Thread.Sleep(1000);
            }
        }

        /// <summary>Устаревший текстовый notify (оставлен для совместимости).</summary>
        public void NotifyAdmins(string subject, string body)
        {
            foreach (var a in _cfg.AdminEmails)
            {
                var to = a.Trim();
                if (to.Length == 0) continue;
                try { Send(to, subject, body); Logger.Info("Уведомление отправлено: " + to); }
                catch (Exception ex) { Logger.Error("Ошибка отправки на " + to, ex); }
                Thread.Sleep(1000);
            }
        }

        // ================================================================
        //  Отправка с retry
        // ================================================================

        private void SendMessage(string to, string subject, string body, bool isHtml)
        {
            if (string.IsNullOrWhiteSpace(_cfg.SmtpHost))
                throw new InvalidOperationException("SMTP-сервер не настроен.");

            Exception lastEx = null;

            for (int attempt = 0; attempt <= RetryDelaysSeconds.Length; attempt++)
            {
                try
                {
                    SendOnce(to, subject, body, isHtml);
                    return;
                }
                catch (SmtpException ex) when (IsTemporary(ex))
                {
                    lastEx = ex;
                    if (attempt < RetryDelaysSeconds.Length)
                    {
                        var delay = RetryDelaysSeconds[attempt];
                        Logger.Warn($"Временная ошибка SMTP при отправке на {to}: {ex.Message}. " +
                                    $"Повтор через {delay} с...");
                        Thread.Sleep(delay * 1000);
                    }
                }
                catch (SmtpException ex)
                {
                    Logger.Error($"SMTP отказал навсегда при отправке на {to}: {ex.StatusCode} {ex.Message}", ex);
                    throw;
                }
            }

            throw lastEx ?? new SmtpException("SMTP: все попытки исчерпаны");
        }

        private void SendOnce(string to, string subject, string body, bool isHtml)
        {
            using (var msg = new MailMessage())
            using (var client = new SmtpClient(_cfg.SmtpHost, _cfg.SmtpPort))
            {
                msg.From = new MailAddress(_cfg.SmtpFrom);
                msg.To.Add(to);
                msg.Subject = subject;
                msg.SubjectEncoding = Encoding.UTF8;
                msg.BodyEncoding = Encoding.UTF8;
                msg.Body = body;
                msg.IsBodyHtml = isHtml;

                client.EnableSsl = _cfg.SmtpUseSsl;
                client.Timeout = _cfg.SmtpTimeoutMs;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;

                if (!string.IsNullOrEmpty(_cfg.SmtpUser))
                    client.Credentials = new NetworkCredential(_cfg.SmtpUser, _cfg.SmtpPassword);

                client.Send(msg);
            }
        }

        private static bool IsTemporary(SmtpException ex)
        {
            switch (ex.StatusCode)
            {
                case SmtpStatusCode.ServiceNotAvailable:
                case SmtpStatusCode.MailboxBusy:
                case SmtpStatusCode.LocalErrorInProcessing:
                case SmtpStatusCode.InsufficientStorage:
                case SmtpStatusCode.ClientNotPermitted:
                    return true;
                default:
                    return ex.Message.IndexOf("temporary", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        // ================================================================
        //  Утилиты
        // ================================================================

        /// <summary>Имя компании клиента. Если не задано — берём CompanyName.</summary>
        private string EffectiveClientName =>
            string.IsNullOrWhiteSpace(_cfg.ClientCompanyName)
                ? _cfg.CompanyName
                : _cfg.ClientCompanyName;

        /// <summary>Гарантирует наличие схемы в URL.</summary>
        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            var u = url.Trim();
            if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return u;
            if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return u;
            if (u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return u;
            return "https://" + u;
        }

        /// <summary>
        /// Подставляет плейсхолдеры {{Key}} в строку темы.
        /// Если значение не передано — плейсхолдер остаётся как есть.
        /// </summary>
        private static string RenderSubject(string template, IDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(template)) return template ?? "";
            var sb = new StringBuilder(template);
            if (values != null)
            {
                foreach (var kv in values)
                    sb.Replace("{{" + kv.Key + "}}", kv.Value ?? "");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Общий набор подстановок для шапки/подвала/тела писем.
        /// {{BrandHtml}} — вставляется как есть, без экранирования (пользовательский HTML).
        /// Остальное — экранируется.
        /// </summary>
        private Dictionary<string, string> CommonBrandValues()
        {
            var siteUrl = NormalizeUrl(_cfg.CompanySite);
            var vendorUrl = NormalizeUrl(_cfg.VendorUrl);

            return new Dictionary<string, string>
            {
                { "BrandHtml",          _cfg.CompanyBrandHtml ?? "" },
                { "CompanyName",        EmailTemplate.Html(_cfg.CompanyName) },
                { "ClientCompanyName",  EmailTemplate.Html(EffectiveClientName) },
                { "CompanyTagline",     EmailTemplate.Html(_cfg.CompanyTagline) },
                { "CompanySite",        EmailTemplate.Html(_cfg.CompanySite) },
                { "CompanySiteUrl",     EmailTemplate.Html(siteUrl) },
                { "CompanyEmail",       EmailTemplate.Html(_cfg.CompanyEmail) },
                { "CompanyPhone",       EmailTemplate.Html(_cfg.CompanyPhone) },
                { "VendorLine",         EmailTemplate.Html(_cfg.VendorLine) },
                { "VendorUrl",          EmailTemplate.Html(vendorUrl) },
            };
        }

        // ================================================================
        //  Построение HTML
        // ================================================================

        private string BuildUserEmail(string userName, string newPassword)
        {
            var tpl = EmailTemplate.Load("EmailUser.html");

            var values = CommonBrandValues();
            values["Greeting"] = "Здравствуйте, " + EmailTemplate.Html(userName) + "!";
            values["UserName"] = EmailTemplate.Html(userName);
            values["NewPassword"] = EmailTemplate.Html(newPassword);
            values["Date"] = DateTime.Now.ToString("dd.MM.yyyy HH:mm");

            return EmailTemplate.Render(tpl, values);
        }

        /// <summary>HTML-письмо пользователю со ссылкой на одноразовый пароль.</summary>
        public void SendUserPasswordLink(string to, string userName, string passwordUrl)
        {
            string html;
            try
            {
                html = BuildUserEmailLink(userName, passwordUrl);
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось построить HTML-письмо (ссылка), отправляю текстом", ex);

                var subjFallback = RenderSubject(_cfg.EmailUserSubject, new Dictionary<string, string>
        {
            { "UserName",          userName },
            { "ClientCompanyName", EffectiveClientName },
            { "CompanyName",       _cfg.CompanyName },
        });

                Send(to, subjFallback, "Ссылка для получения пароля (действует один раз): " + passwordUrl);
                return;
            }

            var subject = RenderSubject(_cfg.EmailUserSubject, new Dictionary<string, string>
    {
        { "UserName",          userName },
        { "ClientCompanyName", EffectiveClientName },
        { "CompanyName",       _cfg.CompanyName },
    });

            SendMessage(to, subject, html, isHtml: true);
        }

        private string BuildUserEmailLink(string userName, string passwordUrl)
        {
            var tpl = EmailTemplate.Load("EmailUserLink.html");

            var values = CommonBrandValues();
            values["Greeting"] = "Здравствуйте, " + EmailTemplate.Html(userName) + "!";
            values["UserName"] = EmailTemplate.Html(userName);
            values["PasswordUrl"] = EmailTemplate.Html(passwordUrl);
            values["Date"] = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            values["TtlMinutes"] = _cfg.OtpApiTtlMinutes.ToString();

            return EmailTemplate.Render(tpl, values);
        }
        private string BuildAdminEmail(List<PasswordChangeResult> successes,
                                        List<PasswordChangeResult> errors,
                                        DateTime nextRun)
        {
            var tpl = EmailTemplate.Load("EmailAdmin.html");

            var rows = new StringBuilder();

            foreach (var r in successes)
            {
                rows.Append("<tr>");
                rows.Append("<td style=\"padding:11px 16px;border-bottom:1px solid #eaeaea;color:#0a0a0a;\">")
                    .Append(EmailTemplate.Html(r.Username)).Append("</td>");
                rows.Append("<td style=\"padding:11px 16px;border-bottom:1px solid #eaeaea;color:#555555;\">")
                    .Append(EmailTemplate.Html(r.Email)).Append("</td>");
                rows.Append("<td style=\"padding:11px 16px;border-bottom:1px solid #eaeaea;\">")
                    .Append("<span style=\"display:inline-block;padding:3px 10px;background:#ffcc00;color:#0a0a0a;font-size:11px;font-weight:800;letter-spacing:1px;\">OK</span>")
                    .Append("</td>");
                rows.Append("</tr>");
            }
            foreach (var r in errors)
            {
                rows.Append("<tr>");
                rows.Append("<td style=\"padding:11px 16px;border-bottom:1px solid #eaeaea;color:#0a0a0a;\">")
                    .Append(EmailTemplate.Html(r.Username)).Append("</td>");
                rows.Append("<td style=\"padding:11px 16px;border-bottom:1px solid #eaeaea;color:#555555;\">")
                    .Append(EmailTemplate.Html(r.Email)).Append("</td>");
                rows.Append("<td style=\"padding:11px 16px;border-bottom:1px solid #eaeaea;color:#c92a2a;font-size:12px;\">")
                    .Append(EmailTemplate.Html(r.Error ?? "ошибка")).Append("</td>");
                rows.Append("</tr>");
            }

            if (rows.Length == 0)
                rows.Append("<tr><td colspan=\"3\" style=\"padding:14px;color:#8a8a8a;text-align:center;\">Нет данных</td></tr>");

            var values = CommonBrandValues();
            values["TotalSuccess"] = successes.Count.ToString();
            values["TotalErrors"] = errors.Count.ToString();
            values["NextRun"] = nextRun.ToString("dd.MM.yyyy HH:mm");
            values["Rows"] = rows.ToString();
            values["ArchivePath"] = EmailTemplate.Html(_cfg.ArchivePath);
            values["LogPath"] = EmailTemplate.Html(_cfg.LogPath);

            return EmailTemplate.Render(tpl, values);
        }
    }
}