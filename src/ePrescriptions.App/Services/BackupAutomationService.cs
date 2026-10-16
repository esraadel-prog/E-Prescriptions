using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ePrescriptions.Core.Helpers;

namespace ePrescriptions.App.Services
{
    /// <summary>نتيجة جولة أتمتة واحدة (ممكن ما يحصلش فيها أي حاجة لو مفيش حاجة مستحقة)</summary>
    public class BackupAutomationResult
    {
        public bool BackupAttempted { get; set; }
        public BackupRunResult? Backup { get; set; }
        public bool RestoreTestAttempted { get; set; }
        public BackupVerifyResult? RestoreTest { get; set; }
        /// <summary>true لو الجولة اتجاهلت لأن جولة تانية شغّالة دلوقتي</summary>
        public bool Skipped { get; set; }
        /// <summary>تنبيهات الصحة الحالية بعد الجولة (فاضية = سليم)</summary>
        public IReadOnlyList<string> Alerts { get; set; } = new List<string>();
    }

    public interface IBackupAutomationService
    {
        /// <summary>
        /// خطوة 41: لو النسخة اليومية مستحقة بتاخدها، ولو اختبار الاستعادة مستحق بيفتح أحدث نسخة في قاعدة مؤقتة.
        /// بتسجّل الحالة وبترجّع التنبيهات. لا ترمي استثناءات. مش بتشتغل مرتين في نفس الوقت.
        /// </summary>
        BackupAutomationResult RunDueTasks(BackupSettings settings, DateTime now);

        Task<BackupAutomationResult> RunDueTasksAsync(BackupSettings settings, DateTime now);

        /// <summary>اختبار استعادة فوري على أحدث نسخة (زر يدوي)، بغض النظر عن الجدولة. بيسجّل النتيجة.</summary>
        BackupVerifyResult RunRestoreTestNow(BackupSettings settings, DateTime now);

        Task<BackupVerifyResult> RunRestoreTestNowAsync(BackupSettings settings, DateTime now);

        /// <summary>تنبيهات الصحة الحالية من ملف الحالة بدون تشغيل أي شيء</summary>
        IReadOnlyList<string> GetAlerts(BackupSettings settings, DateTime now);
    }

    public class BackupAutomationService : IBackupAutomationService
    {
        private readonly IBackupService _backup;
        private readonly BackupStatusStore _store;
        private int _running;

        public BackupAutomationService(IBackupService backup, BackupStatusStore store)
        {
            _backup = backup;
            _store = store;
        }

        public IReadOnlyList<string> GetAlerts(BackupSettings settings, DateTime now)
        {
            try { return BackupHealth.Evaluate(now, _store.Load(), settings); }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupAutomationService.GetAlerts");
                return new List<string>();
            }
        }

        public BackupAutomationResult RunDueTasks(BackupSettings settings, DateTime now)
        {
            var result = new BackupAutomationResult();

            // جولة واحدة في المرة (التايمر + بعد الوردية + بدء التشغيل ممكن يتقابلوا)
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                result.Skipped = true;
                result.Alerts = GetAlerts(settings, now);
                return result;
            }

            try
            {
                var status = _store.Load();

                if (BackupScheduler.IsDailyBackupDue(now, status, settings))
                {
                    result.BackupAttempted = true;
                    // RunConfiguredBackup بيسجّل النجاح/الفشل بنفسه في ملف الحالة
                    result.Backup = _backup.RunConfiguredBackup(settings);
                    status = _store.Load();
                }

                // اختبار الاستعادة بعد النسخة (عشان يختبر الأحدث). لو النسخة فشلت دلوقتي ما نختبرش ملف قديم.
                bool backupJustFailed = result.Backup != null && !result.Backup.Success;
                if (!backupJustFailed && BackupScheduler.IsRestoreTestDue(now, status, settings))
                {
                    string? latest = _backup.FindLatestBackup(settings);
                    if (latest != null)
                    {
                        result.RestoreTestAttempted = true;
                        result.RestoreTest = VerifyAndRecord(latest, now);
                    }
                    // مفيش أي نسخة لسه: مفيش حاجة تتختبر. النسخة اليومية المستحقة هتعملها وتتختبر في الجولة اللي بعدها.
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupAutomationService.RunDueTasks");
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }

            result.Alerts = GetAlerts(settings, now);
            return result;
        }

        public Task<BackupAutomationResult> RunDueTasksAsync(BackupSettings settings, DateTime now)
        {
            return Task.Run(() => RunDueTasks(settings, now));
        }

        public BackupVerifyResult RunRestoreTestNow(BackupSettings settings, DateTime now)
        {
            try
            {
                string? latest = _backup.FindLatestBackup(settings);
                if (latest == null)
                {
                    return new BackupVerifyResult { Success = false, Message = "لا توجد نسخة احتياطية لاختبارها. خذ نسخة أولاً." };
                }
                return VerifyAndRecord(latest, now);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupAutomationService.RunRestoreTestNow");
                return new BackupVerifyResult { Success = false, Message = "تعذر اختبار الاستعادة: " + ex.Message };
            }
        }

        public Task<BackupVerifyResult> RunRestoreTestNowAsync(BackupSettings settings, DateTime now)
        {
            return Task.Run(() => RunRestoreTestNow(settings, now));
        }

        private BackupVerifyResult VerifyAndRecord(string backupFile, DateTime now)
        {
            var verify = _backup.VerifyBackupRestorable(backupFile);
            if (verify.Success) _store.RecordRestoreTestSuccess(now, backupFile);
            else _store.RecordRestoreTestFailure(now, backupFile, verify.Message);
            return verify;
        }
    }
}
