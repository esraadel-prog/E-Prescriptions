using System;
using System.Linq;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>تدفق شاشة أوامر الشراء على مستوى الـ ViewModel</summary>
    public class PurchaseOrdersViewModelTests
    {
        private static (PurchaseOrdersViewModel vm, DatabaseContext db, int supplierId, int boughtItem, int newItem) Setup()
        {
            var db = new DatabaseContext(":memory:"); db.InitializeAndMigrate(seedData: false);
            var items = new ItemRepository(db); var purchases = new PurchaseRepository(db); var suppliers = new SupplierRepository(db);
            int sup = suppliers.SaveSupplier(new Supplier { Name = "شركة الأوامر", Phone = "0100", IsActive = true });
            int bought = items.SaveItem(new Item { Code = "O-1", Barcode = "6224444444441", NameAr = "ناقص مُشترى", StripsPerBox = 3, PiecesPerStrip = 10, RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 3000, MinStockAlertSmallestUnits = 30, IsActive = true });
            int never = items.SaveItem(new Item { Code = "O-2", Barcode = "6224444444442", NameAr = "ناقص جديد", StripsPerBox = 3, PiecesPerStrip = 10, RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 3000, MinStockAlertSmallestUnits = 30, IsActive = true });
            var inv = new PurchaseInvoice { SupplierId = sup, SupplierName = "شركة الأوامر", InvoiceDate = DateTime.Today, SubtotalPiastres = 3000, TotalPiastres = 3000, PaidPiastres = 3000,
                Items = { new PurchaseInvoiceItem { ItemId = bought, ItemCode = "O-1", ItemNameAr = "x", BatchNumber = "O1", ExpiryDate = DateTime.Today.AddYears(1), UnitLevel = UnitLevel.Box, QuantityPurchased = 1, SmallestUnitsCount = 30, UnitCostPiastres = 3000, TotalPiastres = 3000 } } };
            purchases.SavePurchaseInvoice(inv, out string e).Should().BeTrue(e);
            using (var c = db.CreateConnection()) { using var m = c.CreateCommand(); m.CommandText = $"UPDATE Items SET TotalStockSmallestUnits = -5 WHERE Id = {bought}"; m.ExecuteNonQuery(); }
            var vm = new PurchaseOrdersViewModel(purchases, new PurchaseOrderRepository(db), suppliers) { ConfirmAction = (m, t) => true };
            return (vm, db, sup, bought, never);
        }

        private static long Scalar(DatabaseContext db, string sql)
        {
            using var c = db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql;
            return Convert.ToInt64(m.ExecuteScalar() ?? 0);
        }

        [Fact]
        public void Screen_ShouldListSuggestionsBySupplier_WithNoSupplierGroupLast_AndShowLines()
        {
            var (vm, _, sup, bought, never) = Setup();

            vm.Suggestions.Should().HaveCount(2);
            vm.Suggestions[0].HasSupplier.Should().BeTrue();
            vm.Suggestions[0].SupplierName.Should().Be("شركة الأوامر");
            vm.Suggestions[1].HasSupplier.Should().BeFalse();

            vm.SelectedSuggestion = vm.Suggestions[0];
            vm.SuggestionLines.Should().HaveCount(1);
            vm.SuggestionLines[0].ItemName.Should().Be("ناقص مُشترى");
            vm.SuggestionLines[0].OrderBoxes.Should().Be(4);          // رصيد -5 وحد أدنى 30 => 95 وحدة => 4 علب
            vm.SuggestionLines[0].StockText.Should().NotBeNull();     // العرض بيتحمّل مع الرصيد السالب
            vm.NeedsSupplierChoice.Should().BeFalse();

            vm.SelectedSuggestion = vm.Suggestions[1];
            vm.NeedsSupplierChoice.Should().BeTrue();
            vm.SuggestionLines[0].EstimatedText.Should().Be("غير معروف");
        }

        [Fact]
        public void CreateOrder_ShouldSaveAndRemoveItemsFromSuggestions_AndNoSupplierGroupNeedsAChoice()
        {
            var (vm, db, sup, _, _) = Setup();

            vm.SelectedSuggestion = vm.Suggestions[0];
            vm.Notes = "طلب الأسبوع";
            vm.CreateOrder();
            Scalar(db, "SELECT COUNT(*) FROM PurchaseOrders").Should().Be(1);
            vm.Orders.Should().HaveCount(1);
            vm.Orders[0].IsOpen.Should().BeTrue();
            vm.Orders[0].StatusText.Should().Be("مفتوح");
            vm.Suggestions.Should().HaveCount(1);                    // المُشترى اختفى، فاضل الجديد

            vm.SelectedSuggestion = vm.Suggestions[0];
            vm.CreateOrder();                                        // من غير اختيار مورد
            vm.StatusMessage.Should().Contain("اختار المورد");
            Scalar(db, "SELECT COUNT(*) FROM PurchaseOrders").Should().Be(1);

            vm.SelectedSupplier = vm.Suppliers.First(s => s.Id == sup);
            vm.CreateOrder();
            Scalar(db, "SELECT COUNT(*) FROM PurchaseOrders").Should().Be(2);
            vm.Suggestions.Should().BeEmpty();
        }

        [Fact]
        public void CancelAndReceive_ShouldUpdateStatus_AndCancelledOrderItemsReturnToSuggestions()
        {
            var (vm, db, _, _, _) = Setup();
            vm.SelectedSuggestion = vm.Suggestions[0];
            vm.CreateOrder();

            vm.SelectedOrder = vm.Orders[0];
            vm.OrderLines.Should().HaveCount(1);
            vm.CancelOrderCommand.Execute(null);
            vm.Orders[0].StatusText.Should().Be("ملغي");
            vm.Suggestions.Count.Should().Be(2);

            vm.SelectedOrder = vm.Orders[0];
            vm.ReceiveOrderCommand.Execute(null);                    // مش مفتوح خلاص
            vm.StatusMessage.Should().Contain("مش مفتوح");
            Scalar(db, "SELECT Status FROM PurchaseOrders").Should().Be(2);
        }

        [Fact]
        public void DeclinedConfirmation_ShouldSaveNothing()
        {
            var (vm, db, _, _, _) = Setup();
            vm.ConfirmAction = (m, t) => false;
            vm.SelectedSuggestion = vm.Suggestions[0];
            vm.CreateOrder();
            Scalar(db, "SELECT COUNT(*) FROM PurchaseOrders").Should().Be(0);
        }
    }
}
