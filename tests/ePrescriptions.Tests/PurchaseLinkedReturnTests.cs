using System;
using System.IO;
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
    /// خطوة 39-أ: مرتجع المورد المرتبط بفاتورة الشراء الأصلية —
    /// الكمية لا تتجاوز المشترى ناقص ما سبق إرجاعه ولا المتبقي بالتشغيلة، والسعر من فاتورة الشراء مش مدخل يدوي.
    /// </summary>
    public class PurchaseLinkedReturnTests
    {
        private class Ctx
        {
            public DatabaseContext Db = new DatabaseContext(":memory:");
            public ItemRepository Items = null!;
            public PurchaseRepository Purchases = null!;
            public SupplierReturnRepository Returns = null!;
            public int ItemId;
            public int SupplierId;

            public Ctx()
            {
                Db.InitializeAndMigrate(seedData: true);
                Items = new ItemRepository(Db);
                Purchases = new PurchaseRepository(Db);
                Returns = new SupplierReturnRepository(Db);
                ItemId = Items.SaveItem(new Item { Code = "PR-1", Barcode = "6229999999991", NameAr = "دواء مرتجع", StripsPerBox = 1, PiecesPerStrip = 10, RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 3000, IsActive = true });
                SupplierId = new SupplierRepository(Db).SaveSupplier(new Supplier { Name = "شركة التوريد", Phone = "01000000000", IsActive = true });
            }

            public long Scalar(string sql) { using var c = Db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql; return Convert.ToInt64(m.ExecuteScalar() ?? 0); }
            public void Exec(string sql) { using var c = Db.CreateConnection(); using var m = c.CreateCommand(); m.CommandText = sql; m.ExecuteNonQuery(); }
            public long Balance => Scalar($"SELECT BalancePiastres FROM Suppliers WHERE Id={SupplierId}");
            public long Stock => Scalar($"SELECT TotalStockSmallestUnits FROM Items WHERE Id={ItemId}");

            /// <summary>شراء بالقطعة: units قطعة بتكلفة unitCost للقطعة + ضريبة vat إجمالية</summary>
            public PurchaseInvoice Buy(int units, long unitCost, long vat, string batch = "B1", int? supplierId = null, int? itemId = null)
            {
                long taxable = unitCost * units;
                var inv = new PurchaseInvoice
                {
                    SupplierId = supplierId ?? SupplierId, SupplierName = "شركة التوريد", InvoiceDate = DateTime.Today,
                    SubtotalPiastres = taxable, VatPiastres = vat, TotalPiastres = taxable + vat, PaidPiastres = 0, RemainingPiastres = taxable + vat,
                    Items =
                    {
                        new PurchaseInvoiceItem
                        {
                            ItemId = itemId ?? ItemId, ItemCode = "PR-1", ItemNameAr = "دواء مرتجع", BatchNumber = batch,
                            ExpiryDate = DateTime.Today.AddYears(1), UnitLevel = UnitLevel.Piece, QuantityPurchased = units,
                            SmallestUnitsCount = units, UnitCostPiastres = unitCost, VatPiastres = vat, TotalPiastres = taxable + vat
                        }
                    }
                };
                Purchases.SavePurchaseInvoice(inv, out string err).Should().BeTrue(err);
                return inv;
            }

            public SupplierReturn Return(PurchaseInvoice inv, int units, int pct = 0, string reason = "عيب تصنيع", int? purchaseItemId = null) => new SupplierReturn
            {
                PurchaseInvoiceId = inv.Id, Reason = reason, UserId = 1, UserName = "t",
                Items = { new SupplierReturnItem { PurchaseInvoiceItemId = purchaseItemId ?? inv.Items[0].Id, SmallestUnitsCount = units, DeductionPercent = pct } }
            };
        }

        [Fact]
        public void PurchaseSave_ShouldLinkItemToItsBatch_AndReportReturnableUnits()
        {
            var c = new Ctx();
            var inv = c.Buy(100, 50, 750);

            inv.Items[0].Id.Should().BeGreaterThan(0);
            c.Scalar($"SELECT BatchId FROM PurchaseInvoiceItems WHERE Id={inv.Items[0].Id}").Should().Be(inv.Items[0].BatchId);

            var rp = c.Returns.GetReturnablePurchaseByNumber(inv.InvoiceNumber.ToLowerInvariant());
            rp.Should().NotBeNull();
            rp!.Invoice.SupplierId.Should().Be(c.SupplierId);
            rp.Lines.Should().HaveCount(1);
            rp.Lines[0].HasBatch.Should().BeTrue();
            rp.Lines[0].ReturnableUnits.Should().Be(100);
            c.Returns.GetReturnablePurchaseByNumber("NO-SUCH-INVOICE").Should().BeNull();
        }

        [Fact]
        public void ReturnLargerThanPurchased_ShouldBeRejected_IncludingCumulativeEarlierReturns()
        {
            var c = new Ctx();
            var inv = c.Buy(100, 50, 750);

            var tooBig = c.Return(inv, 101);
            c.Returns.Save(tooBig, out string err1).Should().BeFalse();
            err1.Should().Contain("أكبر من المتاح");

            c.Returns.Save(c.Return(inv, 60), out _).Should().BeTrue();

            // 60 اترجعوا، فاللي فاضل 40 بس حتى لو لسه 100 في التشغيلة بعد أي إضافة
            var second = c.Return(inv, 50);
            c.Returns.Save(second, out string err2).Should().BeFalse();
            err2.Should().Contain("سبق إرجاع 60");

            c.Returns.Save(c.Return(inv, 40), out _).Should().BeTrue();
            c.Returns.GetReturnablePurchase(inv.Id)!.Lines[0].ReturnableUnits.Should().Be(0);
            c.Returns.Save(c.Return(inv, 1), out _).Should().BeFalse();
        }

        [Fact]
        public void ValidReturn_ShouldLinkToOriginalInvoice_PriceFromPurchaseLine_AndDeductStockAndSupplierBalance()
        {
            var c = new Ctx();
            var inv = c.Buy(100, 50, 750);   // إجمالي البند 5000 + 750 ضريبة = 5750 لـ 100 قطعة
            long balanceBefore = c.Balance;  // 5750 مستحق للمورد
            long stockBefore = c.Stock;

            var ret = c.Return(inv, 20, pct: 10);
            ret.Items[0].CostPerUnitPiastres = 999999; // محاولة تلاعب من الواجهة لازم تتجاهل
            ret.Items[0].CreditPiastres = 999999;
            c.Returns.Save(ret, out string err).Should().BeTrue(err);

            ret.PurchaseInvoiceId.Should().Be(inv.Id);
            ret.PurchaseInvoiceNumber.Should().Be(inv.InvoiceNumber);
            ret.SupplierId.Should().Be(c.SupplierId);
            ret.Items[0].CostTotalPiastres.Should().Be(1150);          // 5750 × 20 ÷ 100
            ret.Items[0].CreditPiastres.Should().Be(1035);             // بعد خصم 10%
            ret.TotalCostPiastres.Should().Be(1150);
            ret.CreditPiastres.Should().Be(1035);
            ret.LossPiastres.Should().Be(115);

            c.Stock.Should().Be(stockBefore - 20);
            c.Scalar($"SELECT RemainingSmallestUnits FROM ItemBatches WHERE Id={inv.Items[0].BatchId}").Should().Be(80);
            c.Balance.Should().Be(balanceBefore - 1035);

            c.Scalar($"SELECT PurchaseInvoiceId FROM SupplierReturns WHERE Id={ret.Id}").Should().Be(inv.Id);
            c.Scalar($"SELECT PurchaseInvoiceItemId FROM SupplierReturnItems WHERE ReturnId={ret.Id}").Should().Be(inv.Items[0].Id);
            c.Returns.GetRecent().First().PurchaseInvoiceNumber.Should().Be(inv.InvoiceNumber);
            c.Returns.GetReturnablePurchase(inv.Id)!.Lines[0].ReturnedUnits.Should().Be(20);
        }

        [Fact]
        public void MixedVatLines_ShouldReturnEachLineWithItsOwnTax_ExemptLineWithoutVat()
        {
            var c = new Ctx();
            int exemptItem = c.Items.SaveItem(new Item { Code = "EX-1", Barcode = "6228888888881", NameAr = "صنف معفى", StripsPerBox = 1, PiecesPerStrip = 10, RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 3000, IsVatExempt = true, IsActive = true });
            var exp = DateTime.Today.AddYears(1);
            // بند 1: معفى (100 × 50 = 5000، ضريبة 0) — بند 2: ضريبة 14% (100 × 50 = 5000 + 700)
            var inv = new PurchaseInvoice
            {
                SupplierId = c.SupplierId, SupplierName = "شركة التوريد", InvoiceDate = DateTime.Today,
                SubtotalPiastres = 10000, VatPiastres = 700, TotalPiastres = 10700, PaidPiastres = 0, RemainingPiastres = 10700,
                Items =
                {
                    new PurchaseInvoiceItem { ItemId = exemptItem, ItemCode = "EX-1", ItemNameAr = "صنف معفى", BatchNumber = "E1", ExpiryDate = exp, UnitLevel = UnitLevel.Piece, QuantityPurchased = 100, SmallestUnitsCount = 100, UnitCostPiastres = 50, VatPiastres = 0, TotalPiastres = 5000 },
                    new PurchaseInvoiceItem { ItemId = c.ItemId, ItemCode = "PR-1", ItemNameAr = "دواء مرتجع", BatchNumber = "T1", ExpiryDate = exp, UnitLevel = UnitLevel.Piece, QuantityPurchased = 100, SmallestUnitsCount = 100, UnitCostPiastres = 50, VatPiastres = 700, TotalPiastres = 5700 }
                }
            };
            c.Purchases.SavePurchaseInvoice(inv, out string err).Should().BeTrue(err);

            var ret = new SupplierReturn
            {
                PurchaseInvoiceId = inv.Id, Reason = "عيب", UserId = 1, UserName = "t",
                Items =
                {
                    new SupplierReturnItem { PurchaseInvoiceItemId = inv.Items[0].Id, SmallestUnitsCount = 10 },
                    new SupplierReturnItem { PurchaseInvoiceItemId = inv.Items[1].Id, SmallestUnitsCount = 10 }
                }
            };
            c.Returns.Save(ret, out err).Should().BeTrue(err);

            ret.Items[0].CreditPiastres.Should().Be(500);   // معفى: 10 × 50 بدون ضريبة
            ret.Items[1].CreditPiastres.Should().Be(570);   // 14%: 570 = 5700 × 10 ÷ 100
            ret.CreditPiastres.Should().Be(1070);
        }

        [Fact]
        public void PartialReturns_ShouldSumExactlyToTheInvoiceLineTotal_WithoutRoundingDrift()
        {
            var c = new Ctx();
            var inv = c.Buy(3, 333, 1);   // إجمالي البند 999 + 1 = 1000 لـ 3 قطع (1000 ÷ 3 مش بيتقسم)

            long total = 0;
            for (int i = 0; i < 3; i++)
            {
                var r = c.Return(inv, 1);
                c.Returns.Save(r, out string err).Should().BeTrue(err);
                total += r.Items[0].CostTotalPiastres;
            }
            total.Should().Be(1000);
        }

        [Fact]
        public void ReturnShouldBeCappedByWhatIsActuallyLeftInTheBatch()
        {
            var c = new Ctx();
            var inv = c.Buy(100, 50, 0);
            // اتباع 70 قطعة من التشغيلة (محاكاة البيع)
            c.Exec($"UPDATE ItemBatches SET RemainingSmallestUnits = 30 WHERE Id = {inv.Items[0].BatchId}; UPDATE Items SET TotalStockSmallestUnits = TotalStockSmallestUnits - 70 WHERE Id = {c.ItemId};");

            c.Returns.GetReturnablePurchase(inv.Id)!.Lines[0].ReturnableUnits.Should().Be(30);
            c.Returns.Save(c.Return(inv, 40), out string err).Should().BeFalse();
            err.Should().Contain("أكبر من المتاح بالتشغيلة");
            c.Returns.Save(c.Return(inv, 30), out _).Should().BeTrue();
        }

        [Fact]
        public void LinkedReturn_ShouldRejectForeignLines_WrongSupplier_Destroy_MissingReason_AndLegacyUnlinkedLines()
        {
            var c = new Ctx();
            var inv1 = c.Buy(10, 50, 0, "B1");
            var inv2 = c.Buy(10, 50, 0, "B2");
            long stock = c.Stock;

            // بند من فاتورة تانية
            c.Returns.Save(c.Return(inv1, 1, purchaseItemId: inv2.Items[0].Id), out string e1).Should().BeFalse();
            e1.Should().Contain("مش من فاتورة الشراء الأصلية");

            // مورد مختلف
            int other = new SupplierRepository(c.Db).SaveSupplier(new Supplier { Name = "مورد تاني", Phone = "0100", IsActive = true });
            var wrongSupplier = c.Return(inv1, 1); wrongSupplier.SupplierId = other;
            c.Returns.Save(wrongSupplier, out string e2).Should().BeFalse();
            e2.Should().Contain("غير مطابق لمورد");

            // إتلاف مرتبط بفاتورة مرفوض
            var destroy = c.Return(inv1, 1); destroy.Kind = SupplierReturnKind.Destroy;
            c.Returns.Save(destroy, out _).Should().BeFalse();

            // بدون سبب
            c.Returns.Save(c.Return(inv1, 1, reason: "  "), out string e3).Should().BeFalse();
            e3.Should().Contain("سبب");

            // بند بدون ربط بالفاتورة
            var noItem = c.Return(inv1, 1); noItem.Items[0].PurchaseInvoiceItemId = null;
            c.Returns.Save(noItem, out _).Should().BeFalse();

            // بند قديم ما اتربطش بتشغيلة (BatchId = NULL)
            c.Exec($"UPDATE PurchaseInvoiceItems SET BatchId = NULL WHERE Id = {inv1.Items[0].Id}");
            c.Returns.GetReturnablePurchase(inv1.Id)!.Lines[0].ReturnableUnits.Should().Be(0);
            c.Returns.Save(c.Return(inv1, 1), out string e4).Should().BeFalse();
            e4.Should().Contain("شاشة الشطب");

            // ولا واحدة منهم غيّرت حاجة
            c.Stock.Should().Be(stock);
            c.Scalar("SELECT COUNT(*) FROM SupplierReturns").Should().Be(0);
        }

        [Fact]
        public void SameBatchTwiceInOneDocument_ShouldNotExceedBatchRemaining_EvenWithoutPurchaseLink()
        {
            var c = new Ctx();
            var inv = c.Buy(10, 50, 0);
            long batchId = inv.Items[0].BatchId;

            // شطب عادي (غير مرتبط): بندين على نفس التشغيلة مجموعهم 12 > 10 لازم يترفض
            var ret = new SupplierReturn
            {
                SupplierId = c.SupplierId, Reason = "تلف", UserId = 1, UserName = "t",
                Items =
                {
                    new SupplierReturnItem { BatchId = (int)batchId, SmallestUnitsCount = 6 },
                    new SupplierReturnItem { BatchId = (int)batchId, SmallestUnitsCount = 6 }
                }
            };
            c.Returns.Save(ret, out _).Should().BeFalse();
            c.Scalar($"SELECT RemainingSmallestUnits FROM ItemBatches WHERE Id={batchId}").Should().Be(10);
        }

        [Fact]
        public void UnlinkedWriteOff_ShouldStillWorkAsBefore_WithNullLinkColumns()
        {
            var c = new Ctx();
            var inv = c.Buy(10, 50, 0);
            var ret = new SupplierReturn
            {
                SupplierId = c.SupplierId, Reason = "انتهاء", UserId = 1, UserName = "t",
                Items = { new SupplierReturnItem { BatchId = inv.Items[0].BatchId, SmallestUnitsCount = 4, DeductionPercent = 0 } }
            };
            c.Returns.Save(ret, out string err).Should().BeTrue(err);
            c.Scalar($"SELECT COUNT(*) FROM SupplierReturns WHERE Id={ret.Id} AND PurchaseInvoiceId IS NULL").Should().Be(1);
            c.Scalar($"SELECT COUNT(*) FROM SupplierReturnItems WHERE ReturnId={ret.Id} AND PurchaseInvoiceItemId IS NULL").Should().Be(1);
            // الشطب غير المرتبط بيقلّل اللي يتبقى للإرجاع المرتبط (عن طريق المتبقي بالتشغيلة)
            c.Returns.GetReturnablePurchase(inv.Id)!.Lines[0].ReturnableUnits.Should().Be(6);
        }

        [Fact]
        public void SearchPurchases_ShouldFindByNumberOrSupplier_AndTreatWildcardsLiterally()
        {
            var c = new Ctx();
            var inv = c.Buy(5, 50, 0);

            c.Returns.SearchPurchases(inv.InvoiceNumber).Select(p => p.Id).Should().Contain(inv.Id);
            c.Returns.SearchPurchases("التوريد").Select(p => p.Id).Should().Contain(inv.Id);
            c.Returns.SearchPurchases("%").Should().BeEmpty();
            c.Returns.SearchPurchases("_").Should().BeEmpty();
        }

        [Fact]
        public void Migration020Backfill_ShouldLinkOnlyUnambiguousLegacyLines()
        {
            var c = new Ctx();
            var a = c.Buy(10, 50, 0, "LEG-A");
            var d1 = c.Buy(10, 50, 0, "DUP");
            var d2 = c.Buy(10, 50, 0, "DUP");
            // محاكاة بنود قديمة: ما اتخزّنش لها BatchId
            c.Exec("UPDATE PurchaseInvoiceItems SET BatchId = NULL");

            // نفس عبارة التعبئة الموجودة فعلاً في ملف الـ Migration
            string script = File.ReadAllText(FindMigration()).Replace("\r\n", "\n");
            int start = script.IndexOf("UPDATE PurchaseInvoiceItems", StringComparison.Ordinal);
            int end = script.IndexOf(";\n\nALTER TABLE SupplierReturns", StringComparison.Ordinal);
            start.Should().BeGreaterThan(0);
            end.Should().BeGreaterThan(start);
            c.Exec(script.Substring(start, end - start + 1));

            c.Scalar($"SELECT BatchId FROM PurchaseInvoiceItems WHERE Id={a.Items[0].Id}").Should().Be(a.Items[0].BatchId);
            // تشغيلتين بنفس الرقم والصنف وتاريخ الصلاحية = غامض فبيتساب بدون ربط بدل التخمين
            c.Scalar($"SELECT COUNT(*) FROM PurchaseInvoiceItems WHERE Id IN ({d1.Items[0].Id},{d2.Items[0].Id}) AND BatchId IS NULL").Should().Be(2);
        }

        private static string FindMigration()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "src", "ePrescriptions.Data", "Migrations", "020_PurchaseLinkedSupplierReturns.sql");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("تعذر العثور على 020_PurchaseLinkedSupplierReturns.sql");
        }
    }
}
