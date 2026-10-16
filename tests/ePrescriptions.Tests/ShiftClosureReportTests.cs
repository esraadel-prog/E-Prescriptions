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
    /// <summary>
    /// خطوة 37: تقرير إغلاق وردية أوضح ومطابقة النقدية وتفصيل طرق الدفع وعجز/زيادة الكاشير
    /// </summary>
    public class ShiftClosureReportTests
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

        private class DummyExcelService : IExcelExportService
        {
            public string ExportSalesReport(System.Collections.Generic.List<SalesReportRow> sales, SalesPeriodSummary summary, DateTime from, DateTime to, string pharmacyName) => string.Empty;
            public System.Threading.Tasks.Task<string> ExportSalesReportAsync(System.Collections.Generic.List<SalesReportRow> sales, SalesPeriodSummary summary, DateTime from, DateTime to, string pharmacyName) => System.Threading.Tasks.Task.FromResult(string.Empty);
            public string ExportItemMovementReport(System.Collections.Generic.List<ItemMovementReportRow> movements, DateTime from, DateTime to, string pharmacyName) => string.Empty;
            public System.Threading.Tasks.Task<string> ExportItemMovementReportAsync(System.Collections.Generic.List<ItemMovementReportRow> movements, DateTime from, DateTime to, string pharmacyName) => System.Threading.Tasks.Task.FromResult(string.Empty);
            public string ExportLowStockReport(System.Collections.Generic.List<LowStockReportRow> lowStocks, string pharmacyName) => string.Empty;
            public System.Threading.Tasks.Task<string> ExportLowStockReportAsync(System.Collections.Generic.List<LowStockReportRow> lowStocks, string pharmacyName) => System.Threading.Tasks.Task.FromResult(string.Empty);
            public string ExportExpiryReport(System.Collections.Generic.List<ExpiryReportRow> expiries, string pharmacyName) => string.Empty;
            public System.Threading.Tasks.Task<string> ExportExpiryReportAsync(System.Collections.Generic.List<ExpiryReportRow> expiries, string pharmacyName) => System.Threading.Tasks.Task.FromResult(string.Empty);
            public string ExportInventoryValuationReport(InventoryValuationSummary summary, string pharmacyName) => string.Empty;
            public System.Threading.Tasks.Task<string> ExportInventoryValuationReportAsync(InventoryValuationSummary summary, string pharmacyName) => System.Threading.Tasks.Task.FromResult(string.Empty);
            public string ExportCashierShiftsReport(System.Collections.Generic.List<CashierShiftSummaryRow> shifts, System.Collections.Generic.List<CashierDiscrepancySummary> summaries, DateTime from, DateTime to, string pharmacyName) => "export.xlsx";
            public System.Threading.Tasks.Task<string> ExportCashierShiftsReportAsync(System.Collections.Generic.List<CashierShiftSummaryRow> shifts, System.Collections.Generic.List<CashierDiscrepancySummary> summaries, DateTime from, DateTime to, string pharmacyName) => System.Threading.Tasks.Task.FromResult("export.xlsx");
        }
