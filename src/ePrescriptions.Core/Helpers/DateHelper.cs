using System;
using System.Globalization;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// أدوات التعامل مع التواريخ الميلادية والهجرية باستخدام تقويم أم القرى الرسمي (UmAlQuraCalendar)
    /// مع ضمان استخدام الأرقام اللاتينية وعدم التأثر بإعدادات لغة نظام التشغيل.
    /// </summary>
    public static class DateHelper
    {
        private static readonly UmAlQuraCalendar HijriCal = new();
        private static readonly CultureInfo ArSaCulture;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static DateHelper()
        {
            ArSaCulture = new CultureInfo("ar-SA", false);
            // إجبار استخدام الأرقام اللاتينية (0123456789) حتى مع واجهة عربية
            ArSaCulture.NumberFormat.DigitSubstitution = DigitShapes.None;
        }

        /// <summary>
        /// الحصول على التاريخ الهجري الحالي بتقويم أم القرى بصيغة YYYY/MM/DD هـ
        /// </summary>
        public static string ToHijriDateString(DateTime date)
        {
            try
            {
                int year = HijriCal.GetYear(date);
                int month = HijriCal.GetMonth(date);
                int day = HijriCal.GetDayOfMonth(date);
                return $"{year:D4}/{month:D2}/{day:D2} هـ";
            }
            catch
            {
                return "-";
            }
        }

        /// <summary>
        /// تنسيق التاريخ الميلادي بصيغة YYYY-MM-DD
        /// </summary>
        public static string ToGregorianDateString(DateTime date)
        {
            return date.ToString("yyyy-MM-dd", Invariant);
        }

        /// <summary>
        /// تنسيق الوقت بصيغة HH:mm:ss
        /// </summary>
        public static string ToTimeString(DateTime date)
        {
            return date.ToString("HH:mm:ss", Invariant);
        }

        /// <summary>
        /// اسم اليوم باللغة العربية
        /// </summary>
        public static string GetArabicDayName(DateTime date)
        {
            return date.DayOfWeek switch
            {
                DayOfWeek.Saturday => "السبت",
                DayOfWeek.Sunday => "الأحد",
                DayOfWeek.Monday => "الإثنين",
                DayOfWeek.Tuesday => "الثلاثاء",
                DayOfWeek.Wednesday => "الأربعاء",
                DayOfWeek.Thursday => "الخميس",
                DayOfWeek.Friday => "الجمعة",
                _ => ""
            };
        }

        /// <summary>
        /// شريط التاريخ والوقت الكامل للشاشة الرئيسية
        /// </summary>
        public static string FormatFullBanner(DateTime date)
        {
            string dayName = GetArabicDayName(date);
            string greg = ToGregorianDateString(date);
            string hijri = ToHijriDateString(date);
            string time = ToTimeString(date);
            return $"{dayName} | {greg} م ({hijri}) | {time}";
        }
    }
}
