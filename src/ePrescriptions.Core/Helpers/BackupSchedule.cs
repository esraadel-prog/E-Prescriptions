using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// خطوة 41: حالة النسخ الاحتياطي (آخر نجاح/فشل للنسخ ولاختبار الاستعادة).
    /// بتتخزّن في ملف مستقل (backup_status.ini) جنب القاعدة وليس جواها، لنفس سبب backup_settings.ini:
    /// استعادة نسخة قديمة ما ترجّعش الحالة لورا، ولازم تفضل مقروءة حتى لو القاعدة تالفة.
    /// كل التواريخ بتوقيت الجهاز المحلي وبصيغة ثابتة الثقافة.
    /// </summary>
    public class BackupStatus
    {
        public DateTime? LastBackupSuccess { get; set; }
        public string LastBackupFile { get; set; } = string.Empty;
        public DateTime? LastBackupFailure { get; set; }
        public string LastBackupFailureMessage { get; set; } = string.Empty;

        /// <summary>تحذير النسخة الخارجية (النسخة الأساسية نجحت والنسخة على القرص الخارجي فشلت). فاضي = سليم</summary>
        public string ExternalWarning { get; set; } = string.Empty;

        public DateTime? LastRestoreTestSuccess { get; set; }
        public string LastRestoreTestFile { get; set; } = string.Empty;
        public DateTime? LastRestoreTestFailure { get; set; }
        public string LastRestoreTestFailureMessage { get; set; } = string.Empty;

        /// <summary>فشل النسخ لسه ما اتصلحش (آخر فشل أحدث من آخر نجاح)</summary>
        public bool HasUnresolvedBackupFailure =>
            LastBackupFailure.HasValue && (!LastBackupSuccess.HasValue || LastBackupFailure.Value > LastBackupSuccess.Value);

        /// <summary>فشل اختبار الاستعادة لسه ما اتصلحش</summary>
        public bool HasUnresolvedRestoreTestFailure =>
            LastRestoreTestFailure.HasValue && (!LastRestoreTestSuccess.HasValue || LastRestoreTestFailure.Value > LastRestoreTestSuccess.Value);
    }

    /// <summary>قراءة/كتابة حالة النسخ. آمنة للاستدعاء من أكتر من خيط (الواجهة + الخيط الخلفي).</summary>
    public class BackupStatusStore
    {
        public const string FileName = "backup_status.ini";
        private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss";

        private static readonly object Gate = new object();
        private readonly string _path;

        public BackupStatusStore(string? path = null)
        {
            _path = path ?? DefaultPath;
        }

        public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, FileName);

        public string FilePath => _path;

        public BackupStatus Load()
        {
            lock (Gate) { return LoadUnlocked(); }
        }

        private BackupStatus LoadUnlocked()
        {
            var st = new BackupStatus();
            try
            {
                if (!File.Exists(_path)) return st;
                foreach (var raw in File.ReadAllLines(_path))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string val = raw.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "LastBackupSuccess": st.LastBackupSuccess = ParseDate(val); break;
                        case "LastBackupFile": st.LastBackupFile = val; break;
                        case "LastBackupFailure": st.LastBackupFailure = ParseDate(val); break;
                        case "LastBackupFailureMessage": st.LastBackupFailureMessage = val; break;
                        case "ExternalWarning": st.ExternalWarning = val; break;
                        case "LastRestoreTestSuccess": st.LastRestoreTestSuccess = ParseDate(val); break;
                        case "LastRestoreTestFile": st.LastRestoreTestFile = val; break;
                        case "LastRestoreTestFailure": st.LastRestoreTestFailure = ParseDate(val); break;
                        case "LastRestoreTestFailureMessage": st.LastRestoreTestFailureMessage = val; break;
                    }
                }
            }
            catch (Exception ex)
            {
                // ملف حالة تالف ما يوقفش البرنامج: نرجع حالة فاضية
                Logger.LogException(ex, "BackupStatusStore.Load");
            }
            return st;
        }

        private void SaveUnlocked(BackupStatus st)
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) AppPaths.EnsureDirectory(dir!);
            var lines = new List<string>
            {
                "# حالة النسخ الاحتياطي (ملف مستقل عن قاعدة البيانات) - لا يُعدَّل يدوياً",
                "LastBackupSuccess=" + FormatDate(st.LastBackupSuccess),
                "LastBackupFile=" + OneLine(st.LastBackupFile),
                "LastBackupFailure=" + FormatDate(st.LastBackupFailure),
                "LastBackupFailureMessage=" + OneLine(st.LastBackupFailureMessage),
                "ExternalWarning=" + OneLine(st.ExternalWarning),
                "LastRestoreTestSuccess=" + FormatDate(st.LastRestoreTestSuccess),
                "LastRestoreTestFile=" + OneLine(st.LastRestoreTestFile),
                "LastRestoreTestFailure=" + FormatDate(st.LastRestoreTestFailure),
                "LastRestoreTestFailureMessage=" + OneLine(st.LastRestoreTestFailureMessage)
            };
            string tmp = _path + ".tmp";
            File.WriteAllLines(tmp, lines, new System.Text.UTF8Encoding(false));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }

        /// <summary>تحميل → تعديل → حفظ كوحدة واحدة. فشل الحفظ بيتسجّل ولا يرمي (النسخة نفسها أهم من تسجيل حالتها).</summary>
        private void Update(Action<BackupStatus> change, string context)
        {
            lock (Gate)
            {
                try
                {
                    var st = LoadUnlocked();
                    change(st);
                    SaveUnlocked(st);
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "BackupStatusStore." + context);
                }
            }
        }

        public void RecordBackupSuccess(DateTime now, string backupFile, string externalWarning)
        {
            Update(st =>
            {
                st.LastBackupSuccess = now;
                st.LastBackupFile = backupFile ?? string.Empty;
                st.ExternalWarning = externalWarning ?? string.Empty;
            }, "RecordBackupSuccess");
        }

        public void RecordBackupFailure(DateTime now, string message)
        {
            Update(st =>
            {
                st.LastBackupFailure = now;
                st.LastBackupFailureMessage = message ?? string.Empty;
            }, "RecordBackupFailure");
        }

        public void RecordRestoreTestSuccess(DateTime now, string backupFile)
        {
            Update(st =>
            {
                st.LastRestoreTestSuccess = now;
                st.LastRestoreTestFile = backupFile ?? string.Empty;
            }, "RecordRestoreTestSuccess");
        }

        public void RecordRestoreTestFailure(DateTime now, string backupFile, string message)
        {
            Update(st =>
            {
                st.LastRestoreTestFailure = now;
                st.LastRestoreTestFile = backupFile ?? string.Empty;
                st.LastRestoreTestFailureMessage = message ?? string.Empty;
            }, "RecordRestoreTestFailure");
        }

        private static string FormatDate(DateTime? d) =>
            d.HasValue ? d.Value.ToString(DateFormat, CultureInfo.InvariantCulture) : string.Empty;

        private static DateTime? ParseDate(string v) =>
            DateTime.TryParseExact(v, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;

        private static string OneLine(string? s) =>
            (s ?? string.Empty).Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();
    }

    /// <summary>
    /// قرارات الجدولة (منطق بحت بدون ملفات/ساعة النظام عشان يتختبر): امتى نأخذ النسخة اليومية، وامتى نختبر الاستعادة.
    /// </summary>
    public static class BackupScheduler
    {
        /// <summary>بعد فشل نسخة: أقل فاصل قبل إعادة المحاولة (عشان ما نفضلش نضرب القاعدة كل دقيقة)</summary>
        public static readonly TimeSpan BackupRetryDelay = TimeSpan.FromMinutes(30);

        /// <summary>بعد فشل اختبار استعادة: أقل فاصل قبل إعادة المحاولة</summary>
        public static readonly TimeSpan RestoreTestRetryDelay = TimeSpan.FromHours(6);

        /// <summary>آخر موعد نسخة يومية كان المفروض يحصل: النهارده في الميعاد لو عدّى، وإلا إمبارح في الميعاد</summary>
        public static DateTime LastScheduledOccurrence(DateTime now, TimeSpan timeOfDay)
        {
            DateTime today = now.Date + timeOfDay;
            return now >= today ? today : today.AddDays(-1);
        }

        /// <summary>
        /// النسخة اليومية مستحقة لو آخر موعد مجدول عدّى ولسه ما اتأخدتش بعده أي نسخة ناجحة (من أي مصدر:
        /// بعد وردية أو يدوية أو مجدولة). ده بيغطي كمان الجهاز اللي كان مقفول وقت الميعاد (تعويض عند التشغيل).
        /// </summary>
        public static bool IsDailyBackupDue(DateTime now, BackupStatus status, BackupSettings settings)
        {
            if (!settings.DailyBackupEnabled) return false;

            if (status.HasUnresolvedBackupFailure && status.LastBackupFailure.HasValue
                && now - status.LastBackupFailure.Value < BackupRetryDelay)
            {
                return false;
            }

            DateTime occurrence = LastScheduledOccurrence(now, settings.DailyBackupTimeOfDay);
            if (!status.LastBackupSuccess.HasValue) return true;
            return status.LastBackupSuccess.Value < occurrence;
        }

        /// <summary>اختبار الاستعادة مستحق لو ما اتعملش قبل كده، أو عدّى عدد الأيام المحدد على آخر نجاح</summary>
        public static bool IsRestoreTestDue(DateTime now, BackupStatus status, BackupSettings settings)
        {
            if (!settings.RestoreTestEnabled) return false;

            if (status.HasUnresolvedRestoreTestFailure && status.LastRestoreTestFailure.HasValue
                && now - status.LastRestoreTestFailure.Value < RestoreTestRetryDelay)
            {
                return false;
            }

            if (!status.LastRestoreTestSuccess.HasValue) return true;
            return now - status.LastRestoreTestSuccess.Value >= TimeSpan.FromDays(settings.EffectiveRestoreTestEveryDays);
        }
    }

    /// <summary>تقييم صحة النسخ الاحتياطي وتحويله لتنبيهات مقروءة للمستخدم (فاضية = كل حاجة سليمة)</summary>
    public static class BackupHealth
    {
        /// <summary>لو آخر نسخة ناجحة أقدم من كده والنسخ التلقائي شغّال = غالباً الجدولة واقفة</summary>
        public const int StaleAfterDays = 2;

        public static IReadOnlyList<string> Evaluate(DateTime now, BackupStatus status, BackupSettings settings)
        {
            var alerts = new List<string>();

            if (status.HasUnresolvedBackupFailure)
            {
                alerts.Add("⚠ فشلت آخر نسخة احتياطية (" + Stamp(status.LastBackupFailure) + ")"
                    + Reason(status.LastBackupFailureMessage) + ". لا تعتمد على نسخ قديمة؛ راجع الإعدادات.");
            }

            if (!string.IsNullOrWhiteSpace(status.ExternalWarning))
            {
                alerts.Add("⚠ النسخة على القرص الخارجي لم تكتمل: " + status.ExternalWarning);
            }

            if (status.HasUnresolvedRestoreTestFailure)
            {
                alerts.Add("⚠ فشل اختبار استعادة النسخة الاحتياطية (" + Stamp(status.LastRestoreTestFailure) + ")"
                    + Reason(status.LastRestoreTestFailureMessage) + ". النسخة قد لا تكون صالحة للاستعادة.");
            }

            bool autoOn = settings.DailyBackupEnabled || settings.AutoBackupAfterShift;
            if (autoOn && status.LastBackupSuccess.HasValue
                && now - status.LastBackupSuccess.Value > TimeSpan.FromDays(StaleAfterDays)
                && !status.HasUnresolvedBackupFailure)
            {
                int days = (int)(now - status.LastBackupSuccess.Value).TotalDays;
                alerts.Add("⚠ لم تُؤخذ نسخة احتياطية ناجحة منذ " + days.ToString(CultureInfo.InvariantCulture) + " يوم.");
            }

            return alerts;
        }

        private static string Stamp(DateTime? d) =>
            d.HasValue ? d.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "-";

        private static string Reason(string? message) =>
            string.IsNullOrWhiteSpace(message) ? string.Empty : ": " + message!.Trim();
    }
}
