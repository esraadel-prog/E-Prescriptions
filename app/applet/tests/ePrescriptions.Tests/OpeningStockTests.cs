using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class OpeningStockTests
    {
        private static string Src(string relative)
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                var candidate = Path.Combine(dir, "src", "ePrescriptions.App", relative);
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            throw new FileNotFoundException(relative);
        }

        [Fact]
        public void OpeningStockLine_RecalculateLine_ShouldCalculateTotalUnitsAndTotalsCorrectly()
        {
            // علبة فيها شريطين، كل شريط 10 أقراص = 20 قرص بالعلبة
            var line = new OpeningStockVoucherLine
            {
                ItemNameAr = "بانادول إكسترا",
                StripsPerBox = 2,
                PiecesPerStrip = 10,
                CostPriceBoxPounds = 40m,    // 40 ج.م للعلبة
                RetailPriceBoxPounds = 60m,  // 60 ج.م للعلبة
                QuantityBoxes = 5,           // 5 علب = 100 قرص
                QuantityStrips = 1,          // 1 شريط = 10 أقراص
                QuantityUnits = 5            // 5 أقراص
            };

            line.RecalculateLine();

            line.SmallestUnitsPerBox.Should().Be(20);
            line.TotalSmallestUnits.Should().Be(115); // 100 + 10 + 5
            line.TotalCostPounds.Should().Be(230m);   // (115 * 40) / 20 = 230 ج.م
            line.TotalRetailPounds.Should().Be(345m); // (115 * 60) / 20 = 345 ج.م
            line.FormattedQuantity.Should().Be("5 علبة + 1 شريط + 5 قرص");
        }

        [Fact]
        public void OpeningStockVoucher_RecalculateTotals_ShouldSumAllLinesAndMargin()
        {
            var voucher = new OpeningStockVoucher();
            voucher.Lines.Add(new OpeningStockVoucherLine
            {
                ItemNameAr = "صنف 1",
                StripsPerBox = 1,
                PiecesPerStrip = 10,
                CostPriceBoxPounds = 100m,
                RetailPriceBoxPounds = 150m,
                QuantityBoxes = 2
            });
            voucher.Lines.Add(new OpeningStockVoucherLine
            {
                ItemNameAr = "صنف 2",
                StripsPerBox = 1,
                PiecesPerStrip = 10,
                CostPriceBoxPounds = 200m,
                RetailPriceBoxPounds = 250m,
                QuantityBoxes = 1
            });

            voucher.RecalculateTotals();

            voucher.TotalItemsCount.Should().Be(2);
            voucher.TotalSmallestUnits.Should().Be(30); // 20 + 10
            voucher.TotalCostPounds.Should().Be(400m);   // 200 + 200
            voucher.TotalRetailPounds.Should().Be(550m); // 300 + 250
            voucher.TotalProfitPounds.Should().Be(150m); // 550 - 400
            voucher.ProfitMarginPercent.Should().Be(37.5m); // (150 / 400) * 100
        }

        [Fact]
        public void SaveVoucher_WithExistingItem_ShouldUpdateStock_CreateBatch_AndPostJournal()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);

            var itemRepo = new ItemRepository(db);
            var openingStockRepo = new OpeningStockRepository(db, itemRepo);

            // جلب صنف تجريبي موجود
            var existingItems = itemRepo.GetAllItems();
            existingItems.Should().NotBeEmpty();
            var item = existingItems.First();
            int initialStock = item.TotalStockSmallestUnits;

            var voucher = new OpeningStockVoucher
            {
                VoucherDate = DateTime.Today,
                Notes = "رصيد افتتاحي للاختبار",
                Lines = new List<OpeningStockVoucherLine>
                {
                    new OpeningStockVoucherLine
                    {
                        ItemId = item.Id,
                        ItemCode = item.Code,
                        ItemBarcode = item.Barcode,
                        ItemNameAr = item.NameAr,
                        StripsPerBox = item.StripsPerBox,
                        PiecesPerStrip = item.PiecesPerStrip,
                        BatchNumber = "TEST-BATCH-001",
                        ExpiryDate = DateTime.Today.AddYears(1),
                        QuantityBoxes = 10,
                        CostPriceBoxPounds = 50m,
                        RetailPriceBoxPounds = 75m
                    }
                }
            };

            var user = new User { Id = 1, FullName = "مدير الصيدلية", Role = UserRole.Owner };
            openingStockRepo.SaveVoucher(voucher, user);

            voucher.Id.Should().BeGreaterThan(0);
            voucher.VoucherNumber.Should().StartWith("OP-");

            // التحقق من تحديث رصيد الصنف
            var updatedItem = itemRepo.GetById(item.Id);
            updatedItem.Should().NotBeNull();
            int addedUnits = 10 * item.SmallestUnitsPerBox;
            updatedItem!.TotalStockSmallestUnits.Should().Be(initialStock + addedUnits);

            // التحقق من إضافة التشغيلة
            var batches = itemRepo.GetActiveBatches(item.Id);
            batches.Should().Contain(b => b.BatchNumber == "TEST-BATCH-001" && b.RemainingSmallestUnits == addedUnits);

            // التحقق من إنشاء قيد اليومية
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM JournalEntries WHERE EntryNumber LIKE @pat;";
            cmd.Parameters.AddWithValue("@pat", $"JV-{voucher.VoucherNumber}-%");
            long jvCount = Convert.ToInt64(cmd.ExecuteScalar());
            jvCount.Should().BeGreaterThan(0);

            // التحقق من حركة الجرد والتسوية
            using var adjCmd = conn.CreateCommand();
            adjCmd.CommandText = "SELECT COUNT(*) FROM StockAdjustments WHERE Reason LIKE @pat;";
            adjCmd.Parameters.AddWithValue("@pat", $"%رصيد أول المدة - سند رقم {voucher.VoucherNumber}%");
            long adjCount = Convert.ToInt64(adjCmd.ExecuteScalar());
            adjCount.Should().Be(1);
        }

        [Fact]
        public void SaveVoucher_WithBrandNewItem_ShouldCreateItemAndAddStock()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);

            var itemRepo = new ItemRepository(db);
            var openingStockRepo = new OpeningStockRepository(db, itemRepo);

            string newBarcode = "9988776655443";
            var voucher = new OpeningStockVoucher
            {
                VoucherDate = DateTime.Today,
                Lines = new List<OpeningStockVoucherLine>
                {
                    new OpeningStockVoucherLine
                    {
                        ItemId = 0, // صنف جديد تماماً
                        ItemBarcode = newBarcode,
                        ItemNameAr = "دواء جديد أول المدة",
                        ItemNameEn = "New Opening Drug",
                        ActiveIngredient = "Active Molecule",
                        StripsPerBox = 3,
                        PiecesPerStrip = 10,
                        BatchNumber = "OP-NEW-01",
                        ExpiryDate = DateTime.Today.AddYears(2),
                        QuantityBoxes = 4, // 4 * 30 = 120 وحدة
                        CostPriceBoxPounds = 60m,
                        RetailPriceBoxPounds = 90m
                    }
                }
            };

            openingStockRepo.SaveVoucher(voucher);

            // يجب أن يتم إنشاء الصنف في قاعدة البيانات وتحديث معرّفه
            var createdItem = itemRepo.GetByBarcodeOrCode(newBarcode);
            createdItem.Should().NotBeNull();
            createdItem!.NameAr.Should().Be("دواء جديد أول المدة");
            createdItem.StripsPerBox.Should().Be(3);
            createdItem.PiecesPerStrip.Should().Be(10);
            createdItem.TotalStockSmallestUnits.Should().Be(120);

            // التحقق من تخزين السطر في جدول OpeningStockVoucherLines
            var lines = openingStockRepo.GetVoucherLines(voucher.Id);
            lines.Should().HaveCount(1);
            lines[0].ItemNameAr.Should().Be("دواء جديد أول المدة");
            lines[0].TotalSmallestUnits.Should().Be(120);
        }

        [Fact]
        public void Navigation_OpeningStock_ShouldBeIntegratedInMainViewModelAndMainWindow()
        {
            string mainVm = Src("ViewModels/MainViewModel.cs");
            mainVm.Should().Contain("NavigateOpeningStockCommand");
            mainVm.Should().Contain("CanAccessOpeningStock => CanAccessInventory");
            mainVm.Should().Contain("NavigateOpeningStockCommand = new RelayCommand");
            mainVm.Should().Contain("OpenOpeningStockInSeparateWindowCommand");

            string mainXaml = Src("MainWindow.xaml");
            mainXaml.Should().Contain("vm:OpeningStockViewModel");
            mainXaml.Should().Contain("views:OpeningStockView");
            mainXaml.Should().Contain("NavigateOpeningStockCommand");
            mainXaml.Should().Contain("CanAccessOpeningStock");

            string app = Src("App.xaml.cs");
            app.Should().Contain("AddTransient<OpeningStockViewModel>()");
            app.Should().Contain("AddSingleton<OpeningStockRepository>()");
        }
    }
}
