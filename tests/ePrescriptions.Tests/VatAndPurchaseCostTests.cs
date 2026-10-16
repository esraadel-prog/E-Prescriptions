using System;
using System.IO;
using System.Linq;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// تصحيحات بعد خطوة 39: تكلفة وحدة التشغيلة من الشراء، تخزين نسبة ضريبة بند الشراء،
    /// وإقرار الضريبة من بنود الفواتير الفعلية (خاضع / معفى) بدل إجمالي الفاتورة.
    /// </summary>
    public class VatAndPurchaseCostTests
    {
        private static DatabaseContext NewDb(bool seed)
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: seed);
            return db;
        }

        private static long Scalar(DatabaseContext db, string sql)
        {
            using var c = db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql;
            return Convert.ToInt64(m.ExecuteScalar() ?? 0);
        }
        private static void Exec(DatabaseContext db, string sql)
        {
            using var c = db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql; m.ExecuteNonQuery();
        }

        private static int NewItem(ItemRepository items, string code, string barcode) =>
            items.SaveItem(new Item { Code = code, Barcode = barcode, NameAr = "صنف " + code, StripsPerBox = 3, PiecesPerStrip = 10, RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 3000, IsActive = true });

        private static PurchaseInvoiceItem Line(int itemId, string batch, UnitLevel level, int qty, int smallest, long unitCost, long vat, int? vatPercent) =>
            new PurchaseInvoiceItem
            {
                ItemId = itemId, ItemCode = "X", ItemNameAr = "x", BatchNumber = batch, ExpiryDate = DateTime.Today.AddYears(1),
                UnitLevel = level, QuantityPurchased = qty, SmallestUnitsCount = smallest, UnitCostPiastres = unitCost,
                VatPiastres = vat, VatPercent = vatPercent, TotalPiastres = unitCost * qty + vat
            };

        private static PurchaseInvoice Invoice(int supplierId, params PurchaseInvoiceItem[] lines)
        {
            var inv = new PurchaseInvoice { SupplierId = supplierId, SupplierName = "مورد", InvoiceDate = DateTime.Today };
            foreach (var l in lines) inv.Items.Add(l);
            inv.SubtotalPiastres = lines.Sum(l => l.UnitCostPiastres * l.QuantityPurchased);
            inv.VatPiastres = lines.Sum(l => l.VatPiastres);
            inv.TotalPiastres = inv.SubtotalPiastres + inv.VatPiastres;
            inv.PaidPiastres = inv.TotalPiastres;
            return inv;
        }

        // ---------- تكلفة وحدة التشغيلة ----------

        [Fact]
        public void PurchaseBatchCost_ShouldBeCostOfOnePiece_NotDividedByTheWholeQuantity()
        {
            var db = NewDb(false);
            var items = new ItemRepository(db); var pur = new PurchaseRepository(db);
            int sup = new SupplierRepository(db).SaveSupplier(new Supplier { Name = "مورد", Phone = "0100", IsActive = true });
            int a = NewItem(items, "C-1", "6221000000011");
            int b = NewItem(items, "C-2", "6221000000012");
            int c = NewItem(items, "C-3", "6221000000013");

            // 10 علب × 30 قطعة بـ 30.00 للعلبة => القطعة 1.00 = 100 قرش
            pur.SavePurchaseInvoice(Invoice(sup, Line(a, "BA", UnitLevel.Box, 10, 300, 3000, 0, 0)), out string e1).Should().BeTrue(e1);
            // علبة واحدة (كانت بتطلع صح قبل الإصلاح) لازم تفضل 100
            pur.SavePurchaseInvoice(Invoice(sup, Line(b, "BB", UnitLevel.Box, 1, 30, 3000, 0, 0)), out string e2).Should().BeTrue(e2);
            // شراء بالقطعة: 7 قطع بـ 2.50 للقطعة => 250 قرش
            pur.SavePurchaseInvoice(Invoice(sup, Line(c, "BC", UnitLevel.Piece, 7, 7, 250, 0, 0)), out string e3).Should().BeTrue(e3);

            Scalar(db, "SELECT CostPricePerSmallestUnitPiastres FROM ItemBatches WHERE BatchNumber='BA'").Should().Be(100);
            Scalar(db, "SELECT CostPricePerSmallestUnitPiastres FROM ItemBatches WHERE BatchNumber='BB'").Should().Be(100);
            Scalar(db, "SELECT CostPricePerSmallestUnitPiastres FROM ItemBatches WHERE BatchNumber='BC'").Should().Be(250);
        }

        [Fact]
        public void Migration021_ShouldCorrectOldWrongBatchCosts_FromThePurchaseLine()
        {
            var db = NewDb(false);
            var items = new ItemRepository(db); var pur = new PurchaseRepository(db);
            int sup = new SupplierRepository(db).SaveSupplier(new Supplier { Name = "مورد", Phone = "0100", IsActive = true });
            int a = NewItem(items, "M-1", "6221000000021");
            pur.SavePurchaseInvoice(Invoice(sup, Line(a, "OLD", UnitLevel.Box, 10, 300, 3000, 0, 0)), out string e).Should().BeTrue(e);
            Exec(db, "UPDATE ItemBatches SET CostPricePerSmallestUnitPiastres = 10 WHERE BatchNumber='OLD'"); // القيمة الغلط القديمة

            string script = File.ReadAllText(FindMigration()).Replace("\r\n", "\n");
            int start = script.IndexOf("UPDATE ItemBatches", StringComparison.Ordinal);
            int end = script.IndexOf(";\n\n-- 2)", StringComparison.Ordinal);
            start.Should().BeGreaterThan(-1); end.Should().BeGreaterThan(start);
            Exec(db, script.Substring(start, end - start + 1));

            Scalar(db, "SELECT CostPricePerSmallestUnitPiastres FROM ItemBatches WHERE BatchNumber='OLD'").Should().Be(100);
        }

        // ---------- نسبة ضريبة بند الشراء ----------

        [Fact]
        public void PurchaseLineVatPercent_ShouldBeStored_AndReadBackWithNullForLegacyLines()
        {
            var db = NewDb(false);
            var items = new ItemRepository(db); var pur = new PurchaseRepository(db); var ret = new SupplierReturnRepository(db);
            int sup = new SupplierRepository(db).SaveSupplier(new Supplier { Name = "مورد", Phone = "0100", IsActive = true });
            int a = NewItem(items, "V-1", "6221000000031");
            int b = NewItem(items, "V-2", "6221000000032");
            var inv = Invoice(sup, Line(a, "V1", UnitLevel.Piece, 10, 10, 100, 140, 14), Line(b, "V2", UnitLevel.Piece, 10, 10, 100, 0, 0));
            pur.SavePurchaseInvoice(inv, out string err).Should().BeTrue(err);

            var rp = ret.GetReturnablePurchase(inv.Id)!;
            rp.Lines[0].Line.VatPercent.Should().Be(14);
            rp.Lines[1].Line.VatPercent.Should().Be(0);

            Exec(db, $"UPDATE PurchaseInvoiceItems SET VatPercent = NULL WHERE Id = {inv.Items[0].Id}");
            ret.GetReturnablePurchase(inv.Id)!.Lines[0].Line.VatPercent.Should().BeNull();
        }

        // ---------- إقرار الضريبة ----------

        [Fact]
        public void VatReturn_Purchases_ShouldSplitTaxableAndExemptByLine_AndTreatLegacyLinesByTheirVatAmount()
        {
            var db = NewDb(false);
            var items = new ItemRepository(db); var pur = new PurchaseRepository(db);
            int sup = new SupplierRepository(db).SaveSupplier(new Supplier { Name = "مورد", Phone = "0100", IsActive = true });
            int a = NewItem(items, "R-1", "6221000000041");
            int b = NewItem(items, "R-2", "6221000000042");
            int c = NewItem(items, "R-3", "6221000000043");
            var inv = Invoice(sup,
                Line(a, "RA", UnitLevel.Piece, 50, 50, 100, 700, 14),   // خاضع: وعاء 5000، ضريبة 700
                Line(b, "RB", UnitLevel.Piece, 30, 30, 100, 0, 0),      // معفى: 3000
                Line(c, "RC", UnitLevel.Piece, 10, 10, 100, 150, null)); // بند قديم بدون نسبة مخزنة وعليه ضريبة: خاضع 1000
            pur.SavePurchaseInvoice(inv, out string err).Should().BeTrue(err);

            var r = new FinanceRepository(db).GetVatReturnReport(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            r.TaxablePurchasesPiastres.Should().Be(6000);
            r.ExemptPurchasesPiastres.Should().Be(3000);
            r.InputVatPiastres.Should().Be(850);
        }

        [Fact]
        public void VatReturn_Sales_ShouldUseNetLineBase_ExcludeExemptLines_AndIgnoreCancelledInvoices()
        {
            var db = NewDb(true);
            var items = new ItemRepository(db);
            var invoices = new InvoiceRepository(db, items);
            int shift = new ShiftRepository(db).OpenShift(1, "اختبار", 0);
            var item = items.GetByBarcodeOrCode("6281001000101")!;

            Invoice Sale(long l1Net, long l1Vat, long l2Net)
            {
                long total = l1Net + l1Vat + l2Net;
                return new Invoice
                {
                    InvoiceNumber = invoices.GenerateNextInvoiceNumber(InvoiceType.Sales), Type = InvoiceType.Sales, CustomerId = 1, CustomerName = "عميل",
                    PaymentMethod = PaymentMethod.Cash, UserId = 1, UserName = "اختبار", ShiftId = shift,
                    SubtotalPiastres = l1Net + l2Net + 500, DiscountPiastres = 500, VatPiastres = l1Vat, TotalPiastres = total, PaidPiastres = total, RemainingPiastres = 0,
                    Items =
                    {
                        // بند خاضع 14%: إجمالي 10000 − خصم 500 = وعاء 9500 + ضريبة 1330
                        new InvoiceItem { ItemId = item.Id, ItemCode = item.Code, ItemNameAr = item.NameAr, BatchNumber = item.Batches.Count > 0 ? item.Batches[0].BatchNumber : "عام", UnitLevel = UnitLevel.Piece, QuantitySold = 10, SmallestUnitsCount = 10, UnitPricePiastres = (l1Net + 500) / 10, DiscountPiastres = 500, VatRatePercent = 14, VatPiastres = l1Vat, TotalPiastres = l1Net + l1Vat },
                        // بند معفى: 2000 بدون ضريبة
                        new InvoiceItem { ItemId = item.Id, ItemCode = item.Code, ItemNameAr = item.NameAr, BatchNumber = item.Batches.Count > 0 ? item.Batches[0].BatchNumber : "عام", UnitLevel = UnitLevel.Piece, QuantitySold = 5, SmallestUnitsCount = 5, UnitPricePiastres = l2Net / 5, VatRatePercent = 0, VatPiastres = 0, TotalPiastres = l2Net }
                    }
                };
            }

            var keep = Sale(9500, 1330, 2000);
            invoices.SaveInvoice(keep, out string e1).Should().BeTrue(e1);
            var gone = Sale(9500, 1330, 2000);
            invoices.SaveInvoice(gone, out string e2).Should().BeTrue(e2);
            invoices.CancelInvoice(gone.Id, 1, "م", "س", out string e3).Should().BeTrue(e3);

            var r = new FinanceRepository(db).GetVatReturnReport(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            r.TaxableSalesPiastres.Should().Be(9500);   // مش 12000 (إجمالي الفاتورة الخام)
            r.ExemptSalesPiastres.Should().Be(2000);
            r.OutputVatPiastres.Should().Be(1330);
        }

        private static string FindMigration()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "src", "ePrescriptions.Data", "Migrations", "021_PurchaseCostAndVatPercent.sql");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("021_PurchaseCostAndVatPercent.sql");
        }
    }
}
