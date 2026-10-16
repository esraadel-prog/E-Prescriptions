using System;
using System.Security.Cryptography;

namespace ePrescriptions.Core.Security
{
    /// <summary>
    /// تشفير وفحص كلمات المرور بـ PBKDF2-HMAC-SHA256 مع Salt عشوائي (متوافق مع .NET Framework 4.8).
    /// الصيغة الحالية: sha256:{iterations}:{salt_base64}:{hash_base64}
    /// الصيغة القديمة (PBKDF2-SHA1، 50000 تكرار): {iterations}:{salt_base64}:{hash_base64} — لسه بتتحقق منها
    /// وبتتحدّث تلقائياً للصيغة الحالية عند أول دخول ناجح (راجع NeedsRehash).
    /// </summary>
    public static class PasswordHasher
    {
        public const string Sha256Prefix = "sha256";
        private const int SaltByteSize = 16;
        private const int HashByteSize = 32;

        /// <summary>عدد تكرارات الصيغة الحالية. رفعه لاحقاً يخلّي الحسابات تتحدّث تلقائياً عند الدخول.</summary>
        public const int IterationCount = 210000;

        public static string HashPassword(string plainPassword) => HashPassword(plainPassword, IterationCount);

        public static string HashPassword(string plainPassword, int iterations)
        {
            if (string.IsNullOrEmpty(plainPassword))
                throw new ArgumentNullException(nameof(plainPassword));

            byte[] salt = new byte[SaltByteSize];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }

            using var pbkdf2 = new Rfc2898DeriveBytes(plainPassword, salt, iterations, HashAlgorithmName.SHA256);
            byte[] hash = pbkdf2.GetBytes(HashByteSize);

            return $"{Sha256Prefix}:{iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        /// <summary>true لو التجزئة بالصيغة القديمة (SHA1) أو بعدد تكرارات أقل من الحالي</summary>
        public static bool NeedsRehash(string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash)) return false;
            string[] parts = storedHash.Split(':');
            if (parts.Length == 4 && parts[0] == Sha256Prefix)
                return !int.TryParse(parts[1], out int it) || it < IterationCount;
            return true;
        }

        /// <summary>
        /// التحقق من صحة كلمة المرور المدخلة ومقارنتها بالتجزئة المحفوظة (الصيغتين مع تسامح للأرقام العربية/الإنجليزية)
        /// </summary>
        public static bool VerifyPassword(string plainPassword, string storedHash)
        {
            if (string.IsNullOrEmpty(plainPassword) || string.IsNullOrEmpty(storedHash))
                return false;

            if (VerifyPasswordInternal(plainPassword, storedHash))
                return true;

            // تسامح ذكي مع تبديل لوحة المفاتيح بين الأرقام الإنجليزية (0-9) والأرقام العربية (٠-٩)
            string normDigits = Helpers.ArabicTextNormalizer.NormalizeDigits(plainPassword);
            if (!string.Equals(normDigits, plainPassword, StringComparison.Ordinal) && VerifyPasswordInternal(normDigits, storedHash))
                return true;

            string arabicDigits = Helpers.ArabicTextNormalizer.ToArabicIndicDigits(plainPassword);
            if (!string.Equals(arabicDigits, plainPassword, StringComparison.Ordinal) && VerifyPasswordInternal(arabicDigits, storedHash))
                return true;

            return false;
        }

        private static bool VerifyPasswordInternal(string plainPassword, string storedHash)
        {
            try
            {
                string[] parts = storedHash.Split(':');
                HashAlgorithmName algorithm;
                string iterText, saltText, hashText;

                if (parts.Length == 4 && parts[0] == Sha256Prefix)
                {
                    algorithm = HashAlgorithmName.SHA256;
                    iterText = parts[1]; saltText = parts[2]; hashText = parts[3];
                }
                else if (parts.Length == 3)
                {
                    algorithm = HashAlgorithmName.SHA1; // الصيغة القديمة
                    iterText = parts[0]; saltText = parts[1]; hashText = parts[2];
                }
                else
                {
                    return false;
                }

                if (!int.TryParse(iterText, out int iterations) || iterations <= 0)
                    return false;

                byte[] salt = Convert.FromBase64String(saltText);
                byte[] expectedHash = Convert.FromBase64String(hashText);

                using var pbkdf2 = new Rfc2898DeriveBytes(plainPassword, salt, iterations, algorithm);
                byte[] actualHash = pbkdf2.GetBytes(expectedHash.Length);

                return SlowEquals(expectedHash, actualHash);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// مقارنة ذات وقت ثابت (Constant Time) لمنع هجمات التوقيت (Timing Attacks)
        /// </summary>
        private static bool SlowEquals(byte[] a, byte[] b)
        {
            uint diff = (uint)a.Length ^ (uint)b.Length;
            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                diff |= (uint)(a[i] ^ b[i]);
            }
            return diff == 0;
        }
    }
}