#pragma warning restore CS0067

        private static Invoice CreateSaleInvoice(Item item, int shiftId, int userId, PaymentMethod method, long totalPiastres)
        {
            var inv = new Invoice
            {
                InvoiceNumber = "INV-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                Type = InvoiceType.Sales,
                CustomerName = "عميل نقدي",
                PaymentMethod = method,
                UserId = userId,
                UserName = "كاشير",
                ShiftId = shiftId,
                SubtotalPiastres = totalPiastres,
                TotalPiastres = totalPiastres,
                PaidPiastres = method == PaymentMethod.OnAccount ? 0 : totalPiastres,
                RemainingPiastres = method == PaymentMethod.OnAccount ? totalPiastres : 0,
                Items =
                {
                    new InvoiceItem
                    {
                        ItemId = item.Id,
                        ItemCode = item.Code,
                        ItemNameAr = item.NameAr,
                        UnitLevel = UnitLevel.Piece,
                        QuantitySold = 1,
                        SmallestUnitsCount = 1,
                        UnitPricePiastres = totalPiastres,
                        TotalPiastres = totalPiastres
                    }
                }
            };
            return inv;
        }

        [Fact]
        public void GetShiftClosureReport_OpenShift_ShouldReturnAccuratePaymentBreakdownAndExpectedCash()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var itemRepo = new ItemRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var shiftRepo = new ShiftRepository(db);
            var reportRepo = new ReportRepository(db);

            var item = itemRepo.GetByBarcodeOrCode("6281001000101")!;
            int shiftId = shiftRepo.OpenShift(1, "كاشير أحمد", 5000); // 50.00 ج.م رصيد افتتاحي

            // 1) فاتورة نقدية 100 ج.م
            invRepo.SaveInvoice(CreateSaleInvoice(item, shiftId, 1, PaymentMethod.Cash, 10000), out _).Should().BeTrue();

            // 2) فاتورة شبكة 75 ج.م
            invRepo.SaveInvoice(CreateSaleInvoice(item, shiftId, 1, PaymentMethod.Network, 7500), out _).Should().BeTrue();

            // 3) فاتورة مقسمة 50 ج.م (30 نقدي + 20 شبكة)
            var splitInv = CreateSaleInvoice(item, shiftId, 1, PaymentMethod.Mixed, 5000);
            splitInv.Payments.Add(new InvoicePayment { Method = PaymentMethod.Cash, AmountPiastres = 3000 });
            splitInv.Payments.Add(new InvoicePayment { Method = PaymentMethod.Network, AmountPiastres = 2000 });
            invRepo.SaveInvoice(splitInv, out _).Should().BeTrue();

            var report = reportRepo.GetShiftClosureReport(shiftId);
            report.Should().NotBeNull();
            report!.ShiftId.Should().Be(shiftId);
            report.CashierName.Should().Be("كاشير أحمد");
            report.OpeningBalancePiastres.Should().Be(5000);
            report.InvoicesCount.Should().Be(3);
            report.CashSalesPiastres.Should().Be(13000); // 10000 + 3000
            report.NetworkSalesPiastres.Should().Be(9500); // 7500 + 2000
            report.TotalSalesPiastres.Should().Be(22500);

            // المتوقع = 5000 + 13000 - 0 = 18000 (180.00 ج.م)
            report.ExpectedCashPiastres.Should().Be(18000);
            report.ActualCashPiastres.Should().BeNull();
            report.DifferencePiastres.Should().BeNull();
            report.IsClosed.Should().BeFalse();
            report.DifferenceLabel.Should().Contain("مفتوحة");
        }

        [Fact]
        public void GetShiftClosureReport_ClosedShift_WithDeficit_ShouldCalculateAccurateDifferenceAndFlags()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var itemRepo = new ItemRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var shiftRepo = new ShiftRepository(db);
            var reportRepo = new ReportRepository(db);

            var item = itemRepo.GetByBarcodeOrCode("6281001000101")!;
            int shiftId = shiftRepo.OpenShift(1, "كاشير سامي", 10000); // 100.00 ج.م
            invRepo.SaveInvoice(CreateSaleInvoice(item, shiftId, 1, PaymentMethod.Cash, 5000), out _).Should().BeTrue(); // +50 ج.م

            // المتوقع = 150.00 ج.م (15000 قرش)
            // الفعلي تم عدّه 130.00 ج.م (13000 قرش) -> عجز 20.00 ج.م (-2000 قرش)
            shiftRepo.CloseShift(shiftId, 13000, "عجز بسيط");

            var report = reportRepo.GetShiftClosureReport(shiftId);
            report.Should().NotBeNull();
            report!.IsClosed.Should().BeTrue();
            report.ExpectedCashPiastres.Should().Be(15000);
            report.ActualCashPiastres.Should().Be(13000);
            report.DifferencePiastres.Should().Be(-2000);
            report.HasDiscrepancy.Should().BeTrue();
            report.IsDeficit.Should().BeTrue();
            report.IsSurplus.Should().BeFalse();
            report.DifferenceLabel.Should().Contain("عجز");
            report.DifferenceLabel.Should().Contain("20.00");
        }

        [Fact]
        public void GetShiftClosureReport_ClosedShift_WithSurplus_ShouldCalculateAccurateDifferenceAndFlags()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var itemRepo = new ItemRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var shiftRepo = new ShiftRepository(db);
            var reportRepo = new ReportRepository(db);

            var item = itemRepo.GetByBarcodeOrCode("6281001000101")!;
            int shiftId = shiftRepo.OpenShift(1, "كاشير سامي", 10000); // 100.00 ج.م
            invRepo.SaveInvoice(CreateSaleInvoice(item, shiftId, 1, PaymentMethod.Cash, 5000), out _).Should().BeTrue(); // +50 ج.م

            // المتوقع = 150.00 ج.م. الفعلي = 160.00 ج.م -> زيادة 10.00 ج.م (+1000 قرش)
            shiftRepo.CloseShift(shiftId, 16000, "زيادة بقشيش");

            var report = reportRepo.GetShiftClosureReport(shiftId);
            report.Should().NotBeNull();
            report!.DifferencePiastres.Should().Be(1000);
            report.HasDiscrepancy.Should().BeTrue();
            report.IsSurplus.Should().BeTrue();
            report.IsDeficit.Should().BeFalse();
            report.DifferenceLabel.Should().Contain("زيادة");
            report.DifferenceLabel.Should().Contain("10.00");
        }

        [Fact]
        public void GetShiftClosureReport_ClosedShift_ExactMatch_ShouldHaveZeroDifference()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var shiftRepo = new ShiftRepository(db);
            var reportRepo = new ReportRepository(db);

            int shiftId = shiftRepo.OpenShift(1, "كاشير سامي", 10000);
            shiftRepo.CloseShift(shiftId, 10000, "مطابق");

            var report = reportRepo.GetShiftClosureReport(shiftId);
            report.Should().NotBeNull();
            report!.DifferencePiastres.Should().Be(0);
            report.HasDiscrepancy.Should().BeFalse();
            report.DifferenceLabel.Should().Contain("مطابق تماماً");
        }

        [Fact]
        public void GetCashierShiftsReport_AndDiscrepancySummary_ShouldAggregateAcrossCashiers()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var userRepo = new UserRepository(db);
            var itemRepo = new ItemRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var shiftRepo = new ShiftRepository(db);
            var reportRepo = new ReportRepository(db);

            // إنشاء مستخدم ثانٍ
            int cashier2Id = userRepo.SaveStaffUser(0, "cashier2", "كاشير 2", "Pass@1234", UserRole.Cashier, true);

            var item = itemRepo.GetByBarcodeOrCode("6281001000101")!;

            // وردية 1 لكاشير 1: افتتاحي 100 ج.م + بيع 50 ج.م = متوقع 150 ج.م -> فعلي 140 ج.م (عجز 10 ج.م)
            int s1 = shiftRepo.OpenShift(1, "كاشير 1", 10000);
            invRepo.SaveInvoice(CreateSaleInvoice(item, s1, 1, PaymentMethod.Cash, 5000), out _).Should().BeTrue();
            shiftRepo.CloseShift(s1, 14000, null);

            // وردية 2 لكاشير 1: افتتاحي 50 ج.م -> فعلي 55 ج.م (زيادة 5 ج.م)
            int s2 = shiftRepo.OpenShift(1, "كاشير 1", 5000);
            shiftRepo.CloseShift(s2, 5500, null);

            // وردية 1 لكاشير 2: افتتاحي 200 ج.م -> فعلي 220 ج.م (زيادة 20 ج.م)
            int s3 = shiftRepo.OpenShift(cashier2Id, "كاشير 2", 20000);
            shiftRepo.CloseShift(s3, 22000, null);

            var shifts = reportRepo.GetCashierShiftsReport(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            shifts.Should().HaveCount(3);

            var summaries = reportRepo.GetCashierDiscrepancySummary(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            summaries.Should().HaveCount(2);

            var c1Summary = summaries.First(s => s.CashierId == 1);
            c1Summary.ShiftsCount.Should().Be(2);
            c1Summary.ClosedShiftsCount.Should().Be(2);
            c1Summary.TotalShortagePiastres.Should().Be(1000); // 10 ج.م عجز
            c1Summary.TotalSurplusPiastres.Should().Be(500);   // 5 ج.م زيادة
            c1Summary.NetDifferencePiastres.Should().Be(-500); // صافي -5 ج.م

            var c2Summary = summaries.First(s => s.CashierId == cashier2Id);
            c2Summary.ShiftsCount.Should().Be(1);
            c2Summary.TotalShortagePiastres.Should().Be(0);
            c2Summary.TotalSurplusPiastres.Should().Be(2000);  // 20 ج.م زيادة
            c2Summary.NetDifferencePiastres.Should().Be(2000);
        }

        [Fact]
        public void StaffViewModel_ShiftReconciliation_ShouldUpdatePropertiesAndStatusText()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var shiftRepo = new ShiftRepository(db);
            var userRepo = new UserRepository(db);
            var reportRepo = new ReportRepository(db);
            var settingsRepo = new SettingsRepository(db);

            var session = new FakeSession { CurrentUser = userRepo.Authenticate(DatabaseContext.OwnerUsername, DatabaseContext.DefaultOwnerPassword) };
            int sId = shiftRepo.OpenShift(session.CurrentUser!.Id, session.CurrentUser.FullName, 10000); // 100.00 ج.م متوقع

            var vm = new StaffViewModel(userRepo, shiftRepo, session, null, null, reportRepo, settingsRepo);
            vm.CurrentShift.Should().NotBeNull();
            vm.FormattedExpectedCash.Should().Contain("100.00");

            // تعديل النقد الفعلي إلى 90.00 ج.م (عجز 10.00 ج.م)
            vm.ActualClosingCashPounds = 90.00m;
            vm.ShiftDifferencePounds.Should().Be(-10.00m);
            vm.IsDifferenceDeficit.Should().BeTrue();
            vm.IsDifferenceSurplus.Should().BeFalse();
            vm.IsDifferenceZero.Should().BeFalse();
            vm.DifferenceStatusText.Should().Contain("عجز");
            vm.DifferenceStatusText.Should().Contain("10.00");
            vm.DifferenceStatusColor.Should().Be("#DC2626");

            // تعديل النقد الفعلي إلى 105.00 ج.م (زيادة 5.00 ج.م)
            vm.ActualClosingCashPounds = 105.00m;
            vm.ShiftDifferencePounds.Should().Be(5.00m);
            vm.IsDifferenceSurplus.Should().BeTrue();
            vm.DifferenceStatusText.Should().Contain("زيادة");
            vm.DifferenceStatusText.Should().Contain("5.00");
            vm.DifferenceStatusColor.Should().Be("#D97706");

            // إغلاق الوردية
            vm.CloseShift();
            vm.HasOpenShift.Should().BeFalse();
            vm.HasClosedShiftReport.Should().BeTrue();
            vm.LastClosedShiftReport!.ShiftId.Should().Be(sId);
            vm.StatusMessage.Should().Contain("زيادة");
        }

        [Fact]
        public void ReportsViewModel_Tab5_ShouldLoadCashierShiftsAndDiscrepancySummary()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var shiftRepo = new ShiftRepository(db);
            var reportRepo = new ReportRepository(db);
            var settingsRepo = new SettingsRepository(db);

            int sId = shiftRepo.OpenShift(1, "كاشير", 5000);
            shiftRepo.CloseShift(sId, 5000, null);

            var vm = new ReportsViewModel(reportRepo, settingsRepo, new DummyExcelService());
            vm.SelectedReportTabIndex = 5; // تبويب ورديات الكاشيرات وعجز الصندوق

            vm.CashierShiftRows.Should().HaveCount(1);
            vm.CashierDiscrepancyRows.Should().HaveCount(1);
            vm.StatusMessage.Should().Contain("1 وردية");
        }
    }
}
