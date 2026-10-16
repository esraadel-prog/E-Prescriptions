using System;
using System.Security.Cryptography;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Core.Security;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 18: SHA256 بتكرارات أعلى + rehash تلقائي + قفل بعد محاولات فاشلة + قفل بعد خمول + بدون اسم مستخدم افتراضي</summary>
    public class PasswordSecurityTests
    {
        /// <summary>تجزئة بالصيغة القديمة (PBKDF2-SHA1، 50000 تكرار) زي اللي كانت مخزّنة قبل الخطوة</summary>
        private static string LegacyHash(string password)
        {
            byte[] salt = new byte[16];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(salt);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 50000);
            return $"50000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(pbkdf2.GetBytes(32))}";
        }

        private static (DatabaseContext db, UserRepository users) Setup()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            return (db, new UserRepository(db));
        }

        private static string StoredHash(DatabaseContext db, string username)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT PasswordHash FROM Users WHERE Username = @u;";
            cmd.Parameters.AddWithValue("@u", username);
            return cmd.ExecuteScalar()!.ToString()!;
        }

        private static void Exec(DatabaseContext db, string sql, string? p1 = null, string? v1 = null)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            if (p1 != null) cmd.Parameters.AddWithValue(p1, v1);
            cmd.ExecuteNonQuery();
        }

        [Fact]
        public void NewHashes_ShouldUseSha256WithHighIterations_AndVerifyBothFormats()
        {
            string h = PasswordHasher.HashPassword("Secret#123");

            h.StartsWith("sha256:210000:").Should().BeTrue();
            PasswordHasher.VerifyPassword("Secret#123", h).Should().BeTrue();
            PasswordHasher.VerifyPassword("wrong", h).Should().BeFalse();
            PasswordHasher.NeedsRehash(h).Should().BeFalse();
            PasswordHasher.HashPassword("Secret#123").Should().NotBe(h); // salt عشوائي

            string legacy = LegacyHash("Old#Pass1");
            PasswordHasher.VerifyPassword("Old#Pass1", legacy).Should().BeTrue();
            PasswordHasher.NeedsRehash(legacy).Should().BeTrue();
            PasswordHasher.NeedsRehash(PasswordHasher.HashPassword("x", 1000)).Should().BeTrue(); // تكرارات أقل من الحالي
        }

        [Fact]
        public void LegacyHashAccount_ShouldBeRehashedAutomaticallyOnFirstSuccessfulLogin()
        {
            var (db, users) = Setup();
            Exec(db, "INSERT INTO Users (Username, FullName, PasswordHash, Role, IsActive, CreatedAt) VALUES ('old', 'قديم', @h, 3, 1, datetime('now'));", "@h", LegacyHash("Old#Pass1"));
            PasswordHasher.NeedsRehash(StoredHash(db, "old")).Should().BeTrue();

            users.Authenticate("old", "bad", out _).Should().BeNull();
            PasswordHasher.NeedsRehash(StoredHash(db, "old")).Should().BeTrue(); // دخول فاشل ما يغيّرش التجزئة

            var user = users.Authenticate("old", "Old#Pass1");
            user.Should().NotBeNull();

            string after = StoredHash(db, "old");
            after.StartsWith("sha256:").Should().BeTrue();
            user!.PasswordHash.Should().Be(after);
            users.Authenticate("old", "Old#Pass1").Should().NotBeNull(); // بالصيغة الجديدة
            users.Authenticate("old", "bad").Should().BeNull();
        }

        [Fact]
        public void FiveFailedAttempts_ShouldLockAccountTemporarily_EvenForCorrectPassword_ThenUnlockByTime()
        {
            var (db, users) = Setup();
            users.SaveStaffUser(0, "cash1", "كاشير", "Right#1234", UserRole.Cashier, true);
            DateTime now = new DateTime(2026, 10, 12, 9, 0, 0);
            users.Clock = () => now;

            for (int i = 0; i < UserRepository.MaxFailedAttempts - 1; i++)
            {
                users.Authenticate("cash1", "wrong", out string m).Should().BeNull();
                m.Should().BeEmpty();
            }
            users.Authenticate("cash1", "wrong", out string lockedMsg).Should().BeNull(); // الخامسة بتقفل
            lockedMsg.Should().Contain("قفل");

            users.Authenticate("cash1", "Right#1234", out string stillLocked).Should().BeNull();
            stillLocked.Should().Contain("مقفول");

            now = now.AddMinutes(UserRepository.LockoutMinutes - 1);
            users.Authenticate("cash1", "Right#1234").Should().BeNull();

            now = now.AddMinutes(2);
            users.Authenticate("cash1", "Right#1234").Should().NotBeNull();
        }

        [Fact]
        public void SuccessfulLogin_ShouldResetFailureCounter()
        {
            var (db, users) = Setup();
            users.SaveStaffUser(0, "cash2", "كاشير", "Right#1234", UserRole.Cashier, true);
            for (int i = 0; i < 4; i++) users.Authenticate("cash2", "wrong").Should().BeNull();
            users.Authenticate("cash2", "Right#1234").Should().NotBeNull();

            for (int i = 0; i < 4; i++) users.Authenticate("cash2", "wrong").Should().BeNull();
            users.Authenticate("cash2", "Right#1234").Should().NotBeNull(); // لسه مش مقفول (العدّاد اتصفّر بعد النجاح)
        }

        [Fact]
        public void AdminPasswordReset_ShouldClearTheLockout()
        {
            var (db, users) = Setup();
            int id = users.SaveStaffUser(0, "cash3", "كاشير", "Right#1234", UserRole.Cashier, true);
            for (int i = 0; i < UserRepository.MaxFailedAttempts; i++) users.Authenticate("cash3", "wrong");
            users.Authenticate("cash3", "Right#1234").Should().BeNull();

            users.SaveStaffUser(id, "cash3", "كاشير", "NewPass#5678", UserRole.Cashier, true);

            users.Authenticate("cash3", "NewPass#5678").Should().NotBeNull();
        }

        [Fact]
        public void EditingExistingStaffUser_ShouldActuallyPersistChanges()
        {
            var (db, users) = Setup();
            int id = users.SaveStaffUser(0, "cash6", "اسم قديم", "Right#1234", UserRole.Cashier, true);

            users.SaveStaffUser(id, "cash6", "اسم جديد", null, UserRole.Pharmacist, true);

            var u = users.Authenticate("cash6", "Right#1234");
            u.Should().NotBeNull();
            u!.FullName.Should().Be("اسم جديد");
            u.Role.Should().Be(UserRole.Pharmacist);
        }

        [Fact]
        public void ScreenUnlock_ShouldForceLogoutAfterTooManyWrongPasswords()
        {
            var (db, users) = Setup();
            users.SaveStaffUser(0, "cash4", "كاشير", "Right#1234", UserRole.Cashier, true);
            var session = new UserSessionService(users, new ShiftRepository(db));
            session.Login("cash4", "Right#1234", out _).Should().BeTrue();
            session.Lock();

            for (int i = 0; i < UserSessionService.MaxUnlockAttempts - 1; i++)
            {
                session.Unlock("wrong", out _).Should().BeFalse();
                session.IsAuthenticated.Should().BeTrue();
            }
            session.Unlock("wrong", out string msg).Should().BeFalse();

            session.IsAuthenticated.Should().BeFalse();
            msg.Should().Contain("الدخول");
        }

        [Fact]
        public void IdleMonitor_ShouldLockOnlyAfterTimeoutWithoutActivity()
        {
            var (db, users) = Setup();
            users.SaveStaffUser(0, "cash5", "كاشير", "Right#1234", UserRole.Cashier, true);
            var session = new UserSessionService(users, new ShiftRepository(db));
            DateTime now = new DateTime(2026, 10, 12, 9, 0, 0);
            var monitor = new IdleLockMonitor(session) { Clock = () => now, Timeout = TimeSpan.FromMinutes(10) };
            monitor.NotifyActivity();

            monitor.Tick().Should().BeFalse(); // مفيش مستخدم مسجّل
            session.Login("cash5", "Right#1234", out _).Should().BeTrue();
            monitor.NotifyActivity();

            now = now.AddMinutes(9);
            monitor.Tick().Should().BeFalse();
            monitor.NotifyActivity(); // نشاط
            now = now.AddMinutes(9);
            monitor.Tick().Should().BeFalse();
            session.IsLocked.Should().BeFalse();

            now = now.AddMinutes(2);
            monitor.Tick().Should().BeTrue();
            session.IsLocked.Should().BeTrue();

            // مقفولة بالفعل: ما يعيدش القفل، والتعطيل (صفر) بيوقف الميزة
            monitor.Tick().Should().BeFalse();
            session.Unlock("Right#1234", out _).Should().BeTrue();
            monitor.Timeout = TimeSpan.Zero;
            now = now.AddHours(5);
            monitor.Tick().Should().BeFalse();
            session.IsLocked.Should().BeFalse();
        }

        [Fact]
        public void LoginScreen_ShouldNotPrefillAnyUsername()
        {
            var (db, users) = Setup();
            var session = new UserSessionService(users, new ShiftRepository(db));
            var vm = new LoginViewModel(session);

            vm.Username.Should().BeEmpty();
            vm.Username.Should().NotBe("admin");
        }
    }
}
