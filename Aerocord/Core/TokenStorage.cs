using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Aerocord.Core
{
    public static class TokenStorage
    {
        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Aerocord");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "session.bin");
            }
        }

        public static void Save(string token)
        {
            byte[] plain = Encoding.UTF8.GetBytes(token);
            byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, cipher);
        }

        public static string Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                byte[] cipher = File.ReadAllBytes(FilePath);
                byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return null;
            }
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* ignore */ }
        }
    }
}
