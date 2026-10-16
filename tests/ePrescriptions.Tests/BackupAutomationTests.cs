using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using ePrescriptions.App.Services;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 41 (جزء أ): جدولة النسخ اليومي + اختبار استعادة دوري + حالة النسخ + تنبيه الفشل</summary>
    public class BackupAutomationTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ep_bkauto_" + Guid.NewGuid().ToString("N"));
        private readonly string _dbPath;
        private readonly DatabaseContext _db;
        private readonly BackupStatusStore _store;
        private readonly BackupService _svc;
        private readonly BackupAutomationService _auto;
        private DateTime _now = new DateTime(2026, 10, 14, 23, 30, 0);
        private readonly BackupSettings _settings;

        public BackupAutomationTests()
        {
            Directory.CreateDirectory(_root);
            _dbPath = Path.Combine(_root, "live.db");
            _db = new DatabaseContext(_dbPath);
            _db.InitializeAndMigrate(seedData: false);
            _db.CreateOwnerAccount("Test@Owner1234");
            _store = new BackupStatusStore(Path.Combine(_root, "backup_status.ini"));
            _svc = new BackupService(_db, _store, () => _now);
            _auto = new BackupAutomationService(_svc, _store);
            _settings = new BackupSettings { PrimaryFolder = Path.Combine(_root, "primary"), DailyBackupTime = "22:00" };
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_root, true); } catch { }
        }

        private int BackupCount() =>
            Directory.Exists(_settings.PrimaryFolder) ? Directory.GetFiles(_settings.PrimaryFolder, BackupService.BackupFilePattern).Length : 0;

        private static BackupStatus Status(Action<BackupStatus>? f = null)
        {
            var s = new BackupStatus();
            f?.Invoke(s);
            return s;
        }

        private static string[] RestoreTestTempDirs() =>
            Directory.GetDirectories(Path.GetTempPath(), "ep_restoretest_*");

        // ===================== الإعدادات =====================

        [Fact]
        public void Settings_ShouldRoundTripScheduleFields_AndFallBackOnInvalidTime()
        {
            string path = Path.Combine(_root, "s.ini");
            BackupSettingsStore.Save(new BackupSettings
            {
                DailyBackupEnabled = false, DailyBackupTime = "07:45", RestoreTestEnabled = false, RestoreTestEveryDays = 3
            }, path);

            var loaded = BackupSettingsStore.Load(path);
            loaded.DailyBackupEnabled.Should().BeFalse();
            loaded.DailyBackupTimeOfDay.Should().Be(new TimeSpan(7, 45, 0));
            loaded.RestoreTestEnabled.Should().BeFalse();
            loaded.RestoreTestEveryDays.Should().Be(3);

            new BackupSettings { DailyBackupTime = "25:99" }.DailyBackupTimeOfDay.Should().Be(new TimeSpan(22, 0, 0));
            new BackupSettings { DailyBackupTime = "abc" }.DailyBackupTimeOfDay.Should().Be(new TimeSpan(22, 0, 0));
            new BackupSettings { RestoreTestEveryDays = 0 }.EffectiveRestoreTestEveryDays.Should().Be(1);

            // ملف إعدادات قديم (بدون مفاتيح الخطوة 41) يتحمّل بالقيم الافتراضية
            File.WriteAllLines(path, new[] { "RetentionCount=5", "AutoBackupAfterShift=False" });
            var old = BackupSettingsStore.Load(path);
            old.RetentionCount.Should().Be(5);
            old.DailyBackupEnabled.Should().BeTrue();
            old.RestoreTestEnabled.Should().BeTrue();
            old.RestoreTestEveryDays.Should().Be(BackupSettings.DefaultRestoreTestEveryDays);
        }

        // ===================== الجدولة (منطق بحت) =====================

        [Fact]
        public void LastScheduledOccurrence_ShouldBeTodayIfPassed_OtherwiseYesterday()
        {
            var t = new TimeSpan(22, 0, 0);
            BackupScheduler.LastScheduledOccurrence(new DateTime(2026, 10, 14, 23, 0, 0), t).Should().Be(new DateTime(2026, 10, 14, 22, 0, 0));
            BackupScheduler.LastScheduledOccurrence(new DateTime(2026, 10, 14, 9, 0, 0), t).Should().Be(new DateTime(2026, 10, 13, 22, 0, 0));
            BackupScheduler.LastScheduledOccurrence(new DateTime(2026, 10, 14, 22, 0, 0), t).Should().Be(new DateTime(2026, 10, 14, 22, 0, 0));
        }

        [Fact]
        public void DailyBackup_ShouldBeDue_WhenNeverBackedUp()
        {
            BackupScheduler.IsDailyBackupDue(_now, Status(), _settings).Should().BeTrue();
        }

        [Fact]
        public void DailyBackup_ShouldNotBeDue_WhenSuccessfulBackupAlreadyTakenAfterLastOccurrence()
        {
            // نسخة بعد وردية الساعة 22:30 بتغطي ميعاد 22:00 (من أي مصدر)
            var st = Status(s => s.LastBackupSuccess = new DateTime(2026, 10, 14, 22, 30, 0));
            BackupScheduler.IsDailyBackupDue(new DateTime(2026, 10, 14, 23, 30, 0), st, _settings).Should().BeFalse();
        }

        [Fact]
        public void DailyBackup_ShouldBeDue_AfterScheduledTimePasses_AndNoBackupSince()
        {
            var st = Status(s => s.LastBackupSuccess = new DateTime(2026, 10, 14, 15, 0, 0));
            BackupScheduler.IsDailyBackupDue(new DateTime(2026, 10, 14, 21, 59, 0), st, _settings).Should().BeFalse(); // الميعاد لسه ما جاش
            BackupScheduler.IsDailyBackupDue(new DateTime(2026, 10, 14, 22, 0, 0), st, _settings).Should().BeTrue();
        }

        [Fact]
        public void DailyBackup_ShouldCatchUp_WhenComputerWasOffAtScheduledTime()
        {
            // آخر نسخة يوم 12 ، والجهاز اتفتح يوم 14 الصبح: ميعاد يوم 13 فات ← مستحقة فوراً
            var st = Status(s => s.LastBackupSuccess = new DateTime(2026, 10, 12, 22, 5, 0));
            BackupScheduler.IsDailyBackupDue(new DateTime(2026, 10, 14, 8, 0, 0), st, _settings).Should().BeTrue();
        }

        [Fact]
        public void DailyBackup_ShouldBeOff_WhenDisabled()
        {
            _settings.DailyBackupEnabled = false;
            BackupScheduler.IsDailyBackupDue(_now, Status(), _settings).Should().BeFalse();
        }

        [Fact]
        public void DailyBackup_ShouldNotRetryImmediatelyAfterFailure_ButRetriesAfterDelay()
        {
            var failedAt = new DateTime(2026, 10, 14, 22, 10, 0);
            var st = Status(s => s.LastBackupFailure = failedAt);
            BackupScheduler.IsDailyBackupDue(failedAt.AddMinutes(5), st, _settings).Should().BeFalse();
            BackupScheduler.IsDailyBackupDue(failedAt.AddMinutes(31), st, _settings).Should().BeTrue();
        }

        [Fact]
        public void RestoreTest_ShouldBeDue_WhenNeverRun_OrAfterConfiguredDays_AndNotBefore()
        {
            _settings.RestoreTestEveryDays = 7;
            BackupScheduler.IsRestoreTestDue(_now, Status(), _settings).Should().BeTrue();

            var st = Status(s => s.LastRestoreTestSuccess = _now.AddDays(-6));
            BackupScheduler.IsRestoreTestDue(_now, st, _settings).Should().BeFalse();
            st.LastRestoreTestSuccess = _now.AddDays(-7);
            BackupScheduler.IsRestoreTestDue(_now, st, _settings).Should().BeTrue();

            _settings.RestoreTestEnabled = false;
            BackupScheduler.IsRestoreTestDue(_now, Status(), _settings).Should().BeFalse();
        }

        [Fact]
        public void RestoreTest_ShouldWaitBeforeRetryingAfterFailure()
        {
            var failedAt = _now.AddHours(-1);
            var st = Status(s => { s.LastRestoreTestSuccess = _now.AddDays(-30); s.LastRestoreTestFailure = failedAt; });
            BackupScheduler.IsRestoreTestDue(_now, st, _settings).Should().BeFalse();
            BackupScheduler.IsRestoreTestDue(failedAt.AddHours(7), st, _settings).Should().BeTrue();
        }

        // ===================== تنبيهات الصحة =====================

        [Fact]
        public void Health_ShouldBeEmpty_WhenEverythingIsFine()
        {
            var st = Status(s => { s.LastBackupSuccess = _now.AddHours(-2); s.LastRestoreTestSuccess = _now.AddDays(-1); });
            BackupHealth.Evaluate(_now, st, _settings).Should().BeEmpty();
        }

        [Fact]
        public void Health_ShouldAlertOnUnresolvedBackupFailure_AndClearWhenLaterSuccess()
        {
            var st = Status(s =>
            {
                s.LastBackupSuccess = _now.AddDays(-1);
                s.LastBackupFailure = _now.AddHours(-1);
                s.LastBackupFailureMessage = "القرص ممتلئ";
            });
            var alerts = BackupHealth.Evaluate(_now, st, _settings);
            alerts.Should().HaveCount(1);
            alerts[0].Should().Contain("فشلت");
            alerts[0].Should().Contain("القرص ممتلئ");

            st.LastBackupSuccess = _now; // نجاح أحدث يلغي التنبيه
            BackupHealth.Evaluate(_now, st, _settings).Should().BeEmpty();
        }

        [Fact]
        public void Health_ShouldAlertOnRestoreTestFailure_ExternalWarning_AndStaleBackup()
        {
            var st = Status(s =>
            {
                s.LastBackupSuccess = _now.AddDays(-5);
                s.LastRestoreTestFailure = _now.AddHours(-3);
                s.LastRestoreTestFailureMessage = "ملف تالف";
                s.ExternalWarning = "القرص F: غير متصل";
            });
            var alerts = BackupHealth.Evaluate(_now, st, _settings);
            alerts.Should().HaveCount(3);
            alerts.Should().Contain(a => a.Contains("اختبار استعادة") && a.Contains("ملف تالف"));
            alerts.Should().Contain(a => a.Contains("القرص الخارجي") && a.Contains("F:"));
            alerts.Should().Contain(a => a.Contains("5 يوم"));
        }

        [Fact]
        public void Health_ShouldNotWarnAboutStaleBackup_WhenAllAutomaticBackupsAreOff()
        {
            _settings.DailyBackupEnabled = false;
            _settings.AutoBackupAfterShift = false;
            var st = Status(s => s.LastBackupSuccess = _now.AddDays(-20));
            BackupHealth.Evaluate(_now, st, _settings).Should().BeEmpty();
        }

        // ===================== ملف الحالة =====================

        [Fact]
        public void StatusStore_ShouldRoundTrip_CollapseNewlines_AndSurviveCorruptFile()
        {
            _store.RecordBackupSuccess(new DateTime(2026, 10, 14, 22, 5, 7), @"C:\b\x.db", string.Empty);
            _store.RecordBackupFailure(new DateTime(2026, 10, 14, 23, 0, 0), "سطر1\r\nسطر2");
            _store.RecordRestoreTestSuccess(new DateTime(2026, 10, 13, 1, 2, 3), @"C:\b\x.db");

            var st = new BackupStatusStore(_store.FilePath).Load();
            st.LastBackupSuccess.Should().Be(new DateTime(2026, 10, 14, 22, 5, 7));
            st.LastBackupFile.Should().Be(@"C:\b\x.db");
            st.LastBackupFailure.Should().Be(new DateTime(2026, 10, 14, 23, 0, 0));
            st.LastBackupFailureMessage.Should().Be("سطر1 سطر2");
            st.LastRestoreTestSuccess.Should().Be(new DateTime(2026, 10, 13, 1, 2, 3));
            st.HasUnresolvedBackupFailure.Should().BeTrue();

            File.WriteAllText(_store.FilePath, "garbage\n=\nLastBackupSuccess=not-a-date\n");
            var broken = _store.Load();
            broken.LastBackupSuccess.Should().BeNull();
            broken.HasUnresolvedBackupFailure.Should().BeFalse();
        }

        // ===================== اختبار الاستعادة في قاعدة مؤقتة =====================

        [Fact]
        public void Verify_ShouldPassForGoodBackup_ReportCounts_LeaveLiveAndBackupUntouched_AndRemoveTempFiles()
        {
            _svc.CreateBackup(_settings.PrimaryFolder, out string path, out _).Should().BeTrue();
            long backupSize = new FileInfo(path).Length;
            DateTime backupWrite = File.GetLastWriteTimeUtc(path);
            int dirsBefore = RestoreTestTempDirs().Length;

            var r = _svc.VerifyBackupRestorable(path);

            r.Success.Should().BeTrue(r.Message);
            r.UsersCount.Should().Be(1);   // حساب Owner
            r.BackupSchemaVersion.Should().Be(DatabaseContext.LatestSchemaVersion);
            r.Message.Should().Contain("صالحة");
            new FileInfo(path).Length.Should().Be(backupSize);
            File.GetLastWriteTimeUtc(path).Should().Be(backupWrite);
            File.Exists(path + "-wal").Should().BeFalse();
            RestoreTestTempDirs().Length.Should().Be(dirsBefore);
        }

        [Fact]
        public void Verify_ShouldFail_ForMissingFile_GarbageFile_AndNewerSchemaBackup()
        {
            _svc.VerifyBackupRestorable(Path.Combine(_root, "nope.db")).Success.Should().BeFalse();

            string garbage = Path.Combine(_root, "garbage.db");
            File.WriteAllBytes(garbage, Enumerable.Range(0, 4096).Select(i => (byte)(i * 7)).ToArray());
            var g = _svc.VerifyBackupRestorable(garbage);
            g.Success.Should().BeFalse();
            g.Message.Should().NotBeEmpty();

            _svc.CreateBackup(_settings.PrimaryFolder, out string path, out _).Should().BeTrue();
            using (var conn = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA user_version = {DatabaseContext.LatestSchemaVersion + 1};";
                cmd.ExecuteNonQuery();
            }
            var n = _svc.VerifyBackupRestorable(path);
            n.Success.Should().BeFalse();
            n.Message.Should().Contain("أحدث");
        }

        [Fact]
        public void Verify_ShouldPass_ForOlderSchemaBackup_ByApplyingMigrationsOnTempCopyOnly()
        {
            _svc.CreateBackup(_settings.PrimaryFolder, out string path, out _).Should().BeTrue();
            using (var conn = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "ALTER TABLE InvoiceItems DROP COLUMN PrescriptionNumber; ALTER TABLE InvoiceItems DROP COLUMN DoctorName; ALTER TABLE Invoices DROP COLUMN Status; ALTER TABLE Invoices DROP COLUMN CancelledAt; ALTER TABLE Invoices DROP COLUMN CancelledByUserId; ALTER TABLE Invoices DROP COLUMN CancelledByName; ALTER TABLE Invoices DROP COLUMN CancelReason; ALTER TABLE Users DROP COLUMN FailedLoginCount; ALTER TABLE Users DROP COLUMN LockedUntil; ALTER TABLE OwnerAuditLog DROP COLUMN PrevHash; ALTER TABLE OwnerAuditLog DROP COLUMN RowHash; ALTER TABLE StaffAuditLog DROP COLUMN PrevHash; ALTER TABLE StaffAuditLog DROP COLUMN RowHash; DROP INDEX IF EXISTS IX_Customers_DefaultCash; ALTER TABLE Customers DROP COLUMN IsDefaultCash; ALTER TABLE Items DROP COLUMN SearchText; DROP TABLE IF EXISTS ZakatSettings; DROP TABLE IF EXISTS PurchaseOrderItems; DROP TABLE IF EXISTS PurchaseOrders; DROP TABLE IF EXISTS InvoicePayments; DROP INDEX IF EXISTS IX_SupplierReturns_PurchaseInvoice; DROP INDEX IF EXISTS IX_SupplierReturnItems_PurchaseItem; ALTER TABLE SupplierReturns DROP COLUMN PurchaseInvoiceId; ALTER TABLE SupplierReturnItems DROP COLUMN PurchaseInvoiceItemId; ALTER TABLE PurchaseInvoiceItems DROP COLUMN VatPercent; ALTER TABLE PurchaseInvoiceItems DROP COLUMN BatchId; PRAGMA user_version = 9;";
                cmd.ExecuteNonQuery();
            }

            var r = _svc.VerifyBackupRestorable(path);

            r.Success.Should().BeTrue(r.Message);
            r.BackupSchemaVersion.Should().Be(9);
            // ملف النسخة الأصلي فضل قديم (الـ migrations اتطبّقت على النسخة المؤقتة بس)
            using var check = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False;");
            check.Open();
            using var c2 = check.CreateCommand();
            c2.CommandText = "PRAGMA user_version;";
            Convert.ToInt32(c2.ExecuteScalar()).Should().Be(9);
        }

        // ===================== تسجيل نتيجة النسخ في ملف الحالة =====================

        [Fact]
        public void BackupService_ShouldRecordSuccess_AndFailure_InStatusFile()
        {
            var ok = _svc.RunConfiguredBackup(_settings);
            ok.Success.Should().BeTrue();
            var st = _store.Load();
            st.LastBackupSuccess.Should().Be(_now);
            st.LastBackupFile.Should().Be(ok.PrimaryPath);
            st.HasUnresolvedBackupFailure.Should().BeFalse();

            // فشل: المجلد الأساسي "تحت ملف" (مش مجلد)
            string blocker = Path.Combine(_root, "iam_a_file");
            File.WriteAllText(blocker, "x");
            _now = _now.AddMinutes(10);
            var bad = _svc.RunConfiguredBackup(new BackupSettings { PrimaryFolder = Path.Combine(blocker, "sub") });
            bad.Success.Should().BeFalse();
            var st2 = _store.Load();
            st2.LastBackupFailure.Should().Be(_now);
            st2.HasUnresolvedBackupFailure.Should().BeTrue();
            st2.LastBackupFailureMessage.Should().NotBeEmpty();

            // نجاح لاحق يحل المشكلة
            _now = _now.AddMinutes(10);
            _svc.RunConfiguredBackup(_settings).Success.Should().BeTrue();
            _store.Load().HasUnresolvedBackupFailure.Should().BeFalse();
        }

        [Fact]
        public void BackupService_ShouldRecordExternalWarning_WhenExternalCopyFails_AndClearItOnNextCleanRun()
        {
            string blocker = Path.Combine(_root, "usb_is_a_file");
            File.WriteAllText(blocker, "x");
            _settings.ExternalFolder = Path.Combine(blocker, "sub");

            var r = _svc.RunConfiguredBackup(_settings);
            r.Success.Should().BeTrue();
            r.ExternalFailed.Should().BeTrue();
            _store.Load().ExternalWarning.Should().NotBeEmpty();
            _auto.GetAlerts(_settings, _now).Should().Contain(a => a.Contains("القرص الخارجي"));

            _settings.ExternalFolder = string.Empty;
            _now = _now.AddMinutes(5);
            _svc.RunConfiguredBackup(_settings).Success.Should().BeTrue();
            _store.Load().ExternalWarning.Should().BeEmpty();
            _auto.GetAlerts(_settings, _now).Should().BeEmpty();
        }

        // ===================== الأتمتة كاملة =====================

        [Fact]
        public void RunDueTasks_FirstRun_ShouldTakeBackupThenTestRestoreOnIt_AndRecordBoth()
        {
            var r = _auto.RunDueTasks(_settings, _now);

            r.BackupAttempted.Should().BeTrue();
            r.Backup!.Success.Should().BeTrue();
            r.RestoreTestAttempted.Should().BeTrue();
            r.RestoreTest!.Success.Should().BeTrue(r.RestoreTest.Message);
            r.RestoreTest.BackupPath.Should().Be(r.Backup.PrimaryPath); // اختبر النسخة اللي لسه متاخدة
            r.Alerts.Should().BeEmpty();
            BackupCount().Should().Be(1);

            var st = _store.Load();
            st.LastBackupSuccess.Should().Be(_now);
            st.LastRestoreTestSuccess.Should().Be(_now);
            st.LastRestoreTestFile.Should().Be(r.Backup.PrimaryPath);
        }

        [Fact]
        public void RunDueTasks_SecondRunSameDay_ShouldDoNothing()
        {
            _auto.RunDueTasks(_settings, _now);
            var r = _auto.RunDueTasks(_settings, _now.AddMinutes(5));

            r.BackupAttempted.Should().BeFalse();
            r.RestoreTestAttempted.Should().BeFalse();
            BackupCount().Should().Be(1);
        }

        [Fact]
        public void RunDueTasks_NextDayAfterScheduledTime_ShouldTakeAnotherBackup_ButNotRestoreTestYet()
        {
            _auto.RunDueTasks(_settings, _now);               // 14/10 23:30
            _now = new DateTime(2026, 10, 15, 22, 1, 0);      // اليوم التالي بعد الميعاد
            var r = _auto.RunDueTasks(_settings, _now);

            r.BackupAttempted.Should().BeTrue();
            r.Backup!.Success.Should().BeTrue();
            r.RestoreTestAttempted.Should().BeFalse();        // لسه ما عدّاش 7 أيام
            BackupCount().Should().Be(2);
        }

        [Fact]
        public void RunDueTasks_ShouldRunRestoreTestAgain_AfterConfiguredDays()
        {
            _settings.RestoreTestEveryDays = 3;
            _auto.RunDueTasks(_settings, _now);
            _now = new DateTime(2026, 10, 17, 23, 31, 0);     // بعد 3 أيام من اختبار 14/10 23:30
            var r = _auto.RunDueTasks(_settings, _now);

            r.RestoreTestAttempted.Should().BeTrue();
            r.RestoreTest!.Success.Should().BeTrue(r.RestoreTest.Message);
        }

        [Fact]
        public void RunDueTasks_WhenBackupFails_ShouldRaiseAlert_AndNotTestAnOldFile()
        {
            string blocker = Path.Combine(_root, "iam_a_file");
            File.WriteAllText(blocker, "x");
            var broken = new BackupSettings { PrimaryFolder = Path.Combine(blocker, "sub") };

            var r = _auto.RunDueTasks(broken, _now);

            r.BackupAttempted.Should().BeTrue();
            r.Backup!.Success.Should().BeFalse();
            r.RestoreTestAttempted.Should().BeFalse();
            r.Alerts.Should().Contain(a => a.Contains("فشلت"));

            // إعادة المحاولة بعد فاصل مع إعدادات سليمة بتصلّح الوضع وبتشيل التنبيه
            _now = _now.AddMinutes(31);
            var fixedRun = _auto.RunDueTasks(_settings, _now);
            fixedRun.Backup!.Success.Should().BeTrue();
            fixedRun.Alerts.Should().BeEmpty();
        }

        [Fact]
        public void RunDueTasks_WhenLatestBackupIsCorrupt_ShouldFailRestoreTestAndAlert()
        {
            // نسخة سليمة موجودة، وبعدين اتبوّظت (قرص تالف مثلاً)
            _svc.CreateBackup(_settings.PrimaryFolder, out string path, out _).Should().BeTrue();
            _store.RecordBackupSuccess(_now, path, string.Empty);   // النسخة اليومية مش مستحقة، فبس الاختبار هيشتغل
            File.WriteAllBytes(path, Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray());

            var r = _auto.RunDueTasks(_settings, _now.AddMinutes(1));

            r.BackupAttempted.Should().BeFalse();
            r.RestoreTestAttempted.Should().BeTrue();
            r.RestoreTest!.Success.Should().BeFalse();
            r.Alerts.Should().Contain(a => a.Contains("اختبار استعادة"));
            _store.Load().HasUnresolvedRestoreTestFailure.Should().BeTrue();
        }

        [Fact]
        public void RunDueTasks_ShouldDoNothing_WhenBothFeaturesAreDisabled()
        {
            _settings.DailyBackupEnabled = false;
            _settings.RestoreTestEnabled = false;

            var r = _auto.RunDueTasks(_settings, _now);

            r.BackupAttempted.Should().BeFalse();
            r.RestoreTestAttempted.Should().BeFalse();
            BackupCount().Should().Be(0);
        }

        [Fact]
        public void RunRestoreTestNow_ShouldFailClearly_WhenNoBackupExists_AndRecordResultWhenOneExists()
        {
            var none = _auto.RunRestoreTestNow(_settings, _now);
            none.Success.Should().BeFalse();
            none.Message.Should().Contain("نسخة");
            _store.Load().LastRestoreTestSuccess.Should().BeNull();

            _svc.CreateBackup(_settings.PrimaryFolder, out string path, out _).Should().BeTrue();
            var ok = _auto.RunRestoreTestNow(_settings, _now);
            ok.Success.Should().BeTrue(ok.Message);
            _store.Load().LastRestoreTestSuccess.Should().Be(_now);
            _store.Load().LastRestoreTestFile.Should().Be(path);
        }

        [Fact]
        public void FindLatestBackup_ShouldReturnNewestByName_OrNullWhenFolderMissing()
        {
            _svc.FindLatestBackup(_settings).Should().BeNull();

            Directory.CreateDirectory(_settings.PrimaryFolder);
            File.WriteAllText(Path.Combine(_settings.PrimaryFolder, "e_prescriptions_backup_20261001_100000.db"), "a");
            File.WriteAllText(Path.Combine(_settings.PrimaryFolder, "e_prescriptions_backup_20261014_230000.db"), "b");
            File.WriteAllText(Path.Combine(_settings.PrimaryFolder, "other_20991231_000000.db"), "c");

            Path.GetFileName(_svc.FindLatestBackup(_settings)).Should().Be("e_prescriptions_backup_20261014_230000.db");
        }
    }
}
