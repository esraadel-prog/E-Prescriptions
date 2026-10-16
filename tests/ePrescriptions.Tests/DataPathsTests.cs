using System;
using System.IO;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 12: قاعدة البيانات في ProgramData وترحيل البيانات القديمة من AppData</summary>
    public class DataPathsTests
    {
        [Fact]
        public void DefaultDatabasePath_ShouldPointToProgramData()
        {
            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ePrescriptions", "e_prescriptions.db");

            DatabaseContext.DefaultDatabasePath.Should().Be(expected);
            DatabaseContext.DefaultDatabaseDirectory.Should().Be(AppPaths.DataDirectory);
            AppPaths.LogsDirectory.Should().Be(Path.Combine(AppPaths.DataDirectory, "logs"));
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                DatabaseContext.DefaultDatabasePath.Contains(appData).Should().BeFalse();
            }
        }

        private static (string Legacy, string New) TempDirs()
        {
            string root = Path.Combine(Path.GetTempPath(), "ep_mig_" + Guid.NewGuid().ToString("N"));
            return (Path.Combine(root, "legacy"), Path.Combine(root, "new"));
        }

        [Fact]
        public void LegacyMigration_ShouldCopyDatabaseAndBackups_WithoutDeletingOldAndWithoutOverwriting()
        {
            var (legacy, target) = TempDirs();
            Directory.CreateDirectory(Path.Combine(legacy, "backups"));
            File.WriteAllText(Path.Combine(legacy, "e_prescriptions.db"), "OLD-DB");
            File.WriteAllText(Path.Combine(legacy, "e_prescriptions.db-wal"), "OLD-WAL");
            File.WriteAllText(Path.Combine(legacy, "backups", "b1.db"), "BACKUP");

            LegacyDataMigrator.Migrate(legacy, target).Should().Be(LegacyMigrationResult.Migrated);

            File.ReadAllText(Path.Combine(target, "e_prescriptions.db")).Should().Be("OLD-DB");
            File.ReadAllText(Path.Combine(target, "e_prescriptions.db-wal")).Should().Be("OLD-WAL");
            File.ReadAllText(Path.Combine(target, "backups", "b1.db")).Should().Be("BACKUP");
            File.Exists(Path.Combine(legacy, "e_prescriptions.db")).Should().BeTrue();
            File.Exists(Path.Combine(legacy, LegacyDataMigrator.MarkerFileName)).Should().BeTrue();

            // لو القاعدة الجديدة اتعدلت بعد كده، التشغيل التاني ما يكتبش فوقها
            File.WriteAllText(Path.Combine(target, "e_prescriptions.db"), "NEW-DATA");
            LegacyDataMigrator.Migrate(legacy, target).Should().Be(LegacyMigrationResult.AlreadyMigrated);
            File.ReadAllText(Path.Combine(target, "e_prescriptions.db")).Should().Be("NEW-DATA");
        }

        [Fact]
        public void LegacyMigration_ShouldDoNothingWhenNoLegacyDatabase()
        {
            var (legacy, target) = TempDirs();
            LegacyDataMigrator.Migrate(legacy, target).Should().Be(LegacyMigrationResult.NothingToMigrate);
            Directory.Exists(target).Should().BeFalse();
        }
    }
}
