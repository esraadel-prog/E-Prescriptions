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
    /// <summary>خطوة 36: الدفع المقسّم (طريقتين دفع أو أكتر في نفس الفاتورة)</summary>
    public class SplitPaymentTests
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
            var val = cmd.ExecuteScalar();
            return (val == null || Convert.IsDBNull(val)) ? 0 : Convert.ToInt64(val);
        }

        private static Invoice Sale(Ctx c, long total, PaymentMethod method, int customerId, params (PaymentMethod m, long amt)[] parts)
        {
            var inv = new Invoice
            {
                InvoiceNumber = c.Invoices.GenerateNextInvoiceNumber(InvoiceType.Sales), Type = InvoiceType.Sales,
                CustomerId = customerId, CustomerName = "عميل", PaymentMethod = method,
                UserId = 1, UserName = "اختبار", ShiftId = c.ShiftId,
                SubtotalPiastres = total, TotalPiastres = total, PaidPiastres = total, RemainingPiastres = 0,
                Items =
                {
                    new InvoiceItem
                    {
                        ItemId = c.Item.Id, ItemCode = c.Item.Code, ItemNameAr = c.Item.NameAr,
                        BatchNumber = c.Item.Batches.Count > 0 ? c.Item.Batches[0].BatchNumber : "عام",
                        UnitLevel = UnitLevel.Piece, QuantitySold = 10, SmallestUnitsCount = 10,
                        UnitPricePiastres = total / 10, TotalPiastres = total
                    }
                }
            };
            foreach (var p in parts) inv.Payments.Add(new InvoicePayment { Method = p.m, AmountPiastres = p.amt });
            return inv;
        }

        [Fact]
        public void SplitCashAndNetwork_ShouldSaveParts_ShiftColumns_AndJournalPerAccount()
        {
            var c = Setup();
            var inv = Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 6000), (PaymentMethod.Network, 4000));

            c.Invoices.SaveInvoice(inv, out string err).Should().BeTrue(err);

            Scalar(c.Db, $"SELECT PaymentMethod FROM Invoices WHERE Id = {inv.Id}").Should().Be((long)PaymentMethod.Mixed);
            Scalar(c.Db, $"SELECT COUNT(*) FROM InvoicePayments WHERE InvoiceId = {inv.Id}").Should().Be(2);
            Scalar(c.Db, $"SELECT AmountPiastres FROM InvoicePayments WHERE InvoiceId = {inv.Id} AND Method = 1").Should().Be(6000);
            Scalar(c.Db, $"SELECT AmountPiastres FROM InvoicePayments WHERE InvoiceId = {inv.Id} AND Method = 2").Should().Be(4000);

            var shift = c.Shifts.GetCurrentOpenShift(1)!;
            shift.TotalSalesCashPiastres.Should().Be(6000);
            shift.TotalSalesNetworkPiastres.Should().Be(4000);
            shift.ExpectedCashPiastres.Should().Be(6000);

            string like = $"EntryNumber LIKE 'JV-{inv.InvoiceNumber}-%'";
            Scalar(c.Db, $"SELECT SUM(AmountPiastres) FROM JournalEntries WHERE {like} AND DebitAccount = '{Accounts.Cash}'").Should().Be(6000);
            Scalar(c.Db, $"SELECT SUM(AmountPiastres) FROM JournalEntries WHERE {like} AND DebitAccount = '{Accounts.Network}'").Should().Be(4000);
            Scalar(c.Db, $"SELECT SUM(AmountPiastres) FROM JournalEntries WHERE {like} AND DebitAccount = '{Accounts.Customers}'").Should().Be(0);
        }

        [Fact]
        public void SplitWithOnAccountPart_ShouldRaiseCustomerBalanceByThatPartOnly()
        {
            var c = Setup();
            // العميل رقم 2 عميل مسجل بحد ائتمان 5000 جنيه
            long before = Scalar(c.Db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id = 2");
            var inv = Sale(c, 10000, PaymentMethod.Mixed, 2, (PaymentMethod.Cash, 3000), (PaymentMethod.OnAccount, 7000));

            c.Invoices.SaveInvoice(inv, out string err).Should().BeTrue(err);

            Scalar(c.Db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id = 2").Should().Be(before + 7000);
            Scalar(c.Db, $"SELECT RemainingPiastres FROM Invoices WHERE Id = {inv.Id}").Should().Be(7000);
            Scalar(c.Db, $"SELECT PaidPiastres FROM Invoices WHERE Id = {inv.Id}").Should().Be(3000);

            var shift = c.Shifts.GetCurrentOpenShift(1)!;
            shift.TotalSalesCashPiastres.Should().Be(3000);
            shift.TotalSalesOnAccountPiastres.Should().Be(7000);
            shift.ExpectedCashPiastres.Should().Be(3000);

            string like = $"EntryNumber LIKE 'JV-{inv.InvoiceNumber}-%'";
            Scalar(c.Db, $"SELECT SUM(AmountPiastres) FROM JournalEntries WHERE {like} AND DebitAccount = '{Accounts.Customers}'").Should().Be(7000);
            Scalar(c.Db, $"SELECT SUM(AmountPiastres) FROM JournalEntries WHERE {like} AND DebitAccount = '{Accounts.Cash}'").Should().Be(3000);
        }

        [Fact]
        public void SplitWithInsurancePart_ShouldDebitInsuranceReceivable_AndNotTouchCustomerBalance()
        {
            var c = Setup();
            long before = Scalar(c.Db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id = 2");
            var inv = Sale(c, 10000, PaymentMethod.Mixed, 2, (PaymentMethod.Cash, 2000), (PaymentMethod.Insurance, 8000));

            c.Invoices.SaveInvoice(inv, out string err).Should().BeTrue(err);

            Scalar(c.Db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id = 2").Should().Be(before);
            string like = $"EntryNumber LIKE 'JV-{inv.InvoiceNumber}-%'";
            Scalar(c.Db, $"SELECT SUM(AmountPiastres) FROM JournalEntries WHERE {like} AND DebitAccount = '{Accounts.InsuranceReceivable}'").Should().Be(8000);
        }

        [Fact]
        public void InvalidSplits_ShouldBeRejected_AndLeaveNothingSaved()
        {
            var c = Setup();
            long stockBefore = c.Items.GetById(c.Item.Id)!.TotalStockSmallestUnits;

            var cases = new (string Name, Invoice Inv)[]
            {
                ("المجموع أقل من الإجمالي", Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 5000), (PaymentMethod.Network, 4000))),
                ("المجموع أكبر من الإجمالي", Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 7000), (PaymentMethod.Network, 4000))),
                ("جزء واحد فقط", Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 10000))),
                ("طريقة مكررة", Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 5000), (PaymentMethod.Cash, 5000))),
                ("مبلغ صفر", Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 10000), (PaymentMethod.Network, 0))),
                ("مبلغ سالب", Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 12000), (PaymentMethod.Network, -2000))),
                ("آجل على العميل النقدي", Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 5000), (PaymentMethod.OnAccount, 5000))),
                ("آجل بدون عميل", Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 5000), (PaymentMethod.OnAccount, 5000))),
                ("Mixed بدون أجزاء", Sale(c, 10000, PaymentMethod.Mixed, 1)),
            };
            cases[7].Inv.CustomerId = null;

            foreach (var (name, inv) in cases)
            {
                c.Invoices.SaveInvoice(inv, out string err).Should().BeFalse(name);
                err.Should().NotBeEmpty(name);
            }

            Scalar(c.Db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);
            Scalar(c.Db, "SELECT COUNT(*) FROM InvoicePayments").Should().Be(0);
            Scalar(c.Db, "SELECT COUNT(*) FROM JournalEntries").Should().Be(0);
            c.Items.GetById(c.Item.Id)!.TotalStockSmallestUnits.Should().Be((int)stockBefore);
            c.Shifts.GetCurrentOpenShift(1)!.ExpectedCashPiastres.Should().Be(0);
        }

        [Fact]
        public void SingleMethodInvoices_ShouldKeepWorkingExactlyAsBefore_WithNoPaymentRows()
        {
            var c = Setup();
            var cash = Sale(c, 5000, PaymentMethod.Cash, 1);
            c.Invoices.SaveInvoice(cash, out string err).Should().BeTrue(err);

            Scalar(c.Db, "SELECT COUNT(*) FROM InvoicePayments").Should().Be(0);
            Scalar(c.Db, $"SELECT PaymentMethod FROM Invoices WHERE Id = {cash.Id}").Should().Be((long)PaymentMethod.Cash);
            var shift = c.Shifts.GetCurrentOpenShift(1)!;
            shift.TotalSalesCashPiastres.Should().Be(5000);
            shift.ExpectedCashPiastres.Should().Be(5000);
        }

        [Fact]
        public void SplitPaymentOnReturn_ShouldBeRejected()
        {
            var c = Setup();
            var sale = Sale(c, 10000, PaymentMethod.Cash, 1);
            c.Invoices.SaveInvoice(sale, out _).Should().BeTrue();

            var ret = Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 6000), (PaymentMethod.Network, 4000));
            ret.Type = InvoiceType.Return;
            ret.InvoiceNumber = c.Invoices.GenerateNextInvoiceNumber(InvoiceType.Return);
            ret.OriginalInvoiceId = sale.Id;
            ret.Items[0].OriginalInvoiceItemId = sale.Items[0].Id;

            c.Invoices.SaveInvoice(ret, out string err).Should().BeFalse();
            err.Should().Contain("المقسّم");
            Scalar(c.Db, "SELECT COUNT(*) FROM InvoicePayments").Should().Be(0);
        }

        [Fact]
        public void CancelSplitInvoice_ShouldReverseEveryPart_StockCustomerAndShift()
        {
            var c = Setup();
            int stockBefore = c.Items.GetById(c.Item.Id)!.TotalStockSmallestUnits;
            long balBefore = Scalar(c.Db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id = 2");
            var inv = Sale(c, 10000, PaymentMethod.Mixed, 2,
                (PaymentMethod.Cash, 3000), (PaymentMethod.Network, 2000), (PaymentMethod.OnAccount, 5000));
            c.Invoices.SaveInvoice(inv, out string err).Should().BeTrue(err);

            c.Invoices.CancelInvoice(inv.Id, 1, "مدير", "خطأ في الإدخال", out string cerr).Should().BeTrue(cerr);

            c.Items.GetById(c.Item.Id)!.TotalStockSmallestUnits.Should().Be(stockBefore);
            Scalar(c.Db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id = 2").Should().Be(balBefore);
            var shift = c.Shifts.GetCurrentOpenShift(1)!;
            shift.TotalSalesCashPiastres.Should().Be(0);
            shift.TotalSalesNetworkPiastres.Should().Be(0);
            shift.TotalSalesOnAccountPiastres.Should().Be(0);
            shift.ExpectedCashPiastres.Should().Be(0);
            Scalar(c.Db, $"SELECT Status FROM Invoices WHERE Id = {inv.Id}").Should().Be(3);
            // الأجزاء بتفضل مسجّلة للتاريخ
            Scalar(c.Db, $"SELECT COUNT(*) FROM InvoicePayments WHERE InvoiceId = {inv.Id}").Should().Be(3);
        }

        [Fact]
        public void Reports_ShouldSplitMixedInvoicesIntoTheirMethods_AndFilterByAnyPart()
        {
            var c = Setup();
            var cash = Sale(c, 2000, PaymentMethod.Cash, 1);
            c.Invoices.SaveInvoice(cash, out _).Should().BeTrue();
            var mixed = Sale(c, 4000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 1000), (PaymentMethod.Network, 3000));
            c.Invoices.SaveInvoice(mixed, out string err).Should().BeTrue(err);

            var reports = new ReportRepository(c.Db);
            var sum = reports.GetSalesPeriodSummary(DateTime.Today, DateTime.Today);
            sum.TotalSalesPiastres.Should().Be(6000);
            sum.CashSalesPiastres.Should().Be(3000);
            sum.NetworkSalesPiastres.Should().Be(3000);
            (sum.CashSalesPiastres + sum.NetworkSalesPiastres + sum.OnAccountSalesPiastres + sum.InsuranceSalesPiastres)
                .Should().Be(sum.TotalSalesPiastres);

            reports.GetSalesReport(DateTime.Today, DateTime.Today, PaymentMethod.Network).Should().HaveCount(1);
            reports.GetSalesReport(DateTime.Today, DateTime.Today, PaymentMethod.Cash).Should().HaveCount(2);
            reports.GetSalesReport(DateTime.Today, DateTime.Today, PaymentMethod.OnAccount).Should().HaveCount(0);
            reports.GetSalesReport(DateTime.Today, DateTime.Today)
                .Single(r => r.InvoiceId == mixed.Id).PaymentMethodName.Should().Be("مقسّم");
        }

        [Fact]
        public void ReturnableInvoice_ShouldLoadSplitParts_AndReturnCanBeSavedWithSingleMethod()
        {
            var c = Setup();
            var sale = Sale(c, 10000, PaymentMethod.Mixed, 1, (PaymentMethod.Cash, 6000), (PaymentMethod.Network, 4000));
            c.Invoices.SaveInvoice(sale, out string err).Should().BeTrue(err);

            var src = c.Invoices.GetReturnableInvoice(sale.InvoiceNumber, out string e2);
            src.Should().NotBeNull(e2);
            src!.Invoice.PaymentMethod.Should().Be(PaymentMethod.Mixed);
            src.Invoice.Payments.Should().HaveCount(2);
            c.Invoices.GetPayments(sale.Id).Should().HaveCount(2);

            var ret = Sale(c, 10000, PaymentMethod.Cash, 1);
            ret.Type = InvoiceType.Return;
            ret.InvoiceNumber = c.Invoices.GenerateNextInvoiceNumber(InvoiceType.Return);
            ret.OriginalInvoiceId = sale.Id;
            ret.Items[0].OriginalInvoiceItemId = src.Lines[0].Line.Id;
            c.Invoices.SaveInvoice(ret, out string rerr).Should().BeTrue(rerr);
        }

        [Fact]
        public void PaymentSummary_ShouldListEachPart_OrJustTheMethodName()
        {
            var inv = new Invoice { PaymentMethod = PaymentMethod.Mixed };
            inv.Payments.Add(new InvoicePayment { Method = PaymentMethod.Cash, AmountPiastres = 6000 });
            inv.Payments.Add(new InvoicePayment { Method = PaymentMethod.Network, AmountPiastres = 4050 });
            inv.GetPaymentSummary().Should().Be("مقسّم: نقدي 60.00 + شبكة / فيزا 40.50");
            inv.IsSplitPayment.Should().BeTrue();

            new Invoice { PaymentMethod = PaymentMethod.Network }.GetPaymentSummary().Should().Be("شبكة / فيزا");
        }

        // ================= الواجهة (SalesViewModel) =================

        private static SalesViewModel BuildVm(DatabaseContext db, out ShiftRepository shifts)
        {
            var users = new UserRepository(db);
            users.SaveStaffUser(0, "cash1", "كاشير", "Pass#12345", UserRole.Cashier, true);
            var user = users.Authenticate("cash1", "Pass#12345")!;
            shifts = new ShiftRepository(db);
            shifts.OpenShift(user.Id, user.FullName, 0);
            var session = new FakeSession { CurrentUser = user, CurrentShift = shifts.GetCurrentOpenShift(user.Id) };
            var items = new ItemRepository(db);
            var vm = new SalesViewModel(items, new InvoiceRepository(db, items), new CustomerRepository(db), new SettingsRepository(db), null, session, shifts, null, null);
            vm.AddItem(items.GetByBarcodeOrCode("6281001000404"));
            return vm;
        }

        private static DatabaseContext NewDb()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            return db;
        }

        [Fact]
        public void ViewModel_CashIsTheRemainder_AndSaveStoresBothParts()
        {
            var db = NewDb();
            var vm = BuildVm(db, out _);
            long total = vm.NetTotalPiastres;
            total.Should().BeGreaterThan(1000);

            vm.SelectedPaymentMethod = PaymentMethod.Mixed;
            vm.IsMixedPayment.Should().BeTrue();
            vm.SplitNetworkRiyals = 10m;

            vm.SplitCashRiyals.Should().Be((total - 1000) / 100m);
            vm.RemainingPiastres.Should().Be(0);

            vm.SaveCurrentInvoice();

            vm.IsStatusSuccess.Should().BeTrue();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1);
            Scalar(db, "SELECT AmountPiastres FROM InvoicePayments WHERE Method = 2").Should().Be(1000);
            Scalar(db, "SELECT AmountPiastres FROM InvoicePayments WHERE Method = 1").Should().Be(total - 1000);
            Scalar(db, "SELECT PaymentMethod FROM Invoices").Should().Be((long)PaymentMethod.Mixed);
        }

        [Fact]
        public void ViewModel_MixedWithoutSecondMethod_ShouldBeRefused()
        {
            var db = NewDb();
            var vm = BuildVm(db, out _);
            vm.SelectedPaymentMethod = PaymentMethod.Mixed;

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);
            vm.IsStatusSuccess.Should().BeFalse();
            vm.StatusMessage.Should().Contain("طريقتين");
        }

        [Fact]
        public void ViewModel_OtherPartsLargerThanTotal_ShouldBeRefused()
        {
            var db = NewDb();
            var vm = BuildVm(db, out _);
            vm.SelectedPaymentMethod = PaymentMethod.Mixed;
            vm.SplitNetworkRiyals = (vm.NetTotalPiastres / 100m) + 50m;

            vm.SaveCurrentInvoice();

            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);
            vm.StatusMessage.Should().Contain("أكبر من إجمالي");
        }

        [Fact]
        public void ViewModel_OnAccountPart_RequiresRegisteredCustomer_AndWorksWithOne()
        {
            var db = NewDb();
            var vm = BuildVm(db, out _);
            vm.SelectedPaymentMethod = PaymentMethod.Mixed;
            vm.SplitOnAccountRiyals = 20m;

            vm.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);
            vm.StatusMessage.Should().Contain("عميل مسجل");

            vm.CustomerName = "مجمع النخبة الطبي (آجل)";
            vm.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1);
            Scalar(db, "SELECT RemainingPiastres FROM Invoices").Should().Be(2000);
            Scalar(db, "SELECT CurrentBalancePiastres FROM Customers WHERE Id = 2").Should().BeGreaterThan(1999);
        }

        [Fact]
        public void ViewModel_NewInvoiceResetsSplitFieldsAndPaymentMethod()
        {
            var db = NewDb();
            var vm = BuildVm(db, out _);
            vm.SelectedPaymentMethod = PaymentMethod.Mixed;
            vm.SplitNetworkRiyals = 10m;
            vm.SaveCurrentInvoice();

            vm.SelectedPaymentMethod.Should().Be(PaymentMethod.Cash);
            vm.SplitNetworkRiyals.Should().Be(0m);
            vm.IsMixedPayment.Should().BeFalse();
        }
    }
}
