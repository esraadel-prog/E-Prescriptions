using System;
using System.IO;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class SessionAndLabelTests
    {
        [Fact]
        public void UserSessionService_LoginAndLock_ShouldWorkAccurately()
        {
            // Arrange
            string tempDbPath = Path.Combine(Path.GetTempPath(), $"test_auth_{Guid.NewGuid():N}.db");
            var dbContext = new DatabaseContext(tempDbPath);
            dbContext.InitializeAndMigrate(seedData: true);

            var userRepo = new UserRepository(dbContext);
            var shiftRepo = new ShiftRepository(dbContext);
            var sessionService = new UserSessionService(userRepo, shiftRepo);

            // بيانات البذرة لم تعد تحتوي حسابات مستخدمين (خطوة 1)، فننشئ مديراً بكلمة مرور معروفة للاختبار
            userRepo.SaveStaffUser(0, "admin", "مدير الاختبار", "admin123", UserRole.Admin, true);

            try
            {
                // Act: تسجيل دخول بكلمة مرور خاطئة
                bool failed = sessionService.Login("admin", "wrongpassword", out string errorMsg);
                failed.Should().BeFalse();
                sessionService.IsAuthenticated.Should().BeFalse();
                errorMsg.Should().NotBeEmpty();

                // Act: تسجيل دخول بكلمة المرور الصحيحة
                bool success = sessionService.Login("admin", "admin123", out string okMsg);
                success.Should().BeTrue();
                sessionService.IsAuthenticated.Should().BeTrue();
                sessionService.CurrentUser.Should().NotBeNull();
                sessionService.CurrentUser!.FullName.Should().Contain("مدير الاختبار");

                // Act: قفل الشاشة
                sessionService.Lock();
                sessionService.IsLocked.Should().BeTrue();

                // Act: فك القفل بكلمة مرور خاطئة
                bool unlockFail = sessionService.Unlock("wrong", out string unlockErr);
                unlockFail.Should().BeFalse();
                sessionService.IsLocked.Should().BeTrue();

                // Act: فك القفل بكلمة المرور الصحيحة
                bool unlockOk = sessionService.Unlock("admin123", out string unlockOkErr);
                unlockOk.Should().BeTrue();
                sessionService.IsLocked.Should().BeFalse();

                // Act: تسجيل خروج
                sessionService.Logout();
                sessionService.IsAuthenticated.Should().BeFalse();
                sessionService.CurrentUser.Should().BeNull();
            }
            finally
            {
                try { if (File.Exists(tempDbPath)) File.Delete(tempDbPath); } catch { }
            }
        }

        [Fact]
        public void LoginViewModel_InitialState_ShouldHaveNoDefaultUsername()
        {
            // Arrange
            string tempDbPath = Path.Combine(Path.GetTempPath(), $"test_vm_{Guid.NewGuid():N}.db");
            var dbContext = new DatabaseContext(tempDbPath);
            dbContext.InitializeAndMigrate(seedData: true);

            var userRepo = new UserRepository(dbContext);
            var shiftRepo = new ShiftRepository(dbContext);
            var sessionService = new UserSessionService(userRepo, shiftRepo);
            var vm = new LoginViewModel(sessionService);

            try
            {
                // Assert
                vm.Username.Should().BeEmpty(); // خطوة 18: لا اسم مستخدم افتراضي
                vm.IsLockMode.Should().BeFalse();
                vm.LoginErrorMessage.Should().BeEmpty();
            }
            finally
            {
                try { if (File.Exists(tempDbPath)) File.Delete(tempDbPath); } catch { }
            }
        }
    }
}
