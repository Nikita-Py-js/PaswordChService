using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PaswordChService
{
    /// <summary>Загрузка HTML-шаблонов и подстановка {{Placeholder}}.</summary>
    public static class EmailTemplate
    {
        public static string Load(string fileName)
        {
            var path = Path.Combine(Paths.InstallDir, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Шаблон письма не найден: " + path, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public static string Render(string template, IDictionary<string, string> values)
        {
            if (template == null) return "";
            var sb = new StringBuilder(template);
            if (values != null)
            {
                foreach (var kv in values)
                    sb.Replace("{{" + kv.Key + "}}", kv.Value ?? "");
            }
            return sb.ToString();
        }

        public static string Html(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}