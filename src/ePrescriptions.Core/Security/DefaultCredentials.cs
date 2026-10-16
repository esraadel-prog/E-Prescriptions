using System;

namespace ePrescriptions.Core.Security
{
    /// <summary>
    /// خطوة 46: كلمات المرور المعروفة مسبقاً (افتراضية). أي حساب يسجّل دخوله بواحدة منها لازم يغيّرها قبل ما يكمّل.
    /// </summary>
    public static class DefaultCredentials
    {
        /// <summary>كلمة مرور Owner الافتراضية (تثبيتات قديمة أو مسار الاختبارات/البيانات التجريبية)</summary>
        public const string OwnerPassword = "ChangeMe@Owner1";

        /// <summary>هل كلمة المرور دي معروفة مسبقاً (افتراضية)؟ مقارنة حرفية (حساسة لحالة الأحرف)</summary>
        public static bool IsKnownDefaultPassword(string? password)
            => !string.IsNullOrEmpty(password) && string.Equals(password, OwnerPassword, StringComparison.Ordinal);
    }
}
