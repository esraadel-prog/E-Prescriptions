using System;
using System.Linq;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 39-هـ: أوامر الشراء المحفوظة من مقترحات النواقص</summary>
    public class PurchaseOrderTests
    {
        private class Ctx
        {
            public DatabaseContext Db = new DatabaseContext(":memory:");
            public ItemRepository Items = null!; public PurchaseRepository Purchases = null!; public PurchaseOrderRepository Orders = null!;
            public int SupA, ItemShort, ItemShort2;
            public Ctx()
            {
                Db.InitializeAndMigrate(seedData: false);
                Items = new ItemRepository(Db); Purchases = new PurchaseRepository(Db); Orders = new PurchaseOrderRepository(Db);
                SupA = new SupplierRepository(Db).SaveSupplier(new Supplier { Name = "شركة أ", Phone = "0100", IsActive = true });
                ItemShort = NewItem("S-1", "6226000000011", "ناقص 1");
                ItemShort2 = NewItem("S-2", "6226000000012", "ناقص 2");
                Buy(ItemShort); Buy(ItemShort2);
                Exec($"UPDATE Items SET TotalStockSmallestUnits = 5 WHERE Id IN ({ItemShort},{ItemShort2})");
            }
            private int NewItem(string code, string bc, string name) => Items.SaveItem(new Item { Code = code, Barcode = bc, NameAr = name, StripsPerBox = 3, PiecesPerStrip = 10, RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 3000, MinStockAlertSmallestUnits = 30, IsActive = true });
            private void Buy(int itemId)
            {
                var inv = new PurchaseInvoice { SupplierId = SupA, SupplierName = "شركة أ", InvoiceDate = DateTime.Today, SubtotalPiastres = 3000, TotalPiastres = 3000, PaidPiastres = 3000,
                    Items = { new PurchaseInvoiceItem { ItemId = itemId, ItemCode = "X", ItemNameAr = "x", BatchNumber = "B" + itemId, ExpiryDate = DateTime.Today.AddYears(1), UnitLevel = UnitLevel.Box, QuantityPurchased = 1, SmallestUnitsCount = 30, UnitCostPiastres = 3000, TotalPiastres = 3000 } } };
                Purchases.SavePurchaseInvoice(inv, out string e).Should().BeTrue(e);
            }
            public void Exec(string sql) { using var c = Db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql; m.ExecuteNonQuery(); }
            public long Scalar(string sql) { using var c = Db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql; return Convert.ToInt64(m.ExecuteScalar() ?? 0); }
        }

        [Fact]
        public void CreateFromSuggestion_ShouldSaveOrderWithWholeBoxLines_NumberedAndWithoutAnyStockOrJournalEffect()
        {
            var c = new Ctx();
            var suggestion = c.Purchases.GetSuggestedPurchaseOrders().Single();
            long stock = c.Scalar("SELECT SUM(TotalStockSmallestUnits) FROM Items");
            long journal = c.Scalar("SELECT COUNT(*) FROM JournalEntries");
            long invoices = c.Scalar("SELECT COUNT(*) FROM PurchaseInvoices");

            int id = c.Orders.Create(suggestion, null, 1, "مدير", "طلب أسبوعي", out string err);
            id.Should().BeGreaterThan(0, err);

            var o = c.Orders.Get(id)!;
            o.OrderNumber.StartsWith("PO-").Should().BeTrue();
            o.SupplierId.Should().Be(c.SupA);
            o.SupplierName.Should().Be("شركة أ");
            o.Status.Should().Be(PurchaseOrderStatus.Open);
            o.Notes.Should().Be("طلب أسبوعي");
            o.Items.Should().HaveCount(2);
            // رصيد 5 وحد أدنى 30: عجز 25 + 60 = 85 وحدة => 3 علب (90)
            o.Items.All(i => i.OrderUnits == 90 && i.OrderBoxes == 3).Should().BeTrue();
            o.EstimatedTotalPiastres.Should().Be(o.Items.Sum(i => i.EstimatedCostPiastres));
            o.EstimatedTotalPiastres.Should().Be(2 * 9000);   // 3 علب × 30.00 لكل صنف

            c.Scalar("SELECT SUM(TotalStockSmallestUnits) FROM Items").Should().Be(stock);
            c.Scalar("SELECT COUNT(*) FROM JournalEntries").Should().Be(journal);
            c.Scalar("SELECT COUNT(*) FROM PurchaseInvoices").Should().Be(invoices);
        }

        [Fact]
        public void ItemsOnAnOpenOrder_ShouldDisappearFromSuggestions_AndComeBackWhenOrderIsCancelled()
        {
            var c = new Ctx();
            int id = c.Orders.Create(c.Purchases.GetSuggestedPurchaseOrders().Single(), null, 1, "م", null, out string err);
            id.Should().BeGreaterThan(0, err);

            c.Purchases.GetSuggestedPurchaseOrders().Should().BeEmpty();

            c.Orders.SetStatus(id, PurchaseOrderStatus.Cancelled, out _).Should().BeTrue();
            c.Purchases.GetSuggestedPurchaseOrders().SelectMany(o => o.Lines).Should().HaveCount(2);
        }

        [Fact]
        public void StaleSuggestion_ShouldNotCreateASecondOpenOrderForTheSameItems()
        {
            var c = new Ctx();
            var suggestion = c.Purchases.GetSuggestedPurchaseOrders().Single();
            c.Orders.Create(suggestion, null, 1, "م", null, out _).Should().BeGreaterThan(0);

            c.Orders.Create(suggestion, null, 1, "م", null, out string err).Should().Be(0);
            err.Should().Contain("أمر شراء مفتوح");
            c.Scalar("SELECT COUNT(*) FROM PurchaseOrders").Should().Be(1);
        }

        [Fact]
        public void NoSupplierGroup_NeedsAnActiveSupplierToBeChosen()
        {
            var c = new Ctx();
            int never = c.Items.SaveItem(new Item { Code = "N-1", Barcode = "6226000000099", NameAr = "جديد", StripsPerBox = 3, PiecesPerStrip = 10, RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 3000, MinStockAlertSmallestUnits = 30, IsActive = true });
            var noSup = c.Purchases.GetSuggestedPurchaseOrders().Single(o => !o.HasSupplier);
            noSup.Lines.Single().ItemId.Should().Be(never);

            c.Orders.Create(noSup, null, 1, "م", null, out string e1).Should().Be(0);
            e1.Should().Contain("المورد");

            int dead = new SupplierRepository(c.Db).SaveSupplier(new Supplier { Name = "متوقف", Phone = "0100", IsActive = true });
            c.Exec($"UPDATE Suppliers SET IsActive = 0 WHERE Id = {dead}");
            c.Orders.Create(noSup, dead, 1, "م", null, out string e2).Should().Be(0);
            e2.Should().Contain("متوقف");

            int ok = c.Orders.Create(noSup, c.SupA, 1, "م", null, out string e3);
            ok.Should().BeGreaterThan(0, e3);
            c.Orders.Get(ok)!.SupplierName.Should().Be("شركة أ");
            // الصنف الجديد وصل لعدم تقدير تكلفة
            c.Orders.Get(ok)!.Items.Single().EstimatedCostPiastres.Should().Be(0);
        }

        [Fact]
        public void StatusFlow_OnlyOpenOrdersCanBeReceivedOrCancelled_AndListFiltersByStatus()
        {
            var c = new Ctx();
            int id = c.Orders.Create(c.Purchases.GetSuggestedPurchaseOrders().Single(), null, 1, "م", null, out _);

            c.Orders.GetRecent(10, PurchaseOrderStatus.Open).Select(o => o.Id).Should().Contain(id);
            c.Orders.SetStatus(id, PurchaseOrderStatus.Open, out string e0).Should().BeFalse();
            c.Orders.SetStatus(id, PurchaseOrderStatus.Received, out _).Should().BeTrue();
            c.Orders.GetRecent(10, PurchaseOrderStatus.Open).Should().BeEmpty();
            c.Orders.SetStatus(id, PurchaseOrderStatus.Cancelled, out string e1).Should().BeFalse();
            e1.Should().Contain("مش مفتوح");
            c.Orders.SetStatus(9999, PurchaseOrderStatus.Cancelled, out _).Should().BeFalse();
            c.Orders.Get(9999).Should().BeNull();
        }

        [Fact]
        public void EmptyOrDuplicateLines_ShouldBeRejected()
        {
            var c = new Ctx();
            var empty = new SuggestedPurchaseOrder { SupplierId = c.SupA, SupplierName = "شركة أ" };
            c.Orders.Create(empty, null, 1, "م", null, out string e1).Should().Be(0);
            e1.Should().Contain("أصناف");

            var dup = c.Purchases.GetSuggestedPurchaseOrders().Single();
            dup.Lines.Add(dup.Lines[0]);
            c.Orders.Create(dup, null, 1, "م", null, out string e2).Should().Be(0);
            e2.Should().Contain("متكرر");
        }
    }
}
