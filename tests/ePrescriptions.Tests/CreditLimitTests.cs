using System;
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
    /// <summary>خطوة 11: مراقبة حد الائتمان في البيع الآجل</summary>
    public class CreditLimitTests
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

        private static long Scalar(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        private static Invoice OnAccountSale(int customerId, int itemId, long totalPiastres, int shiftId, int userId) => new Invoice
        {
            InvoiceNumber = "CR-" + Guid.NewGuid().ToString("N").Substring(0, 6),
            Type = InvoiceType.Sales, CustomerId = customerId, CustomerName = "مجمع النخبة الطبي (آجل)",
            PaymentMethod = PaymentMethod.OnAccount, UserId = userId, UserName = "اختبار", ShiftId = shiftId,
            SubtotalPiastres = totalPiastres, TotalPiastres = totalPiastres, PaidPiastres = 0, RemainingPiastres = totalPiastres,
            Items = { new InvoiceItem { ItemId = itemId, ItemCode = "MED-004", ItemNameAr = "نكسيوم", UnitLevel = UnitLevel.Piece, QuantitySold = 1, SmallestUnitsCount = 1, UnitPricePiastres = totalPiastres, TotalPiastres = totalPiastres } }
        };

        [Fact]
        public void Repository_ShouldRejectOnAccountSaleBeyondLimit_AndAllowItWithApprovedOverride()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var items = new ItemRepository(db);
            var invoices = new InvoiceRepository(db, items);
            int shiftId = new ShiftRepository(db).OpenShift(1, "اختبار", 0);
            int itemId = items.GetByBarcodeOrCode("6281001000404")!.Id;

            // العميل رقم 2: حد ائتمان 5000 جنيه (500000 قرش)
            invoices.SaveInvoice(OnAccountSale(2, itemId, 400000, shiftId, 1), out string ok1).Should().BeTrue();
            Scalar(db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id=2").Should().Be(400000);

            var over = OnAccountSale(2, itemId, 150000, shiftId, 1);
            invoices.SaveInvoice(over, out string err).Should().BeFalse();
            err.Should().Contain(InvoiceRepository.CreditLimitErrorPrefix);
            Scalar(db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id=2").Should().Be(400000);

            over.CreditLimitOverridden = true;
            invoices.SaveInvoice(over, out _).Should().BeTrue();
            Scalar(db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id=2").Should().Be(550000);
        }

        private static SalesViewModel BuildVm(DatabaseContext db, UserRole role, out Session session, bool withAudit = false)
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
                users.SaveStaffUser(0, name, "مستخدم " + role, "Pass#12345", role, true);
                user = users.Authenticate(name, "Pass#12345")!;
            }

            var shiftRepo = new ShiftRepository(db);
            shiftRepo.OpenShift(user.Id, user.FullName, 0);
            session = new Session { CurrentUser = user, CurrentShift = shiftRepo.GetCurrentOpenShift(user.Id) };

            var items = new ItemRepository(db);
            var vm = new SalesViewModel(items, new InvoiceRepository(db, items), new CustomerRepository(db), new SettingsRepository(db), null, session, shiftRepo,
                withAudit ? new OwnerAuditLogRepository(db) : null, withAudit ? new StaffAuditLogRepository(db) : null);
            vm.AddItem(items.GetByBarcodeOrCode("6281001000404"));
            vm.CartItems[0].Quantity = 100; // 100 علبة × 88 جنيه = 8800 جنيه > حد 5000
            vm.CustomerName = "مجمع النخبة الطبي (آجل)";
            vm.SelectedPaymentMethod = PaymentMethod.OnAccount;
            return vm;
        }

        [Theory]
        [InlineData(UserRole.Cashier)]
        [InlineData(UserRole.Pharmacist)]
        public void SalesViewModel_ShouldRefuseOverLimitCreditSaleForCashierAndPharmacist_EvenIfConfirmWouldSayYes(UserRole role)
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var vm = BuildVm(db, role, out _);
            bool askedForConfirm = false;
            vm.ConfirmCreditLimitOverride = _ => { askedForConfirm = true; return true; };

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);
            askedForConfirm.Should().BeFalse();
            vm.IsStatusSuccess.Should().BeFalse();
            vm.StatusMessage.Should().Contain("حد الائتمان");
        }

        [Fact]
        public void SalesViewModel_ShouldAllowOverLimitCreditSaleForAdminOnlyAfterConfirmation()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var vm = BuildVm(db, UserRole.Admin, out _);

            vm.ConfirmCreditLimitOverride = _ => false;
            vm.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);

            vm.ConfirmCreditLimitOverride = _ => true;
            vm.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1);
            Scalar(db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id=2").Should().Be(880000);
        }

        [Fact]
        public void AdminOverride_ShouldBeLoggedInStaffAuditLogWithCustomerAmountLimitAndApprover()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var vm = BuildVm(db, UserRole.Admin, out _, withAudit: true);
            vm.ConfirmCreditLimitOverride = _ => true;

            vm.SaveCurrentInvoice();

            var staff = new StaffAuditLogRepository(db).GetRecent();
            staff.Should().ContainSingle(e => e.EntityType == "CreditLimitOverride");
            var entry = staff.Find(e => e.EntityType == "CreditLimitOverride")!;
            entry.EntityLabel.Should().Contain("مجمع النخبة الطبي");
            entry.ActorUserName.Should().Contain("Admin");
            entry.OldValue.Should().Contain("5000.00");
            entry.NewValue.Should().Contain("8800.00");
            entry.NewValue.Should().Contain("وافق");
            new OwnerAuditLogRepository(db).GetRecent().Should().BeEmpty();
        }

        [Fact]
        public void OwnerOverride_ShouldBeLoggedOnlyInOwnerAuditLog()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var vm = BuildVm(db, UserRole.Owner, out _, withAudit: true);
            vm.ConfirmCreditLimitOverride = _ => true;

            vm.SaveCurrentInvoice();

            new OwnerAuditLogRepository(db).GetRecent().Should().ContainSingle(e => e.EntityType == "CreditLimitOverride");
            new StaffAuditLogRepository(db).GetRecent().Should().BeEmpty();
        }
    }
}
