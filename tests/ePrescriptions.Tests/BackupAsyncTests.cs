using System;
using System.IO;
using ePrescriptions.App.Services;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 28 (جزء أ): النسخ الاحتياطي والاستعادة غير المتزامنين</summary>
    public class BackupAsyncTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ep_bkasync_" + Guid.NewGuid().ToString("N"));
        private readonly DatabaseContext _db;
        private readonly BackupService _backup;

        public BackupAsyncTests()
        {
            Directory.CreateDirectory(_root);
            _db = new DatabaseContext(Path.Combine(_root, "live.db"));
            _db.InitializeAndMigrate(seedData: true);
            _backup = new BackupService(_db);
        }

        public void Dispose()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_root, true); } catch (Exception) { }
        }

        [Fact]
        public void RunConfiguredBackupAsync_ShouldCreateBackupFile_LikeTheSyncVersion()
        {
            string folder = Path.Combine(_root, "primary");
            var result = _backup.RunConfiguredBackupAsync(new BackupSettings { PrimaryFolder = folder })
                                .GetAwaiter().GetResult();

            result.Success.Should().BeTrue();
            File.Exists(result.PrimaryPath).Should().BeTrue();
            Directory.GetFiles(folder, BackupService.BackupFilePattern).Length.Should().Be(1);
        }

        [Fact]
        public void RunAfterShiftBackupAsync_ShouldReturnNull_WhenAutoBackupDisabled_AndResultWhenEnabled()
        {
            string folder = Path.Combine(_root, "auto");

            _backup.RunAfterShiftBackupAsync(new BackupSettings { PrimaryFolder = folder, AutoBackupAfterShift = false })
                   .GetAwaiter().GetResult().Should().BeNull();
            Directory.Exists(folder).Should().BeFalse();

            var result = _backup.RunAfterShiftBackupAsync(new BackupSettings { PrimaryFolder = folder, AutoBackupAfterShift = true })
                                .GetAwaiter().GetResult();
            result.Should().NotBeNull();
            result!.Success.Should().BeTrue();
        }

        [Fact]
        public void RestoreBackupAsync_ShouldRestoreValidBackup_AndRejectMissingFile()
        {
            string folder = Path.Combine(_root, "primary");
            var made = _backup.RunConfiguredBackupAsync(new BackupSettings { PrimaryFolder = folder })
                              .GetAwaiter().GetResult();
            made.Success.Should().BeTrue();

            var ok = _backup.RestoreBackupAsync(made.PrimaryPath).GetAwaiter().GetResult();
            ok.Success.Should().BeTrue();
            ok.ErrorMessage.Should().BeEmpty();

            var bad = _backup.RestoreBackupAsync(Path.Combine(_root, "nope.db")).GetAwaiter().GetResult();
            bad.Success.Should().BeFalse();
            bad.ErrorMessage.Should().NotBeEmpty();
        }
    }
}
