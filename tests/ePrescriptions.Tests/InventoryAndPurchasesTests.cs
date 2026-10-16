using System;
using System.Data.SQLite;
using ePrescriptions.Core.Financial;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class InventoryAndPurchasesTests
    {
        [Fact]
        public void PurchaseInvoice_ShouldIncreaseStockAndRecordSupplierBalance()
        {
            // Arrange
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);

            var itemRepo = new ItemRepository(db);
            var suppRepo = new SupplierRepository(db);
            var purRepo = new PurchaseRepository(db);

            // إضافة مورد جديد
            int suppId = suppRepo.SaveSupplier(new Supplier
            {
                Name = "مستودع الأدوية الحديث",
                Phone = "0501112233",
                TaxNumber = "300099887700003",
                BalancePiastres = 0
            });

            // جلب صنف تجريبي (بانادول)
            var item = itemRepo.GetByBarcodeOrCode("6281001000101")!;
            int initialStock = item.TotalStockSmallestUnits;

            // Act: إنشاء فاتورة شراء 10 علب (كل علبة 24 قرص = 240 قرص)
            string invNum = purRepo.GenerateNextPurchaseInvoiceNumber();
            var purInv = new PurchaseInvoice
            {
                InvoiceNumber = invNum,
                SupplierId = suppId,
                SupplierName = "مستودع الأدوية الحديث",
                InvoiceDate = DateTime.Today,
                SubtotalPiastres = 9000,
                VatPiastres = 1350,
                TotalPiastres = 10350,
                PaidPiastres = 5000,
                RemainingPiastres = 5350
            };

            purInv.Items.Add(new PurchaseInvoiceItem
            {
                ItemId = item.Id,
                ItemCode = item.Code,
                ItemNameAr = item.NameAr,
                BatchNumber = "BATCH-NEW-99",
                ExpiryDate = DateTime.Today.AddYears(2),
                UnitLevel = UnitLevel.Box,
                QuantityPurchased = 10,
                SmallestUnitsCount = 240,
                UnitCostPiastres = 900,
                VatPiastres = 1350,
                TotalPiastres = 10350
            });

            bool success = purRepo.SavePurchaseInvoice(purInv, out string err);

            // Assert
            success.Should().BeTrue();
            err.Should().BeEmpty();

            // فحص زيادة رصيد الصنف
            var updatedItem = itemRepo.GetByBarcodeOrCode(item.Code)!;
            updatedItem.TotalStockSmallestUnits.Should().Be(initialStock + 240);

            // فحص رصيد المورد (المتبقي = 5350 هللة)
            var updatedSupp = suppRepo.GetById(suppId)!;
            updatedSupp.BalancePiastres.Should().Be(5350);

            // Act 2: سداد 3000 هللة بسند صرف
            var voucher = new SupplierVoucher
            {
                VoucherNumber = purRepo.GenerateNextSupplierVoucherNumber(),
                SupplierId = suppId,
                SupplierName = updatedSupp.Name,
                Date = DateTime.Now,
                AmountPiastres = 3000,
                PaymentMethod = PaymentMethod.Cash
            };
            bool vSuccess = purRepo.AddSupplierVoucher(voucher);
            vSuccess.Should().BeTrue();

            var finalSupp = suppRepo.GetById(suppId)!;
            finalSupp.BalancePiastres.Should().Be(2350);
        }

        [Fact]
        public void CustomerVoucher_ShouldReduceCustomerBalanceAndReflectInStatement()
        {
            // Arrange
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);

            var custRepo = new CustomerRepository(db);

            int custId = custRepo.AddCustomer(new Customer
            {
                Name = "أحمد خالد الشمري",
                Phone = "0559988776",
                CreditLimitPiastres = 100000,
                CurrentBalancePiastres = 50000 // مديونية سابقة 500 ريال
            });

            // Act: سداد 200 ريال (20000 هللة)
            string vNum = custRepo.GenerateNextCustomerVoucherNumber();
            bool success = custRepo.AddCustomerVoucher(new CustomerVoucher
            {
                VoucherNumber = vNum,
                CustomerId = custId,
                CustomerName = "أحمد خالد الشمري",
                Date = DateTime.Now,
                AmountPiastres = 20000,
                PaymentMethod = PaymentMethod.Cash,
                Notes = "سداد نقدي"
            });

            // Assert
            success.Should().BeTrue();

            var updatedCust = custRepo.GetById(custId)!;
            updatedCust.CurrentBalancePiastres.Should().Be(30000); // 50000 - 20000 = 30000

            var statement = custRepo.GetCustomerStatement(custId);
            statement.Should().NotBeEmpty();
            statement.Exists(r => r.ReferenceNumber == vNum && r.CreditPiastres == 20000).Should().BeTrue();
        }
    }
}
