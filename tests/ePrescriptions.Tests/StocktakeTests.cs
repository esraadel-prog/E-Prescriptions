using System;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// خطوة 38: جرد بالباركود (مسح الأصناف ومقارنة الكمية الفعلية بالمسجّلة)
    /// </summary>
    public class StocktakeTests
    {
        private static DatabaseContext CreateDb()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            return db;
        }

        private static int NewItem(ItemRepository repo, string code, string barcode, int stock, int strips = 3, int pieces = 10) =>
            repo.SaveItem(new Item
            {
                Code = code,
                Barcode = barcode,
                NameAr = "صنف " + code,
                StripsPerBox = strips,
                PiecesPerStrip = pieces,
                RetailPriceBoxPiastres = 1000,
                TotalStockSmallestUnits = stock,
                IsActive = true
            });

        [Fact]
        public void Scan_ByItemBarcode_ShouldResolveItem_AndCountOneBoxPerScan()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            NewItem(repo, "S001", "628100000001", 90); // العلبة = 30 وحدة

            var session = new StocktakeSession();
            for (int i = 0; i < 3; i++)
            {
                var resolved = repo.ResolveScannedCode("628100000001");
                resolved.Should().NotBeNull();
                resolved!.Value.Batch.Should().BeNull();
                session.AddScannedBox(resolved.Value.Item);
            }

            session.Lines.Should().HaveCount(1);
            session.ScanCount.Should().Be(3);
            session.Lines[0].CountedSmallestUnits.Should().Be(90);
            session.Lines[0].BookSmallestUnits.Should().Be(90);
            session.Lines[0].Status.Should().Be(StocktakeStatus.Match);
        }

        [Fact]
        public void Scan_ByBatchLabel_ShouldResolveSameItem_AndReturnBatch()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            int itemId = NewItem(repo, "S002", "628100000002", 0);
            repo.AddBatch(itemId, "B-77", DateTime.Today.AddYears(1), 60, 1000);
            var batch = repo.GetActiveBatches(itemId).Single();

            var resolved = repo.ResolveScannedCode(BatchLabelCode.Encode(batch.Id));

            resolved.Should().NotBeNull();
            resolved!.Value.Item.Id.Should().Be(itemId);
            resolved.Value.Batch.Should().NotBeNull();
            resolved.Value.Batch!.Id.Should().Be(batch.Id);
        }

        [Fact]
        public void Scan_UnknownCode_ShouldReturnNull()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            repo.ResolveScannedCode("000000000000").Should().BeNull();
            repo.ResolveScannedCode("   ").Should().BeNull();
            repo.ResolveScannedCode(null).Should().BeNull();
        }

        [Fact]
        public void Compare_ShouldClassifyShortageSurplusAndMatch()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            var a = repo.GetById(NewItem(repo, "A", "6281A", 60))!;  // دفتري 60، هيتعدّ 30 → عجز 30
            var b = repo.GetById(NewItem(repo, "B", "6281B", 30))!;  // دفتري 30، هيتعدّ 60 → زيادة 30
            var c = repo.GetById(NewItem(repo, "C", "6281C", 30))!;  // دفتري 30، هيتعدّ 30 → مطابق

            var s = new StocktakeSession();
            s.AddScannedBox(a);
            s.AddScannedBox(b); s.AddScannedBox(b);
            s.AddScannedBox(c);

            s.Lines.Single(l => l.Code == "A").Status.Should().Be(StocktakeStatus.Shortage);
            s.Lines.Single(l => l.Code == "A").DifferenceSmallestUnits.Should().Be(-30);
            s.Lines.Single(l => l.Code == "B").Status.Should().Be(StocktakeStatus.Surplus);
            s.Lines.Single(l => l.Code == "B").DifferenceSmallestUnits.Should().Be(30);
            s.Lines.Single(l => l.Code == "C").Status.Should().Be(StocktakeStatus.Match);
            s.LinesWithDifference().Should().HaveCount(2);
        }

        [Fact]
        public void SetCounted_ShouldOverrideScanCount_AndRejectNegative()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            var a = repo.GetById(NewItem(repo, "A", "6281A", 60))!;

            var s = new StocktakeSession();
            s.AddScannedBox(a);
            s.SetCounted(a, 45); // علبة ونص (أشرطة سائبة)
            s.Lines[0].CountedSmallestUnits.Should().Be(45);

            Action neg = () => s.SetCounted(a, -1);
            neg.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void IncludeUncounted_ShouldShowFullShortage_ButOnlyWhenRequested()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            var a = repo.GetById(NewItem(repo, "A", "6281A", 60))!;
            var b = repo.GetById(NewItem(repo, "B", "6281B", 30))!;

            var s = new StocktakeSession();
            s.AddScannedBox(a);                 // b ما اتمسحش → مش في الجلسة (جرد جزئي)
            s.Lines.Should().HaveCount(1);

            s.IncludeUncounted(b);              // طلب صريح: يعتبر صفر
            var lineB = s.Lines.Single(l => l.Code == "B");
            lineB.CountedSmallestUnits.Should().Be(0);
            lineB.Status.Should().Be(StocktakeStatus.Shortage);
        }

        [Fact]
        public void ApplyStocktake_ShouldAdjustOnlyDifferingItems_AndRecordAdjustments()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            int idA = NewItem(repo, "A", "6281A", 60);
            int idB = NewItem(repo, "B", "6281B", 30);
            int idC = NewItem(repo, "C", "6281C", 30);

            var s = new StocktakeSession();
            s.AddScannedBox(repo.GetById(idA)!);                              // A: 60 → 30
            s.AddScannedBox(repo.GetById(idB)!); s.AddScannedBox(repo.GetById(idB)!); // B: 30 → 60
            s.AddScannedBox(repo.GetById(idC)!);                              // C: مطابق

            var applied = repo.ApplyStocktake(s);

            applied.Should().HaveCount(2);
            applied.Select(x => x.AdjustmentNumber).Distinct().Should().HaveCount(2);
            repo.GetById(idA)!.TotalStockSmallestUnits.Should().Be(30);
            repo.GetById(idB)!.TotalStockSmallestUnits.Should().Be(60);
            repo.GetById(idC)!.TotalStockSmallestUnits.Should().Be(30);

            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM StockAdjustments WHERE Reason = @r;";
            cmd.Parameters.AddWithValue("@r", "جرد بالباركود");
            Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(2);
        }

        [Fact]
        public void RemoveLine_And_Clear_ShouldResetSession()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            var a = repo.GetById(NewItem(repo, "A", "6281A", 60))!;
            var s = new StocktakeSession();
            s.AddScannedBox(a);
            s.RemoveLine(a.Id).Should().BeTrue();
            s.Lines.Should().BeEmpty();
            s.AddScannedBox(a);
            s.Clear();
            s.Lines.Should().BeEmpty();
            s.ScanCount.Should().Be(0);
        }
    }
}

