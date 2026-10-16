using System;
using System.Linq;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class OwnerHiddenAccountTests
    {
        [Fact]
        public void SeedData_ShouldCreateExactlyOneOwnerAccount_AutomaticallyOnFirstRun()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var userRepo = new UserRepository(db);

            var allIncludingOwner = userRepo.GetAll(includeOwner: true);
            allIncludingOwner.Count(u => u.Role == UserRole.Owner).Should().Be(1);
        }

        [Fact]
        public void GetAll_Default_ShouldNeverIncludeOwnerAccount()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var userRepo = new UserRepository(db);

            var visibleUsers = userRepo.GetAll();
            visibleUsers.Should().NotContain(u => u.Role == UserRole.Owner);
        }

        [Fact]
        public void Authenticate_ShouldStillLogInOwnerAccount_EvenThoughHiddenFromLists()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var userRepo = new UserRepository(db);

            var owner = userRepo.Authenticate("pharmacy_owner", "ChangeMe@Owner1");
            owner.Should().NotBeNull();
            owner!.Role.Should().Be(UserRole.Owner);
        }

        [Fact]
        public void SaveStaffUser_ShouldRejectCreatingOrEditingAnOwnerRoleAccount()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var userRepo = new UserRepository(db);

            Action createOwner = () => userRepo.SaveStaffUser(0, "fakeowner", "محاولة انتحال", "Pass@1234", UserRole.Owner, true);
            createOwner.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void SaveStaffUser_ShouldRejectEditingTheRealOwnerAccountById()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var userRepo = new UserRepository(db);

            var owner = userRepo.GetAll(includeOwner: true).Single(u => u.Role == UserRole.Owner);

            Action editOwner = () => userRepo.SaveStaffUser(owner.Id, "hacked", "تم", null, UserRole.Admin, true);
            editOwner.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void OwnerAuditLog_ShouldRecordAndRetrieveFieldChanges_InReverseChronologicalOrder()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var ownerAuditRepo = new OwnerAuditLogRepository(db);

            ownerAuditRepo.LogChange(1, "Item", "5", "بروفين 400مجم", "TotalStockSmallestUnits", "11", "10");
            ownerAuditRepo.LogChange(1, "Item", "5", "بروفين 400مجم", "RetailPriceBoxPiastres", "2500", "2200");

            var recent = ownerAuditRepo.GetRecent(10);
            recent.Should().HaveCount(2);
            recent[0].FieldName.Should().Be("RetailPriceBoxPiastres"); // الأحدث أولاً
            recent[0].OldValue.Should().Be("2500");
            recent[0].NewValue.Should().Be("2200");
        }

        [Fact]
        public void StaffAuditLog_ShouldRecordActorNameAndRole_SeparatelyFromOwnerAuditLog()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var staffAuditRepo = new StaffAuditLogRepository(db);
            var ownerAuditRepo = new OwnerAuditLogRepository(db);

            // مدير يعدّل صنف: يُسجَّل في StaffAuditLog فقط
            staffAuditRepo.LogChange(2, "محمد المدير", "Admin", "Item", "5", "بروفين 400مجم", "TotalStockSmallestUnits", "11", "10");

            var staffLogs = staffAuditRepo.GetRecent(10);
            staffLogs.Should().HaveCount(1);
            staffLogs[0].ActorUserName.Should().Be("محمد المدير");
            staffLogs[0].ActorRole.Should().Be("Admin");

            ownerAuditRepo.GetRecent(10).Should().BeEmpty();
        }

        [Fact]
        public void OwnerAndStaffAuditLogs_ShouldNeverMixEntries_EvenForTheSameEntity()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var staffAuditRepo = new StaffAuditLogRepository(db);
            var ownerAuditRepo = new OwnerAuditLogRepository(db);

            // نفس الصنف، تعديل من صاحب الصيدلية وتعديل من كاشير
            ownerAuditRepo.LogChange(1, "Item", "5", "بروفين 400مجم", "RetailPriceBoxPiastres", "2500", "2200");
            staffAuditRepo.LogChange(3, "سارة الكاشير", "Cashier", "Item", "5", "بروفين 400مجم", "Barcode", "111", "222");

            ownerAuditRepo.GetRecent(10).Should().HaveCount(1);
            staffAuditRepo.GetRecent(10).Should().HaveCount(1);

            ownerAuditRepo.GetRecent(10)[0].FieldName.Should().Be("RetailPriceBoxPiastres");
            staffAuditRepo.GetRecent(10)[0].FieldName.Should().Be("Barcode");
        }

        [Fact]
        public void ChangeOwnPassword_ShouldSucceed_OnlyWithCorrectCurrentPassword_AndAllowLoginWithNewOne()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var userRepo = new UserRepository(db);
            var owner = userRepo.Authenticate("pharmacy_owner", "ChangeMe@Owner1")!;

            userRepo.ChangeOwnPassword(owner.Id, "WrongPassword", "NewStrongPass1", out string err1).Should().BeFalse();
            err1.Should().NotBeNullOrEmpty();

            userRepo.ChangeOwnPassword(owner.Id, "ChangeMe@Owner1", "NewStrongPass1", out string err2).Should().BeTrue();

            userRepo.Authenticate("pharmacy_owner", "ChangeMe@Owner1").Should().BeNull();
            userRepo.Authenticate("pharmacy_owner", "NewStrongPass1").Should().NotBeNull();
        }

        [Fact]
        public void OwnerAuditLog_ShouldNotRecordWhenOldAndNewValuesAreIdentical()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var ownerAuditRepo = new OwnerAuditLogRepository(db);

            ownerAuditRepo.LogChange(1, "Item", "5", "بروفين 400مجم", "NameAr", "بروفين 400مجم", "بروفين 400مجم");

            ownerAuditRepo.GetRecent(10).Should().BeEmpty();
        }
    }
}
