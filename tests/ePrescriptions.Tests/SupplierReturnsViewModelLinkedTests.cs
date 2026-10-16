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
    /// <summary>تدفق شاشة المرتجع في وضع "مرتجع من فاتورة شراء" على مستوى الـ ViewModel</summary>
    public class SupplierReturnsViewModelLinkedTests
    {
        private static (SupplierReturnsViewModel vm, DatabaseContext db, PurchaseInvoice inv) Setup()
        {
            var db = new DatabaseContext(":memory:"); db.InitializeAndMigrate(seedData: false);
            var items = new ItemRepository(db);
            int itemId = items.SaveItem(new Item { Code = "UI-1", Barcode = "6225555555551", NameAr = "صنف واجهة", StripsPerBox = 1, PiecesPerStrip = 10, RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 3000, IsActive = true });
            var suppliers = new SupplierRepository(db);
            int sup = suppliers.SaveSupplier(new Supplier { Name = "شركة الواجهة", Phone = "0100", IsActive = true });
            var inv = new PurchaseInvoice
            {
                SupplierId = sup, SupplierName = "شركة الواجهة", InvoiceDate = DateTime.Today,
                SubtotalPiastres = 5000, VatPiastres = 700, TotalPiastres = 5700, PaidPiastres = 0, RemainingPiastres = 5700,
                Items = { new PurchaseInvoiceItem { ItemId = itemId, ItemCode = "UI-1", ItemNameAr = "صنف واجهة", BatchNumber = "U1", ExpiryDate = DateTime.Today.AddYears(1), UnitLevel = UnitLevel.Piece, QuantityPurchased = 100, SmallestUnitsCount = 100, UnitCostPiastres = 50, VatPiastres = 700, VatPercent = 14, TotalPiastres = 5700 } }
            };
            new PurchaseRepository(db).SavePurchaseInvoice(inv, out string err).Should().BeTrue(err);
            var vm = new SupplierReturnsViewModel(items, suppliers, new SupplierReturnRepository(db));
            return (vm, db, inv);
        }

        private static long Scalar(DatabaseContext db, string sql)
        {
            using var c = db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql;
            return Convert.ToInt64(m.ExecuteScalar() ?? 0);
        }

        [Fact]
        public void SearchSelectAddAndSave_ShouldCreateLinkedReturn_RefreshAvailableAndClearDocument()
        {
            var (vm, db, inv) = Setup();
            vm.IsFromInvoice = true;
            vm.DeductionPercent.Should().Be(0);     // عيب/خطأ توريد: استرداد كامل افتراضياً
            vm.Reason.Should().Be("عيب تصنيع");

            vm.InvoiceSearchText = inv.InvoiceNumber;
            vm.SearchInvoices();
            vm.InvoiceSearchResults.Should().HaveCount(1);

            vm.SelectedInvoiceResult = vm.InvoiceSearchResults[0];
            vm.InvoiceSourceLines.Should().HaveCount(1);
            vm.InvoiceSourceLines[0].CanReturn.Should().BeTrue();
            vm.InvoiceInfoText.Should().Contain(inv.InvoiceNumber);

            vm.AddInvoiceLine(vm.InvoiceSourceLines[0]);
            vm.InvoiceReturnLines.Should().HaveCount(1);
            var line = vm.InvoiceReturnLines[0];
            line.UnitLevel = UnitLevel.Piece;
            line.Quantity = 20;
            line.SmallestUnits.Should().Be(20);
            line.CostTotalPiastres.Should().Be(1140);   // 5700 × 20 ÷ 100 من فاتورة الشراء
            line.CreditPiastres.Should().Be(1140);
            line.ExceedsReturnable.Should().BeFalse();

            // أكبر من المتاح: الحفظ يترفض قبل أي تأكيد ولا يتسجل شيء
            line.Quantity = 101;
            line.ExceedsReturnable.Should().BeTrue();
            vm.Save();
            vm.StatusMessage.Should().Contain("أكبر من المتاح");
            Scalar(db, "SELECT COUNT(*) FROM SupplierReturns").Should().Be(0);

            line.Quantity = 20;
            vm.ConfirmAction = (m, t) => true;
            vm.Save();

            Scalar(db, "SELECT COUNT(*) FROM SupplierReturns").Should().Be(1);
            Scalar(db, "SELECT PurchaseInvoiceId FROM SupplierReturns").Should().Be(inv.Id);
            Scalar(db, "SELECT CreditPiastres FROM SupplierReturns").Should().Be(1140);
            vm.InvoiceReturnLines.Should().BeEmpty();
            vm.InvoiceSourceLines[0].Source.ReturnedUnits.Should().Be(20);   // اتحدّث المتاح بعد الحفظ
            vm.InvoiceSourceLines[0].Source.ReturnableUnits.Should().Be(80);
            vm.History.Any(h => h.PurchaseInvoiceText == inv.InvoiceNumber).Should().BeTrue();
        }

        [Fact]
        public void FullyReturnedLine_ShouldNotBeAddable_AndModesAreMutuallyExclusive()
        {
            var (vm, db, inv) = Setup();
            vm.IsFromInvoice = true;
            vm.InvoiceSearchText = "";
            vm.SearchInvoices();
            vm.SelectedInvoiceResult = vm.InvoiceSearchResults[0];
            vm.AddInvoiceLine(vm.InvoiceSourceLines[0]);     // الافتراضي: كل المتاح (100 قطعة = 10 علب)
            vm.InvoiceReturnLines[0].SmallestUnits.Should().Be(100);

            vm.ConfirmAction = (m, t) => true;
            vm.Save();
            Scalar(db, "SELECT COUNT(*) FROM SupplierReturns").Should().Be(1);

            vm.InvoiceSourceLines[0].CanReturn.Should().BeFalse();
            vm.AddInvoiceLine(vm.InvoiceSourceLines[0]);
            vm.InvoiceReturnLines.Should().BeEmpty();

            // تنافر الوضعين
            vm.IsDestroy = true;
            vm.IsFromInvoice.Should().BeFalse();
            vm.IsFromInvoice = true;
            vm.IsDestroy.Should().BeFalse();
            vm.IsSupplierSelectable.Should().BeFalse();
            vm.IsFromInvoice = false;
            vm.IsSupplierSelectable.Should().BeTrue();
            vm.DeductionPercent.Should().Be(10);
        }

        [Fact]
        public void SaveWithoutInvoiceOrLines_ShouldShowMessageAndSaveNothing()
        {
            var (vm, db, _) = Setup();
            vm.IsFromInvoice = true;
            vm.Save();
            vm.StatusMessage.Should().Contain("فاتورة الشراء");
            Scalar(db, "SELECT COUNT(*) FROM SupplierReturns").Should().Be(0);
        }
    }
}
