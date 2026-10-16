using System;
using System.Collections.Generic;
using System.Linq;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 15: قيود اليومية بتتولّد تلقائياً مع البيع والمشتريات داخل نفس الـ Transaction</summary>
    public class JournalPostingTests
    {
        private static Invoice Build(InvoiceRepository invRepo, InvoiceType type, int shiftId, Item item, PaymentMethod method,
            long total, long vat, long remaining, Invoice? original = null)
        {
            var inv = new Invoice
            {
                InvoiceNumber = invRepo.GenerateNextInvoiceNumber(type),
                Type = type, CustomerId = 1, CustomerName = "عميل", PaymentMethod = method,
                UserId = 1, UserName = "اختبار", ShiftId = shiftId,
                SubtotalPiastres = total - vat, VatPiastres = vat, TotalPiastres = total,
                PaidPiastres = total - remaining, RemainingPiastres = remaining,
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
            inv.OriginalInvoiceId = original?.Id;
            return inv;
        }

        private static List<JournalEntry> Entries(DatabaseContext db, string like)
        {
            var finance = new FinanceRepository(db);
            return finance.GetJournalEntries(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1))
                .Where(j => j.EntryNumber.Contains(like)).ToList();
        }

        private static (DatabaseContext db, InvoiceRepository inv, Item item, int shift) Setup()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var itemRepo = new ItemRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            int shift = new ShiftRepository(db).OpenShift(1, "اختبار", 0);
            return (db, invRepo, itemRepo.GetByBarcodeOrCode("6281001000101")!, shift);
        }

        [Fact]
        public void CashSale_ShouldCreateBalancedEntries_CashDebit_RevenueAndVatCredit()
        {
            var (db, invRepo, item, shift) = Setup();
            var sale = Build(invRepo, InvoiceType.Sales, shift, item, PaymentMethod.Cash, 11400, 1400, 0);

            invRepo.SaveInvoice(sale, out string err).Should().BeTrue();

            var jv = Entries(db, sale.InvoiceNumber);
            jv.Should().HaveCount(2);
            jv.Sum(j => j.AmountPiastres).Should().Be(11400);
            jv.Should().ContainSingle(j => j.DebitAccount == Accounts.Cash && j.CreditAccount == Accounts.SalesRevenue && j.AmountPiastres == 10000);
            jv.Should().ContainSingle(j => j.DebitAccount == Accounts.Cash && j.CreditAccount == Accounts.VatPayable && j.AmountPiastres == 1400);
        }

        [Fact]
        public void OnAccountSale_ShouldDebitCustomers()
        {
            var (db, invRepo, item, shift) = Setup();
            var sale = Build(invRepo, InvoiceType.Sales, shift, item, PaymentMethod.OnAccount, 5000, 0, 5000);

            invRepo.SaveInvoice(sale, out _).Should().BeTrue();

            var jv = Entries(db, sale.InvoiceNumber);
            jv.Should().HaveCount(1);
            jv[0].DebitAccount.Should().Be(Accounts.Customers);
            jv[0].CreditAccount.Should().Be(Accounts.SalesRevenue);
            jv[0].AmountPiastres.Should().Be(5000);
        }

        [Fact]
        public void PartiallyPaidOnAccountSale_ShouldSplitBetweenCashAndCustomers_AndStayBalanced()
        {
            var (db, invRepo, item, shift) = Setup();
            var sale = Build(invRepo, InvoiceType.Sales, shift, item, PaymentMethod.OnAccount, 5700, 700, 3000);

            invRepo.SaveInvoice(sale, out _).Should().BeTrue();

            var jv = Entries(db, sale.InvoiceNumber);
            jv.Sum(j => j.AmountPiastres).Should().Be(5700);
            jv.Where(j => j.DebitAccount == Accounts.Customers).Sum(j => j.AmountPiastres).Should().Be(3000);
            jv.Where(j => j.DebitAccount == Accounts.Cash).Sum(j => j.AmountPiastres).Should().Be(2700);
            jv.Where(j => j.CreditAccount == Accounts.VatPayable).Sum(j => j.AmountPiastres).Should().Be(700);
        }

        [Fact]
        public void Return_ShouldReverseTheSaleEntry()
        {
            var (db, invRepo, item, shift) = Setup();
            var sale = Build(invRepo, InvoiceType.Sales, shift, item, PaymentMethod.Cash, 5000, 0, 0);
            invRepo.SaveInvoice(sale, out _).Should().BeTrue();
            var ret = Build(invRepo, InvoiceType.Return, shift, item, PaymentMethod.Cash, 5000, 0, 0, sale);

            invRepo.SaveInvoice(ret, out string err).Should().BeTrue();

            var jv = Entries(db, ret.InvoiceNumber);
            jv.Should().HaveCount(1);
            jv[0].DebitAccount.Should().Be(Accounts.SalesReturns);
            jv[0].CreditAccount.Should().Be(Accounts.Cash);
            jv[0].AmountPiastres.Should().Be(5000);
        }

        [Fact]
        public void RejectedSale_ShouldLeaveNoJournalEntries()
        {
            var (db, invRepo, item, shift) = Setup();
            var sale = Build(invRepo, InvoiceType.Sales, 0, item, PaymentMethod.Cash, 5000, 0, 0); // بدون وردية = مرفوض

            invRepo.SaveInvoice(sale, out _).Should().BeFalse();

            Entries(db, sale.InvoiceNumber).Should().BeEmpty();
        }

        [Fact]
        public void PurchaseInvoice_ShouldDebitInventory_AndCreditCashAndSuppliers()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var itemRepo = new ItemRepository(db);
            var purRepo = new PurchaseRepository(db);
            int suppId = new SupplierRepository(db).SaveSupplier(new Supplier { Name = "مورد", Phone = "0", TaxNumber = "300099887700003" });
            var item = itemRepo.GetByBarcodeOrCode("6281001000101")!;
            var pur = new PurchaseInvoice
            {
                InvoiceNumber = purRepo.GenerateNextPurchaseInvoiceNumber(), SupplierId = suppId, SupplierName = "مورد",
                InvoiceDate = DateTime.Today, SubtotalPiastres = 9000, VatPiastres = 1350, TotalPiastres = 10350,
                PaidPiastres = 5000, RemainingPiastres = 5350
            };
            pur.Items.Add(new PurchaseInvoiceItem
            {
                ItemId = item.Id, ItemCode = item.Code, ItemNameAr = item.NameAr, BatchNumber = "B-JV-1",
                ExpiryDate = DateTime.Today.AddYears(2), UnitLevel = UnitLevel.Box, QuantityPurchased = 10,
                SmallestUnitsCount = 240, UnitCostPiastres = 900, VatPiastres = 1350, TotalPiastres = 10350
            });

            purRepo.SavePurchaseInvoice(pur, out string err).Should().BeTrue();

            var jv = Entries(db, pur.InvoiceNumber);
            jv.Sum(j => j.AmountPiastres).Should().Be(10350);
            jv.Should().ContainSingle(j => j.DebitAccount == Accounts.Inventory && j.CreditAccount == Accounts.Cash && j.AmountPiastres == 5000);
            jv.Should().ContainSingle(j => j.DebitAccount == Accounts.Inventory && j.CreditAccount == Accounts.Suppliers && j.AmountPiastres == 5350);
        }

        [Fact]
        public void Expense_ShouldStillCreateItsEntry()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var fin = new FinanceRepository(db);
            string num = fin.GenerateNextExpenseNumber();
            fin.AddExpense(new Expense { ExpenseNumber = num, Category = "إيجار", AmountPiastres = 70000, Date = DateTime.Now, PaidTo = "مالك" });

            Entries(db, num).Should().ContainSingle(j => j.DebitAccount == "مصروفات: إيجار" && j.CreditAccount == Accounts.Cash && j.AmountPiastres == 70000);
        }

        [Fact]
        public void Allocate_ShouldRejectUnbalancedEntries()
        {
            Action act = () => JournalPosting.Allocate(
                new List<JournalPosting.Leg> { new JournalPosting.Leg("a", 100) },
                new List<JournalPosting.Leg> { new JournalPosting.Leg("b", 90) });
            act.Should().Throw<InvalidOperationException>();
        }
    }
}
