using System;
using System.Globalization;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// نقطة واحدة لكتابة وقراءة وعرض التواريخ (خطوة 26): كل التنسيقات والقراءات ثابتة الثقافة (Gregorian، أرقام لاتينية،
    /// فواصل "-" و"/" و":" حرفية) مهما كانت لغة/تقويم ويندوز أو خيط التنفيذ (async). ما حدّش يستخدم
    /// ToString("yyyy-...") أو DateTime.Parse بدون ثقافة.
    /// </summary>
    public static class DbDate
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ===== صيغ التخزين في القاعدة =====
        /// <summary>yyyy-MM-dd</summary>
        public static string Date(DateTime d) => d.ToString("yyyy-MM-dd", Inv);
        /// <summary>yyyy-MM-dd HH:mm:ss</summary>
        public static string Full(DateTime d) => d.ToString("yyyy-MM-dd HH:mm:ss", Inv);
        /// <summary>ISO 8601 (yyyy-MM-ddTHH:mm:ss) — لأعمدة CreatedAt</summary>
        public static string Iso(DateTime d) => d.ToString("s", Inv);
        public static string NowIso() => Iso(DateTime.Now);
        public static string NowFull() => Full(DateTime.Now);

        // ===== صيغ العرض والتقارير وأسماء الملفات =====
        /// <summary>yyyy-MM-dd HH:mm</summary>
        public static string Minutes(DateTime d) => d.ToString("yyyy-MM-dd HH:mm", Inv);
        /// <summary>yyyy/MM/dd</summary>
        public static string Slash(DateTime d) => d.ToString("yyyy/MM/dd", Inv);
        /// <summary>yyyy/MM/dd HH:mm</summary>
        public static string SlashMinutes(DateTime d) => d.ToString("yyyy/MM/dd HH:mm", Inv);
        /// <summary>yyyyMMdd</summary>
        public static string Compact(DateTime d) => d.ToString("yyyyMMdd", Inv);
        /// <summary>yyyyMMddHHmmss</summary>
        public static string CompactFull(DateTime d) => d.ToString("yyyyMMddHHmmss", Inv);
        /// <summary>yyyyMMdd_HHmmss (أسماء الملفات)</summary>
        public static string Stamp(DateTime d) => d.ToString("yyyyMMdd_HHmmss", Inv);
        /// <summary>yyyyMMdd-HHmmss</summary>
        public static string DashStamp(DateTime d) => d.ToString("yyyyMMdd-HHmmss", Inv);
        /// <summary>HH:mm:ss</summary>
        public static string Time(DateTime d) => d.ToString("HH:mm:ss", Inv);
        /// <summary>yyyy-MM-dd HH:mm:ss.fff (سجلات الأخطاء)</summary>
        public static string Millis(DateTime d) => d.ToString("yyyy-MM-dd HH:mm:ss.fff", Inv);

        // ===== القراءة =====
        /// <summary>قراءة تاريخ مخزّن (ISO أو yyyy-MM-dd [HH:mm:ss]) بثقافة ثابتة. يرمي استثناء لو النص مش تاريخ.</summary>
        public static DateTime Parse(string? text) =>
            DateTime.Parse(text ?? string.Empty, Inv, DateTimeStyles.None);

        public static bool TryParse(string? text, out DateTime result) =>
            DateTime.TryParse(text, Inv, DateTimeStyles.None, out result);
    }
}
