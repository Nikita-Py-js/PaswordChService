using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PaswordChService
{
    /// <summary>
    /// Читает/пишет INI-файл с автоопределением кодировки
    /// (UTF-8, UTF-8 BOM, UTF-16 LE/BE BOM, Windows-1251).
    /// Запись «на месте» — сохраняет комментарии и структуру файла.
    /// Потокобезопасен, не ломает ini 
    /// </summary>
    public class INIManager
    {
        private readonly string _path;
        private readonly object _lock = new object();
        private Dictionary<string, Dictionary<string, string>> _data;
        private DateTime _lastReadUtc = DateTime.MinValue;

        public INIManager(string iniPath)
        {
            _path = iniPath;
            _data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Reload();
        }

        // ---------- Публичный API ----------

        public string ReadString(string section, string key, string defaultValue = "")
        {
            lock (_lock)
            {
                EnsureFresh();
                if (_data.TryGetValue(section, out var s) && s.TryGetValue(key, out var v))
                    return v;
                return defaultValue;
            }
        }

        public int ReadInt(string section, string key, int defaultValue)
            => int.TryParse(ReadString(section, key, defaultValue.ToString()), out var r) ? r : defaultValue;

        public bool ReadBool(string section, string key, bool defaultValue)
            => bool.TryParse(ReadString(section, key, defaultValue.ToString()), out var r) ? r : defaultValue;

        /// <summary>
        /// Пишет значение в файл, сохраняя комментарии, порядок строк и кодировку.
        /// Если ключ не найден — добавляет строку в конец секции (или в конец файла).
        /// </summary>
        public void WriteString(string section, string key, string value)
        {
            lock (_lock)
            {
                value = value ?? "";

                if (!File.Exists(_path))
                {
                    File.WriteAllText(_path, $"[{section}]\r\n{key}={value}\r\n", new UTF8Encoding(false));
                }
                else
                {
                    WriteInPlace(section, key, value);
                }

                // Обновляем in-memory
                if (!_data.TryGetValue(section, out var s))
                {
                    s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _data[section] = s;
                }
                s[key] = value;

                _lastReadUtc = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
            }
        }

        // ---------- Внутренняя логика ----------

        private void EnsureFresh()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var last = File.GetLastWriteTimeUtc(_path);
                if (last > _lastReadUtc) Reload();
            }
            catch { }
        }

        /// <summary>
        /// Перечитывает INI-файл с диска и заново заполняет внутренний кеш секций и ключей.
        /// Если файла нет — кеш очищается, метка времени сбрасывается.
        /// </summary>
        private void Reload()
        {
            _data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(_path))
            {
                _lastReadUtc = DateTime.MinValue;
                return;
            }

            var text = ReadAllTextAuto(_path);
            _lastReadUtc = File.GetLastWriteTimeUtc(_path);

            string currentSection = "";
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    if (!_data.ContainsKey(currentSection))
                        _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();

                // Inline-комментарий ; или # после пробела
                var cut = -1;
                for (int i = 0; i < val.Length; i++)
                {
                    if ((val[i] == ';' || val[i] == '#') && i > 0 && char.IsWhiteSpace(val[i - 1]))
                    {
                        cut = i; break;
                    }
                }
                if (cut > 0) val = val.Substring(0, cut).TrimEnd();

                if (currentSection.Length == 0) continue;

                if (!_data.TryGetValue(currentSection, out var s))
                {
                    s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _data[currentSection] = s;
                }
                s[key] = val;
            }
        }

        /// <summary>
        /// Заменяет значение ключа в файле, сохраняя все остальные строки, комментарии
        /// и исходную кодировку.
        /// </summary>
        private void WriteInPlace(string section, string key, string value)
        {
            var (text, encoding) = ReadAllTextAutoWithEncoding(_path);

            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            var result = new List<string>(lines.Length);
            string currentSection = "";
            bool replaced = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmedStart = line.TrimStart();

                if (trimmedStart.StartsWith("[") && trimmedStart.Contains("]"))
                {
                    var close = trimmedStart.IndexOf(']');
                    currentSection = trimmedStart.Substring(1, close - 1).Trim();
                    result.Add(line);
                    continue;
                }

                if (!replaced &&
                    string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                {
                    var eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        var k = line.Substring(0, eq).Trim();
                        if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                        {
                            var indent = line.Substring(0, line.Length - trimmedStart.Length);
                            result.Add(indent + key + "=" + value);
                            replaced = true;
                            continue;
                        }
                    }
                }

                result.Add(line);
            }

            if (!replaced)
            {
                // Ключ не найден — добавляем в конец секции или создаём новую.
                var sb = new StringBuilder(string.Join(newline, result).TrimEnd('\r', '\n'));

                int sectionIdx = result.FindIndex(l => l.Trim().Equals("[" + section + "]",
                                            StringComparison.OrdinalIgnoreCase));
                if (sectionIdx >= 0)
                {
                    // Ищем конец секции (следующая [ или конец)
                    int insertAt = result.Count;
                    for (int i = sectionIdx + 1; i < result.Count; i++)
                    {
                        var t = result[i].TrimStart();
                        if (t.StartsWith("[") && t.Contains("]")) { insertAt = i; break; }
                    }
                    result.Insert(insertAt, key + "=" + value);
                    File.WriteAllText(_path, string.Join(newline, result), encoding);
                }
                else
                {
                    sb.Append(newline).Append(newline)
                      .Append('[').Append(section).Append(']').Append(newline)
                      .Append(key).Append('=').Append(value);
                    File.WriteAllText(_path, sb.ToString(), encoding);
                }
                return;
            }

            File.WriteAllText(_path, string.Join(newline, result), encoding);
        }

        // ---------- Кодировки ----------

        private static string ReadAllTextAuto(string path)
            => ReadAllTextAutoWithEncoding(path).Text;

        private static (string Text, Encoding Encoding) ReadAllTextAutoWithEncoding(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return ("", new UTF8Encoding(false));

            // UTF-8 BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));

            // UTF-16 LE BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), new UnicodeEncoding(false, true));

            // UTF-16 BE BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), new UnicodeEncoding(true, true));

            // UTF-32 LE BOM
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                return (Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4), new UTF32Encoding(false, true));

            // Без BOM — пробуем строгий UTF-8, при ошибке — Windows-1251
            try
            {
                var strict = new UTF8Encoding(false, true);
                return (strict.GetString(bytes), new UTF8Encoding(false));
            }
            catch
            {
                try
                {
                    var enc = Encoding.GetEncoding(1251);
                    return (enc.GetString(bytes), enc);
                }
                catch
                {
                    return (Encoding.Default.GetString(bytes), Encoding.Default);
                }
            }
        }
    }
}