namespace ePrescriptions.Tests
{
    using ePrescriptions.App.Services;
    using ePrescriptions.App.ViewModels;
    using ePrescriptions.Domain.Enums;

    /// <summary>خطوة 38 جزء ب: شاشة الجرد (ViewModel) + توجيه التدقيق للسجل الصحيح</summary>
    public class StocktakeViewModelTests
    {
#pragma warning disable CS0067
        private class Session : IUserSessionService
        {
            public User? CurrentUser { get; set; }
            public Shift? CurrentShift { get; set; }
            public bool IsAuthenticated => CurrentUser != null;
            public bool IsLocked => false;
            public event Action? SessionChanged;
            public event Action<bool>? LockStateChanged;
            public bool Login(string u, string p, out string e) { e = string.Empty; return false; }
            public void Logout() { }
            public void Lock() { }
            public bool Unlock(string p, out string e) { e = string.Empty; return false; }
            public void SetCurrentShift(Shift? s) { CurrentShift = s; }
        }
#pragma warning restore CS0067

        private static DatabaseContext CreateDb()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            return db;
        }

        private static long Count(DatabaseContext db, string table)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        private static (StocktakeViewModel Vm, ItemRepository Repo, int ItemId, DatabaseContext Db) Build(UserRole role)
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            int id = repo.SaveItem(new Item
            {
                Code = "V1", Barcode = "6282000", NameAr = "صنف الجرد",
                StripsPerBox = 3, PiecesPerStrip = 10, RetailPriceBoxPiastres = 1000,
                TotalStockSmallestUnits = 90, IsActive = true
            });
            var session = new Session { CurrentUser = new User { Id = 5, FullName = "اختبار", Role = role } };
            var vm = new StocktakeViewModel(repo, session, new OwnerAuditLogRepository(db), new StaffAuditLogRepository(db));
            return (vm, repo, id, db);
        }

        [Fact]
        public void ViewModel_ScanUnknown_ShouldNotCreateRow()
        {
            var (vm, _, _, _) = Build(UserRole.Pharmacist);
            vm.Scan("999999").Should().BeFalse();
            vm.Rows.Should().BeEmpty();
        }

        [Fact]
        public void ViewModel_ScanAndEdit_ShouldUpdateDifferenceCounters()
        {
            var (vm, _, _, _) = Build(UserRole.Pharmacist);
            vm.Scan("6282000").Should().BeTrue();   // علبة = 30 → عجز 60
            vm.Rows.Should().HaveCount(1);
            vm.Rows[0].DifferenceSmallestUnits.Should().Be(-60);
            vm.DifferenceLinesCount.Should().Be(1);

            vm.Rows[0].CountedSmallestUnits = 90;   // تعديل يدوي → مطابق
            vm.Rows[0].DifferenceSmallestUnits.Should().Be(0);
            vm.DifferenceLinesCount.Should().Be(0);
        }

        [Fact]
        public void ViewModel_Apply_ByPharmacist_ShouldAdjustStock_AndLogToStaffLogOnly()
        {
            var (vm, repo, id, db) = Build(UserRole.Pharmacist);
            vm.Scan("6282000"); // معدود 30 مقابل 90

            vm.ApplyConfirmed().Should().Be(1);

            repo.GetById(id)!.TotalStockSmallestUnits.Should().Be(30);
            Count(db, "StaffAuditLog").Should().Be(1);
            Count(db, "OwnerAuditLog").Should().Be(0);
            vm.Rows.Should().BeEmpty(); // الجلسة اتفرّغت بعد الاعتماد
        }

        [Fact]
        public void ViewModel_Apply_ByOwner_ShouldLogToOwnerLogOnly()
        {
            var (vm, _, _, db) = Build(UserRole.Owner);
            vm.Scan("6282000");

            vm.ApplyConfirmed().Should().Be(1);

            Count(db, "OwnerAuditLog").Should().Be(1);
            Count(db, "StaffAuditLog").Should().Be(0);
        }
    }
}

