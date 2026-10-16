using System;
using System.IO;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 29: سعر جرام الذهب في الزكاة إدخال إلزامي يُحفظ، بدون قيمة افتراضية</summary>
    public class ZakatGoldPriceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ep_zakat_" + Guid.NewGuid().ToString("N"));
        private readonly DatabaseContext _db;
        private readonly FinanceRepository _repo;

        public ZakatGoldPriceTests()
        {
            Directory.CreateDirectory(_root);
            _db = new DatabaseContext(Path.Combine(_root, "zakat.db"));
            _db.InitializeAndMigrate(seedData: false);
            _repo = new FinanceRepository(_db);
        }

        public void Dispose()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_root, true); } catch (Exception) { }
        }

        [Fact]
        public void ZakatCalculation_ShouldHaveNoDefaultGoldPrice_AndNeverBeDueWithoutIt()
        {
            var zakat = new ZakatCalculation { StockValuePiastres = 99999999999 };

            zakat.GoldGramPricePiastres.Should().Be(0);
            zakat.NisabPiastres.Should().Be(0);
            zakat.IsZakatDue.Should().BeFalse();       // قبل التعديل: النصاب صفر فكل وعاء كان "واجب"
            zakat.ZakatAmountPiastres.Should().Be(0);
        }

        [Fact]
        public void NoSavedPrice_ByDefault_OnAFreshDatabase()
        {
            _repo.GetZakatGoldPrice().Should().BeNull();
        }

        [Fact]
        public void SaveZakatGoldPrice_ShouldPersist_AndUpdateInPlace()
        {
            _repo.SaveZakatGoldPrice(450000);
            var first = _repo.GetZakatGoldPrice();
            first.Should().NotBeNull();
            first!.GramPricePiastres.Should().Be(450000);
            (first.UpdatedAt > DateTime.Now.AddMinutes(-5)).Should().BeTrue();

            _repo.SaveZakatGoldPrice(470000);
            _repo.GetZakatGoldPrice()!.GramPricePiastres.Should().Be(470000);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        public void SaveZakatGoldPrice_ShouldRejectNonPositive_AndSaveNothing(long bad)
        {
            Action act = () => _repo.SaveZakatGoldPrice(bad);
            act.Should().Throw<ArgumentOutOfRangeException>();
            _repo.GetZakatGoldPrice().Should().BeNull();
        }

        [Fact]
        public void CalculateZakat_ShouldRejectMissingGoldPrice()
        {
            Action act = () => _repo.CalculateZakat(true, 0, true);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void SavingPharmacySettings_ShouldNotWipeTheSavedGoldPrice()
        {
            _repo.SaveZakatGoldPrice(450000);

            var settingsRepo = new SettingsRepository(_db);
            var settings = settingsRepo.GetSettings();
            settingsRepo.SaveSettings(settings); // INSERT OR REPLACE

            _repo.GetZakatGoldPrice()!.GramPricePiastres.Should().Be(450000);
        }

        [Fact]
        public void ViewModel_OnFreshDatabase_ShouldStartEmpty_AndNotComputeZakat()
        {
            var vm = new ZakatViewModel(_repo);

            vm.GoldGramPricePounds.Should().BeNull();
            vm.HasValidGoldPrice.Should().BeFalse();
            vm.IsZakatDue.Should().BeFalse();
            vm.FormattedNisab.Should().Be("—");
            vm.ZakatStatusText.Should().Contain("أدخل سعر جرام الذهب");
            vm.SavedGoldPriceInfo.Should().Contain("لم يُحفظ");
            vm.SaveGoldPriceCommand.CanExecute(null).Should().BeFalse();
        }

        [Fact]
        public void ViewModel_ShouldRejectSavingEmptyZeroOrNegativePrice()
        {
            var vm = new ZakatViewModel(_repo);

            vm.SaveGoldPrice().Should().BeFalse();       // فاضي
            vm.GoldGramPricePounds = 0m;
            vm.SaveGoldPrice().Should().BeFalse();
            vm.GoldGramPricePounds = -5m;
            vm.SaveGoldPrice().Should().BeFalse();

            _repo.GetZakatGoldPrice().Should().BeNull();
            vm.SaveGoldPriceCommand.CanExecute(null).Should().BeFalse();
        }

        [Fact]
        public void ViewModel_ShouldCalculateOnlyAfterPriceEntered_AndSaveItForNextTime()
        {
            var vm = new ZakatViewModel(_repo);

            vm.GoldGramPricePounds = 4000m;
            vm.HasValidGoldPrice.Should().BeTrue();
            vm.HasUnsavedGoldPrice.Should().BeTrue();
            vm.ZakatResult.GoldGramPricePiastres.Should().Be(400000);
            vm.ZakatResult.NisabPiastres.Should().Be(85 * 400000);
            vm.FormattedNisab.Should().NotBe("—");
            vm.SaveGoldPriceCommand.CanExecute(null).Should().BeTrue();

            vm.SaveGoldPrice().Should().BeTrue();
            vm.HasUnsavedGoldPrice.Should().BeFalse();
            vm.SavedGoldPriceInfo.Should().Contain("آخر سعر محفوظ");

            // جلسة جديدة (إعادة فتح الشاشة): السعر المحفوظ بيتحمّل لوحده
            var vm2 = new ZakatViewModel(_repo);
            vm2.GoldGramPricePounds.Should().Be(4000m);
            vm2.HasUnsavedGoldPrice.Should().BeFalse();
            vm2.ZakatResult.NisabPiastres.Should().Be(85 * 400000);

            // تغيير السعر بدون حفظ بيبان كغير محفوظ
            vm2.GoldGramPricePounds = 4100m;
            vm2.HasUnsavedGoldPrice.Should().BeTrue();

            // مسح الخانة بيرجع لحالة "مطلوب إدخال" بدل ما يحسب بصفر
            vm2.GoldGramPricePounds = null;
            vm2.HasValidGoldPrice.Should().BeFalse();
            vm2.FormattedZakatDue.Should().Be("—");
        }
    }
}
