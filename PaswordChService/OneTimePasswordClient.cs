using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace PaswordChService
{
    /// <summary>
    /// Клиент API одноразовых паролей (IIS-сервис на /Api.ashx).
    /// Синхронный — рассчитан на вызовы из фоновых задач службы.
    /// Не использует внешних JSON-библиотек, парсит только нужные поля.
    /// </summary>
    public sealed class OneTimePasswordClient : IDisposable
    {
        private readonly HttpClient _http;

        public OneTimePasswordClient(string baseUrl, string apiKey, bool ignoreSslErrors = false)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("baseUrl не задан", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("apiKey не задан", nameof(apiKey));

            var handler = new HttpClientHandler();

            if (ignoreSslErrors)
            {
                handler.ServerCertificateCustomValidationCallback =
                    (msg, cert, chain, errors) => true;
            }

            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(15)
            };
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        /// <summary>
        /// Создаёт ссылку. Бросает Exception при любой ошибке HTTP/парсинга.
        /// </summary>
        public OneTimePasswordResult CreateLink(string secret, int ttlMinutes)
        {
            // Собираем JSON вручную — никаких внешних сериализаторов.
            // Экранируем только кавычки и обратный слэш — этого достаточно для паролей.
            string payload = "{\"secret\":\"" + EscapeJson(secret) + "\",\"ttl\":" + ttlMinutes + "}";

            using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
            using (var resp = _http.PostAsync("Api.ashx", content).GetAwaiter().GetResult())
            {
                var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"API {(int)resp.StatusCode}: {Trim(body, 500)}");

                var url = ExtractString(body, "url");
                var id = ExtractString(body, "id");
                var expiresMinutes = ExtractInt(body, "expiresMinutes");
                var expiresAtUtc = ExtractString(body, "expiresAtUtc");

                if (string.IsNullOrEmpty(url))
                    throw new Exception("Ответ API не содержит url: " + Trim(body, 300));

                return new OneTimePasswordResult
                {
                    Url = url,
                    Id = id,
                    ExpiresMinutes = expiresMinutes,
                    ExpiresAtUtc = expiresAtUtc
                };
            }
        }

        public void Dispose() => _http.Dispose();

        // ---------- Мини-парсер JSON ----------

        /// <summary>Ищет "key":"value" и возвращает value с раскодированием escape-последовательностей.</summary>
        private static string ExtractString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            // Ищем "key"\s*:\s*"value"
            var pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"";
            var m = Regex.Match(json, pattern);
            if (!m.Success) return null;

            return UnescapeJson(m.Groups[1].Value);
        }

        /// <summary>Ищет "key":value и возвращает int, если получилось.</summary>
        private static int ExtractInt(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return 0;

            var pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)";
            var m = Regex.Match(json, pattern);
            if (!m.Success) return 0;

            int v;
            return int.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i == s.Length - 1) { sb.Append(s[i]); continue; }
                char n = s[++i];
                switch (n)
                {
                    case '\\': sb.Append('\\'); break;
                    case '\"': sb.Append('\"'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < s.Length &&
                            int.TryParse(s.Substring(i + 1, 4),
                                         System.Globalization.NumberStyles.HexNumber,
                                         null, out int code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }

    public sealed class OneTimePasswordResult
    {
        public string Url { get; set; }
        public string Id { get; set; }
        public int ExpiresMinutes { get; set; }
        public string ExpiresAtUtc { get; set; }
    }
}