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
    /// <summary>
    /// خطوة 39-ج: أوامر شراء مقترحة تلقائياً من قائمة النواقص، مجمّعة بمورد آخر شراء لكل صنف.
    /// </summary>
    public class SuggestedPurchaseOrderTests
    {
        private class Ctx
        {
            public DatabaseContext Db = new DatabaseContext(":memory:");
            public ItemRepository Items = null!;
            public PurchaseRepository Purchases = null!;
            public SupplierRepository Suppliers = null!;

            public Ctx()
            {
                Db.InitializeAndMigrate(seedData: false); // بدون أصناف تجريبية عشان النتائج ما تتلوّثش
                Items = new ItemRepository(Db);
                Purchases = new PurchaseRepository(Db);
                Suppliers = new SupplierRepository(Db);
            }

            public void Exec(string sql) { using var c = Db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql; m.ExecuteNonQuery(); }
            public long Scalar(string sql) { using var c = Db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql; return Convert.ToInt64(m.ExecuteScalar() ?? 0); }

            private int _n;
            /// <summary>صنف بعلبة = 30 وحدة. stock/min بأصغر وحدة</summary>
            public int NewItem(string name, int stock, int min, bool active = true)
            {
                _n++;
                int id = Items.SaveItem(new Item { Code = "SG-" + _n, Barcode = "622000000" + _n.ToString("D4"), NameAr = name, StripsPerBox = 3, PiecesPerStrip = 10, RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 3000, MinStockAlertSmallestUnits = min, IsActive = active });
                Exec($"UPDATE Items SET TotalStockSmallestUnits = {stock} WHERE Id = {id}");
                return id;
            }

            public int NewSupplier(string name) => Suppliers.SaveSupplier(new Supplier { Name = name, Phone = "01000000000", IsActive = true });

            /// <summary>شراء علب كاملة (30 وحدة/علبة) بسعر العلبة بالقرش، في تاريخ معين</summary>
            public void BuyBoxes(int supplierId, string supplierName, int itemId, int boxes, long boxCost, DateTime date)
            {
                long taxable = boxCost * boxes;
                var inv = new PurchaseInvoice
                {
                    SupplierId = supplierId, SupplierName = supplierName, InvoiceDate = date,
                    SubtotalPiastres = taxable, TotalPiastres = taxable, PaidPiastres = taxable,
                    Items = { new PurchaseInvoiceItem { ItemId = itemId, ItemCode = "X", ItemNameAr = "x", BatchNumber = "B" + Guid.NewGuid().ToString("N").Substring(0, 6), ExpiryDate = DateTime.Today.AddYears(1), UnitLevel = UnitLevel.Box, QuantityPurchased = boxes, SmallestUnitsCount = boxes * 30, UnitCostPiastres = boxCost, TotalPiastres = taxable } }
                };
                Purchases.SavePurchaseInvoice(inv, out string err).Should().BeTrue(err);
            }
        }

        [Fact]
        public void ShortageItems_ShouldBeGroupedBySupplier_WithExistingReportQuantityRule_RoundedToBoxes_AndEstimatedFromLastPurchase()
        {
            var c = new Ctx();
            int supA = c.NewSupplier("أ - شركة الأولى");
            int supB = c.NewSupplier("ب - شركة الثانية");
            int item1 = c.NewItem("صنف واحد", stock: 0, min: 30);
            int item2 = c.NewItem("صنف اثنين", stock: 0, min: 100);
            c.BuyBoxes(supA, "أ - شركة الأولى", item1, 10, 3000, DateTime.Today.AddDays(-5));
            c.BuyBoxes(supB, "ب - شركة الثانية", item2, 5, 2000, DateTime.Today.AddDays(-5));
            c.Exec($"UPDATE Items SET TotalStockSmallestUnits = 10 WHERE Id = {item1}");   // أقل من الحد 30
            c.Exec($"UPDATE Items SET TotalStockSmallestUnits = 100 WHERE Id = {item2}");  // يساوي الحد 100 = ناقص

            var orders = c.Purchases.GetSuggestedPurchaseOrders();

            orders.Select(o => o.SupplierName).Should().Equal("أ - شركة الأولى", "ب - شركة الثانية");
            orders.All(o => o.HasSupplier).Should().BeTrue();

            var l1 = orders[0].Lines.Single();
            l1.ItemId.Should().Be(item1);
            l1.DeficitUnits.Should().Be(20);
            l1.SuggestedUnits.Should().Be(80);              // عجز 20 + ضعف الحد 60 (نفس قاعدة تقرير النواقص)
            l1.SuggestedBoxes.Should().Be(3);               // 80 ÷ 30 = 2.67 → 3 علب
            l1.OrderUnits.Should().Be(90);
            l1.EstimatedCostPiastres.Should().Be(9000);     // 3 علب × 30.00 جنيه (سعر آخر شراء)
            orders[0].EstimatedTotalPiastres.Should().Be(9000);

            var l2 = orders[1].Lines.Single();
            l2.DeficitUnits.Should().Be(0);
            l2.SuggestedUnits.Should().Be(200);
            l2.SuggestedBoxes.Should().Be(7);               // 200 ÷ 30 = 6.67 → 7 علب
            l2.EstimatedCostPiastres.Should().Be(14000);    // 7 علب × 20.00 جنيه
        }

        [Fact]
        public void LatestPurchaseDecidesTheSupplier_AndInactiveSupplierFallsBackToNoSupplierGroup()
        {
            var c = new Ctx();
            int old = c.NewSupplier("المورد القديم");
            int recent = c.NewSupplier("المورد الأحدث");
            int dead = c.NewSupplier("مورد متوقف");
            int itemA = c.NewItem("أ", 0, 30);
            int itemB = c.NewItem("ب", 0, 30);
            c.BuyBoxes(old, "المورد القديم", itemA, 1, 3000, DateTime.Today.AddDays(-60));
            c.BuyBoxes(recent, "المورد الأحدث", itemA, 1, 3500, DateTime.Today.AddDays(-2));
            c.BuyBoxes(dead, "مورد متوقف", itemB, 1, 3000, DateTime.Today.AddDays(-2));
            c.Exec($"UPDATE Items SET TotalStockSmallestUnits = 5 WHERE Id IN ({itemA},{itemB})");
            c.Exec($"UPDATE Suppliers SET IsActive = 0 WHERE Id = {dead}");

            var orders = c.Purchases.GetSuggestedPurchaseOrders();

            var recentOrder = orders.Single(o => o.SupplierId == recent);
            recentOrder.Lines.Select(l => l.ItemId).Should().Equal(itemA);
            recentOrder.Lines[0].EstimatedCostPiastres.Should().Be(10500); // بسعر آخر شراء (35.00 للعلبة) × 3 علب
            orders.Any(o => o.SupplierId == old).Should().BeFalse();

            // المورد المتوقف ما يتقترحش
            orders.Any(o => o.SupplierId == dead).Should().BeFalse();
            orders.Last().HasSupplier.Should().BeFalse();
            orders.Last().Lines.Select(l => l.ItemId).Should().Equal(itemB);
        }

        [Fact]
        public void ItemsNeverPurchased_ShouldGoToTheNoSupplierGroup_LastAndUnpriced()
        {
            var c = new Ctx();
            int sup = c.NewSupplier("شركة");
            int bought = c.NewItem("اشتُري", 0, 30);
            int never = c.NewItem("عمره ما اتشترى", 0, 30);
            c.BuyBoxes(sup, "شركة", bought, 1, 3000, DateTime.Today);
            c.Exec($"UPDATE Items SET TotalStockSmallestUnits = 0 WHERE Id IN ({bought},{never})");

            var orders = c.Purchases.GetSuggestedPurchaseOrders();

            orders.Should().HaveCount(2);
            orders[0].SupplierId.Should().Be(sup);
            var last = orders[1];
            last.HasSupplier.Should().BeFalse();
            last.Lines.Single().ItemId.Should().Be(never);
            last.Lines.Single().HasPurchaseHistory.Should().BeFalse();
            last.Lines.Single().EstimatedCostPiastres.Should().Be(0);
            last.HasUnpricedLines.Should().BeTrue();
            orders[0].HasUnpricedLines.Should().BeFalse();
        }

        [Fact]
        public void OnlyActiveItemsAtOrBelowMinimum_WithPositiveSuggestion_AreSuggested_AndNegativeStockIsCovered()
        {
            var c = new Ctx();
            int low = c.NewItem("ناقص", 10, 30);
            int atMin = c.NewItem("عند الحد", 30, 30);
            c.NewItem("فوق الحد", 31, 30);
            c.NewItem("موقوف", 0, 30, active: false);
            c.NewItem("بدون حد ورصيد صفر", 0, 0);
            int oversold = c.NewItem("مباع بالسالب", -5, 30);

            var all = c.Purchases.GetSuggestedPurchaseOrders().SelectMany(o => o.Lines).ToList();

            var expected = new[] { low, atMin, oversold }.OrderBy(x => x).ToArray();
            all.Select(l => l.ItemId).OrderBy(x => x).Should().Equal(expected[0], expected[1], expected[2]);
            var o = all.Single(l => l.ItemId == oversold);
            o.DeficitUnits.Should().Be(35);          // 30 - (-5)
            o.SuggestedUnits.Should().Be(95);        // 35 + 60
            o.SuggestedBoxes.Should().Be(4);
        }

        [Fact]
        public void NothingShort_ShouldReturnEmptyList()
        {
            var c = new Ctx();
            c.NewItem("كفاية", 500, 30);
            c.Purchases.GetSuggestedPurchaseOrders().Should().BeEmpty();
        }

        [Fact]
        public void Suggestions_ShouldBeReadOnly_NoInvoicesCountersOrStockChange()
        {
            var c = new Ctx();
            int sup = c.NewSupplier("شركة");
            int item = c.NewItem("ناقص", 0, 30);
            c.BuyBoxes(sup, "شركة", item, 1, 3000, DateTime.Today);
            c.Exec($"UPDATE Items SET TotalStockSmallestUnits = 3 WHERE Id = {item}");

            long invoices = c.Scalar("SELECT COUNT(*) FROM PurchaseInvoices");
            long counters = c.Scalar("SELECT IFNULL(SUM(Value),0) FROM Counters");
            long stock = c.Scalar("SELECT SUM(TotalStockSmallestUnits) FROM Items");

            c.Purchases.GetSuggestedPurchaseOrders().Should().NotBeEmpty();
            c.Purchases.GetSuggestedPurchaseOrders().Should().NotBeEmpty();

            c.Scalar("SELECT COUNT(*) FROM PurchaseInvoices").Should().Be(invoices);
            c.Scalar("SELECT IFNULL(SUM(Value),0) FROM Counters").Should().Be(counters);
            c.Scalar("SELECT SUM(TotalStockSmallestUnits) FROM Items").Should().Be(stock);
        }
    }
}
