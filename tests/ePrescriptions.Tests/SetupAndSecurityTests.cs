using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Data.Setup;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// اختبارات الإعداد الأول وأمان الحسابات (خطوات 1–3 وما بعدها من خطة الإكمال).
    /// </summary>
    public class SetupAndSecurityTests
    {
        private static string ReadEmbeddedMigration(string fileName)
        {
            var asm = typeof(DatabaseContext).Assembly;
            string? resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            resource.Should().NotBeNull();
            using var stream = asm.GetManifestResourceStream(resource!)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static (DatabaseContext Db, FirstRunSetupService Setup, UserRepository Users) NewCleanInstall()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false); // نفس ما يفعله التطبيق الفعلي
            return (db, new FirstRunSetupService(db, new SettingsRepository(db)), new UserRepository(db));
        }

        private static long Count(DatabaseContext db, string table)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        [Fact]
        public void CleanInstall_ShouldHaveNoUsersNoDemoData_AndNoKnownOwnerPassword()
        {
            var (db, setup, users) = NewCleanInstall();

            setup.IsFirstRun().Should().BeTrue();
            Count(db, "Users").Should().Be(0);
            Count(db, "Items").Should().Be(0);
            Count(db, "Suppliers").Should().Be(0);
            users.Authenticate(DatabaseContext.OwnerUsername, DatabaseContext.DefaultOwnerPassword).Should().BeNull();
        }

        [Fact]
        public void CompleteSetup_ShouldCreateOwnerWithChosenPassword_AndNotWithDefaultPassword()
        {
            var (db, setup, users) = NewCleanInstall();

            bool ok = setup.CompleteSetup("صيدلية الشفاء", "123456789", "MyStrong#2026", "MyStrong#2026", false, out string error);

            ok.Should().BeTrue();
            error.Should().BeEmpty();
            setup.IsFirstRun().Should().BeFalse();
            users.Authenticate(DatabaseContext.OwnerUsername, "MyStrong#2026").Should().NotBeNull();
            users.Authenticate(DatabaseContext.OwnerUsername, "ChangeMe@Owner1").Should().BeNull();
            Count(db, "Items").Should().Be(0);
            Count(db, "Users").Should().Be(1);

            var settings = new SettingsRepository(db).GetSettings();
            settings.PharmacyNameAr.Should().Be("صيدلية الشفاء");
            settings.TaxNumber.Should().Be("123456789");
            settings.PharmacistInCharge.Should().BeEmpty();

            setup.CompleteSetup(null, null, "AnotherPass#1", "AnotherPass#1", false, out string again).Should().BeFalse();
        }

        [Fact]
        public void CompleteSetup_WithDemoData_ShouldLoadDemoItemsButStillOnlyOneUser()
        {
            var (db, setup, users) = NewCleanInstall();

            setup.CompleteSetup("صيدلية التجربة", null, "MyStrong#2026", "MyStrong#2026", true, out _).Should().BeTrue();

            Count(db, "Items").Should().BeGreaterThanOrEqualTo(5);
            Count(db, "Customers").Should().Be(2);
            Count(db, "Suppliers").Should().Be(1);
            Count(db, "Users").Should().Be(1);
            new SettingsRepository(db).GetSettings().PharmacyNameAr.Should().Be("صيدلية التجربة");
            db.LoadDemoData().Should().BeFalse(); // مفيش تكرار على قاعدة فيها أصناف
        }

        [Fact]
        public void CompleteSetup_ShouldRejectWeakMismatchedOrDefaultPasswords_AndBadTaxNumber()
        {
            var (db, setup, users) = NewCleanInstall();

            setup.CompleteSetup(null, null, "", "", false, out _).Should().BeFalse();
            setup.CompleteSetup(null, null, "short1", "short1", false, out _).Should().BeFalse();
            setup.CompleteSetup(null, null, "ChangeMe@Owner1", "ChangeMe@Owner1", false, out _).Should().BeFalse();
            setup.CompleteSetup(null, null, "MyStrong#2026", "Different#2026", false, out string mismatch).Should().BeFalse();
            mismatch.Should().NotBeEmpty();
            setup.CompleteSetup(null, "12345", "MyStrong#2026", "MyStrong#2026", false, out string badTax).Should().BeFalse();
            badTax.Should().Contain("الرقم الضريبي");

            setup.IsFirstRun().Should().BeTrue();
            Count(db, "Users").Should().Be(0);
        }

        [Fact]
        public void DemoData_ShouldContainNoUsersAndNoSaudiRemnants()
        {
            string sql = ReadEmbeddedMigration("DemoData.sql");
            string withoutComments = string.Join("\n", sql.Split('\n').Where(l => !l.TrimStart().StartsWith("--")));

            Regex.IsMatch(withoutComments, @"INSERT\s+(OR\s+\w+\s+)?INTO\s+Users\b", RegexOptions.IgnoreCase).Should().BeFalse();
            withoutComments.Contains("Halalas").Should().BeFalse();
            withoutComments.Contains("الدمام").Should().BeFalse();
            withoutComments.Contains("'05").Should().BeFalse();
        }

        [Fact]
        public void SeedData_ShouldNotInsertAnyUserRows()
        {
            string sql = ReadEmbeddedMigration("002_SeedData.sql");

            // نتجاهل أسطر التعليقات ثم نبحث عن أي INSERT في جدول Users
            string withoutComments = string.Join("\n", sql.Split('\n').Where(l => !l.TrimStart().StartsWith("--")));
            Regex.IsMatch(withoutComments, @"INSERT\s+(OR\s+\w+\s+)?INTO\s+Users\b", RegexOptions.IgnoreCase).Should().BeFalse();
        }
    }
}
