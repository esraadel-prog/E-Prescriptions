using System;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 24: بحث عربي موحّد (تطبيع أ/إ/ا، ة/ه، ى/ي، إزالة التشكيل) عبر عمود Items.SearchText</summary>
    public class ArabicSearchTests
    {
        private static void Exec(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static long Scalar(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        private static (DatabaseContext db, ItemRepository repo) NewEmpty()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            return (db, new ItemRepository(db));
        }

        private static int Add(ItemRepository repo, string code, string nameAr, string nameEn = "", string ingredient = "") =>
            repo.SaveItem(new Item { Code = code, Barcode = "622" + code, NameAr = nameAr, NameEn = nameEn, ActiveIngredient = ingredient, StripsPerBox = 1, PiecesPerStrip = 10, RetailPriceBoxPiastres = 1000, CostPriceBoxPiastres = 800, IsActive = true });

        [Fact]
        public void Normalize_ShouldUnifyAlefTaaMarbutaYaaDiacriticsAndDigits()
        {
            ArabicTextNormalizer.Normalize("أَسْبِرِين").Should().Be("اسبرين");
            ArabicTextNormalizer.Normalize("إيبوبروفين آمن").Should().Be("ايبوبروفين امن");
            ArabicTextNormalizer.Normalize("صيدلية").Should().Be("صيدليه");
            ArabicTextNormalizer.Normalize("ربى").Should().Be(ArabicTextNormalizer.Normalize("ربي"));
            ArabicTextNormalizer.Normalize("مســكّــن").Should().Be("مسكن");
            ArabicTextNormalizer.Normalize("  PANADOL   Extra ٥٠٠ ").Should().Be("panadol extra 500");
            ArabicTextNormalizer.Normalize(null).Should().Be("");
            ArabicTextNormalizer.Normalize("   ").Should().Be("");
        }

        [Fact]
        public void Search_ShouldIgnoreAlefHamzaTaaMarbutaYaaAndDiacritics()
        {
            var (db, repo) = NewEmpty();
            int aspirin = Add(repo, "A1", "أسبرين بروتكت");
            int cream = Add(repo, "A2", "كريم مرطّب للبشرة");
            int syrup = Add(repo, "A3", "شراب كحة");

            repo.Search("اسبرين").Select(i => i.Id).Should().Contain(aspirin);       // بدون همزة
            repo.Search("إسبرين").Select(i => i.Id).Should().Contain(aspirin);       // همزة تحت
            repo.Search("أَسبِرين").Select(i => i.Id).Should().Contain(aspirin);     // بتشكيل
            repo.Search("مرطب").Select(i => i.Id).Should().Contain(cream);           // الاسم مشكّل والبحث لا
            repo.Search("كحه").Select(i => i.Id).Should().Contain(syrup);            // ة ← ه
            repo.Search("كحة").Select(i => i.Id).Should().Contain(syrup);
            repo.Search("اسبرين").Select(i => i.Id).Should().NotContain(syrup);
        }

        [Fact]
        public void Search_ShouldMatchEnglishCaseInsensitiveActiveIngredientAndMultiWordInAnyOrder()
        {
            var (db, repo) = NewEmpty();
            int id = Add(repo, "B1", "بنادول إكسترا", "Panadol Extra", "باراسيتامول");
            int other = Add(repo, "B2", "بنادول ليلي", "Panadol Night", "باراسيتامول");

            var found = repo.Search("PANADOL").Select(i => i.Id).ToList();
            found.Should().Contain(id);
            found.Should().Contain(other);
            repo.Search("باراسيتامول").Should().HaveCount(2);
            var twoWords = repo.Search("اكسترا بنادول").Select(i => i.Id).ToList(); // كلمتان بترتيب مختلف
            twoWords.Should().HaveCount(1);
            twoWords[0].Should().Be(id);
            var withYaa = repo.Search("بنادول ليلى").Select(i => i.Id).ToList(); // ى ← ي
            withYaa.Should().HaveCount(1);
            withYaa[0].Should().Be(other);
        }

        [Fact]
        public void Search_ShouldStillFindByCodeAndBarcode_AndTreatWildcardsLiterally()
        {
            var (db, repo) = NewEmpty();
            int id = Add(repo, "XY77", "دواء تجريبي");
            Add(repo, "XY78", "دواء آخر");

            repo.Search("XY77").Select(i => i.Id).Should().Contain(id);
            repo.Search("622XY77").Select(i => i.Id).Should().Contain(id);
            repo.Search("%").Should().BeEmpty();   // % حرفية مش "كل حاجة"
            repo.Search("_").Should().BeEmpty();
        }

        [Fact]
        public void EditingItemName_ShouldRefreshSearchText()
        {
            var (db, repo) = NewEmpty();
            int id = Add(repo, "C1", "اوجمنتين");
            repo.Search("اوجمنتين").Should().HaveCount(1);

            var item = repo.GetById(id)!;
            item.NameAr = "أوغمنتين";
            repo.SaveItem(item);

            repo.Search("اوجمنتين").Should().BeEmpty();
            repo.Search("اوغمنتين").Select(i => i.Id).Should().Contain(id);
        }

        [Fact]
        public void ItemsInsertedWithRawSql_ShouldBeIndexedOnFirstSearch()
        {
            var (db, repo) = NewEmpty();
            Exec(db, "INSERT INTO Items (Code, Barcode, NameAr, NameEn, ActiveIngredient, StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres, WholesalePriceBoxPiastres, CostPriceBoxPiastres, MinStockAlertSmallestUnits, TotalStockSmallestUnits, IsActive) VALUES ('R1','622R1','أموكسيل','Amoxil','أموكسيسيلين',1,10,1000,1000,800,5,0,1);");
            Scalar(db, "SELECT COUNT(*) FROM Items WHERE SearchText IS NULL").Should().Be(1);

            repo.Search("اموكسيل").Should().HaveCount(1);
            repo.Search("اموكسيسيلين").Should().HaveCount(1);
            Scalar(db, "SELECT COUNT(*) FROM Items WHERE SearchText IS NULL").Should().Be(0);
        }

        [Fact]
        public void Migration016_ShouldAddColumn_AndDemoDataShouldBeSearchableWithNormalization()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            Scalar(db, "SELECT COUNT(*) FROM pragma_table_info('Items') WHERE name = 'SearchText'").Should().Be(1);
            Scalar(db, "SELECT COUNT(*) FROM Items WHERE SearchText IS NULL").Should().Be(0);

            var repo = new ItemRepository(db);
            var first = repo.GetAllItems().First();
            // اسم الصنف نفسه بعد "تخريبه" بالتشكيل والتطويل لازم يلاقيه
            string mangled = string.Join("\u0640", first.NameAr.Select(c => c.ToString()));
            repo.Search(first.NameAr.Replace("ة", "ه").Replace("أ", "ا")).Select(i => i.Id).Should().Contain(first.Id);
            repo.Search(mangled).Select(i => i.Id).Should().Contain(first.Id);
        }
    }
}
