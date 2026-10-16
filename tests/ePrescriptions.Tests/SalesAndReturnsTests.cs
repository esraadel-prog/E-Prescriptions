using System;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class SalesAndReturnsTests
    {
        private static Invoice BuildInvoice(InvoiceRepository invRepo, InvoiceType type, int shiftId, Item item, int customerId, PaymentMethod method, long total, Invoice? original = null)
        {
            var inv = new Invoice
            {
                InvoiceNumber = invRepo.GenerateNextInvoiceNumber(type),
                Type = type,
                CustomerId = customerId,
                CustomerName = "عميل",
                PaymentMethod = method,
                UserId = 1,
                UserName = "كاشير الاختبار",
                ShiftId = shiftId,
                SubtotalPiastres = total,
                TotalPiastres = total,
                PaidPiastres = method == PaymentMethod.OnAccount ? 0 : total,
                RemainingPiastres = method == PaymentMethod.OnAccount ? total : 0,
                Items =
                {
                    new InvoiceItem
                    {
                        ItemId = item.Id, ItemCode = item.Code, ItemNameAr = item.NameAr,
                        BatchNumber = item.Batches.Count > 0 ? item.Batches[0].BatchNumber : "عام",
                        UnitLevel = UnitLevel.Piece, QuantitySold = 10, SmallestUnitsCount = 10,
                        UnitPricePiastres = total / 10, TotalPiastres = total,
                        OriginalInvoiceItemId = original?.Items[0].Id
                    }
                }
            };
            // مرتجع المبيعات لازم يكون مرتبطاً بفاتورة البيع الأصلية
            inv.OriginalInvoiceId = original?.Id;
            return inv;
        }

        [Fact]
        public void Sale_ThenReturn_ShouldRestoreStockAndAdjustShiftTotals()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var itemRepo = new ItemRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var shiftRepo = new ShiftRepository(db);

            int shiftId = shiftRepo.OpenShift(1, "كاشير الاختبار", 0);
            var item = itemRepo.GetByBarcodeOrCode("6281001000101")!;
            int before = item.TotalStockSmallestUnits;

            var sale = BuildInvoice(invRepo, InvoiceType.Sales, shiftId, item, 1, PaymentMethod.Cash, 5000);
            invRepo.SaveInvoice(sale, out string err1).Should().BeTrue();
            itemRepo.GetById(item.Id)!.TotalStockSmallestUnits.Should().Be(before - 10);
            shiftRepo.GetCurrentOpenShift(1)!.ExpectedCashPiastres.Should().Be(5000);

            var ret = BuildInvoice(invRepo, InvoiceType.Return, shiftId, item, 1, PaymentMethod.Cash, 5000, sale);
            invRepo.SaveInvoice(ret, out string err2).Should().BeTrue();

            itemRepo.GetById(item.Id)!.TotalStockSmallestUnits.Should().Be(before);
            var shift = shiftRepo.GetCurrentOpenShift(1)!;
            shift.ExpectedCashPiastres.Should().Be(0);
            shift.TotalReturnsCashPiastres.Should().Be(5000);
        }

        [Fact]
        public void OnAccountSale_ShouldIncreaseCustomerBalance_AndReturnShouldDecreaseIt()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var itemRepo = new ItemRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var custRepo = new CustomerRepository(db);
            var shiftRepo = new ShiftRepository(db);

            int shiftId = shiftRepo.OpenShift(1, "كاشير الاختبار", 0);
            var item = itemRepo.GetByBarcodeOrCode("6281001000101")!;
            int custId = 2; // عميل آجل من البيانات الأولية
            long start = custRepo.GetById(custId)!.CurrentBalancePiastres;

            var onAccountSale = BuildInvoice(invRepo, InvoiceType.Sales, shiftId, item, custId, PaymentMethod.OnAccount, 3000);
            invRepo.SaveInvoice(onAccountSale, out _).Should().BeTrue();
            custRepo.GetById(custId)!.CurrentBalancePiastres.Should().Be(start + 3000);

            invRepo.SaveInvoice(BuildInvoice(invRepo, InvoiceType.Return, shiftId, item, custId, PaymentMethod.OnAccount, 3000, onAccountSale), out _).Should().BeTrue();
            custRepo.GetById(custId)!.CurrentBalancePiastres.Should().Be(start);
        }

        [Fact]
        public void SaveItem_Update_ShouldKeepWholesalePrice_WhenEditedFromStoredItem()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var itemRepo = new ItemRepository(db);

            var item = itemRepo.GetByBarcodeOrCode("6281001000101")!;
            item.WholesalePriceBoxPiastres = 4321;
            itemRepo.SaveItem(item);

            var again = itemRepo.GetById(item.Id)!;
            again.WholesalePriceBoxPiastres.Should().Be(4321);
        }
    }
}
