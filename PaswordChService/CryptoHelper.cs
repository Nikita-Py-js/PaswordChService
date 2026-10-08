using System;
using System.Security.Cryptography;
using System.Text;

namespace PaswordChService
{
    /// <summary>
    /// Шифрование секретов (пароли SMTP, ZIP, AD) через Windows DPAPI.
    /// Привязка к машине (LocalMachine scope): зашифрованное значение
    /// нельзя расшифровать на другом компьютере.
    /// </summary>
    public static class CryptoHelper
    {
        public const string Prefix = "ENC:";

        // Дополнительная соль — снижает риск blind-pattern атак,
        // не является секретом сама по себе.
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("PaswordChService::v1::entropy");

        /// <summary>Зашифровать строку. Возвращает "ENC:base64".</summary>
        public static string Encrypt(string plain)
        {
            if (plain == null) return null;
            if (plain.Length == 0) return plain;

            var bytes = Encoding.UTF8.GetBytes(plain);
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.LocalMachine);
            return Prefix + Convert.ToBase64String(encrypted);
        }

        /// <summary>
        /// Расшифровать строку. Если она не начинается с "ENC:" — возвращается как есть
        /// (обратная совместимость с открытыми значениями).
        /// При ошибке расшифровки бросает исключение.
        /// </summary>
        public static string Decrypt(string value)
        {
            if (value == null) return null;
            if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;

            var b64 = value.Substring(Prefix.Length);
            var bytes = Convert.FromBase64String(b64);
            var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }

        /// <summary>True, если значение уже зашифровано.</summary>
        public static bool IsEncrypted(string value)
        {
            return value != null && value.StartsWith(Prefix, StringComparison.Ordinal);
        }
    }
}