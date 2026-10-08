using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PaswordChService
{
    /// <summary>Упаковка файла в ZIP с AES-шифрованием содержимого.</summary>
    public static class ZipArchiver
    {
        public static string CreateEncryptedZip(string sourceFile, string archiveDir, string password)
        {
            Directory.CreateDirectory(archiveDir);
            var baseName = Path.GetFileNameWithoutExtension(sourceFile);
            var zipPath = Path.Combine(archiveDir,
                baseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip");

            var encrypted = EncryptString(File.ReadAllText(sourceFile, Encoding.UTF8), password);

            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, encrypted, Encoding.UTF8);
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(tempFile, Path.GetFileName(sourceFile) + ".enc",
                                            CompressionLevel.Optimal);
                }
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
            return zipPath;
        }

        private static string EncryptString(string plain, string password)
        {
            var salt = new byte[16];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(salt);

            using (var derive = new Rfc2898DeriveBytes(password, salt, 100000))
            {
                var key = derive.GetBytes(32);
                var iv = derive.GetBytes(16);

                using (var aes = Aes.Create())
                {
                    aes.Key = key; aes.IV = iv;
                    using (var ms = new MemoryStream())
                    {
                        ms.Write(salt, 0, salt.Length);
                        ms.Write(iv, 0, iv.Length);
                        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                        using (var sw = new StreamWriter(cs, Encoding.UTF8))
                        {
                            sw.Write(plain);
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
        }
    }
}