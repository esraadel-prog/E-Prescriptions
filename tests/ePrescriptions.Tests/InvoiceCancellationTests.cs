using System;
using System.Linq;
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
    /// <summary>خطوة 16: إلغاء الفاتورة (Admin/Owner) يرجّع المخزون ويعكس الأثر ويتسجل في التدقيق</summary>
    public class InvoiceCancellationTests
    {
#pragma warning disable CS0067
        private class FakeSession : IUserSessionService
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

        private class Ctx
        {
            public DatabaseContext Db = new DatabaseContext(":memory:");
            public ItemRepository Items = null!;
            public InvoiceRepository Invoices = null!;
            public ShiftRepository Shifts = null!;
            public Item Item = null!;
            public int ShiftId;
        }

        private static Ctx Setup()
        {
            var c = new Ctx();
            c.Db.InitializeAndMigrate(seedData: true);
            c.Items = new ItemRepository(c.Db);
            c.Invoices = new InvoiceRepository(c.Db, c.Items);
            c.Shifts = new ShiftRepository(c.Db);
            c.ShiftId = c.Shifts.OpenShift(1, "اختبار", 0);
            c.Item = c.Items.GetByBarcodeOrCode("6281001000101")!;
            return c;
        }

        private static long Scalar(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        private static Invoice Build(Ctx c, InvoiceType type, PaymentMethod method, long total, int customerId = 1, Invoice? original = null)
        {
            bool onAccount = method == PaymentMethod.OnAccount;
            var inv = new Invoice
            {
                InvoiceNumber = c.Invoices.GenerateNextInvoiceNumber(type), Type = type, CustomerId = customerId,
                CustomerName = "عميل", PaymentMethod = method, UserId = 1, UserName = "اختبار", ShiftId = c.ShiftId,
                SubtotalPiastres = total, TotalPiastres = total,
                PaidPiastres = onAccount ? 0 : total, RemainingPiastres = onAccount ? total : 0,
                Items =
                {
                    new InvoiceItem
                    {
                        ItemId = c.Item.Id, ItemCode = c.Item.Code, ItemNameAr = c.Item.NameAr,
                        BatchNumber = c.Item.Batches.Count > 0 ? c.Item.Batches[0].BatchNumber : "عام",
                        UnitLevel = UnitLevel.Piece, QuantitySold = 10, SmallestUnitsCount = 10,
                        UnitPricePiastres = total / 10, TotalPiastres = total,
                        OriginalInvoiceItemId = original?.Items[0].Id
                    }
                }
            };
            inv.OriginalInvoiceId = original?.Id;
            return inv;
        }

        [Fact]
        public void CancelSale_ShouldRestoreStockBatchShiftAndJournal_AndMarkCancelled()
        {
            var c = Setup();
            int stockBefore = c.Items.GetById(c.Item.Id)!.TotalStockSmallestUnits;
            long batchBefore = Scalar(c.Db, $"SELECT SUM(RemainingSmallestUnits) FROM ItemBatches WHERE ItemId = {c.Item.Id}");
            var sale = Build(c, InvoiceType.Sales, PaymentMethod.Cash, 5000);
            c.Invoices.SaveInvoice(sale, out _).Should().BeTrue();
            c.Items.GetById(c.Item.Id)!.TotalStockSmallestUnits.Should().Be(stockBefore - 10);

            c.Invoices.CancelInvoice(sale.Id, 1, "مدير", "خطأ في الإدخال", out string err).Should().BeTrue();
            err.Should().BeEmpty();

            c.Items.GetById(c.Item.Id)!.TotalStockSmallestUnits.Should().Be(stockBefore);
            Scalar(c.Db, $"SELECT SUM(RemainingSmallestUnits) FROM ItemBatches WHERE ItemId = {c.Item.Id}").Should().Be(batchBefore);
            Scalar(c.Db, $"SELECT Status FROM Invoices WHERE Id = {sale.Id}").Should().Be(3);
            Scalar(c.Db, $"SELECT COUNT(*) FROM Invoices WHERE Id = {sale.Id} AND CancelReason = 'خطأ في الإدخال' AND CancelledByName = 'مدير'").Should().Be(1);
            var shift = c.Shifts.GetCurrentOpenShift(1)!;
            shift.ExpectedCashPiastres.Should().Be(0);
            shift.TotalSalesCashPiastres.Should().Be(0);
            // القيد العكسي بيصفّر أثر الفاتورة: مجموع المدين = مجموع الدائن لكل حساب = 0
            Scalar(c.Db, $"SELECT COUNT(*) FROM JournalEntries WHERE EntryNumber LIKE 'JV-CXL-{sale.InvoiceNumber}-%'").Should().Be(1);
            Scalar(c.Db, $"SELECT SUM(AmountPiastres) FROM JournalEntries WHERE DebitAccount = '{Accounts.Cash}' AND EntryNumber LIKE '%{sale.InvoiceNumber}%'").Should().Be(5000);
            Scalar(c.Db, $"SELECT SUM(AmountPiastres) FROM JournalEntries WHERE CreditAccount = '{Accounts.Cash}' AND EntryNumber LIKE '%{sale.InvoiceNumber}%'").Should().Be(5000);
        }

        [Fact]
        public void CancelOnAccountSale_ShouldReduceCustomerBalanceBack()
        {
            var c = Setup();
            long before = Scalar(c.Db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id = 2");
            var sale = Build(c, InvoiceType.Sales, PaymentMethod.OnAccount, 4000, customerId: 2);
            c.Invoices.SaveInvoice(sale, out _).Should().BeTrue();
            Scalar(c.Db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id = 2").Should().Be(before + 4000);

            c.Invoices.CancelInvoice(sale.Id, 1, "مدير", "سبب", out _).Should().BeTrue();

            Scalar(c.Db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id = 2").Should().Be(before);
            c.Shifts.GetCurrentOpenShift(1)!.TotalSalesOnAccountPiastres.Should().Be(0);
        }

        [Fact]
        public void Cancel_ShouldBeRefused_ForDoubleCancel_Returns_ClosedShift_AndMissingReason_WithNoSideEffects()
        {
            var c = Setup();
            int stock0 = c.Items.GetById(c.Item.Id)!.TotalStockSmallestUnits;

            var sale = Build(c, InvoiceType.Sales, PaymentMethod.Cash, 5000);
            c.Invoices.SaveInvoice(sale, out _).Should().BeTrue();
            c.Invoices.CancelInvoice(sale.Id, 1, "م", "  ", out string noReason).Should().BeFalse();
            noReason.Should().Contain("سبب");

            c.Invoices.CancelInvoice(sale.Id, 1, "م", "س", out _).Should().BeTrue();
            c.Invoices.CancelInvoice(sale.Id, 1, "م", "س", out string twice).Should().BeFalse();
            twice.Should().Contain("ملغاة");
            c.Items.GetById(c.Item.Id)!.TotalStockSmallestUnits.Should().Be(stock0); // ما رجعش المخزون مرتين

            // فاتورة عليها مرتجع
            var sale2 = Build(c, InvoiceType.Sales, PaymentMethod.Cash, 5000);
            c.Invoices.SaveInvoice(sale2, out _).Should().BeTrue();
            var ret = Build(c, InvoiceType.Return, PaymentMethod.Cash, 5000, original: sale2);
            c.Invoices.SaveInvoice(ret, out string retErr).Should().BeTrue();
            c.Invoices.CancelInvoice(sale2.Id, 1, "م", "س", out string withReturn).Should().BeFalse();
            withReturn.Should().Contain("مرتجع");

            // فاتورة مرتجع نفسها ما تتلغيش
            c.Invoices.CancelInvoice(ret.Id, 1, "م", "س", out string retCancel).Should().BeFalse();

            // وردية مقفولة
            var sale3 = Build(c, InvoiceType.Sales, PaymentMethod.Cash, 5000);
            c.Invoices.SaveInvoice(sale3, out _).Should().BeTrue();
            c.Shifts.CloseShift(c.ShiftId, 5000, null);
            c.Invoices.CancelInvoice(sale3.Id, 1, "م", "س", out string closed).Should().BeFalse();
            closed.Should().Contain("مغلقة");
            Scalar(c.Db, $"SELECT Status FROM Invoices WHERE Id = {sale3.Id}").Should().Be(1);
        }

        [Fact]
        public void ReturnOnCancelledInvoice_ShouldBeRefused()
        {
            var c = Setup();
            var sale = Build(c, InvoiceType.Sales, PaymentMethod.Cash, 5000);
            c.Invoices.SaveInvoice(sale, out _).Should().BeTrue();
            c.Invoices.CancelInvoice(sale.Id, 1, "م", "س", out _).Should().BeTrue();

            c.Invoices.GetReturnableInvoice(sale.InvoiceNumber, out string lookupErr).Should().BeNull();
            lookupErr.Should().Contain("ملغاة");

            var ret = Build(c, InvoiceType.Return, PaymentMethod.Cash, 5000, original: sale);
            c.Invoices.SaveInvoice(ret, out string err).Should().BeFalse();
            err.Should().Contain("ملغاة");
        }

        [Fact]
        public void CancelledInvoice_ShouldDisappearFromSalesReportsVatCashAndCustomerStatement()
        {
            var c = Setup();
            var keep = Build(c, InvoiceType.Sales, PaymentMethod.Cash, 3000);
            c.Invoices.SaveInvoice(keep, out string e1).Should().BeTrue(e1);
            var cancelled = Build(c, InvoiceType.Sales, PaymentMethod.OnAccount, 7000, customerId: 2);
            c.Invoices.SaveInvoice(cancelled, out string e2).Should().BeTrue(e2);
            c.Invoices.CancelInvoice(cancelled.Id, 1, "م", "س", out string e3).Should().BeTrue(e3);

            var from = DateTime.Today.AddDays(-1); var to = DateTime.Today.AddDays(1);
            var report = new ReportRepository(c.Db);
            report.GetSalesReport(from, to, null).Select(r => r.InvoiceNumber).Should().Equal(keep.InvoiceNumber);
            new FinanceRepository(c.Db).GetVatReturnReport(from, to).TaxableSalesPiastres.Should().Be(3000);
            new CustomerRepository(c.Db).GetCustomerStatement(2).Any(r => r.ReferenceNumber == cancelled.InvoiceNumber).Should().BeFalse();
        }

        private static (SalesViewModel vm, Ctx c, Invoice sale) VmFor(UserRole role)
        {
            var c = Setup();
            var users = new UserRepository(c.Db);
            User user;
            if (role == UserRole.Owner) user = users.Authenticate(DatabaseContext.OwnerUsername, DatabaseContext.DefaultOwnerPassword)!;
            else
            {
                users.SaveStaffUser(0, "u" + role, "مستخدم " + role, "Pass#12345", role, true);
                user = users.Authenticate("u" + role, "Pass#12345")!;
            }
            var sale = Build(c, InvoiceType.Sales, PaymentMethod.Cash, 5000);
            c.Invoices.SaveInvoice(sale, out _).Should().BeTrue();
            var session = new FakeSession { CurrentUser = user, CurrentShift = c.Shifts.GetCurrentOpenShift(1) };
            var vm = new SalesViewModel(c.Items, c.Invoices, new CustomerRepository(c.Db), new SettingsRepository(c.Db), null, session, c.Shifts,
                new OwnerAuditLogRepository(c.Db), new StaffAuditLogRepository(c.Db));
            vm.ConfirmCancelInvoice = _ => true;
            vm.CancelInvoiceNumber = sale.InvoiceNumber;
            vm.CancelReason = "طلب العميل";
            return (vm, c, sale);
        }

        [Theory]
        [InlineData(UserRole.Cashier)]
        [InlineData(UserRole.Pharmacist)]
        public void CashierAndPharmacist_ShouldNotBeAbleToCancel(UserRole role)
        {
            var (vm, c, sale) = VmFor(role);

            vm.CanCancelInvoice.Should().BeFalse();
            vm.CancelInvoice();

            Scalar(c.Db, $"SELECT Status FROM Invoices WHERE Id = {sale.Id}").Should().Be(1);
            new StaffAuditLogRepository(c.Db).GetRecent().Should().BeEmpty();
        }

        [Fact]
        public void AdminCancel_ShouldBeLoggedInStaffAuditLogOnly()
        {
            var (vm, c, sale) = VmFor(UserRole.Admin);

            vm.CanCancelInvoice.Should().BeTrue();
            vm.CancelInvoice();

            Scalar(c.Db, $"SELECT Status FROM Invoices WHERE Id = {sale.Id}").Should().Be(3);
            var entry = new StaffAuditLogRepository(c.Db).GetRecent().Find(e => e.EntityType == "InvoiceCancel")!;
            entry.Should().NotBeNull();
            entry.EntityLabel.Should().Be(sale.InvoiceNumber);
            entry.NewValue.Should().Contain("طلب العميل");
            new OwnerAuditLogRepository(c.Db).GetRecent().Should().BeEmpty();
        }

        [Fact]
        public void OwnerCancel_ShouldBeLoggedInOwnerAuditLogOnly_AndDeclinedConfirmationCancelsNothing()
        {
            var (vm, c, sale) = VmFor(UserRole.Owner);

            vm.ConfirmCancelInvoice = _ => false;
            vm.CancelInvoice();
            Scalar(c.Db, $"SELECT Status FROM Invoices WHERE Id = {sale.Id}").Should().Be(1);

            vm.ConfirmCancelInvoice = _ => true;
            vm.CancelInvoice();
            Scalar(c.Db, $"SELECT Status FROM Invoices WHERE Id = {sale.Id}").Should().Be(3);
            new OwnerAuditLogRepository(c.Db).GetRecent().Should().ContainSingle(e => e.EntityType == "InvoiceCancel");
            new StaffAuditLogRepository(c.Db).GetRecent().Should().BeEmpty();
        }
    }
}
