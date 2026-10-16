using System;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>قيد اليومية لمرتجع المورد والشطب: مخزون دائن، موردون مدين بالاسترداد، والفرق خسائر تالف. بدون حركة نقدية.</summary>
    public class SupplierReturnJournalTests
    {
        private static long Scalar(DatabaseContext db, string sql)
        {
            using var c = db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql;
            return Convert.ToInt64(m.ExecuteScalar() ?? 0);
        }

        private static (DatabaseContext db, SupplierReturnRepository ret, PurchaseInvoice inv, int supplierId) Setup()
        {
            var db = new DatabaseContext(":memory:"); db.InitializeAndMigrate(seedData: false);
            var items = new ItemRepository(db);
            int itemId = items.SaveItem(new Item { Code = "J-1", Barcode = "6227777777771", NameAr = "صنف", StripsPerBox = 1, PiecesPerStrip = 10, RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 3000, IsActive = true });
            int sup = new SupplierRepository(db).SaveSupplier(new Supplier { Name = "مورد", Phone = "0100", IsActive = true });
            var inv = new PurchaseInvoice
            {
                SupplierId = sup, SupplierName = "مورد", InvoiceDate = DateTime.Today,
                SubtotalPiastres = 5000, VatPiastres = 700, TotalPiastres = 5700, PaidPiastres = 0, RemainingPiastres = 5700,
                Items = { new PurchaseInvoiceItem { ItemId = itemId, ItemCode = "J-1", ItemNameAr = "صنف", BatchNumber = "J1", ExpiryDate = DateTime.Today.AddYears(1), UnitLevel = UnitLevel.Piece, QuantityPurchased = 100, SmallestUnitsCount = 100, UnitCostPiastres = 50, VatPiastres = 700, VatPercent = 14, TotalPiastres = 5700 } }
            };
            new PurchaseRepository(db).SavePurchaseInvoice(inv, out string err).Should().BeTrue(err);
            return (db, new SupplierReturnRepository(db), inv, sup);
        }

        [Fact]
        public void LinkedReturnWithDeduction_ShouldPostBalancedEntry_InventoryCredit_SupplierDebit_LossForTheDifference()
        {
            var (db, repo, inv, _) = Setup();
            var ret = new SupplierReturn
            {
                PurchaseInvoiceId = inv.Id, Reason = "عيب", UserId = 1, UserName = "t",
                Items = { new SupplierReturnItem { PurchaseInvoiceItemId = inv.Items[0].Id, SmallestUnitsCount = 20, DeductionPercent = 10 } }
            };
            repo.Save(ret, out string err).Should().BeTrue(err);   // تكلفة 1140 (5700×20÷100)، استرداد 1026، خسارة 114

            string num = "JV-SRT-" + ret.ReturnNumber;
            Scalar(db, $"SELECT COUNT(*) FROM JournalEntries WHERE EntryNumber LIKE '{num}-%'").Should().Be(2);
            Scalar(db, $"SELECT AmountPiastres FROM JournalEntries WHERE EntryNumber LIKE '{num}-%' AND DebitAccount='{Accounts.Suppliers}' AND CreditAccount='{Accounts.Inventory}'").Should().Be(1026);
            Scalar(db, $"SELECT AmountPiastres FROM JournalEntries WHERE EntryNumber LIKE '{num}-%' AND DebitAccount='{Accounts.InventoryLoss}' AND CreditAccount='{Accounts.Inventory}'").Should().Be(114);
            Scalar(db, $"SELECT SUM(AmountPiastres) FROM JournalEntries WHERE EntryNumber LIKE '{num}-%'").Should().Be(ret.TotalCostPiastres);
            Scalar(db, $"SELECT COUNT(*) FROM JournalEntries WHERE EntryNumber LIKE '{num}-%' AND (DebitAccount='{Accounts.Cash}' OR CreditAccount='{Accounts.Cash}')").Should().Be(0);
        }

        [Fact]
        public void WriteOff_ShouldPostFullCostAsInventoryLoss_WithNoSupplierLeg()
        {
            var (db, repo, inv, sup) = Setup();
            var ret = new SupplierReturn
            {
                Kind = SupplierReturnKind.Destroy, Reason = "انتهاء صلاحية", UserId = 1, UserName = "t",
                Items = { new SupplierReturnItem { BatchId = inv.Items[0].BatchId, SmallestUnitsCount = 10 } }
            };
            repo.Save(ret, out string err).Should().BeTrue(err);

            string num = "JV-SRT-" + ret.ReturnNumber;
            Scalar(db, $"SELECT COUNT(*) FROM JournalEntries WHERE EntryNumber LIKE '{num}-%'").Should().Be(1);
            Scalar(db, $"SELECT AmountPiastres FROM JournalEntries WHERE EntryNumber LIKE '{num}-%' AND DebitAccount='{Accounts.InventoryLoss}' AND CreditAccount='{Accounts.Inventory}'").Should().Be(ret.TotalCostPiastres);
            ret.TotalCostPiastres.Should().BeGreaterThan(0);
        }

        [Fact]
        public void RejectedReturn_ShouldPostNoJournalEntry()
        {
            var (db, repo, inv, _) = Setup();
            long before = Scalar(db, "SELECT COUNT(*) FROM JournalEntries");
            var bad = new SupplierReturn
            {
                PurchaseInvoiceId = inv.Id, Reason = "عيب", UserId = 1, UserName = "t",
                Items = { new SupplierReturnItem { PurchaseInvoiceItemId = inv.Items[0].Id, SmallestUnitsCount = 101 } }
            };
            repo.Save(bad, out _).Should().BeFalse();
            Scalar(db, "SELECT COUNT(*) FROM JournalEntries").Should().Be(before);
        }
    }
}