namespace ePrescriptions.Tests
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using ePrescriptions.App.Services;
    using ePrescriptions.App.ViewModels;

    /// <summary>خطوة 38 جزء ج: قائمة الجرد المطبوعة (تجهيز السطور + استدعاء خدمة PDF)</summary>
    public class StocktakeSheetTests
    {
        private class FakePdf : IPdfReportService
        {
            public List<StocktakeSheetRow>? Received;
            public string GenerateA4InvoicePdf(Invoice invoice, PharmacySettings settings) => "x";
            public Task<string> GenerateA4InvoicePdfAsync(Invoice invoice, PharmacySettings settings) => Task.FromResult("x");
            public string GenerateShiftClosurePdf(ShiftClosureReport report, PharmacySettings settings) => "x";
            public Task<string> GenerateShiftClosurePdfAsync(ShiftClosureReport report, PharmacySettings settings) => Task.FromResult("x");
            public string GenerateStocktakeSheetPdf(List<StocktakeSheetRow> rows, PharmacySettings settings)
            {
                Received = rows;
                return "C:/fake/قائمة_جرد.pdf";
            }
            public Task<string> GenerateStocktakeSheetPdfAsync(List<StocktakeSheetRow> rows, PharmacySettings settings)
            {
                Received = rows;
                return Task.FromResult("C:/fake/قائمة_جرد.pdf");
            }
            public string GenerateCustomerStatementPdf(Customer customer, List<StatementRow> statement, PharmacySettings settings) => "x";
            public Task<string> GenerateCustomerStatementPdfAsync(Customer customer, List<StatementRow> statement, PharmacySettings settings) => Task.FromResult("x");
            public string GenerateSupplierStatementPdf(Supplier supplier, List<StatementRow> statement, PharmacySettings settings) => "x";
            public Task<string> GenerateSupplierStatementPdfAsync(Supplier supplier, List<StatementRow> statement, PharmacySettings settings) => Task.FromResult("x");
            public string GenerateVoucherPdf(string voucherNumber, DateTime date, string partyName, long amountPiastres, string voucherType, string notes, PharmacySettings settings) => "x";
            public Task<string> GenerateVoucherPdfAsync(string voucherNumber, DateTime date, string partyName, long amountPiastres, string voucherType, string notes, PharmacySettings settings) => Task.FromResult("x");
        }

        private static DatabaseContext CreateDb()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            return db;
        }

        [Fact]
        public void Builder_ShouldSortByName_NumberFromOne_AndSkipInactive()
        {
            var items = new List<Item>
            {
                new Item { Id = 1, Code = "C", NameAr = "ب صنف", StripsPerBox = 3, PiecesPerStrip = 10, TotalStockSmallestUnits = 30, IsActive = true },
                new Item { Id = 2, Code = "A", NameAr = "أ صنف", StripsPerBox = 3, PiecesPerStrip = 10, TotalStockSmallestUnits = 0, IsActive = true },
                new Item { Id = 3, Code = "X", NameAr = "ت موقوف", StripsPerBox = 3, PiecesPerStrip = 10, TotalStockSmallestUnits = 50, IsActive = false }
            };

            var rows = StocktakeSheetBuilder.Build(items);

            rows.Should().HaveCount(2);
            rows[0].Number.Should().Be(1);
            rows[0].Code.Should().Be("A");
            rows[1].Number.Should().Be(2);
            rows[1].BookSmallestUnits.Should().Be(30);
            rows.Any(r => r.Code == "X").Should().BeFalse();
        }

        [Fact]
        public void Builder_ExcludeZeroStock_ShouldDropEmptyItems()
        {
            var items = new List<Item>
            {
                new Item { Id = 1, Code = "A", NameAr = "أ", TotalStockSmallestUnits = 0, IsActive = true },
                new Item { Id = 2, Code = "B", NameAr = "ب", TotalStockSmallestUnits = 5, IsActive = true }
            };
            StocktakeSheetBuilder.Build(items, includeZeroStock: false).Should().HaveCount(1);
            StocktakeSheetBuilder.Build(items).Should().HaveCount(2);
        }

        [Fact]
        public async Task ViewModel_PrintSheet_ShouldPassAllActiveItemsToPdfService()
        {
            var db = CreateDb();
            var repo = new ItemRepository(db);
            repo.SaveItem(new Item { Code = "P1", Barcode = "1", NameAr = "صنف 1", TotalStockSmallestUnits = 10, IsActive = true });
            repo.SaveItem(new Item { Code = "P2", Barcode = "2", NameAr = "صنف 2", TotalStockSmallestUnits = 0, IsActive = true });
            var pdf = new FakePdf();
            var vm = new StocktakeViewModel(repo, null, null, null, pdf, new SettingsRepository(db));

            await vm.PrintSheetAsync();

            pdf.Received.Should().NotBeNull();
            pdf.Received!.Should().HaveCount(2);
            vm.StatusMessage.Should().Contain("2 صنف");
            vm.IsBusy.Should().BeFalse();
        }

        [Fact]
        public async Task ViewModel_PrintSheet_WithoutPdfService_ShouldNotThrow()
        {
            var db = CreateDb();
            var vm = new StocktakeViewModel(new ItemRepository(db));
            await vm.PrintSheetAsync();
            vm.StatusMessage.Should().Contain("PDF");
        }
    }
}
