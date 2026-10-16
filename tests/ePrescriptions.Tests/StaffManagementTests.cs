using System;
using System.Linq;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 32: تعديل موظف موجود، تعطيله/تفعيله، إعادة تعيين كلمة مروره، ورفض أي تعامل مع حساب Owner من هذا المسار</summary>
    public class StaffManagementTests
    {
#pragma warning disable CS0067
        private class FakeSession : ePrescriptions.App.Services.IUserSessionService
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

        private const string OldPass = "Right#1234";

        private readonly DatabaseContext _db;
        private readonly UserRepository _users;
        private readonly int _adminId;
        private readonly int _cashierId;

        public StaffManagementTests()
        {
            _db = new DatabaseContext(":memory:");
            _db.InitializeAndMigrate(seedData: true);
            _users = new UserRepository(_db);
            _adminId = _users.SaveStaffUser(0, "mgr1", "مدير الاختبار", "Admin#12345", UserRole.Admin, true);
            _cashierId = _users.SaveStaffUser(0, "cash1", "كاشير قديم", OldPass, UserRole.Cashier, true);
        }

        private StaffViewModel NewVm(User? actor)
        {
            var vm = new StaffViewModel(_users, new ShiftRepository(_db), new FakeSession { CurrentUser = actor });
            return vm;
        }

        private StaffViewModel AdminVm() => NewVm(_users.Authenticate("mgr1", "Admin#12345"));

        private static void Select(StaffViewModel vm, int id) => vm.SelectedUser = vm.UsersList.Single(u => u.Id == id);

        private User Stored(int id) => _users.GetAll().Single(u => u.Id == id);

        [Fact]
        public void EditExistingStaff_ShouldUpdateNameUsernameAndRole_AndKeepPassword()
        {
            var vm = AdminVm();
            Select(vm, _cashierId);

            vm.EditFullName.Should().Be("كاشير قديم"); // الحقول اتملت بالقيم الحالية
            vm.EditFullName = "  صيدلي جديد  ";
            vm.EditUsername = "pharm9";
            vm.EditRole = UserRole.Pharmacist;
            vm.SaveEdit();

            var u = Stored(_cashierId);
            u.FullName.Should().Be("صيدلي جديد");
            u.Username.Should().Be("pharm9");
            u.Role.Should().Be(UserRole.Pharmacist);
            u.IsActive.Should().BeTrue();
            _users.Authenticate("pharm9", OldPass).Should().NotBeNull("كلمة المرور ما اتغيّرتش بالتعديل");
            vm.StatusMessage.Should().Contain("تم تعديل");
        }

        [Fact]
        public void EditStaff_ShouldRejectBlankNames_AndDuplicateUsername()
        {
            var vm = AdminVm();
            Select(vm, _cashierId);

            vm.EditFullName = "   ";
            vm.SaveEdit();
            Stored(_cashierId).FullName.Should().Be("كاشير قديم");

            vm.EditFullName = "كاشير قديم"; // نفس الموظف لسه محدد فالحقل بيفضل زي ما كتبناه
            vm.EditUsername = "mgr1"; // مستخدم قبل كده
            vm.SaveEdit();
            Stored(_cashierId).Username.Should().Be("cash1");
            vm.StatusMessage.Should().Contain("مستخدم قبل كده");
        }

        [Fact]
        public void DisableStaff_ShouldBlockLogin_AndReEnableShouldRestoreIt()
        {
            var vm = AdminVm();
            Select(vm, _cashierId);
            vm.ToggleActiveButtonText.Should().Be("تعطيل الحساب");

            vm.ToggleActive();
            Stored(_cashierId).IsActive.Should().BeFalse();
            _users.Authenticate("cash1", OldPass).Should().BeNull("الحساب المعطّل ما يدخلش");
            vm.SelectedUser!.Id.Should().Be(_cashierId, "التحديد بيفضل بعد إعادة التحميل");
            vm.ToggleActiveButtonText.Should().Be("تفعيل الحساب");

            vm.ToggleActive();
            Stored(_cashierId).IsActive.Should().BeTrue();
            _users.Authenticate("cash1", OldPass).Should().NotBeNull();
        }

        [Fact]
        public void ResetPassword_ShouldReplaceOldPassword_AndClearLockout()
        {
            // نقفل الحساب الأول بمحاولات فاشلة
            for (int i = 0; i < UserRepository.MaxFailedAttempts; i++) _users.Authenticate("cash1", "wrong");
            _users.Authenticate("cash1", OldPass, out string lockMsg).Should().BeNull();
            lockMsg.Should().NotBeEmpty();

            var vm = AdminVm();
            Select(vm, _cashierId);
            vm.ResetPasswordText = "Brand#New99";
            vm.ResetPassword();

            _users.Authenticate("cash1", OldPass).Should().BeNull();
            _users.Authenticate("cash1", "Brand#New99").Should().NotBeNull("القفل اتفك والكلمة الجديدة شغالة");
            vm.ResetPasswordText.Should().BeEmpty("الحقل بيتفضّى بعد الحفظ");
        }

        [Fact]
        public void ResetPassword_ShouldRejectTooShortPassword_AndLeaveOldOneWorking()
        {
            var vm = AdminVm();
            Select(vm, _cashierId);
            vm.ResetPasswordText = "short";
            vm.ResetPassword();

            vm.StatusMessage.Should().Contain("8");
            _users.Authenticate("cash1", OldPass).Should().NotBeNull();
        }

        [Fact]
        public void OwnerAccount_ShouldBeRejectedFromAllThreeManagementActions()
        {
            var owner = _users.GetAll(includeOwner: true).Single(u => u.Role == UserRole.Owner);
            var vm = AdminVm();
            vm.SelectedUser = owner; // حتى لو اتحدد بالكود (الواجهة ما بتعرضوش أصلاً)

            vm.EditFullName = "منتحل"; vm.EditUsername = "hacked"; vm.EditRole = UserRole.Admin;
            vm.SaveEdit();
            vm.StatusMessage.Should().Contain("صاحب الصيدلية");
            vm.ToggleActive();
            vm.ResetPasswordText = "Hacked#12345";
            vm.ResetPassword();

            var after = _users.GetAll(includeOwner: true).Single(u => u.Id == owner.Id);
            after.Role.Should().Be(UserRole.Owner);
            after.Username.Should().Be(owner.Username);
            after.IsActive.Should().BeTrue();
            _users.Authenticate(owner.Username, DatabaseContext.DefaultOwnerPassword).Should().NotBeNull("كلمة مرور Owner ما اتغيّرتش");
        }

        [Fact]
        public void EditRole_ShouldRefuseAssigningOwnerRole()
        {
            var vm = AdminVm();
            Select(vm, _cashierId);
            vm.EditRole = UserRole.Owner;
            vm.SaveEdit();

            Stored(_cashierId).Role.Should().Be(UserRole.Cashier);
            _users.GetAll(includeOwner: true).Count(u => u.Role == UserRole.Owner).Should().Be(1);
        }

        [Fact]
        public void NonManagerSession_ShouldNotBeAllowedToManageStaff()
        {
            var cashier = _users.Authenticate("cash1", OldPass);
            var vm = NewVm(cashier);
            Select(vm, _adminId);

            vm.ToggleActive();
            vm.ResetPasswordText = "Hacked#12345";
            vm.ResetPassword();

            Stored(_adminId).IsActive.Should().BeTrue();
            _users.Authenticate("mgr1", "Admin#12345").Should().NotBeNull();
            vm.StatusMessage.Should().Contain("للمدير");
        }

        [Fact]
        public void Manager_ShouldNotDisableOrDemoteOwnAccount()
        {
            var vm = AdminVm();
            Select(vm, _adminId);

            vm.ToggleActive();
            Stored(_adminId).IsActive.Should().BeTrue();

            vm.EditRole = UserRole.Cashier;
            vm.SaveEdit();
            Stored(_adminId).Role.Should().Be(UserRole.Admin);
        }

        [Fact]
        public void OwnerSession_ShouldBeAbleToManageStaff()
        {
            var owner = _users.Authenticate(DatabaseContext.OwnerUsername, DatabaseContext.DefaultOwnerPassword);
            var vm = NewVm(owner);
            Select(vm, _cashierId);
            vm.ToggleActive();
            Stored(_cashierId).IsActive.Should().BeFalse();
        }

        [Fact]
        public void NoSelection_ShouldNotThrow_AndTellUserToSelect()
        {
            var vm = AdminVm();
            vm.SelectedUser.Should().BeNull();
            vm.HasSelectedUser.Should().BeFalse();
            vm.SaveEdit(); vm.ToggleActive(); vm.ResetPassword();
            vm.StatusMessage.Should().Contain("اختار");
        }

        [Fact]
        public void AddNewAdminUserNamedAdmin_WithPassword123456_ShouldAuthenticateSuccessfully()
        {
            var vm = AdminVm();
            vm.NewFullName = "ادمن";
            vm.NewUsername = string.Empty; // ترك اسم المستخدم فارغاً فينسخ الاسم
            vm.NewPassword = "123456";
            vm.NewRole = UserRole.Admin;
            vm.AddUser();

            vm.StatusMessage.Should().Contain("تم حفظ حساب الموظف");

            // الدخول بنفس الاسم العربي "ادمن" وبكلمة المرور "123456"
            var u1 = _users.Authenticate("ادمن", "123456");
            u1.Should().NotBeNull();
            u1!.FullName.Should().Be("ادمن");

            // الدخول ببديل الحروف الإنجليزية "admin"
            var u2 = _users.Authenticate("admin", "123456");
            u2.Should().NotBeNull();
            u2!.Id.Should().Be(u1.Id);

            // الدخول بأرقام عربية-هندية "١٢٣٤٥٦"
            var u3 = _users.Authenticate("ادمن", "١٢٣٤٥٦");
            u3.Should().NotBeNull();
            u3!.Id.Should().Be(u1.Id);

            // الدخول بألف مهموزة "أدمن"
            var u4 = _users.Authenticate("أدمن", "123456");
            u4.Should().NotBeNull();
            u4!.Id.Should().Be(u1.Id);

            // كلمة مرور خاطئة ترفض الدخول
            _users.Authenticate("ادمن", "wrongpass").Should().BeNull();
        }

        [Fact]
        public void CustomPermissions_CanBeConfiguredAndPersisted_ForStaffEmployee()
        {
            var vm = AdminVm();
            Select(vm, _cashierId);

            // تفعيل الصلاحيات المخصصة
            vm.IsCustomPermissions = true;
            vm.PermCanAccessSales = true;
            vm.PermCanAccessCustomers = true; // كاشير لكن مسموح له بالعملاء
            vm.PermCanAccessInventory = true; // ومسموح له بالمخزون
            vm.PermCanAccessReports = false;
            vm.PermMaxDiscountPercent = 15m;  // رفع سقف الخصم إلى 15%
            vm.PermCanPerformReturnsWithoutApproval = true; // السماح له بالمرتجع دون موافقة
            vm.SaveEdit();

            vm.StatusMessage.Should().Contain("تم تعديل بيانات وصلاحيات");

            // قراءة الموظف من قاعدة البيانات والتحقق من تطبيق الصلاحيات
            var user = _users.Authenticate("cash1", OldPass);
            user.Should().NotBeNull();
            user!.EffectivePermissions.IsCustom.Should().BeTrue();
            user.EffectivePermissions.CanAccessSales.Should().BeTrue();
            user.EffectivePermissions.CanAccessCustomers.Should().BeTrue();
            user.EffectivePermissions.CanAccessInventory.Should().BeTrue();
            user.EffectivePermissions.CanAccessReports.Should().BeFalse();
            user.EffectivePermissions.MaxDiscountPercent.Should().Be(15m);
            user.EffectivePermissions.CanPerformReturnsWithoutApproval.Should().BeTrue();

            // فحص ActionPermissionPolicy للمستخدم
            ePrescriptions.Domain.Helpers.ActionPermissionPolicy.MaxDiscountPercent(user).Should().Be(15m);
            ePrescriptions.Domain.Helpers.ActionPermissionPolicy.ReturnRequiresApproval(user).Should().BeFalse();
            user.PermissionsSummary.Should().Be("صلاحيات مخصصة");
        }

        [Fact]
        public void ResetPermissionsToRoleDefault_ShouldRevertToRoleDefaults()
        {
            var vm = AdminVm();
            Select(vm, _cashierId);

            // أولاً: تفعيل صلاحيات مخصصة وحفظها
            vm.IsCustomPermissions = true;
            vm.PermCanAccessFinance = true;
            vm.SaveEdit();
            Stored(_cashierId).EffectivePermissions.IsCustom.Should().BeTrue();

            // ثانياً: إعادة التعيين للافتراضي
            Select(vm, _cashierId);
            vm.ResetPermissionsToRoleDefaultCommand.Execute(null);
            vm.SavePermissionsCommand.Execute(null);

            var user = Stored(_cashierId);
            user.EffectivePermissions.IsCustom.Should().BeFalse();
            user.EffectivePermissions.CanAccessFinance.Should().BeFalse();
            user.EffectivePermissions.MaxDiscountPercent.Should().Be(5m);
            user.PermissionsSummary.Should().Be("افتراضي (كاشير)");
        }
    }
}

