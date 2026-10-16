using System;
using System.Collections.Generic;
using System.IO;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 13 (جزء ب): نسخة احتياطية تلقائية بعد إغلاق الوردية</summary>
    public class AutoBackupAfterShiftTests : IDisposable
    {
#pragma warning disable CS0067
        private class FakeSession : IUserSessionService
        {
            public User? CurrentUser { get; set; }
            public Shift? CurrentShift { get; set; }
            public bool IsAuthenticated => CurrentUser != null;
            public bool IsLocked => false;
            public event Action? SessionChanged;
            public event Action<bool>? LockStateChanged;
            public bool Login(string username, string password, out string errorMessage) { errorMessage = string.Empty; return false; }
            public void Logout() { CurrentUser = null; CurrentShift = null; }
            public void Lock() { }
            public bool Unlock(string password, out string errorMessage) { errorMessage = string.Empty; return false; }
            public void SetCurrentShift(Shift? shift) { CurrentShift = shift; }
        }
#pragma warning restore CS0067

        private readonly string _root = Path.Combine(Path.GetTempPath(), "ep_autobk_" + Guid.NewGuid().ToString("N"));
        private readonly DatabaseContext _db;
        private readonly BackupService _backup;
        private readonly StaffViewModel _vm;
        private BackupSettings _settings = new BackupSettings();
        private readonly List<string> _warnings = new List<string>();

        public AutoBackupAfterShiftTests()
        {
            Directory.CreateDirectory(_root);
            _db = new DatabaseContext(Path.Combine(_root, "live.db"));
            _db.InitializeAndMigrate(seedData: true);
            _backup = new BackupService(_db);
            var userRepo = new UserRepository(_db);
            var session = new FakeSession { CurrentUser = userRepo.Authenticate(DatabaseContext.OwnerUsername, DatabaseContext.DefaultOwnerPassword) };
            _vm = new StaffViewModel(userRepo, new ShiftRepository(_db), session, _backup, () => _settings);
            _vm.BackupWarningNotifier = m => _warnings.Add(m);
        }

        public void Dispose()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_root, true); } catch { }
        }

        private int BackupCount(string folder) => Directory.Exists(folder) ? Directory.GetFiles(folder, BackupService.BackupFilePattern).Length : 0;

        [Fact]
        public void ClosingShift_ShouldTakeAutomaticBackup_AndKeepPrimaryAndExternalCopies()
        {
            string primary = Path.Combine(_root, "primary"), usb = Path.Combine(_root, "usb");
            _settings = new BackupSettings { PrimaryFolder = primary, ExternalFolder = usb, AutoBackupAfterShift = true };

            _vm.OpenShift();
            _vm.HasOpenShift.Should().BeTrue();
            BackupCount(primary).Should().Be(0); // فتح الوردية لا يأخذ نسخة
            _vm.CloseShift();

            _vm.HasOpenShift.Should().BeFalse();
            BackupCount(primary).Should().Be(1);
            BackupCount(usb).Should().Be(1);
            _vm.StatusMessage.Should().Contain("نسخة احتياطية");
            _warnings.Should().BeEmpty();
        }

        [Fact]
        public void ClosingShift_WithAutoBackupDisabled_ShouldNotBackup()
        {
            string primary = Path.Combine(_root, "primary");
            _settings = new BackupSettings { PrimaryFolder = primary, AutoBackupAfterShift = false };

            _vm.OpenShift();
            _vm.CloseShift();

            _vm.HasOpenShift.Should().BeFalse();
            BackupCount(primary).Should().Be(0);
        }

        [Fact]
        public void ClosingShift_WhenBackupFails_ShouldStillCloseShift_AndWarnUser()
        {
            string blocker = Path.Combine(_root, "blocker");
            File.WriteAllText(blocker, "x");
            _settings = new BackupSettings { PrimaryFolder = Path.Combine(blocker, "sub"), AutoBackupAfterShift = true };

            _vm.OpenShift();
            _vm.CloseShift();

            _vm.HasOpenShift.Should().BeFalse();
            _warnings.Should().HaveCount(1);
            _vm.StatusMessage.Should().Contain("⚠");
        }

        // ===== خطوة 28 (جزء ب): الإغلاق غير المتزامن =====

        [Fact]
        public void CloseShiftAsync_ShouldCloseShift_TakeBackup_AndResetIsBusy()
        {
            string primary = Path.Combine(_root, "primary"), usb = Path.Combine(_root, "usb");
            _settings = new BackupSettings { PrimaryFolder = primary, ExternalFolder = usb, AutoBackupAfterShift = true };

            _vm.OpenShift();
            _vm.CloseShiftAsync().GetAwaiter().GetResult();

            _vm.HasOpenShift.Should().BeFalse();
            _vm.IsBusy.Should().BeFalse();
            BackupCount(primary).Should().Be(1);
            BackupCount(usb).Should().Be(1);
            _vm.StatusMessage.Should().Contain("نسخة احتياطية");
            _warnings.Should().BeEmpty();
        }

        [Fact]
        public void CloseShiftAsync_WithAutoBackupDisabled_ShouldNotBackup()
        {
            string primary = Path.Combine(_root, "primary");
            _settings = new BackupSettings { PrimaryFolder = primary, AutoBackupAfterShift = false };

            _vm.OpenShift();
            _vm.CloseShiftAsync().GetAwaiter().GetResult();

            _vm.HasOpenShift.Should().BeFalse();
            _vm.IsBusy.Should().BeFalse();
            BackupCount(primary).Should().Be(0);
        }

        [Fact]
        public void CloseShiftAsync_WhenBackupFails_ShouldStillCloseShift_WarnUser_AndResetIsBusy()
        {
            string blocker = Path.Combine(_root, "blocker");
            File.WriteAllText(blocker, "x");
            _settings = new BackupSettings { PrimaryFolder = Path.Combine(blocker, "sub"), AutoBackupAfterShift = true };

            _vm.OpenShift();
            _vm.CloseShiftAsync().GetAwaiter().GetResult();

            _vm.HasOpenShift.Should().BeFalse();
            _vm.IsBusy.Should().BeFalse();
            _warnings.Should().HaveCount(1);
            _vm.StatusMessage.Should().Contain("⚠");
        }

        [Fact]
        public void CloseShiftAsync_WithoutOpenShift_ShouldReportAndResetIsBusy()
        {
            _settings = new BackupSettings { PrimaryFolder = Path.Combine(_root, "primary"), AutoBackupAfterShift = true };

            _vm.CloseShiftAsync().GetAwaiter().GetResult();

            _vm.IsBusy.Should().BeFalse();
            _vm.StatusMessage.Should().Contain("لا توجد وردية مفتوحة");
            BackupCount(Path.Combine(_root, "primary")).Should().Be(0);
        }

        [Fact]
        public void CloseShiftAsync_WhileBusy_ShouldBeIgnored()
        {
            string primary = Path.Combine(_root, "primary");
            _settings = new BackupSettings { PrimaryFolder = primary, AutoBackupAfterShift = true };

            _vm.OpenShift();
            _vm.IsBusy = true; // محاكاة ضغطة ثانية أثناء نسخ شغّال
            _vm.CloseShiftAsync().GetAwaiter().GetResult();

            _vm.HasOpenShift.Should().BeTrue();   // ما اتقفلتش
            BackupCount(primary).Should().Be(0);
        }
    }
}
