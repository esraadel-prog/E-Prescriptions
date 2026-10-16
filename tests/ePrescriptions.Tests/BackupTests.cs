using System;
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
    /// <summary>خطوة 13: نسخ احتياطي بـ VACUUM INTO، قرص خارجي، حد احتفاظ، واستعادة مع تطبيق migrations الناقصة</summary>
    public class BackupTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ep_backup_" + Guid.NewGuid().ToString("N"));
        private readonly string _dbPath;
        private readonly DatabaseContext _db;
        private readonly BackupService _svc;

        public BackupTests()
        {
            Directory.CreateDirectory(_root);
            _dbPath = Path.Combine(_root, "live.db");
            _db = new DatabaseContext(_dbPath);
            _db.InitializeAndMigrate(seedData: false);
            Exec(_db, "CREATE TABLE IF NOT EXISTS BackupMarker (Id INTEGER PRIMARY KEY, Val TEXT NOT NULL);");
            _svc = new BackupService(_db);
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_root, true); } catch { }
        }

        private static void Exec(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static long Scalar(string dbFile, string sql)
        {
            using var conn = new SQLiteConnection($"Data Source={dbFile};Version=3;Pooling=False;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        private long Live(string sql) => Scalar(_dbPath, sql);

        [Fact]
        public void Backup_ShouldIncludeAllCommittedData_EvenWhileDatabaseIsOpen_AndKeepSchemaVersion()
        {
            Exec(_db, "INSERT INTO BackupMarker (Val) VALUES ('a'), ('b'), ('c');");

            // اتصال مفتوح أثناء النسخ (نفس حالة البرنامج وهو شغّال)
            using var openConn = _db.CreateConnection();

            _svc.CreateBackup(Path.Combine(_root, "b"), out string path, out string err).Should().BeTrue();
            err.Should().BeEmpty();

            File.Exists(path).Should().BeTrue();
            File.Exists(path + ".tmp").Should().BeFalse();
            Scalar(path, "SELECT COUNT(*) FROM BackupMarker").Should().Be(3);
            Scalar(path, "PRAGMA user_version").Should().Be(DatabaseContext.LatestSchemaVersion);
            _svc.ValidateBackupFile(path, out string v).Should().BeTrue();
        }

        [Fact]
        public void Backup_TwiceInSameSecond_ShouldNotOverwriteEachOther()
        {
            string folder = Path.Combine(_root, "b");
            _svc.CreateBackup(folder, out string p1, out _).Should().BeTrue();
            _svc.CreateBackup(folder, out string p2, out _).Should().BeTrue();
            p1.Should().NotBe(p2);
            Directory.GetFiles(folder, BackupService.BackupFilePattern).Length.Should().Be(2);
        }

        [Fact]
        public void ConfiguredBackup_ShouldWriteToSeparateExternalFolder_AndRejectSameFolder()
        {
            string primary = Path.Combine(_root, "primary");
            string external = Path.Combine(_root, "usb");
            Exec(_db, "INSERT INTO BackupMarker (Val) VALUES ('x');");

            var r = _svc.RunConfiguredBackup(new BackupSettings { PrimaryFolder = primary, ExternalFolder = external, RetentionCount = 5 });

            r.Success.Should().BeTrue();
            r.ExternalFailed.Should().BeFalse();
            File.Exists(r.PrimaryPath).Should().BeTrue();
            File.Exists(r.ExternalPath).Should().BeTrue();
            Path.GetDirectoryName(r.ExternalPath).Should().Be(external);
            Scalar(r.ExternalPath, "SELECT COUNT(*) FROM BackupMarker").Should().Be(1);

            // نفس المجلد = مش نسخة منفصلة: النسخة الأساسية تنجح والتحذير بيظهر
            var same = _svc.RunConfiguredBackup(new BackupSettings { PrimaryFolder = primary, ExternalFolder = primary });
            same.Success.Should().BeTrue();
            same.ExternalFailed.Should().BeTrue();
        }

        [Fact]
        public void ConfiguredBackup_WhenExternalUnavailable_ShouldKeepPrimaryAndReportFailure()
        {
            // مسار خارجي مستحيل الإنشاء (جواه ملف عادي مش مجلد)
            string blocker = Path.Combine(_root, "blocker");
            File.WriteAllText(blocker, "x");

            var r = _svc.RunConfiguredBackup(new BackupSettings { PrimaryFolder = Path.Combine(_root, "primary"), ExternalFolder = Path.Combine(blocker, "sub") });

            r.Success.Should().BeTrue();
            r.ExternalFailed.Should().BeTrue();
            File.Exists(r.PrimaryPath).Should().BeTrue();
            r.Message.Should().Contain("القرص الخارجي");
        }

        [Fact]
        public void Retention_ShouldKeepNewestN_AndDeleteOlder_WithoutTouchingOtherFiles()
        {
            string folder = Path.Combine(_root, "b");
            Directory.CreateDirectory(folder);
            for (int d = 1; d <= 6; d++)
                File.WriteAllText(Path.Combine(folder, $"e_prescriptions_backup_2026010{d}_120000.db"), "x" + d);
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "keep me");
            File.WriteAllText(Path.Combine(folder, "other.db"), "keep me too");

            int deleted = _svc.PruneOldBackups(folder, 3);

            deleted.Should().Be(3);
            var left = Directory.GetFiles(folder, BackupService.BackupFilePattern).Select(Path.GetFileName).OrderBy(x => x).ToList();
            left.Should().HaveCount(3);
            left.Should().Contain("e_prescriptions_backup_20260106_120000.db");
            left.Should().Contain("e_prescriptions_backup_20260104_120000.db");
            left.Should().NotContain("e_prescriptions_backup_20260101_120000.db");
            File.Exists(Path.Combine(folder, "notes.txt")).Should().BeTrue();
            File.Exists(Path.Combine(folder, "other.db")).Should().BeTrue();

            // الحد الأدنى 1: صفر أو سالب ما يمسحش كل النسخ
            _svc.PruneOldBackups(folder, 0);
            Directory.GetFiles(folder, BackupService.BackupFilePattern).Length.Should().Be(1);
        }

        [Fact]
        public void ConfiguredBackup_ShouldApplyRetentionInBothFolders()
        {
            string primary = Path.Combine(_root, "primary");
            string usb = Path.Combine(_root, "usb");
            var s = new BackupSettings { PrimaryFolder = primary, ExternalFolder = usb, RetentionCount = 2 };
            for (int i = 0; i < 4; i++) _svc.RunConfiguredBackup(s).Success.Should().BeTrue();

            Directory.GetFiles(primary, BackupService.BackupFilePattern).Length.Should().Be(2);
            Directory.GetFiles(usb, BackupService.BackupFilePattern).Length.Should().Be(2);
        }

        [Fact]
        public void AfterShiftBackup_ShouldDoNothingWhenDisabled_AndBackupWhenEnabled()
        {
            string primary = Path.Combine(_root, "primary");
            _svc.RunAfterShiftBackup(new BackupSettings { PrimaryFolder = primary, AutoBackupAfterShift = false }).Should().BeNull();
            Directory.Exists(primary).Should().BeFalse();

            var r = _svc.RunAfterShiftBackup(new BackupSettings { PrimaryFolder = primary, AutoBackupAfterShift = true });
            r.Should().NotBeNull();
            r!.Success.Should().BeTrue();
            Directory.GetFiles(primary, BackupService.BackupFilePattern).Length.Should().Be(1);
        }

        [Fact]
        public void BackupThenRestore_ShouldBringBackOriginalData_AndLatestSchemaVersion()
        {
            Exec(_db, "INSERT INTO BackupMarker (Val) VALUES ('before1'), ('before2');");
            _svc.CreateBackup(Path.Combine(_root, "b"), out string path, out _).Should().BeTrue();

            // تغيير بعد النسخة (هيتلغي بالاستعادة)
            Exec(_db, "DELETE FROM BackupMarker; INSERT INTO BackupMarker (Val) VALUES ('after');");
            Live("SELECT COUNT(*) FROM BackupMarker").Should().Be(1);

            _svc.RestoreBackup(path, out string err).Should().BeTrue();
            err.Should().BeEmpty();

            Live("SELECT COUNT(*) FROM BackupMarker").Should().Be(2);
            Live("SELECT COUNT(*) FROM BackupMarker WHERE Val = 'after'").Should().Be(0);
            Live("PRAGMA user_version").Should().Be(DatabaseContext.LatestSchemaVersion);
            
            File.Exists(_dbPath + ".pre_restore.bak").Should().BeTrue();

            // القاعدة المستعادة شغّالة بالاتصال الجديد
            Exec(_db, "INSERT INTO BackupMarker (Val) VALUES ('new');");
            Live("SELECT COUNT(*) FROM BackupMarker").Should().Be(3);
        }

        [Fact]
        public void Restore_OfOlderSchemaBackup_ShouldApplyMissingMigrations()
        {
            Exec(_db, "INSERT INTO BackupMarker (Val) VALUES ('old-data');");
            _svc.CreateBackup(Path.Combine(_root, "b"), out string path, out _).Should().BeTrue();

            // نحوّل النسخة لشكل إصدار 9 (قبل migrations 010 و011 و013 و014 و015)
            using (var conn = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "ALTER TABLE InvoiceItems DROP COLUMN PrescriptionNumber; ALTER TABLE InvoiceItems DROP COLUMN DoctorName; ALTER TABLE Invoices DROP COLUMN Status; ALTER TABLE Invoices DROP COLUMN CancelledAt; ALTER TABLE Invoices DROP COLUMN CancelledByUserId; ALTER TABLE Invoices DROP COLUMN CancelledByName; ALTER TABLE Invoices DROP COLUMN CancelReason; ALTER TABLE Users DROP COLUMN FailedLoginCount; ALTER TABLE Users DROP COLUMN LockedUntil; ALTER TABLE OwnerAuditLog DROP COLUMN PrevHash; ALTER TABLE OwnerAuditLog DROP COLUMN RowHash; ALTER TABLE StaffAuditLog DROP COLUMN PrevHash; ALTER TABLE StaffAuditLog DROP COLUMN RowHash; DROP INDEX IF EXISTS IX_Customers_DefaultCash; ALTER TABLE Customers DROP COLUMN IsDefaultCash; ALTER TABLE Items DROP COLUMN SearchText; DROP TABLE IF EXISTS ZakatSettings; DROP TABLE IF EXISTS PurchaseOrderItems; DROP TABLE IF EXISTS PurchaseOrders; DROP TABLE IF EXISTS InvoicePayments; DROP INDEX IF EXISTS IX_SupplierReturns_PurchaseInvoice; DROP INDEX IF EXISTS IX_SupplierReturnItems_PurchaseItem; ALTER TABLE SupplierReturns DROP COLUMN PurchaseInvoiceId; ALTER TABLE SupplierReturnItems DROP COLUMN PurchaseInvoiceItemId; ALTER TABLE PurchaseInvoiceItems DROP COLUMN VatPercent; ALTER TABLE PurchaseInvoiceItems DROP COLUMN BatchId; PRAGMA user_version = 9;";
                cmd.ExecuteNonQuery();
            }
            Scalar(path, "SELECT COUNT(*) FROM pragma_table_info('InvoiceItems') WHERE name = 'PrescriptionNumber'").Should().Be(0);

            _svc.RestoreBackup(path, out string err).Should().BeTrue();
            err.Should().BeEmpty();

            Live("PRAGMA user_version").Should().Be(DatabaseContext.LatestSchemaVersion);
            Live("SELECT COUNT(*) FROM pragma_table_info('InvoiceItems') WHERE name = 'PrescriptionNumber'").Should().Be(1);
            Live("SELECT COUNT(*) FROM pragma_table_info('Invoices') WHERE name = 'Status'").Should().Be(1);
            Live("SELECT COUNT(*) FROM pragma_table_info('StaffAuditLog') WHERE name = 'RowHash'").Should().Be(1);
            Live("SELECT COUNT(*) FROM pragma_table_info('PurchaseInvoiceItems') WHERE name = 'BatchId'").Should().Be(1);
            Live("SELECT COUNT(*) FROM pragma_table_info('PurchaseInvoiceItems') WHERE name = 'VatPercent'").Should().Be(1);
            Live("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PurchaseOrders'").Should().Be(1);
            Live("SELECT COUNT(*) FROM pragma_table_info('SupplierReturns') WHERE name = 'PurchaseInvoiceId'").Should().Be(1);
            Live("SELECT COUNT(*) FROM BackupMarker WHERE Val = 'old-data'").Should().Be(1);
        }

        [Fact]
        public void Restore_OfNewerSchemaBackup_ShouldBeRefused_AndLeaveCurrentDatabaseUntouched()
        {
            Exec(_db, "INSERT INTO BackupMarker (Val) VALUES ('keep');");
            _svc.CreateBackup(Path.Combine(_root, "b"), out string path, out _).Should().BeTrue();
            using (var conn = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False;"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA user_version = {DatabaseContext.LatestSchemaVersion + 1};";
                cmd.ExecuteNonQuery();
            }
            Exec(_db, "INSERT INTO BackupMarker (Val) VALUES ('current-only');");

            _svc.RestoreBackup(path, out string err).Should().BeFalse();

            err.Should().Contain("أحدث");
            Live("SELECT COUNT(*) FROM BackupMarker WHERE Val = 'current-only'").Should().Be(1);
        }

        [Fact]
        public void Restore_OfCorruptFile_ShouldBeRefused_AndLeaveCurrentDatabaseUntouched()
        {
            Exec(_db, "INSERT INTO BackupMarker (Val) VALUES ('safe');");
            string bad = Path.Combine(_root, "bad.db");
            File.WriteAllText(bad, "this is not a sqlite database at all, just text padding padding padding padding");

            _svc.RestoreBackup(bad, out string err).Should().BeFalse();

            err.Should().NotBeEmpty();
            Live("SELECT COUNT(*) FROM BackupMarker WHERE Val = 'safe'").Should().Be(1);
        }

        [Fact]
        public void BackupSettings_ShouldBeStoredInSeparateFile_AndSurviveRestore()
        {
            string ini = Path.Combine(_root, "backup_settings.ini");
            BackupSettingsStore.Save(new BackupSettings { PrimaryFolder = @"D:\bk", ExternalFolder = @"E:\usb", RetentionCount = 7, AutoBackupAfterShift = false }, ini);

            // استعادة نسخة قاعدة لا تغيّر ملف الإعدادات
            _svc.CreateBackup(Path.Combine(_root, "b"), out string path, out _).Should().BeTrue();
            _svc.RestoreBackup(path, out _).Should().BeTrue();

            var loaded = BackupSettingsStore.Load(ini);
            loaded.PrimaryFolder.Should().Be(@"D:\bk");
            loaded.ExternalFolder.Should().Be(@"E:\usb");
            loaded.RetentionCount.Should().Be(7);
            loaded.AutoBackupAfterShift.Should().BeFalse();
            Path.GetDirectoryName(BackupSettingsStore.DefaultPath).Should().Be(AppPaths.DataDirectory);
            BackupSettingsStore.DefaultPath.EndsWith(".db").Should().BeFalse();

            // ملف مفقود/تالف = قيم افتراضية بدون استثناء
            BackupSettingsStore.Load(Path.Combine(_root, "missing.ini")).RetentionCount.Should().Be(BackupSettings.DefaultRetentionCount);
            File.WriteAllText(ini, "garbage\nRetentionCount=abc\n");
            BackupSettingsStore.Load(ini).RetentionCount.Should().Be(BackupSettings.DefaultRetentionCount);
            new BackupSettings { RetentionCount = 0 }.EffectiveRetention.Should().Be(1);
        }
    }
}
