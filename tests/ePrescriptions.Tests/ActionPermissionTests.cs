using System;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using ePrescriptions.Domain.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 33: صلاحيات على مستوى الإجراء (سقف خصم لكل دور + موافقة Admin/Owner)</summary>
    public class ActionPermissionTests
    {
#pragma warning disable CS0067
        private class Session : IUserSessionService
        {
            public User? CurrentUser { get; set; }
            public Shift? CurrentShift { get; set; }
            public bool IsAuthenticated => CurrentUser != null;
            public bool IsLocked => false;
            public event Action? SessionChanged;
            public event Action<bool>? LockStateChanged;
            public bool Login(string u, string p, out string e) { e = string.Empty; return false; }
            public void Logout() { }
            public void Lock() { }
            public bool Unlock(string p, out string e) { e = string.Empty; return false; }
            public void SetCurrentShift(Shift? s) { CurrentShift = s; }
        }
#pragma warning restore CS0067

        private const string Pass = "Pass#12345";

        private static long Scalar(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        private static DatabaseContext NewDb()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            return db;
        }

        private static SalesViewModel BuildVm(DatabaseContext db, UserRole role)
        {
            var users = new UserRepository(db);
            User user;
            if (role == UserRole.Owner)
            {
                user = users.Authenticate(DatabaseContext.OwnerUsername, DatabaseContext.DefaultOwnerPassword)!;
            }
            else
            {
                string name = "u" + role;
                if (users.Authenticate(name, Pass) == null) users.SaveStaffUser(0, name, "مستخدم " + role, Pass, role, true);
                user = users.Authenticate(name, Pass)!;
            }

            var shiftRepo = new ShiftRepository(db);
            if (shiftRepo.GetCurrentOpenShift(user.Id) == null) shiftRepo.OpenShift(user.Id, user.FullName, 0);
            var session = new Session { CurrentUser = user, CurrentShift = shiftRepo.GetCurrentOpenShift(user.Id) };

            var items = new ItemRepository(db);
            return new SalesViewModel(items, new InvoiceRepository(db, items), new CustomerRepository(db), new SettingsRepository(db), null, session, shiftRepo,
                new OwnerAuditLogRepository(db), new StaffAuditLogRepository(db), users);
        }

        /// <summary>سلة فيها علبة واحدة (88 جنيه = 8800 قرش) بخصم نسبته percent٪</summary>
        private static void FillCart(SalesViewModel vm, int discountPercent)
        {
            vm.AddItem(new ItemRepository(vm_db(vm)).GetByBarcodeOrCode("6281001000404"));
            vm.CartItems[0].Quantity = 1;
            vm.CartItems[0].DiscountPiastres = 8800L * discountPercent / 100;
        }

        // الـ ViewModel ما بيكشفش الـ db؛ بنخزّنها هنا لكل اختبار
        private static DatabaseContext? _current;
        private static DatabaseContext vm_db(SalesViewModel _) => _current!;

        private static SalesViewModel Build(UserRole role, int discountPercent, out DatabaseContext db)
        {
            db = NewDb();
            _current = db;
            var vm = BuildVm(db, role);
            FillCart(vm, discountPercent);
            return vm;
        }

        [Fact]
        public void Policy_CashierCapShouldBeLowerThanPharmacist_AndManagersUnlimited()
        {
            ActionPermissionPolicy.MaxDiscountPercent(UserRole.Cashier).Should().BeLessThan(ActionPermissionPolicy.MaxDiscountPercent(UserRole.Pharmacist));
            ActionPermissionPolicy.MaxDiscountPercent(UserRole.Admin).Should().Be(100m);
            ActionPermissionPolicy.MaxDiscountPercent(UserRole.Owner).Should().Be(100m);
            ActionPermissionPolicy.DiscountExceedsRoleCap(UserRole.Cashier, 10000, 500).Should().BeFalse("المساواة بالسقف مسموحة");
            ActionPermissionPolicy.DiscountExceedsRoleCap(UserRole.Cashier, 10000, 501).Should().BeTrue();
        }

        [Fact]
        public void Cashier_ShouldBeRejected_WhenDiscountExceedsCashierCap()
        {
            var vm = Build(UserRole.Cashier, 10, out var db);

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);
            vm.IsStatusSuccess.Should().BeFalse();
            vm.StatusMessage.Should().Contain("موافقة");
            vm.NeedsManagerApproval.Should().BeTrue();
        }

        [Fact]
        public void Cashier_ShouldBeAllowed_WhenDiscountWithinCap_WithoutApproval()
        {
            var vm = Build(UserRole.Cashier, 5, out var db);

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1);
            vm.NeedsManagerApproval.Should().BeFalse();
        }

        [Fact]
        public void SameDiscount_ShouldBeAllowedForCashier_WhenAdminApproves_AndLoggedUnderApprover()
        {
            var vm = Build(UserRole.Cashier, 10, out var db);
            new UserRepository(db).SaveStaffUser(0, "mgr1", "المدير أحمد", Pass, UserRole.Admin, true);
            vm.ApproverUsername = "mgr1";
            vm.ApproverPassword = Pass;

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1);
            vm.NeedsManagerApproval.Should().BeFalse();
            vm.ApproverPassword.Should().BeEmpty();
            var entry = new StaffAuditLogRepository(db).GetRecent().Find(e => e.EntityType == "DiscountApproval");
            entry.Should().NotBeNull();
            entry!.ActorUserName.Should().Contain("المدير أحمد");
            entry.NewValue.Should().Contain("وافق");
            new OwnerAuditLogRepository(db).GetRecent().Should().BeEmpty();
        }

        [Fact]
        public void SameDiscount_ShouldBeAllowedForPharmacist_AboveHisCap_WhenOwnerApproves_AndLoggedOnlyInOwnerLog()
        {
            var vm = Build(UserRole.Pharmacist, 20, out var db);
            vm.ApproverUsername = DatabaseContext.OwnerUsername;
            vm.ApproverPassword = DatabaseContext.DefaultOwnerPassword;

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1);
            new OwnerAuditLogRepository(db).GetRecent().Should().ContainSingle(e => e.EntityType == "DiscountApproval");
            new StaffAuditLogRepository(db).GetRecent().Should().BeEmpty();
        }

        [Fact]
        public void Pharmacist_ShouldBeAllowedTenPercent_ThatCashierCannot()
        {
            var vm = Build(UserRole.Pharmacist, 10, out var db);

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1);
        }

        [Fact]
        public void Approval_ShouldBeRefused_ForWrongPassword_NonManagerApprover_AndDisabledManager()
        {
            var vm = Build(UserRole.Cashier, 10, out var db);
            var users = new UserRepository(db);
            users.SaveStaffUser(0, "mgr2", "مدير", Pass, UserRole.Admin, true);
            users.SaveStaffUser(0, "pharm2", "صيدلي تاني", Pass, UserRole.Pharmacist, true);
            users.SaveStaffUser(0, "mgr3", "مدير معطّل", Pass, UserRole.Admin, false);

            vm.ApproverUsername = "mgr2"; vm.ApproverPassword = "wrong";
            vm.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);

            vm.ApproverUsername = "pharm2"; vm.ApproverPassword = Pass;
            vm.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);
            vm.StatusMessage.Should().Contain("مدير");

            vm.ApproverUsername = "mgr3"; vm.ApproverPassword = Pass;
            vm.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);
        }

        [Theory]
        [InlineData(UserRole.Admin)]
        [InlineData(UserRole.Owner)]
        public void Managers_ShouldNotNeedApproval_ForLargeDiscount(UserRole role)
        {
            var vm = Build(role, 40, out var db);

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1);
            vm.NeedsManagerApproval.Should().BeFalse();
        }

        private static string SellAsAdminAndGetNumber(DatabaseContext db)
        {
            _current = db;
            var admin = BuildVm(db, UserRole.Admin);
            FillCart(admin, 0);
            string number = admin.InvoiceNumber;
            admin.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1);
            return number;
        }

        [Theory]
        [InlineData(UserRole.Cashier)]
        [InlineData(UserRole.Pharmacist)]
        public void Return_ShouldBeRejectedWithoutManagerApproval(UserRole role)
        {
            var db = NewDb();
            string number = SellAsAdminAndGetNumber(db);
            var vm = BuildVm(db, role);
            vm.ToggleReturnMode();
            vm.OriginalInvoiceNumber = number;
            vm.LoadOriginalInvoice();
            vm.CartItems.Count.Should().BeGreaterThan(0);

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1, "المرتجع ما اتسجلش");
            vm.NeedsManagerApproval.Should().BeTrue();
            vm.StatusMessage.Should().Contain("مرتجع");
        }

        [Fact]
        public void Return_ShouldSucceedForCashier_WhenAdminApproves_AndBeLoggedAsReturnApproval()
        {
            var db = NewDb();
            string number = SellAsAdminAndGetNumber(db);
            new UserRepository(db).SaveStaffUser(0, "mgr4", "مدير المرتجعات", Pass, UserRole.Admin, true);
            var vm = BuildVm(db, UserRole.Cashier);
            vm.ToggleReturnMode();
            vm.OriginalInvoiceNumber = number;
            vm.LoadOriginalInvoice();
            vm.ApproverUsername = "mgr4";
            vm.ApproverPassword = Pass;

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(2);
            new StaffAuditLogRepository(db).GetRecent().Should().ContainSingle(e => e.EntityType == "ReturnApproval");
        }

        [Fact]
        public void Return_ShouldNotNeedApproval_ForAdmin()
        {
            var db = NewDb();
            string number = SellAsAdminAndGetNumber(db);
            var vm = BuildVm(db, UserRole.Admin);
            vm.ToggleReturnMode();
            vm.OriginalInvoiceNumber = number;
            vm.LoadOriginalInvoice();

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(2);
        }
    }
}
