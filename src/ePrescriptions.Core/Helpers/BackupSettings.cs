using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// إعدادات النسخ الاحتياطي. بتتخزّن في ملف مستقل (backup_settings.ini) جنب القاعدة وليس جواها،
    /// عشان استعادة نسخة قديمة من القاعدة ما ترجّعش إعدادات النسخ (مسار القرص الخارجي/عدد الاحتفاظ) لقيم قديمة،
    /// ولأن مسار النسخ لازم يكون معروف حتى لو القاعدة نفسها تالفة.
    /// </summary>
    public class BackupSettings
    {
        public const int DefaultRetentionCount = 14;
        public const int MinRetentionCount = 1;

        /// <summary>مجلد النسخ الأساسي (فاضي = المجلد الافتراضي في ProgramData)</summary>
        public string PrimaryFolder { get; set; } = string.Empty;

        /// <summary>مجلد/قرص خارجي منفصل (USB مثلاً) لنسخة ثانية. فاضي = معطّل</summary>
        public string ExternalFolder { get; set; } = string.Empty;

        /// <summary>عدد النسخ الأحدث المحتفظ بها في كل مجلد؛ الأقدم يُحذف تلقائياً</summary>
        public int RetentionCount { get; set; } = DefaultRetentionCount;

        /// <summary>أخذ نسخة تلقائية بعد إغلاق كل وردية</summary>
        public bool AutoBackupAfterShift { get; set; } = true;

        public int EffectiveRetention => RetentionCount < MinRetentionCount ? MinRetentionCount : RetentionCount;

        // ===== خطوة 41: الجدولة اليومية واختبار الاستعادة الدوري =====
        public const string DefaultDailyBackupTime = "22:00";
        public const int DefaultRestoreTestEveryDays = 7;
        public const int MinRestoreTestEveryDays = 1;

        /// <summary>نسخة احتياطية يومية مضمونة (لو ما اتأخدتش نسخة بعد ميعاد اليوم، بتتاخد أول ما البرنامج يشتغل)</summary>
        public bool DailyBackupEnabled { get; set; } = true;

        /// <summary>ميعاد النسخة اليومية بصيغة HH:mm (توقيت الجهاز)</summary>
        public string DailyBackupTime { get; set; } = DefaultDailyBackupTime;

        /// <summary>اختبار استعادة دوري: فتح أحدث نسخة في قاعدة مؤقتة والتأكد من سلامتها</summary>
        public bool RestoreTestEnabled { get; set; } = true;

        /// <summary>كل كام يوم يتعمل اختبار الاستعادة</summary>
        public int RestoreTestEveryDays { get; set; } = DefaultRestoreTestEveryDays;

        public int EffectiveRestoreTestEveryDays => RestoreTestEveryDays < MinRestoreTestEveryDays ? MinRestoreTestEveryDays : RestoreTestEveryDays;

        /// <summary>ميعاد النسخة اليومية كفترة زمنية؛ قيمة غير صالحة ترجع للافتراضي</summary>
        public TimeSpan DailyBackupTimeOfDay
        {
            get
            {
                if (TimeSpan.TryParseExact((DailyBackupTime ?? string.Empty).Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var t)
                    && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                {
                    return t;
                }
                return new TimeSpan(22, 0, 0);
            }
        }
    }

    public static class BackupSettingsStore
    {
        public const string FileName = "backup_settings.ini";

        public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, FileName);

        public static BackupSettings Load(string? path = null)
        {
            var s = new BackupSettings();
            path ??= DefaultPath;
            try
            {
                if (!File.Exists(path)) return s;
                foreach (var raw in File.ReadAllLines(path))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string val = raw.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "PrimaryFolder": s.PrimaryFolder = val; break;
                        case "ExternalFolder": s.ExternalFolder = val; break;
                        case "RetentionCount":
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) s.RetentionCount = n;
                            break;
                        case "AutoBackupAfterShift":
                            if (bool.TryParse(val, out bool b)) s.AutoBackupAfterShift = b;
                            break;
                        case "DailyBackupEnabled":
                            if (bool.TryParse(val, out bool d)) s.DailyBackupEnabled = d;
                            break;
                        case "DailyBackupTime":
                            if (!string.IsNullOrWhiteSpace(val)) s.DailyBackupTime = val;
                            break;
                        case "RestoreTestEnabled":
                            if (bool.TryParse(val, out bool rt)) s.RestoreTestEnabled = rt;
                            break;
                        case "RestoreTestEveryDays":
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rd)) s.RestoreTestEveryDays = rd;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                // ملف إعدادات تالف لا يوقف البرنامج: نرجع للقيم الافتراضية
                Logger.LogException(ex, "BackupSettingsStore.Load");
            }
            return s;
        }

        public static void Save(BackupSettings s, string? path = null)
        {
            path ??= DefaultPath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) AppPaths.EnsureDirectory(dir!);
            var lines = new List<string>
            {
                "# إعدادات النسخ الاحتياطي (ملف مستقل عن قاعدة البيانات)",
                "PrimaryFolder=" + (s.PrimaryFolder ?? string.Empty),
                "ExternalFolder=" + (s.ExternalFolder ?? string.Empty),
                "RetentionCount=" + s.EffectiveRetention.ToString(CultureInfo.InvariantCulture),
                "AutoBackupAfterShift=" + s.AutoBackupAfterShift.ToString(CultureInfo.InvariantCulture),
                "DailyBackupEnabled=" + s.DailyBackupEnabled.ToString(CultureInfo.InvariantCulture),
                "DailyBackupTime=" + s.DailyBackupTimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                "RestoreTestEnabled=" + s.RestoreTestEnabled.ToString(CultureInfo.InvariantCulture),
                "RestoreTestEveryDays=" + s.EffectiveRestoreTestEveryDays.ToString(CultureInfo.InvariantCulture)
            };
            string tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
