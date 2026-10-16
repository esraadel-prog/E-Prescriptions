using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using ePrescriptions.Domain.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 40 جزء ب: شاشة تحديث الأسعار من Excel على مستوى الـ ViewModel (القارئ الحقيقي ClosedXML بيتأكد على Windows في خطوة 42)</summary>
    public class PriceListImportViewModelTests
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

        private class FakeReader : IPriceListFileReader
        {
            public IReadOnlyList<string[]>? Grid { get; set; }
            public Exception? Throw { get; set; }
            public IReadOnlyList<string[]> ReadGrid(string path)
            {
                if (Throw != null) throw Throw;
                return Grid!;
            }
        }

        private static string[] H(params string[] cells) => cells;

        private static IReadOnlyList<string[]> StandardGrid() => new List<string[]>
        {
            H("الباركود", "اسم الصنف", "سعر الجمهور", "سعر التكلفة"),
            H("6224444444441", "صنف عادي", "55.00", "30.00"),     // +10% جمهور  => آمن
            H("6224444444442", "صنف قفزة", "80.00", "30.00"),     // +60% جمهور  => يحتاج مراجعة
            H("6224444444443", "صنف تحت التكلفة", "35.00", "40.00"), // جمهور أقل من التكلفة => يحتاج مراجعة
            H("9999999999999", "صنف مش عندنا", "10.00", "5.00"),   // مش موجود
        };

        private class Env
        {
            public PriceListImportViewModel Vm = null!;
            public DatabaseContext Db = null!;
            public ItemRepository Items = null!;
            public Session Session = null!;
            public FakeReader Reader = null!;
            public int A, B, C;
            public string File = Path.Combine(Path.GetTempPath(), "pl_" + Guid.NewGuid().ToString("N") + ".xlsx");
        }

        private static Env Setup(UserRole? role = UserRole.Admin, bool withStaffLog = true, bool withOwnerLog = true)
        {
            var e = new Env();
            e.Db = new DatabaseContext(":memory:"); e.Db.InitializeAndMigrate(seedData: false);
            e.Items = new ItemRepository(e.Db);
            Item Make(string code, string barcode, string name, long retail, long cost) => new Item
            {
                Code = code, Barcode = barcode, NameAr = name, StripsPerBox = 3, PiecesPerStrip = 10,
                RetailPriceBoxPiastres = retail, WholesalePriceBoxPiastres = retail - 200, CostPriceBoxPiastres = cost, IsActive = true
            };
            e.A = e.Items.SaveItem(Make("PL-1", "6224444444441", "صنف عادي", 5000, 3000));
            e.B = e.Items.SaveItem(Make("PL-2", "6224444444442", "صنف قفزة", 5000, 3000));
            e.C = e.Items.SaveItem(Make("PL-3", "6224444444443", "صنف تحت التكلفة", 5000, 4000));

            e.Session = new Session { CurrentUser = role == null ? null : new User { Id = 7, Username = "u", FullName = "المستخدم", Role = role.Value } };
            e.Reader = new FakeReader { Grid = StandardGrid() };
            var suppliers = new SupplierRepository(e.Db);
            suppliers.SaveSupplier(new Supplier { Name = "شركة الأسعار", Phone = "0100", IsActive = true });
            e.Vm = new PriceListImportViewModel(e.Items, e.Reader, e.Session,
                withOwnerLog ? new OwnerAuditLogRepository(e.Db) : null,
                withStaffLog ? new StaffAuditLogRepository(e.Db) : null, suppliers)
            { ConfirmAction = (m, t) => true };
            e.Vm.SelectedSupplier = e.Vm.Suppliers.First();
            return e;
        }

        private static long Retail(Env e, int id) => e.Items.GetById(id)!.RetailPriceBoxPiastres;
        private static long Cost(Env e, int id) => e.Items.GetById(id)!.CostPriceBoxPiastres;

        // ---------- المعاينة ----------

        [Fact]
        public void LoadFile_ShouldBuildPreview_WithoutWritingAnything()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File).Should().BeTrue();

            e.Vm.Lines.Should().HaveCount(4);
            Retail(e, e.A).Should().Be(5000);
            Retail(e, e.B).Should().Be(5000);
            Retail(e, e.C).Should().Be(5000);
            e.Vm.SummaryText.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void LoadFile_ShouldLeaveReviewRowsUnticked_AndSafeRowsTicked()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File);

            var normal = e.Vm.Lines.First(l => l.ItemName == "صنف عادي");
            var jump = e.Vm.Lines.First(l => l.ItemName == "صنف قفزة");
            var belowCost = e.Vm.Lines.First(l => l.ItemName == "صنف تحت التكلفة");

            normal.Include.Should().BeTrue();
            jump.RequiresReview.Should().BeTrue();
            jump.Include.Should().BeFalse();
            belowCost.RequiresReview.Should().BeTrue();
            belowCost.Include.Should().BeFalse();
            e.Vm.ReviewLinesCount.Should().Be(2);
            normal.ChangesText.Should().Contain("55.00");
            normal.ChangesText.Should().Contain("50.00");
        }

        [Fact]
        public void NotFoundRow_ShouldBeShown_ButCannotBeTicked()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File);

            var missing = e.Vm.Lines.First(l => l.Line.Status == PriceLineStatus.NotFound);
            missing.StatusText.Should().Be("غير موجود");
            missing.Include = true;
            missing.Include.Should().BeFalse();
        }

        // ---------- الاعتماد ----------

        [Fact]
        public void Apply_ShouldChangeOnlyTickedRows()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File);

            e.Vm.ApplyConfirmed().Should().BeGreaterThan(0);

            Retail(e, e.A).Should().Be(5500);   // المعلّم اتغيّر
            Retail(e, e.B).Should().Be(5000);   // التغيير الكبير غير معلّم، ما اتغيّرش
            Retail(e, e.C).Should().Be(5000);   // تحت التكلفة غير معلّم، ما اتغيّرش
        }

        [Fact]
        public void Apply_ShouldChangeReviewRow_WhenUserTicksItManually()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File);

            e.Vm.Lines.First(l => l.ItemName == "صنف قفزة").Include = true;
            e.Vm.ApplyConfirmed();

            Retail(e, e.B).Should().Be(8000);
            Retail(e, e.C).Should().Be(5000);
        }

        [Fact]
        public void Apply_ShouldRebuildPreview_SoAppliedRowsBecomeUnchanged()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File);
            e.Vm.ApplyConfirmed();

            e.Vm.Lines.First(l => l.ItemName == "صنف عادي").Line.Status.Should().Be(PriceLineStatus.Unchanged);
            e.Vm.StatusMessage.Should().Contain("تم تغيير");
        }

        [Fact]
        public void Apply_WithNothingTicked_ShouldDoNothing()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File);
            e.Vm.ClearSelectionCommand.Execute(null);

            e.Vm.ApplyConfirmed().Should().Be(0);
            Retail(e, e.A).Should().Be(5000);
        }

        [Fact]
        public void ApplyCommand_ShouldDoNothing_WhenConfirmationDeclined()
        {
            var e = Setup();
            e.Vm.ConfirmAction = (m, t) => false;
            e.Vm.LoadFile(e.File);

            e.Vm.ApplyCommand.Execute(null);
            Retail(e, e.A).Should().Be(5000);

            e.Vm.ConfirmAction = (m, t) => true;
            e.Vm.ApplyCommand.Execute(null);
            Retail(e, e.A).Should().Be(5500);
        }

        [Fact]
        public void Apply_ShouldSkipRow_WhenPriceChangedAfterPreview()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File);

            using (var c = e.Db.CreateConnection())
            {
                using var m = c.CreateCommand();
                m.CommandText = $"UPDATE Items SET RetailPriceBoxPiastres = 5200 WHERE Id = {e.A}";
                m.ExecuteNonQuery();
            }

            e.Vm.ApplyConfirmed();
            Retail(e, e.A).Should().Be(5200);              // ما اتكتبش فوق التعديل الأحدث
            e.Vm.StatusMessage.Should().Contain("اتخطّى");
        }

        // ---------- خيارات الحقول ----------

        [Fact]
        public void TogglingRetailOff_ShouldRemoveRetailChangesFromPreview()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File);
            e.Vm.UpdateRetail = false;
            e.Vm.UpdateCost = false;

            e.Vm.Lines.SelectMany(l => l.Line.Changes).Should().BeEmpty();
            e.Vm.ApplyConfirmed().Should().Be(0);
            Retail(e, e.A).Should().Be(5000);
        }

        [Fact]
        public void SelectSafe_AndClearSelection_ShouldWork()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File);

            e.Vm.ClearSelectionCommand.Execute(null);
            e.Vm.SelectedLinesCount.Should().Be(0);

            e.Vm.SelectSafeCommand.Execute(null);
            string.Join(",", e.Vm.Lines.Where(l => l.Include).Select(l => l.ItemName)).Should().Be("صنف عادي");
            e.Vm.SelectedChangesCount.Should().BeGreaterThan(0);
        }

        // ---------- التدقيق ----------

        [Fact]
        public void Apply_AsAdmin_ShouldLogToStaffAuditOnly_WithOldNewSupplierAndFile()
        {
            var e = Setup(UserRole.Admin);
            e.Vm.LoadFile(e.File);
            e.Vm.ApplyConfirmed();

            var staff = new StaffAuditLogRepository(e.Db).GetRecent();
            var entry = staff.First(x => x.EntityId == e.A.ToString() && x.FieldName.StartsWith("RetailPriceBoxPiastres"));
            entry.OldValue.Should().Be("50.00");
            entry.NewValue.Should().Be("55.00");
            entry.FieldName.Should().Contain("شركة الأسعار");
            entry.FieldName.Should().Contain(Path.GetFileName(e.File));
            entry.ActorRole.Should().Be("Admin");
            (entry.ChangedAt > DateTime.Now.AddMinutes(-5)).Should().BeTrue();
            new OwnerAuditLogRepository(e.Db).GetRecent().Should().BeEmpty();
        }

        [Fact]
        public void Apply_AsOwner_ShouldLogToOwnerAuditOnly()
        {
            var e = Setup(UserRole.Owner);
            e.Vm.LoadFile(e.File);
            e.Vm.ApplyConfirmed();

            var owner = new OwnerAuditLogRepository(e.Db).GetRecent();
            var entry = owner.First(x => x.EntityId == e.A.ToString() && x.FieldName == "RetailPriceBoxPiastres");
            entry.OldValue.Should().Be("50.00");
            entry.NewValue.Should().Be("55.00");
            entry.Note.Should().Contain("شركة الأسعار");
            entry.Note.Should().Contain(Path.GetFileName(e.File));
            new StaffAuditLogRepository(e.Db).GetRecent().Should().BeEmpty();
        }

        [Fact]
        public void Apply_ShouldWarn_WhenAuditLoggingFails_ButStillApplyPrices()
        {
            var e = Setup(UserRole.Admin, withStaffLog: false);
            e.Vm.LoadFile(e.File);
            e.Vm.ApplyConfirmed().Should().BeGreaterThan(0);

            Retail(e, e.A).Should().Be(5500);
            e.Vm.StatusMessage.Should().Contain("تحذير");
            e.Vm.StatusMessage.Should().Contain("التدقيق");
        }

        // ---------- الصلاحيات ----------

        [Theory]
        [InlineData(UserRole.Cashier)]
        [InlineData(UserRole.Pharmacist)]
        public void NonAdminRoles_ShouldNotLoadOrApply(UserRole role)
        {
            var e = Setup(role);
            e.Vm.CanImport.Should().BeFalse();
            e.Vm.LoadFile(e.File).Should().BeFalse();
            e.Vm.Lines.Should().BeEmpty();
            e.Vm.ApplyConfirmed().Should().Be(0);
        }

        [Fact]
        public void NoSession_ShouldNotLoad()
        {
            var e = Setup(role: null);
            e.Vm.LoadFile(e.File).Should().BeFalse();
        }

        [Fact]
        public void RoleChangedAfterPreview_ShouldBlockApply()
        {
            var e = Setup(UserRole.Admin);
            e.Vm.LoadFile(e.File).Should().BeTrue();

            e.Session.CurrentUser = new User { Id = 9, Username = "c", FullName = "كاشير", Role = UserRole.Cashier };

            e.Vm.ApplyConfirmed().Should().Be(0);
            Retail(e, e.A).Should().Be(5000);
            new StaffAuditLogRepository(e.Db).GetRecent().Should().BeEmpty();
        }

        // ---------- رفض الملفات ----------

        [Fact]
        public void ReaderFileError_ShouldShowItsMessage_AndNotBuildPreview()
        {
            var e = Setup();
            e.Reader.Throw = new PriceListFileException("الملف فاضي");
            e.Vm.LoadFile(e.File).Should().BeFalse();
            e.Vm.StatusMessage.Should().Be("الملف فاضي");
            e.Vm.Lines.Should().BeEmpty();
        }

        [Fact]
        public void ReaderUnexpectedError_ShouldNotCrash()
        {
            var e = Setup();
            e.Reader.Throw = new InvalidOperationException("boom");
            e.Vm.LoadFile(e.File).Should().BeFalse();
            e.Vm.StatusMessage.Should().Contain("تعذر");
        }

        [Fact]
        public void UnrecognisedHeaders_ShouldBeRejectedWithMessage()
        {
            var e = Setup();
            e.Reader.Grid = new List<string[]> { H("عمود١", "عمود٢"), H("a", "b") };
            e.Vm.LoadFile(e.File).Should().BeFalse();
            e.Vm.StatusMessage.Should().Contain("أعمدة");
            e.Vm.Lines.Should().BeEmpty();
        }

        [Fact]
        public void FileWithoutPriceColumn_ShouldBeRejected()
        {
            var e = Setup();
            e.Reader.Grid = new List<string[]> { H("الباركود", "اسم الصنف"), H("6224444444441", "صنف عادي") };
            e.Vm.LoadFile(e.File).Should().BeFalse();
            e.Vm.Lines.Should().BeEmpty();
        }

        [Fact]
        public void Clear_ShouldResetPreview()
        {
            var e = Setup();
            e.Vm.LoadFile(e.File);
            e.Vm.ClearCommand.Execute(null);
            e.Vm.Lines.Should().BeEmpty();
            e.Vm.FilePath.Should().BeEmpty();
            e.Vm.ApplyConfirmed().Should().Be(0);
        }

        [Fact]
        public void FormatMoney_ShouldUseInvariantTwoDecimals()
        {
            PriceListImportViewModel.FormatMoney(5000).Should().Be("50.00");
            PriceListImportViewModel.FormatMoney(1).Should().Be("0.01");
        }
    }

    /// <summary>حارس ملف قائمة الأسعار (بدون اعتماد على ClosedXML)</summary>
    public class PriceListFileGuardTests
    {
        private static string Msg(Action act)
        {
            try { act(); }
            catch (PriceListFileException ex) { return ex.Message; }
            throw new InvalidOperationException("لم يُرمَ PriceListFileException");
        }

        private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), "plg_" + Guid.NewGuid().ToString("N") + ext);

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyPath_ShouldBeRejected(string path)
        {
            Action act = () => PriceListFileGuard.CheckFile(path);
            act.Should().Throw<PriceListFileException>();
        }

        [Fact]
        public void NullPath_ShouldBeRejected()
        {
            Action act = () => PriceListFileGuard.CheckFile(null);
            act.Should().Throw<PriceListFileException>();
        }

        [Fact]
        public void OldXlsFormat_ShouldBeRejected_WithSaveAsHint()
        {
            string p = Temp(".xls"); File.WriteAllText(p, "x");
            try
            {
                Msg(() => PriceListFileGuard.CheckFile(p)).Should().Contain(".xlsx");
            }
            finally { File.Delete(p); }
        }

        [Theory]
        [InlineData(".csv")]
        [InlineData(".txt")]
        [InlineData(".pdf")]
        public void OtherFormats_ShouldBeRejected(string ext)
        {
            string p = Temp(ext); File.WriteAllText(p, "x");
            try
            {
                Action act = () => PriceListFileGuard.CheckFile(p);
                act.Should().Throw<PriceListFileException>();
            }
            finally { File.Delete(p); }
        }

        [Fact]
        public void MissingFile_ShouldBeRejected()
        {
            Msg(() => PriceListFileGuard.CheckFile(Temp(".xlsx"))).Should().Contain("مش موجود");
        }

        [Fact]
        public void OversizedFile_ShouldBeRejected_AndLimitFileAccepted()
        {
            string big = Temp(".xlsx");
            using (var fs = new FileStream(big, FileMode.Create)) fs.SetLength(PriceListFileGuard.MaxFileBytes + 1);
            string ok = Temp(".xlsm");
            using (var fs = new FileStream(ok, FileMode.Create)) fs.SetLength(PriceListFileGuard.MaxFileBytes);
            try
            {
                Msg(() => PriceListFileGuard.CheckFile(big)).Should().Contain("ميجا");
                PriceListFileGuard.CheckFile(ok);   // الحد بالظبط مقبول (لو رمى يفشل الاختبار)
            }
            finally { File.Delete(big); File.Delete(ok); }
        }

        [Fact]
        public void RowCount_ShouldBeLimitedTo50000()
        {
            PriceListFileGuard.MaxRows.Should().Be(50000);
            PriceListFileGuard.CheckRowCount(50000);   // الحد بالظبط مقبول
            Msg(() => PriceListFileGuard.CheckRowCount(50001)).Should().Contain("50,000");
        }
    }

    /// <summary>فحص نصي لتوصيل الشاشة (WPF/ClosedXML ما بيتترجموش على Linux؛ التحقق الفعلي في خطوة 42)</summary>
    public class PriceListImportWiringTests
    {
        private static string Src(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "src", "ePrescriptions.App", relative);
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                dir = dir.Parent;
            }
            throw new FileNotFoundException(relative);
        }

        [Fact]
        public void Navigation_ShouldBeAdminOrOwnerOnly_AndGuardedByCanExecute()
        {
            string main = Src("ViewModels/MainViewModel.cs");
            main.Should().Contain("CanAccessPriceListImport => IsAdminOrOwner");
            main.Should().Contain("NavigateTo<PriceListImportViewModel>(), () => CanAccessPriceListImport");
            main.Should().Contain("NavigatePriceListImportCommand.NotifyCanExecuteChanged()");
            main.Should().Contain("nameof(CanAccessPriceListImport)");
        }

        [Fact]
        public void MainWindow_ShouldMapViewModelToView_AndHideSidebarButtonByPermission()
        {
            string xaml = Src("MainWindow.xaml");
            xaml.Should().Contain("vm:PriceListImportViewModel");
            xaml.Should().Contain("views:PriceListImportView");
            xaml.Should().Contain("Command=\"{Binding NavigatePriceListImportCommand}\"");
            xaml.Should().Contain("Binding CanAccessPriceListImport");
        }

        [Fact]
        public void Services_ShouldBeRegisteredInDi()
        {
            string app = Src("App.xaml.cs");
            app.Should().Contain("AddTransient<PriceListImportViewModel>()");
            app.Should().Contain("AddSingleton<IPriceListFileReader, ExcelPriceListReader>()");
        }

        [Fact]
        public void View_ShouldRestrictFileDialogToXlsx_AndDelegateLoadingToViewModel()
        {
            string code = Src("Views/PriceListImportView.xaml.cs");
            code.Should().Contain("*.xlsx;*.xlsm");
            code.Should().Contain("vm.LoadFile(");
            string xaml = Src("Views/PriceListImportView.xaml");
            xaml.Should().Contain("Binding ApplyCommand");
            xaml.Should().Contain("Binding Include");
        }

        [Fact]
        public void ExcelReader_ShouldUseGuard_AndReadSharedFile_AndRawNumbers()
        {
            string reader = Src("Services/ExcelPriceListReader.cs");
            reader.Should().Contain("PriceListFileGuard.CheckFile(path)");
            reader.Should().Contain("PriceListFileGuard.CheckRowCount(");
            reader.Should().Contain("FileShare.ReadWrite");
            reader.Should().Contain("XLDataType.Number");
        }
    }
}
