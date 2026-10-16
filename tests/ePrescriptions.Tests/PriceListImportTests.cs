using System;
using System.Collections.Generic;
using System.Linq;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// خطوة 40 (جزء أ): تحديث الأسعار من ملف Excel لقوائم أسعار الموردين — القراءة والمطابقة وخطة التحديث
    /// والاعتماد على القاعدة (من غير ClosedXML: الجدول النصي هو اللي القارئ بيسلّمه).
    /// </summary>
    public class PriceListImportTests
    {
        private static DatabaseContext CreateDb()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            return db;
        }

        private static Item MakeItem(ItemRepository repo, string code, string barcode, string nameAr,
            long retail = 10000, long wholesale = 0, long cost = 8000, bool active = true, string nameEn = "")
        {
            var item = new Item
            {
                Code = code,
                Barcode = barcode,
                NameAr = nameAr,
                NameEn = nameEn,
                RetailPriceBoxPiastres = retail,
                WholesalePriceBoxPiastres = wholesale,
                CostPriceBoxPiastres = cost,
                IsActive = active
            };
            item.Id = repo.SaveItem(item);
            return item;
        }

        private static List<string[]> Grid(params string[][] rows) => rows.ToList();

        // ───────────── قراءة الأسعار ─────────────

        [Theory]
        [InlineData("12.50", 1250L)]
        [InlineData("12", 1200L)]
        [InlineData("١٢٫٥٠", 1250L)]
        [InlineData("1,250.00", 125000L)]
        [InlineData("12,5", 1250L)]
        [InlineData("1.234,50", 123450L)]
        [InlineData("1,250", 125000L)]
        [InlineData("45 ج.م", 4500L)]
        [InlineData("EGP 99.99", 9999L)]
        [InlineData("10.005", 1001L)]
        public void TryParsePiastres_ShouldAcceptCommonSupplierFormats(string text, long expected)
        {
            PriceListParser.TryParsePiastres(text, out long piastres).Should().BeTrue();
            piastres.Should().Be(expected);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("0")]
        [InlineData("0.00")]
        [InlineData("-5")]
        [InlineData("12-15")]
        [InlineData("1.234.567")]
        [InlineData("12 علبة")]
        public void TryParsePiastres_ShouldRejectNonPricesZeroAndNegative(string text)
        {
            PriceListParser.TryParsePiastres(text, out long piastres).Should().BeFalse();
            piastres.Should().Be(0L);
        }

        [Fact]
        public void CleanIdentifier_ShouldStripExcelNumericArtifacts()
        {
            PriceListParser.CleanIdentifier("6221030012345.0").Should().Be("6221030012345");
            PriceListParser.CleanIdentifier("6.221030012345E12").Should().Be("6221030012345");
            PriceListParser.CleanIdentifier(" ٦٢٢١٠٣٠ ٠١٢٣٤٥ ").Should().Be("6221030012345");
            PriceListParser.CleanIdentifier("AB-100").Should().Be("ab-100");
            PriceListParser.CleanIdentifier(null).Should().Be(string.Empty);
        }

        // ───────────── اكتشاف الأعمدة ─────────────

        [Fact]
        public void DetectMapping_ShouldFindArabicHeaders_EvenBelowTitleRows()
        {
            var grid = Grid(
                new[] { "شركة الأمل للأدوية — قائمة أسعار أكتوبر", "", "", "" },
                new[] { "", "", "", "" },
                new[] { "الباركود", "اسم الصنف", "سعر الجمهور", "سعر الصيدلي" },
                new[] { "6221", "بنادول", "50", "40" });

            var mapping = PriceListParser.DetectMapping(grid);

            mapping.Should().NotBeNull();
            mapping!.HeaderRowIndex.Should().Be(2);
            mapping.Columns[PriceListColumn.Barcode].Should().Be(0);
            mapping.Columns[PriceListColumn.Name].Should().Be(1);
            mapping.Columns[PriceListColumn.Retail].Should().Be(2);
            mapping.Columns[PriceListColumn.Cost].Should().Be(3);
        }

        [Fact]
        public void DetectMapping_ShouldFindEnglishHeaders_AndDistinguishBarcodeFromCode()
        {
            var grid = Grid(new[] { "Item Code", "Barcode", "Item Name", "Public Price", "Wholesale Price" });

            var mapping = PriceListParser.DetectMapping(grid);

            mapping.Should().NotBeNull();
            mapping!.Columns[PriceListColumn.Code].Should().Be(0);
            mapping.Columns[PriceListColumn.Barcode].Should().Be(1);
            mapping.Columns[PriceListColumn.Name].Should().Be(2);
            mapping.Columns[PriceListColumn.Retail].Should().Be(3);
            mapping.Columns[PriceListColumn.Wholesale].Should().Be(4);
        }

        [Fact]
        public void DetectMapping_ShouldReturnNull_WhenNoIdentifierOrNoPriceColumn()
        {
            PriceListParser.DetectMapping(Grid(new[] { "الصنف", "الكمية" })).Should().BeNull();
            PriceListParser.DetectMapping(Grid(new[] { "سعر الجمهور", "سعر الجملة" })).Should().BeNull();
            PriceListParser.DetectMapping(new List<string[]>()).Should().BeNull();
        }

        [Fact]
        public void Parse_ShouldKeepSheetRowNumbers_SkipBlankRows_AndFlagInvalidPrices()
        {
            var grid = Grid(
                new[] { "باركود", "الصنف", "سعر الجمهور" },
                new[] { "111", "صنف أ", "10.5" },
                new[] { "", "", "" },
                new[] { "222", "صنف ب", "غير متاح" },
                new[] { "333", "صنف ج", "" });

            var mapping = PriceListParser.DetectMapping(grid)!;
            var rows = PriceListParser.Parse(grid, mapping);

            rows.Should().HaveCount(3);
            rows[0].RowNumber.Should().Be(2);
            rows[0].RetailPiastres.Should().Be(1050L);
            rows[1].RowNumber.Should().Be(4);
            rows[1].InvalidPrices.Should().HaveCount(1);
            rows[1].RetailPiastres.Should().BeNull();
            rows[2].RowNumber.Should().Be(5);
            rows[2].HasAnyPrice.Should().BeFalse();
        }

        // ───────────── مطابقة وخطة التحديث ─────────────

        [Fact]
        public void Planner_ShouldMatchByBarcode_ThenCode_ThenNormalizedName()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            var a = MakeItem(repo, "A1", "6221000000011", "بنادول اكسترا");
            var b = MakeItem(repo, "B1", "6221000000028", "أوجمنتين 1 جم");
            var c = MakeItem(repo, "C1", "6221000000035", "كونجستال");

            var rows = new List<PriceListRow>
            {
                new PriceListRow { RowNumber = 2, Barcode = "6221000000011", Name = "اسم مختلف تماماً", RetailPiastres = 11000 },
                new PriceListRow { RowNumber = 3, Code = "b1", RetailPiastres = 12000 },
                new PriceListRow { RowNumber = 4, Name = "كونجستال", RetailPiastres = 13000 }
            };
            var plan = PriceListPlanner.Build(rows, repo.GetAllItems());

            plan.Lines[0].Item!.Id.Should().Be(a.Id);
            plan.Lines[0].MatchedBy.Should().Be("باركود");
            plan.Lines[1].Item!.Id.Should().Be(b.Id);
            plan.Lines[1].MatchedBy.Should().Be("كود");
            plan.Lines[2].Item!.Id.Should().Be(c.Id);
            plan.Lines[2].MatchedBy.Should().Be("اسم");
            plan.UpdateCount.Should().Be(3);
        }

        [Fact]
        public void Planner_NameMatch_ShouldIgnoreHamzaDiacriticsAndTaMarbuta_ButNotMatchPartialNames()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            MakeItem(repo, "A1", "1", "أسبرين بروتكت");
            MakeItem(repo, "A2", "2", "بنادول");

            var rows = new List<PriceListRow>
            {
                new PriceListRow { RowNumber = 2, Name = "اسبرين  بروتكت", RetailPiastres = 5000 },
                new PriceListRow { RowNumber = 3, Name = "بنادول اكسترا", RetailPiastres = 6000 }
            };
            var plan = PriceListPlanner.Build(rows, repo.GetAllItems());

            plan.Lines[0].Status.Should().Be(PriceLineStatus.WillUpdate);
            plan.Lines[1].Status.Should().Be(PriceLineStatus.NotFound);
        }

        [Fact]
        public void Planner_ShouldFlagNotFound_Ambiguous_Unchanged_NoPrice_AndInactiveItems()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            MakeItem(repo, "A1", "111", "صنف واحد", retail: 5000);
            MakeItem(repo, "D1", "222", "صنف مكرر");
            MakeItem(repo, "D2", "333", "صنف مكرر");
            MakeItem(repo, "X1", "444", "صنف معطّل", active: false);

            var rows = new List<PriceListRow>
            {
                new PriceListRow { RowNumber = 2, Barcode = "999", RetailPiastres = 100 },                 // مش موجود
                new PriceListRow { RowNumber = 3, Name = "صنف مكرر", RetailPiastres = 100 },               // أكتر من صنف
                new PriceListRow { RowNumber = 4, Barcode = "111", RetailPiastres = 5000 },                // نفس السعر
                new PriceListRow { RowNumber = 5, Barcode = "111" },                                       // مفيش سعر
                new PriceListRow { RowNumber = 6, Barcode = "444", RetailPiastres = 100 }                  // معطّل
            };
            var plan = PriceListPlanner.Build(rows, repo.GetAllItems());

            plan.Lines[0].Status.Should().Be(PriceLineStatus.NotFound);
            plan.Lines[1].Status.Should().Be(PriceLineStatus.Ambiguous);
            plan.Lines[2].Status.Should().Be(PriceLineStatus.Unchanged);
            plan.Lines[3].Status.Should().Be(PriceLineStatus.NoPrice);
            plan.Lines[4].Status.Should().Be(PriceLineStatus.NotFound);
            plan.UpdateCount.Should().Be(0);
            plan.ChangeCount.Should().Be(0);
        }

        [Fact]
        public void Planner_ShouldFlagInvalidPriceRows_AndNeverProposeTheirOtherPrices()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            MakeItem(repo, "A1", "111", "صنف");

            var row = new PriceListRow { RowNumber = 2, Barcode = "111", RetailPiastres = 7000 };
            row.InvalidPrices[PriceField.Cost] = "؟؟";
            var plan = PriceListPlanner.Build(new[] { row }, repo.GetAllItems());

            plan.Lines[0].Status.Should().Be(PriceLineStatus.InvalidPrice);
            plan.Lines[0].Changes.Should().BeEmpty();
        }

        [Fact]
        public void Planner_SameItemTwiceInFile_IdenticalPrices_ShouldBeIgnored_ButDifferentPrices_ShouldBlockBoth()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            MakeItem(repo, "A1", "111", "صنف أ");
            MakeItem(repo, "B1", "222", "صنف ب");

            var rows = new List<PriceListRow>
            {
                new PriceListRow { RowNumber = 2, Barcode = "111", RetailPiastres = 11000 },
                new PriceListRow { RowNumber = 3, Barcode = "111", RetailPiastres = 11000 },   // تكرار مطابق
                new PriceListRow { RowNumber = 4, Barcode = "222", RetailPiastres = 12000 },
                new PriceListRow { RowNumber = 5, Barcode = "222", RetailPiastres = 12500 }    // تعارض
            };
            var plan = PriceListPlanner.Build(rows, repo.GetAllItems());

            plan.Lines[0].Status.Should().Be(PriceLineStatus.WillUpdate);
            plan.Lines[1].Status.Should().Be(PriceLineStatus.Unchanged);
            plan.Lines[2].Status.Should().Be(PriceLineStatus.Conflict);
            plan.Lines[3].Status.Should().Be(PriceLineStatus.Conflict);
            plan.Lines[2].Changes.Should().BeEmpty();
            plan.Lines[3].Changes.Should().BeEmpty();
            plan.ChangeCount.Should().Be(1);
        }

        [Fact]
        public void Planner_ShouldComputeOldNewAndPercent_AndFlagLargeChangesAndRetailBelowCost()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            MakeItem(repo, "A1", "111", "صنف عادي", retail: 10000, cost: 8000);
            MakeItem(repo, "B1", "222", "صنف قفزة", retail: 10000, cost: 8000);
            MakeItem(repo, "C1", "333", "صنف خسارة", retail: 10000, cost: 8000);

            var rows = new List<PriceListRow>
            {
                new PriceListRow { RowNumber = 2, Barcode = "111", RetailPiastres = 10500 },                       // +5%
                new PriceListRow { RowNumber = 3, Barcode = "222", RetailPiastres = 13000 },                       // +30%
                new PriceListRow { RowNumber = 4, Barcode = "333", RetailPiastres = 7000, CostPiastres = 7500 }    // أقل من التكلفة
            };
            var plan = PriceListPlanner.Build(rows, repo.GetAllItems());

            var normal = plan.Lines[0].Changes.Single();
            normal.OldPiastres.Should().Be(10000L);
            normal.NewPiastres.Should().Be(10500L);
            normal.ChangePercent.Should().Be(5m);
            normal.IsLargeChange.Should().BeFalse();
            plan.Lines[0].RequiresReview.Should().BeFalse();

            plan.Lines[1].Changes.Single().IsLargeChange.Should().BeTrue();
            plan.Lines[1].RequiresReview.Should().BeTrue();

            plan.Lines[2].RetailBelowCost.Should().BeTrue();
            plan.Lines[2].RequiresReview.Should().BeTrue();
            plan.ReviewCount.Should().Be(2);
        }

        [Fact]
        public void Planner_ShouldRespectFieldOptions_AndNotTreatUnsetWholesaleAsLargeChange()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            MakeItem(repo, "A1", "111", "صنف", retail: 10000, wholesale: 0, cost: 8000);

            var rows = new List<PriceListRow>
            {
                new PriceListRow { RowNumber = 2, Barcode = "111", RetailPiastres = 11000, WholesalePiastres = 9500, CostPiastres = 8200 }
            };

            var all = PriceListPlanner.Build(rows, repo.GetAllItems());
            all.Lines[0].Changes.Should().HaveCount(3);
            all.Lines[0].Changes.Single(c => c.Field == PriceField.Wholesale).IsLargeChange.Should().BeFalse("السعر القديم صفر = غير مضبوط");

            var onlyCost = PriceListPlanner.Build(rows, repo.GetAllItems(),
                new PriceListOptions { UpdateRetail = false, UpdateWholesale = false, UpdateCost = true });
            onlyCost.Lines[0].Changes.Should().HaveCount(1);
            onlyCost.Lines[0].Changes[0].Field.Should().Be(PriceField.Cost);
        }

        [Fact]
        public void Planner_ShouldNotMutateTheItemsItWasGiven()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            MakeItem(repo, "A1", "111", "صنف", retail: 10000);

            var items = repo.GetAllItems();
            PriceListPlanner.Build(new[] { new PriceListRow { RowNumber = 2, Barcode = "111", RetailPiastres = 99999 } }, items);

            items[0].RetailPriceBoxPiastres.Should().Be(10000L);
            repo.GetById(items[0].Id)!.RetailPriceBoxPiastres.Should().Be(10000L);
        }

        // ───────────── الاعتماد على القاعدة ─────────────

        [Fact]
        public void ApplyPriceChanges_ShouldUpdateOnlyTheChangedColumns_AndReturnWhatWasApplied()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            var item = MakeItem(repo, "A1", "111", "صنف", retail: 10000, wholesale: 9000, cost: 8000);

            var plan = PriceListPlanner.Build(new[]
            {
                new PriceListRow { RowNumber = 2, Barcode = "111", RetailPiastres = 10500, CostPiastres = 8100 }
            }, repo.GetAllItems());

            var result = repo.ApplyPriceChanges(plan.UpdatableLines.SelectMany(l => l.Changes));

            result.Applied.Should().HaveCount(2);
            result.Skipped.Should().BeEmpty();
            var after = repo.GetById(item.Id)!;
            after.RetailPriceBoxPiastres.Should().Be(10500L);
            after.CostPriceBoxPiastres.Should().Be(8100L);
            after.WholesalePriceBoxPiastres.Should().Be(9000L, "السعر اللي مش في الملف ما بيتغيّرش");
            after.NameAr.Should().Be("صنف");
        }

        [Fact]
        public void ApplyPriceChanges_ShouldSkipChange_WhenPriceChangedSincePreview()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            var item = MakeItem(repo, "A1", "111", "صنف", retail: 10000);

            var plan = PriceListPlanner.Build(new[]
            {
                new PriceListRow { RowNumber = 2, Barcode = "111", RetailPiastres = 11000 }
            }, repo.GetAllItems());

            // حد عدّل السعر يدوياً بعد المعاينة
            var live = repo.GetById(item.Id)!;
            live.RetailPriceBoxPiastres = 10200;
            repo.SaveItem(live);

            var result = repo.ApplyPriceChanges(plan.UpdatableLines.SelectMany(l => l.Changes));

            result.Applied.Should().BeEmpty();
            result.Skipped.Should().HaveCount(1);
            result.Skipped[0].Reason.Should().Contain("اتغيّر");
            repo.GetById(item.Id)!.RetailPriceBoxPiastres.Should().Be(10200L, "تعديل المستخدم ما يتداسش عليه");
        }

        [Fact]
        public void ApplyPriceChanges_ShouldSkipInactiveItemsAndNonPositivePrices()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            var active = MakeItem(repo, "A1", "111", "صنف نشط", retail: 10000);
            var inactive = MakeItem(repo, "X1", "222", "صنف معطّل", retail: 10000, active: false);

            var changes = new[]
            {
                new ItemPriceChange { ItemId = inactive.Id, Field = PriceField.Retail, OldPiastres = 10000, NewPiastres = 12000 },
                new ItemPriceChange { ItemId = active.Id, Field = PriceField.Retail, OldPiastres = 10000, NewPiastres = 0 },
                new ItemPriceChange { ItemId = 9999, Field = PriceField.Retail, OldPiastres = 1, NewPiastres = 2 }
            };
            var result = repo.ApplyPriceChanges(changes);

            result.Applied.Should().BeEmpty();
            result.Skipped.Should().HaveCount(3);
            repo.GetById(active.Id)!.RetailPriceBoxPiastres.Should().Be(10000L);
        }

        [Fact]
        public void ApplyPriceChanges_ShouldNotTouchStockBatchesOrOtherItems()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            var a = MakeItem(repo, "A1", "111", "صنف أ", retail: 10000);
            var b = MakeItem(repo, "B1", "222", "صنف ب", retail: 20000);
            repo.AddBatch(a.Id, "B-1", new DateTime(2030, 1, 1), 30, 8000);

            var plan = PriceListPlanner.Build(new[]
            {
                new PriceListRow { RowNumber = 2, Barcode = "111", RetailPiastres = 10500 }
            }, repo.GetAllItems());
            repo.ApplyPriceChanges(plan.UpdatableLines.SelectMany(l => l.Changes));

            repo.GetById(b.Id)!.RetailPriceBoxPiastres.Should().Be(20000L);
            var afterA = repo.GetById(a.Id)!;
            afterA.TotalStockSmallestUnits.Should().Be(30);
            repo.GetActiveBatches(a.Id).Should().HaveCount(1);
        }

        [Fact]
        public void EndToEnd_FromSheetGridToUpdatedPrices()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            var a = MakeItem(repo, "A1", "6221000000011", "بنادول اكسترا", retail: 5000, cost: 4000);
            var b = MakeItem(repo, "B1", "6221000000028", "أوجمنتين", retail: 9000, cost: 7000);

            var grid = Grid(
                new[] { "قائمة أسعار شركة الأمل", "", "", "" },
                new[] { "الباركود", "اسم الصنف", "سعر الجمهور", "سعر الصيدلي" },
                new[] { "6221000000011.0", "بنادول اكسترا", "52.50", "41" },
                new[] { "6221000000028", "أوجمنتين", "90", "70" },       // نفس الأسعار
                new[] { "9999999999999", "صنف مش عندنا", "10", "8" },
                new[] { "", "", "", "" });

            var mapping = PriceListParser.DetectMapping(grid)!;
            var rows = PriceListParser.Parse(grid, mapping);
            var plan = PriceListPlanner.Build(rows, repo.GetAllItems());

            plan.TotalRows.Should().Be(3);
            plan.UpdateCount.Should().Be(1);
            plan.UnchangedCount.Should().Be(1);
            plan.NotFoundCount.Should().Be(1);

            repo.ApplyPriceChanges(plan.UpdatableLines.SelectMany(l => l.Changes));

            repo.GetById(a.Id)!.RetailPriceBoxPiastres.Should().Be(5250L);
            repo.GetById(a.Id)!.CostPriceBoxPiastres.Should().Be(4100L);
            repo.GetById(b.Id)!.RetailPriceBoxPiastres.Should().Be(9000L);
        }
    }
}
