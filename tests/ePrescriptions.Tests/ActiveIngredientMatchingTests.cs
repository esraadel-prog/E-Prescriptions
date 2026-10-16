using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// خطوة 35 (تعديل): البدائل بتعتمد على اسم المادة الفعالة فقط، بغض النظر عن التركيز أو المواد المضافة
    /// </summary>
    public class ActiveIngredientMatchingTests
    {
        [Theory]
        [InlineData("Paracetamol", "paracetamol")]
        [InlineData("Paracetamol 500mg", "paracetamol")]
        [InlineData("Paracetamol 500 mg", "paracetamol")]
        [InlineData("PARACETAMOL 1000mg", "paracetamol")]
        [InlineData("Paracetamol 0.5 g", "paracetamol")]
        [InlineData("  Paracetamol  ", "paracetamol")]
        [InlineData("Paracetamol (Acetaminophen) 500mg", "paracetamol")]
        public void ExtractNames_ShouldStripDoseAndUnits(string input, string expected)
        {
            var names = ActiveIngredientMatcher.ExtractNames(input);
            names.Should().HaveCount(1);
            names.Should().Contain(expected);
        }

        [Theory]
        [InlineData("Paracetamol + Vitamin C", "paracetamol", "vitamin c")]
        [InlineData("Paracetamol 500mg + Caffeine 65mg", "paracetamol", "caffeine")]
        [InlineData("Amoxicillin/Clavulanic Acid 875/125", "amoxicillin", "clavulanic acid")]
        [InlineData("Paracetamol and Vitamin C", "paracetamol", "vitamin c")]
        [InlineData("باراسيتامول + فيتامين ج", "باراسيتامول", "فيتامين ج")]
        public void ExtractNames_ShouldSplitCombinedIngredients(string input, string first, string second)
        {
            var names = ActiveIngredientMatcher.ExtractNames(input);
            names.Should().HaveCount(2);
            names.Should().Contain(first);
            names.Should().Contain(second);
        }

        [Fact]
        public void ExtractNames_ShouldKeepVitaminNumbersThatAreNotDoses()
        {
            var b1 = ActiveIngredientMatcher.ExtractNames("Vitamin B1");
            var b12 = ActiveIngredientMatcher.ExtractNames("Vitamin B12 1000mcg");
            b1.Should().Contain("vitamin b1");
            b12.Should().Contain("vitamin b12");
            b1.Overlaps(b12).Should().BeFalse();
        }

        [Fact]
        public void ExtractNames_EmptyOrNull_ShouldReturnNothing()
        {
            ActiveIngredientMatcher.ExtractNames(null).Should().BeEmpty();
            ActiveIngredientMatcher.ExtractNames("   ").Should().BeEmpty();
            ActiveIngredientMatcher.ExtractNames("500mg").Should().BeEmpty();
            ActiveIngredientMatcher.SharesIngredient("", "").Should().BeFalse();
        }

        [Fact]
        public void GetAlternatives_ParacetamolVariants_ShouldAllBeAlternativesOfEachOther()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            var repo = new ItemRepository(db);

            int Save(string code, string ingredient, int stock = 50) => repo.SaveItem(new Item
            {
                Code = code,
                Barcode = "6280000" + code,
                NameAr = "صنف " + code,
                NameEn = "Item " + code,
                ActiveIngredient = ingredient,
                RetailPriceBoxPiastres = 1000,
                TotalStockSmallestUnits = stock,
                IsActive = true
            });

            int plain = Save("101", "Paracetamol");
            int mg500 = Save("102", "Paracetamol 500mg");
            int mg1000 = Save("103", "Paracetamol 1000mg");
            int withC = Save("104", "Paracetamol + Vitamin C");
            int ibu = Save("105", "Ibuprofen 400mg");
            int vitC = Save("106", "Vitamin C 1000mg");

            // من أي صنف باراسيتامول نوصل لباقي الباراسيتامول (كلهم بدائل لبعض)
            var all = new[] { plain, mg500, mg1000, withC };
            foreach (int id in all)
            {
                var item = repo.GetById(id)!;
                var alts = repo.GetAlternativesByActiveIngredient(item.ActiveIngredient, excludeItemId: id);
                foreach (int other in all.Where(x => x != id))
                    alts.Should().Contain(a => a.Id == other);
                alts.Should().NotContain(a => a.Id == id);
                alts.Should().NotContain(a => a.Id == ibu);
            }

            // الصنف المركّب بيشترك مع فيتامين سي في مادة فبيظهر كبديل ليه (وده سلوك متوقع من القاعدة)
            var forVitC = repo.GetAlternativesByActiveIngredient("Vitamin C 1000mg", excludeItemId: vitC);
            forVitC.Should().Contain(a => a.Id == withC);
            forVitC.Should().NotContain(a => a.Id == plain);
        }
    }
}
