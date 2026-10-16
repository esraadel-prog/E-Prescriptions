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
    /// منع بيع المنتهي، الرصيد السالب وتسويته بالشراء، مرتجع المبيعات المرتبط بالفاتورة الأصلية،
    /// مرتجع/إتلاف المنتهي، وباركود ملصق التشغيلة.
    /// </summary>
    public class ExpiryReturnsAndLabelsTests
    {
        private class Ctx
        {
            public DatabaseContext Db = new DatabaseContext(":memory:");
            public ItemRepository Items = null!;
            public InvoiceRepository Invoices = null!;
            public PurchaseRepository Purchases = null!;
            public SupplierReturnRepository SupplierReturns = null!;
            public int ItemId;
            public int SupplierId;

            public Ctx()
            {
                Db.InitializeAndMigrate(seedData: true);
                Items = new ItemRepository(Db);
                Invoices = new InvoiceRepository(Db, Items);
                Purchases = new PurchaseRepository(Db);
                SupplierReturns = new SupplierReturnRepository(Db);
                Exec("INSERT OR IGNORE INTO Shifts (Id, UserId, UserName, StartTime, Status) VALUES (1, 1, 't', datetime('now'), 1);");
                ItemId = Items.SaveItem(new Item { Code = "TST-1", Barcode = "6221234567890", NameAr = "دواء اختبار", StripsPerBox = 3, PiecesPerStrip = 10, RetailPriceBoxPiastres = 3000, CostPriceBoxPiastres = 3000, IsActive = true });
                SupplierId = new SupplierRepository(Db).SaveSupplier(new Supplier { Name = "شركة اختبار", Phone = "01000000000", IsActive = true });
            }

            public void Exec(string sql) { using var c = Db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql; m.ExecuteNonQuery(); }
            public long Scalar(string sql) { using var c = Db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql; return Convert.ToInt64(m.ExecuteScalar() ?? 0); }
            public long Total => Scalar($"SELECT TotalStockSmallestUnits FROM Items WHERE Id={ItemId}");
            public long Oversold => Scalar($"SELECT OversoldSmallestUnits FROM Items WHERE Id={ItemId}");
            public long Remaining(string batch) => Scalar($"SELECT RemainingSmallestUnits FROM ItemBatches WHERE ItemId={ItemId} AND BatchNumber='{batch}' ORDER BY Id DESC LIMIT 1");
            public long BatchId(string batch) => Scalar($"SELECT Id FROM ItemBatches WHERE ItemId={ItemId} AND BatchNumber='{batch}' ORDER BY Id DESC LIMIT 1");

            public void AddBatch(string number, int days, int units) =>
                Items.AddBatch(new ItemBatch { ItemId = ItemId, BatchNumber = number, ExpiryDate = DateTime.Today.AddDays(days), RemainingSmallestUnits = units, CostPricePerSmallestUnitPiastres = 100 });

            public Invoice Sale(int units, string no, int? batchId = null) => new Invoice
            {
                InvoiceNumber = no, Type = InvoiceType.Sales, CustomerName = "عميل نقدي", UserId = 1, UserName = "t", ShiftId = 1,
                PaymentMethod = PaymentMethod.Cash, TotalPiastres = 100L * units, PaidPiastres = 100L * units, SubtotalPiastres = 100L * units,
                Items = { new InvoiceItem { ItemId = ItemId, ItemCode = "X", ItemNameAr = "صنف", UnitLevel = UnitLevel.Piece, QuantitySold = units, SmallestUnitsCount = units, UnitPricePiastres = 100, TotalPiastres = 100L * units, BatchId = batchId } }
            };

            public Invoice Return(Invoice original, int units, long price = 100) => new Invoice
            {
                InvoiceNumber = "R-" + Guid.NewGuid().ToString("N").Substring(0, 6), Type = InvoiceType.Return, CustomerName = "عميل نقدي", UserId = 1, UserName = "t", ShiftId = 1,
                PaymentMethod = PaymentMethod.Cash, OriginalInvoiceId = original.Id, TotalPiastres = price * units, PaidPiastres = price * units,
                Items = { new InvoiceItem { ItemId = ItemId, ItemCode = "X", ItemNameAr = "صنف", UnitLevel = UnitLevel.Piece, QuantitySold = units, SmallestUnitsCount = units, UnitPricePiastres = price, TotalPiastres = price * units, OriginalInvoiceItemId = original.Items[0].Id } }
            };
        }

        [Fact]
        public void Sale_ShouldSkipExpiredBatches_AndRejectExpiredBatchChosenByLabel()
        {
            var c = new Ctx();
            c.AddBatch("EXP", -1, 10);
            c.AddBatch("A", 30, 20);
            c.AddBatch("B", 400, 50);

            c.Items.GetActiveBatches(c.ItemId).Select(b => b.BatchNumber).Should().Equal("A", "B");

            var sale = c.Sale(25, "T-1");
            c.Invoices.SaveInvoice(sale, out _).Should().BeTrue();
            c.Remaining("EXP").Should().Be(10);
            c.Remaining("A").Should().Be(0);
            c.Remaining("B").Should().Be(45);
            sale.Items[0].BatchNumber.Should().Be("A + B");

            var bad = c.Sale(1, "T-BAD", (int)c.BatchId("EXP"));
            c.Invoices.SaveInvoice(bad, out string err).Should().BeFalse();
            err.Should().Contain("منتهية");
            c.Remaining("EXP").Should().Be(10);
        }

        [Fact]
        public void NegativeSale_ShouldBeSettledByLaterPurchase()
        {
            var c = new Ctx();
            c.AddBatch("B", 400, 10);

            c.Invoices.SaveInvoice(c.Sale(25, "T-2"), out _).Should().BeTrue();
            c.Total.Should().Be(-15);
            c.Oversold.Should().Be(15);

            var purchase = new PurchaseInvoice
            {
                InvoiceNumber = "P-1", SupplierId = c.SupplierId, SupplierName = "ش", TotalPiastres = 10000, PaidPiastres = 10000,
                Items = { new PurchaseInvoiceItem { ItemId = c.ItemId, ItemCode = "TST-1", ItemNameAr = "دواء", BatchNumber = "C", ExpiryDate = DateTime.Today.AddDays(700), UnitLevel = UnitLevel.Piece, QuantityPurchased = 100, SmallestUnitsCount = 100, UnitCostPiastres = 100, TotalPiastres = 10000 } }
            };
            c.Purchases.SavePurchaseInvoice(purchase, out _).Should().BeTrue();

            c.Total.Should().Be(85);
            c.Oversold.Should().Be(0);
            c.Remaining("C").Should().Be(85);
            purchase.Items[0].BatchId.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Return_ShouldRequireOriginalInvoice_AndRespectSoldQuantity()
        {
            var c = new Ctx();
            c.AddBatch("A", 30, 20);
            c.AddBatch("B", 400, 50);
            var sale = c.Sale(25, "T-3");
            c.Invoices.SaveInvoice(sale, out _).Should().BeTrue();

            var noOriginal = c.Return(sale, 1);
            noOriginal.OriginalInvoiceId = null;
            c.Invoices.SaveInvoice(noOriginal, out string e1).Should().BeFalse();
            e1.Should().Contain("فاتورة بيع أصلية");

            c.Invoices.SaveInvoice(c.Return(sale, 1, price: 999), out _).Should().BeFalse();
            c.Invoices.SaveInvoice(c.Return(sale, 26), out _).Should().BeFalse();

            c.Invoices.SaveInvoice(c.Return(sale, 22), out _).Should().BeTrue();
            c.Remaining("A").Should().Be(20);
            c.Remaining("B").Should().Be(47);

            c.Invoices.SaveInvoice(c.Return(sale, 4), out _).Should().BeFalse();
            c.Invoices.SaveInvoice(c.Return(sale, 3), out _).Should().BeTrue();
            c.Invoices.GetReturnableInvoice("T-3", out string done).Should().BeNull();
            done.Should().NotBeEmpty();
        }

        [Fact]
        public void SupplierReturn_ShouldDeductStock_ReduceSupplierBalance_AndRecordLoss()
        {
            var c = new Ctx();
            c.AddBatch("EXP", -1, 10);
            long totalBefore = c.Total;

            var ret = new SupplierReturn
            {
                SupplierId = c.SupplierId, UserId = 1, UserName = "t",
                Items = { new SupplierReturnItem { BatchId = (int)c.BatchId("EXP"), SmallestUnitsCount = 10, DeductionPercent = 20 } }
            };
            c.SupplierReturns.Save(ret, out _).Should().BeTrue();

            ret.TotalCostPiastres.Should().Be(1000);
            ret.CreditPiastres.Should().Be(800);
            ret.LossPiastres.Should().Be(200);
            c.Remaining("EXP").Should().Be(0);
            c.Total.Should().Be(totalBefore - 10);
            c.Scalar($"SELECT BalancePiastres FROM Suppliers WHERE Id={c.SupplierId}").Should().Be(-800);

            var tooMuch = new SupplierReturn
            {
                SupplierId = c.SupplierId, UserId = 1, UserName = "t",
                Items = { new SupplierReturnItem { BatchId = (int)c.BatchId("EXP"), SmallestUnitsCount = 1 } }
            };
            c.SupplierReturns.Save(tooMuch, out _).Should().BeFalse();
        }

        [Fact]
        public void BatchLabelCode_ShouldRoundTrip_AndNotCollideWithNormalBarcodes()
        {
            string code = BatchLabelCode.Encode(123);
            code.Should().Be("9900000123");
            BatchLabelCode.TryDecode(code, out int id).Should().BeTrue();
            id.Should().Be(123);
            BatchLabelCode.TryDecode("6221234567890", out _).Should().BeFalse();
            BatchLabelCode.TryDecode("MED-001", out _).Should().BeFalse();
        }

        [Fact]
        public void ExpiredBatch_EvenIfItIsTheOnlyOne_ShouldNeverBeSold_AndSaleGoesNegative()
        {
            var c = new Ctx();
            c.AddBatch("EXP", -5, 10);

            c.Items.GetActiveBatches(c.ItemId).Should().BeEmpty();
            c.Invoices.SaveInvoice(c.Sale(3, "T-ONLY"), out _).Should().BeTrue();

            c.Remaining("EXP").Should().Be(10);
            c.Oversold.Should().Be(3);
        }

        [Fact]
        public void NegativeBalance_AfterPurchase_ShouldKeepTotalEqualToSumOfBatches()
        {
            var c = new Ctx();
            c.Invoices.SaveInvoice(c.Sale(5, "T-NEG"), out _).Should().BeTrue();
            c.Total.Should().Be(-5);

            var purchase = new PurchaseInvoice
            {
                InvoiceNumber = "P-NEG", SupplierId = c.SupplierId, SupplierName = "ش", TotalPiastres = 2000, PaidPiastres = 2000,
                Items = { new PurchaseInvoiceItem { ItemId = c.ItemId, ItemCode = "TST-1", ItemNameAr = "دواء", BatchNumber = "N1", ExpiryDate = DateTime.Today.AddDays(500), UnitLevel = UnitLevel.Piece, QuantityPurchased = 20, SmallestUnitsCount = 20, UnitCostPiastres = 100, TotalPiastres = 2000 } }
            };
            c.Purchases.SavePurchaseInvoice(purchase, out _).Should().BeTrue();

            c.Total.Should().Be(15);
            c.Remaining("N1").Should().Be(15);
            c.Scalar($"SELECT SUM(RemainingSmallestUnits) FROM ItemBatches WHERE ItemId={c.ItemId}").Should().Be(15);
        }

        [Fact]
        public void SaleAcrossTwoBatches_ShouldRecordTwoAllocationRowsWithCorrectQuantities()
        {
            var c = new Ctx();
            c.AddBatch("A", 30, 20);
            c.AddBatch("B", 400, 50);

            var sale = c.Sale(25, "T-ALLOC");
            c.Invoices.SaveInvoice(sale, out _).Should().BeTrue();

            c.Scalar($"SELECT COUNT(*) FROM InvoiceItemBatches WHERE InvoiceItemId={sale.Items[0].Id}").Should().Be(2);
            c.Scalar($"SELECT Units FROM InvoiceItemBatches WHERE InvoiceItemId={sale.Items[0].Id} AND BatchId={c.BatchId("A")}").Should().Be(20);
            c.Scalar($"SELECT Units FROM InvoiceItemBatches WHERE InvoiceItemId={sale.Items[0].Id} AND BatchId={c.BatchId("B")}").Should().Be(5);
        }

        [Fact]
        public void BatchLabel_ShouldResolveToSameItemBatchAndExpiry_AndOlderBatchShouldBeDetected()
        {
            var c = new Ctx();
            c.AddBatch("A", 30, 20);
            c.AddBatch("B", 400, 50);

            string code = BatchLabelCode.Encode((int)c.BatchId("B"));
            BatchLabelCode.TryDecode(code, out int id).Should().BeTrue();

            var found = c.Items.GetBatchWithItem(id);
            found.Should().NotBeNull();
            found!.Value.Item.Id.Should().Be(c.ItemId);
            found.Value.Batch.BatchNumber.Should().Be("B");
            found.Value.Batch.ExpiryDate.Date.Should().Be(DateTime.Today.AddDays(400));

            var older = c.Items.GetOlderSellableBatches(c.ItemId, found.Value.Batch.ExpiryDate, id);
            older.Select(b => b.BatchNumber).Should().Equal("A");
            c.Items.GetOlderSellableBatches(c.ItemId, DateTime.Today.AddDays(30), (int)c.BatchId("A")).Should().BeEmpty();
        }

        [Fact]
        public void SupplierReturn_ShouldStoreReason()
        {
            var c = new Ctx();
            c.AddBatch("EXP", -1, 10);
            var ret = new SupplierReturn
            {
                SupplierId = c.SupplierId, Reason = "تلف", UserId = 1, UserName = "t",
                Items = { new SupplierReturnItem { BatchId = (int)c.BatchId("EXP"), SmallestUnitsCount = 4, DeductionPercent = 0 } }
            };
            c.SupplierReturns.Save(ret, out _).Should().BeTrue();
            c.SupplierReturns.GetRecent().First().Reason.Should().Be("تلف");
        }
    }
}
