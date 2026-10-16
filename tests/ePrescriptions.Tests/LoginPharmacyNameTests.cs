using System;
using System.IO;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Data.Setup;
using ePrescriptions.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>اسم الصيدلية في شاشة الدخول = المكتوب في الإعدادات (أو اللي اتكتب في معالج أول تشغيل)، مش اسم ثابت</summary>
    public class LoginPharmacyNameTests
    {
        private static (DatabaseContext db, SettingsRepository settings, LoginViewModel vm, string path) Setup()
        {
            string path = Path.Combine(Path.GetTempPath(), $"test_loginname_{Guid.NewGuid():N}.db");
            var db = new DatabaseContext(path);
            db.InitializeAndMigrate(seedData: false);
            var users = new UserRepository(db);
            var session = new UserSessionService(users, new ShiftRepository(db));
            var settings = new SettingsRepository(db);
            return (db, settings, new LoginViewModel(session, users, settings), path);
        }

        private static void Cleanup(string p) { try { if (File.Exists(p)) File.Delete(p); } catch (IOException) { } }

        [Fact]
        public void LoginScreen_ShouldShowPharmacyNameFromSettings_AndFollowLaterChanges()
        {
            var (db, settings, vm, path) = Setup();
            try
            {
                var s = settings.GetSettings();
                s.PharmacyNameAr = "صيدلية النور";
                settings.SaveSettings(s);

                vm.RefreshPharmacyName();
                vm.PharmacyName.Should().Be("صيدلية النور");

                // تعديل من شاشة الإعدادات يظهر عند فتح شاشة الدخول/القفل تاني
                s.PharmacyNameAr = "صيدلية الشفاء";
                settings.SaveSettings(s);
                vm.SetMode(lockMode: false);
                vm.PharmacyName.Should().Be("صيدلية الشفاء");
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void LoginScreen_ShouldShowTheNameEnteredInFirstRunSetup()
        {
            string path = Path.Combine(Path.GetTempPath(), $"test_loginname_fr_{Guid.NewGuid():N}.db");
            try
            {
                var db = new DatabaseContext(path);
                db.InitializeAndMigrate(seedData: false);
                var users = new UserRepository(db);
                var settings = new SettingsRepository(db);
                var setup = new FirstRunSetupService(db, settings);

                setup.CompleteSetup("صيدلية الأمل", null, "MyStrong#2026", "MyStrong#2026", false, out _).Should().BeTrue();

                var vm = new LoginViewModel(new UserSessionService(users, new ShiftRepository(db)), users, settings);
                vm.PharmacyName.Should().Be("صيدلية الأمل");
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void LoginScreen_WithEmptyOrMissingSettings_ShouldUseNeutralNameNotAPersonalOne()
        {
            var (db, settings, vm, path) = Setup();
            try
            {
                var s = settings.GetSettings();
                s.PharmacyNameAr = "   ";
                settings.SaveSettings(s);
                vm.RefreshPharmacyName();
                vm.PharmacyName.Should().Be(PharmacySettings.DefaultPharmacyName);

                // بدون Repository خالص
                var users = new UserRepository(db);
                var bare = new LoginViewModel(new UserSessionService(users, new ShiftRepository(db)));
                bare.PharmacyName.Should().Be(PharmacySettings.DefaultPharmacyName);
            }
            finally { Cleanup(path); }
        }

        private static string FindRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "e_prescriptions.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("e_prescriptions.sln");
        }
    }
}
