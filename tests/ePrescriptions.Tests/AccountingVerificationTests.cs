using System;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// خطوة 47: مراجعة حسابات الزكاة والأرباح وكشوف الحساب بأرقام نعرف نتيجتها الصحيحة مسبقاً (محسوبة يدوياً).
    ///
    /// السيناريو (كل المبالغ بالقرش):
    ///   صنف A: علبة = 30 وحدة (3×10)، تكلفة العلبة 10000، سعر البيع 15000 (بدون ضريبة)، رصيد أول 90 وحدة.
    ///   صنف B: علبة = وحدة واحدة، تكلفة 5000، سعر البيع 7000 + ضريبة 14%، رصيد أول 10 وحدات.
    ///   وردية مفتوحة برصيد افتتاحي 5000.
    ///   S1: بيع نقدي علبة A كاملة (30 وحدة): 15000 (ضريبة 0).
    ///   S2: بيع آجل لعميل C2 وحدتين B: صافي 14000 + ضريبة 1960 = 15960.
    ///   R1: مرتجع آجل لـ C2 وحدة B من S2: صافي 7000 + ضريبة 980 = 7980 (يخفض مديونية C2).
    ///   R2: مرتجع نقدي 6 وحدات A من S1: 6×500 = 3000 (ضريبة 0).
    ///   V1: سند قبض من C2 بقيمة 3000.
    ///   C3: عميل برصيد افتتاحي 12000 مدين، بدون حركة.
    ///   مورد: رصيد مستحق 20000.
    /// </summary>
    public class AccountingVerificationTests
    {
        private class Scenario
        {
            public DatabaseContext Db = null!;
            public int ItemAId, ItemBId, C2, C3, ShiftId, UserId;
        }

        private static long Scalar(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        private static void Exec(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static Scenario Build()
        {
            var s = new Scenario { Db = new DatabaseContext(":memory:") };
            s.Db.InitializeAndMigrate(seedData: false);
            var items = new ItemRepository(s.Db);
            var invoices = new InvoiceRepository(s.Db, items);
            var customers = new CustomerRepository(s.Db);

            var a = new Item
            {
                Code = "ACC-A", Barcode = "9000000000011", NameAr = "صنف أ", NameEn = "A", ActiveIngredient = "x",
                StripsPerBox = 3, PiecesPerStrip = 10, RetailPriceBoxPiastres = 15000, WholesalePriceBoxPiastres = 15000,
                CostPriceBoxPiastres = 10000, IsVatExempt = true, TotalStockSmallestUnits = 90, IsActive = true
            };
            s.ItemAId = items.SaveItem(a);
            var b = new Item
            {
                Code = "ACC-B", Barcode = "9000000000028", NameAr = "صنف ب", NameEn = "B", ActiveIngredient = "y",
                StripsPerBox = 1, PiecesPerStrip = 1, RetailPriceBoxPiastres = 7000, WholesalePriceBoxPiastres = 7000,
                CostPriceBoxPiastres = 5000, IsVatExempt = false, TotalStockSmallestUnits = 10, IsActive = true
            };
            s.ItemBId = items.SaveItem(b);

            string expiry = ExpiryPolicy.ToIso(DateTime.Today.AddYears(2));
            Exec(s.Db, $@"INSERT INTO ItemBatches (ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits, CostPricePerSmallestUnitPiastres, CreatedAt)
                          VALUES ({s.ItemAId}, 'A1', '{expiry}', 90, 333, datetime('now')),
                                 ({s.ItemBId}, 'B1', '{expiry}', 10, 5000, datetime('now'));");

            s.C2 = customers.AddCustomer(new Customer { Name = "عميل 2", Phone = "01", CreditLimitPiastres = 10000000, CurrentBalancePiastres = 0, IsActive = true });
            s.C3 = customers.AddCustomer(new Customer { Name = "عميل 3", Phone = "02", CreditLimitPiastres = 10000000, CurrentBalancePiastres = 12000, IsActive = true });
            Exec(s.Db, "INSERT INTO Suppliers (Name, Phone, BalancePiastres, IsActive) VALUES ('مورد', '03', 20000, 1);");

            var users = new UserRepository(s.Db);
            users.SaveStaffUser(0, "acc_cashier", "كاشير اختبار", "Pass#12345", UserRole.Cashier, true);
            s.UserId = users.Authenticate("acc_cashier", "Pass#12345")!.Id;
            s.ShiftId = new ShiftRepository(s.Db).OpenShift(s.UserId, "اختبار", 5000);

            // S1: بيع نقدي علبة A
            var s1 = new Invoice
            {
                Type = InvoiceType.Sales, PaymentMethod = PaymentMethod.Cash, UserId = s.UserId, UserName = "اختبار", ShiftId = s.ShiftId,
                SubtotalPiastres = 15000, VatPiastres = 0, TotalPiastres = 15000, PaidPiastres = 15000, RemainingPiastres = 0,
                Items = { new InvoiceItem { ItemId = s.ItemAId, ItemCode = "ACC-A", ItemNameAr = "صنف أ", UnitLevel = UnitLevel.Box, QuantitySold = 1, SmallestUnitsCount = 30, UnitPricePiastres = 15000, VatRatePercent = 0, VatPiastres = 0, TotalPiastres = 15000 } }
            };
            invoices.SaveInvoice(s1, out string e1).Should().BeTrue(e1);

            // S2: بيع آجل لـ C2: وحدتين B
            var s2 = new Invoice
            {
                Type = InvoiceType.Sales, PaymentMethod = PaymentMethod.OnAccount, CustomerId = s.C2, CustomerName = "عميل 2", UserId = s.UserId, UserName = "اختبار", ShiftId = s.ShiftId,
                SubtotalPiastres = 14000, VatPiastres = 1960, TotalPiastres = 15960, PaidPiastres = 0, RemainingPiastres = 15960,
                Items = { new InvoiceItem { ItemId = s.ItemBId, ItemCode = "ACC-B", ItemNameAr = "صنف ب", UnitLevel = UnitLevel.Box, QuantitySold = 2, SmallestUnitsCount = 2, UnitPricePiastres = 7000, VatRatePercent = 14, VatPiastres = 1960, TotalPiastres = 15960 } }
            };
            invoices.SaveInvoice(s2, out string e2).Should().BeTrue(e2);

            // R1: مرتجع آجل لوحدة B
            var r1 = new Invoice
            {
                Type = InvoiceType.Return, PaymentMethod = PaymentMethod.OnAccount, CustomerId = s.C2, CustomerName = "عميل 2", UserId = s.UserId, UserName = "اختبار", ShiftId = s.ShiftId,
                OriginalInvoiceId = s2.Id,
                SubtotalPiastres = 7000, VatPiastres = 980, TotalPiastres = 7980, PaidPiastres = 0, RemainingPiastres = 7980,
                Items = { new InvoiceItem { ItemId = s.ItemBId, ItemCode = "ACC-B", ItemNameAr = "صنف ب", UnitLevel = UnitLevel.Box, QuantitySold = 1, SmallestUnitsCount = 1, UnitPricePiastres = 7000, VatRatePercent = 14, VatPiastres = 980, TotalPiastres = 7980, OriginalInvoiceItemId = s2.Items[0].Id } }
            };
            invoices.SaveInvoice(r1, out string e3).Should().BeTrue(e3);

            // R2: مرتجع نقدي 6 وحدات A (6 × 500)
            var r2 = new Invoice
            {
                Type = InvoiceType.Return, PaymentMethod = PaymentMethod.Cash, UserId = s.UserId, UserName = "اختبار", ShiftId = s.ShiftId,
                OriginalInvoiceId = s1.Id,
                SubtotalPiastres = 3000, VatPiastres = 0, TotalPiastres = 3000, PaidPiastres = 3000, RemainingPiastres = 0,
                Items = { new InvoiceItem { ItemId = s.ItemAId, ItemCode = "ACC-A", ItemNameAr = "صنف أ", UnitLevel = UnitLevel.Piece, QuantitySold = 6, SmallestUnitsCount = 6, UnitPricePiastres = 500, VatRatePercent = 0, VatPiastres = 0, TotalPiastres = 3000, OriginalInvoiceItemId = s1.Items[0].Id } }
            };
            // سعر المرتجع لازم يطابق سعر بند الأصل ووحدته؛ نرجّع بنفس الوحدة (علبة) كسرياً غير ممكن، فنثبّت بند الأصل بوحدة العلبة
            r2.Items[0].UnitLevel = UnitLevel.Box;
            r2.Items[0].UnitPricePiastres = 15000;
            r2.Items[0].QuantitySold = 1;
            r2.Items[0].SmallestUnitsCount = 6;
            invoices.SaveInvoice(r2, out string e4).Should().BeTrue(e4);

            // V1: سند قبض من C2
            customers.AddCustomerVoucher(new CustomerVoucher
            {
                VoucherNumber = customers.GenerateNextCustomerVoucherNumber(), CustomerId = s.C2, CustomerName = "عميل 2",
                Date = DateTime.Now, AmountPiastres = 3000, PaymentMethod = PaymentMethod.Cash
            }).Should().BeTrue();

            return s;
        }

        [Fact]
        public void Scenario_StockAndBalances_ShouldMatchHandCalculation()
        {
            var s = Build();
            // A: 90 - 30 + 6 = 66 ، B: 10 - 2 + 1 = 9
            Scalar(s.Db, $"SELECT TotalStockSmallestUnits FROM Items WHERE Id={s.ItemAId}").Should().Be(66);
            Scalar(s.Db, $"SELECT TotalStockSmallestUnits FROM Items WHERE Id={s.ItemBId}").Should().Be(9);
            // C2: 15960 - 7980 - 3000 = 4980
            Scalar(s.Db, $"SELECT CurrentBalancePiastres FROM Customers WHERE Id={s.C2}").Should().Be(4980);
            // الوردية المفتوحة: 5000 + 15000 - 3000 = 17000
            Scalar(s.Db, $"SELECT ExpectedCashPiastres FROM Shifts WHERE Id={s.ShiftId}").Should().Be(17000);
        }

        [Fact]
        public void SalesProfit_ShouldExcludeVat_NetOutReturns_AndNotTruncateUnitCost()
        {
            var s = Build();
            var sum = new ReportRepository(s.Db).GetSalesPeriodSummary(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

            // إجمالي المبيعات الفعلي بعد المرتجعات (شامل الضريبة): 15000 + 15960 - 7980 - 3000 = 19980
            sum.NetSalesAfterReturnsPiastres.Should().Be(19980);
            // ضريبة صافية: 1960 - 980 = 980
            sum.NetVatPiastres.Should().Be(980);
            // التكلفة الصافية: A علبة 10000 + B وحدتين 10000 - مرتجع B 5000 - مرتجع A (6×10000/30) 2000 = 13000
            sum.NetCostPiastres.Should().Be(13000);
            // الربح = (19980 - 980) - 13000 = 6000 (الضريبة مش ربح)
            sum.NetProfitPiastres.Should().Be(6000);
        }

        [Fact]
        public void SalesReportRow_EstimatedCost_ShouldNotTruncateUnitCost()
        {
            var s = Build();
            var rows = new ReportRepository(s.Db).GetSalesReport(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            var s1 = rows.Single(r => r.TotalPiastres == 15000 && r.PaymentMethod == PaymentMethod.Cash);
            // علبة A كاملة تكلفتها 10000 بالظبط (مش 30 × 333 = 9990)
            s1.EstimatedCostPiastres.Should().Be(10000);
        }

        [Fact]
        public void Zakat_ShouldMatchHandCalculation()
        {
            var s = Build();
            var z = new FinanceRepository(s.Db).CalculateZakat(evaluateAtCost: true, goldGramPricePiastres: 900, isHijri: true);

            // مخزون بالتكلفة: A 66×10000/30 = 22000 ، B 9×5000 = 45000 → 67000
            z.StockValuePiastres.Should().Be(67000);
            // نقدية الوردية المفتوحة: 17000
            z.CashAndBankPiastres.Should().Be(17000);
            // مديونيات العملاء: C2 4980 + C3 12000 = 16980
            z.GoodReceivablesPiastres.Should().Be(16980);
            // التزامات الموردين: 20000
            z.DuePayablesPiastres.Should().Be(20000);
            // الوعاء = 67000 + 17000 + 16980 - 20000 = 80980
            z.ZakatBasePiastres.Should().Be(80980);
            // النصاب = 85 × 900 = 76500 → واجبة
            z.NisabPiastres.Should().Be(76500);
            z.IsZakatDue.Should().BeTrue();
            // هجري: 80980 × 2.5% = 2024.5 → 2025 (تقريب لأعلى عند النصف)
            z.ZakatAmountPiastres.Should().Be(2025);
            // ميلادي: 80980 × 2.577% = 2086.85 → 2087
            z.IsHijriYear = false;
            z.ZakatAmountPiastres.Should().Be(2087);

            // التقييم بسعر البيع: A 66×15000/30 = 33000 ، B 9×7000 = 63000 → 96000
            new FinanceRepository(s.Db).CalculateZakat(false, 900, true).StockValuePiastres.Should().Be(96000);
        }

        [Fact]
        public void CustomerStatement_ShouldTreatReturnsAsCredit_IncludeOpeningBalance_AndEndAtCurrentBalance()
        {
            var s = Build();
            var customers = new CustomerRepository(s.Db);

            var st2 = customers.GetCustomerStatement(s.C2);
            // آخر رصيد في الكشف = رصيد العميل الفعلي = 4980
            st2.Last().RunningBalancePiastres.Should().Be(4980);
            st2.Sum(r => r.DebitPiastres - r.CreditPiastres).Should().Be(4980);
            // المرتجع دائن (يخفض المديونية) وليس مدين
            var ret = st2.Single(r => r.Description.Contains("مرتجع"));
            ret.CreditPiastres.Should().Be(7980);
            ret.DebitPiastres.Should().Be(0);

            // عميل برصيد افتتاحي 12000 وبدون حركة: الكشف يبدأ برصيد سابق وينتهي عند 12000
            var st3 = customers.GetCustomerStatement(s.C3);
            st3.Should().ContainSingle();
            st3[0].RunningBalancePiastres.Should().Be(12000);
        }

        [Fact]
        public void CashFlow_ShouldTreatCashReturnsAsOutflow_NotAsReceipts()
        {
            var s = Build();
            var cf = new FinanceRepository(s.Db).GetCashFlowSummary(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            // مبيعات نقدية 15000 فقط (المرتجع النقدي 3000 مش مبيعات ولا مقبوضات)
            cf.CashSalesPiastres.Should().Be(15000);
            cf.CashReturnsPiastres.Should().Be(3000);
            cf.CustomerReceiptsPiastres.Should().Be(3000);
            // داخل = 15000 + 3000 ، خارج = 3000 (مرتجع نقدي) ← الصافي 15000
            cf.TotalInflowPiastres.Should().Be(18000);
            cf.TotalOutflowPiastres.Should().Be(3000);
            cf.NetCashFlowPiastres.Should().Be(15000);
        }

        [Fact]
        public void VatReturn_ShouldNetOutSalesReturns()
        {
            var s = Build();
            var vat = new FinanceRepository(s.Db).GetVatReturnReport(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            // ضريبة المخرجات: 1960 (S2) - 980 (R1) = 980
            vat.OutputVatPiastres.Should().Be(980);
            // وعاء خاضع: B صافي 14000 - 7000 = 7000
            vat.TaxableSalesPiastres.Should().Be(7000);
            // معفى: A 15000 - 3000 = 12000
            vat.ExemptSalesPiastres.Should().Be(12000);
            vat.InputVatPiastres.Should().Be(0);
            vat.NetVatPayablePiastres.Should().Be(980);
        }

        [Fact]
        public void ItemMovementReport_ShouldReportRevenueWithoutVat()
        {
            var s = Build();
            var rows = new ReportRepository(s.Db).GetItemMovementReport(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            var b = rows.Single(r => r.ItemCode == "ACC-B");
            // B: 2 وحدات × 7000 = 14000 صافي (الإجمالي 15960 فيه ضريبة 1960)، تكلفة 10000 ← ربح 4000
            b.TotalRevenuePiastres.Should().Be(14000);
            b.TotalCostPiastres.Should().Be(10000);
            b.TotalProfitPiastres.Should().Be(4000);
            // A: علبة 15000 بدون ضريبة، تكلفة 10000 بالظبط (مش 9990) ← ربح 5000
            var a = rows.Single(r => r.ItemCode == "ACC-A");
            a.TotalRevenuePiastres.Should().Be(15000);
            a.TotalCostPiastres.Should().Be(10000);
            a.TotalProfitPiastres.Should().Be(5000);
        }

        [Fact]
        public void SalesReportRow_Profit_ShouldExcludeVat()
        {
            var s = Build();
            var rows = new ReportRepository(s.Db).GetSalesReport(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            var s2 = rows.Single(r => r.TotalPiastres == 15960);
            // 15960 - ضريبة 1960 - تكلفة 10000 = 4000
            s2.ProfitPiastres.Should().Be(4000);
        }

        [Fact]
        public void InventoryValuation_Retail_ShouldMultiplyBeforeDividing()
        {
            var s = Build();
            var v = new ReportRepository(s.Db).GetInventoryValuation();
            // بسعر البيع: A 66×15000/30 = 33000 ، B 9×7000 = 63000 → 96000 (نفس تقييم الزكاة بسعر البيع)
            v.TotalValuationRetailPiastres.Should().Be(96000);
        }
    }
}
