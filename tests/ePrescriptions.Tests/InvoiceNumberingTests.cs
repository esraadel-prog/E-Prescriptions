using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 17: ترقيم الفواتير بعدّاد داخل الـ Transaction (بدون تكرار)</summary>
    public class InvoiceNumberingTests
    {
        private static Invoice Build(Item item, int shiftId, InvoiceType type = InvoiceType.Sales, string preview = "", Invoice? original = null) => new Invoice
        {
            InvoiceNumber = preview, Type = type, CustomerId = 1, CustomerName = "عميل", PaymentMethod = PaymentMethod.Cash,
            UserId = 1, UserName = "اختبار", ShiftId = shiftId, SubtotalPiastres = 1000, TotalPiastres = 1000, PaidPiastres = 1000,
            QrCodeText = "فاتورة: " + preview + " | x",
            OriginalInvoiceId = original?.Id,
            Items =
            {
                new InvoiceItem
                {
                    ItemId = item.Id, ItemCode = item.Code, ItemNameAr = item.NameAr, BatchNumber = "عام",
                    UnitLevel = UnitLevel.Piece, QuantitySold = 1, SmallestUnitsCount = 1, UnitPricePiastres = 1000, TotalPiastres = 1000,
                    OriginalInvoiceItemId = original?.Items.FirstOrDefault()?.Id
                }
            }
        };

        private static (DatabaseContext db, InvoiceRepository inv, Item item, int shift) Setup(string? file = null)
        {
            var db = new DatabaseContext(file ?? ":memory:");
            db.InitializeAndMigrate(seedData: true);
            var items = new ItemRepository(db);
            return (db, new InvoiceRepository(db, items), items.GetByBarcodeOrCode("6281001000101")!, new ShiftRepository(db).OpenShift(1, "اختبار", 0));
        }

        [Fact]
        public void TwoInvoicesPreparedWithTheSamePreviewNumber_ShouldGetDistinctSequentialNumbers()
        {
            var (db, inv, item, shift) = Setup();
            string preview = inv.GenerateNextInvoiceNumber(InvoiceType.Sales);
            var a = Build(item, shift, preview: preview);
            var b = Build(item, shift, preview: preview); // نفس رقم العرض (جهازين بيجهّزوا فاتورة في نفس اللحظة)

            inv.SaveInvoice(a, out string e1).Should().BeTrue(e1);
            inv.SaveInvoice(b, out string e2).Should().BeTrue(e2);

            a.InvoiceNumber.Should().Be(preview);
            b.InvoiceNumber.Should().NotBe(a.InvoiceNumber);
            b.InvoiceNumber.EndsWith("00002").Should().BeTrue();
            a.QrCodeText!.Contains(a.InvoiceNumber).Should().BeTrue();
            b.QrCodeText!.Contains(b.InvoiceNumber).Should().BeTrue(); // نص الـ QR اتصحّح للرقم النهائي
        }

        [Fact]
        public void PreviewNumber_ShouldEqualTheNumberActuallySaved_AndSalesAndReturnsHaveSeparateSequences()
        {
            var (db, inv, item, shift) = Setup();
            string p1 = inv.GenerateNextInvoiceNumber(InvoiceType.Sales);
            var sale = Build(item, shift, preview: p1);
            inv.SaveInvoice(sale, out _).Should().BeTrue();
            sale.InvoiceNumber.Should().Be(p1);

            inv.GenerateNextInvoiceNumber(InvoiceType.Sales).EndsWith("00002").Should().BeTrue();

            string rp = inv.GenerateNextInvoiceNumber(InvoiceType.Return);
            rp.StartsWith("RET-").Should().BeTrue();
            rp.EndsWith("00001").Should().BeTrue();
            var ret = Build(item, shift, InvoiceType.Return, rp, sale);
            inv.SaveInvoice(ret, out string err).Should().BeTrue(err);
            ret.InvoiceNumber.Should().Be(rp);
        }

        [Fact]
        public void RejectedInvoice_ShouldNotConsumeANumber()
        {
            var (db, inv, item, shift) = Setup();
            var bad = Build(item, 0, preview: "x"); // بدون وردية = مرفوض
            inv.SaveInvoice(bad, out _).Should().BeFalse();

            var ok = Build(item, shift);
            inv.SaveInvoice(ok, out _).Should().BeTrue();
            ok.InvoiceNumber.EndsWith("00001").Should().BeTrue();
        }

        [Fact]
        public void LegacyDatabaseWithoutCounter_ShouldContinueAfterTheHighestExistingNumber()
        {
            var (db, inv, item, shift) = Setup();
            for (int i = 0; i < 3; i++) inv.SaveInvoice(Build(item, shift), out _).Should().BeTrue();
            using (var conn = db.CreateConnection())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Counters;"; // حالة قاعدة اتحدّثت من الترقيم القديم
                cmd.ExecuteNonQuery();
            }

            inv.GenerateNextInvoiceNumber(InvoiceType.Sales).EndsWith("00004").Should().BeTrue();
            var next = Build(item, shift);
            inv.SaveInvoice(next, out _).Should().BeTrue();
            next.InvoiceNumber.EndsWith("00004").Should().BeTrue();
        }

        [Fact]
        public void ConcurrentSaves_FromTwoThreadsAndTwoConnections_ShouldNeverProduceDuplicateNumbers()
        {
            string file = Path.Combine(Path.GetTempPath(), "ep_num_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var (db, inv, item, shift) = Setup(file);
                var numbers = new List<string>();
                var errors = new List<string>();
                var threads = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
                {
                    var db2 = new DatabaseContext(file);
                    var inv2 = new InvoiceRepository(db2, new ItemRepository(db2));
                    for (int i = 0; i < 6; i++)
                    {
                        var x = Build(item, shift, preview: "INV-PREVIEW");
                        bool ok = inv2.SaveInvoice(x, out string err);
                        lock (numbers) { if (ok) numbers.Add(x.InvoiceNumber); else errors.Add(err); }
                    }
                })).ToList();
                threads.ForEach(t => t.Start());
                threads.ForEach(t => t.Join());

                errors.Should().BeEmpty();
                numbers.Should().HaveCount(12);
                numbers.Distinct().Count().Should().Be(12);
            }
            finally
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                foreach (var f in new[] { file, file + "-wal", file + "-shm" }) try { File.Delete(f); } catch { }
            }
        }

        [Fact]
        public void PurchaseInvoices_ShouldAlsoGetDistinctNumbersEvenWithSamePreview()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var items = new ItemRepository(db);
            var repo = new PurchaseRepository(db);
            int supp = new SupplierRepository(db).SaveSupplier(new Supplier { Name = "مورد", Phone = "0", TaxNumber = "300099887700003" });
            var item = items.GetByBarcodeOrCode("6281001000101")!;
            string preview = repo.GenerateNextPurchaseInvoiceNumber();

            PurchaseInvoice Make() 
            {
                var p = new PurchaseInvoice { InvoiceNumber = preview, SupplierId = supp, SupplierName = "مورد", InvoiceDate = DateTime.Today, SubtotalPiastres = 1000, TotalPiastres = 1000, PaidPiastres = 1000 };
                p.Items.Add(new PurchaseInvoiceItem { ItemId = item.Id, ItemCode = item.Code, ItemNameAr = item.NameAr, BatchNumber = "B" + Guid.NewGuid().ToString("N").Substring(0, 5),
                    ExpiryDate = DateTime.Today.AddYears(2), UnitLevel = UnitLevel.Box, QuantityPurchased = 1, SmallestUnitsCount = 24, UnitCostPiastres = 1000, TotalPiastres = 1000 });
                return p;
            }
            var a = Make(); var b = Make();

            repo.SavePurchaseInvoice(a, out string e1).Should().BeTrue(e1);
            repo.SavePurchaseInvoice(b, out string e2).Should().BeTrue(e2);

            a.InvoiceNumber.Should().Be(preview);
            b.InvoiceNumber.Should().NotBe(a.InvoiceNumber);
        }
    }
}
