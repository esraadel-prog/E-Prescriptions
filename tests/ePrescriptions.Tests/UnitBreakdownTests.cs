using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class UnitBreakdownTests
    {
        [Fact]
        public void CalculateSmallestUnits_ShouldMultiplyCorrectlyForBoxAndStrip()
        {
            // علبة بها 2 شريط وكل شريط به 12 قرص (24 قرص للعلبة)
            int stripsPerBox = 2;
            int piecesPerStrip = 12;

            // بيع 3 علب -> 3 * 24 = 72 قرص
            int smallestFromBoxes = UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel.Box, 3, stripsPerBox, piecesPerStrip);
            smallestFromBoxes.Should().Be(72);

            // بيع 5 أشرطة -> 5 * 12 = 60 قرص
            int smallestFromStrips = UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel.Strip, 5, stripsPerBox, piecesPerStrip);
            smallestFromStrips.Should().Be(60);

            // بيع 7 أقراص -> 7 أقراص
            int smallestFromPieces = UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel.Piece, 7, stripsPerBox, piecesPerStrip);
            smallestFromPieces.Should().Be(7);
        }

        [Fact]
        public void BreakdownStock_ShouldDecomposeSmallestUnitsIntoBoxesStripsAndPieces()
        {
            // علبة بها 2 شريط وكل شريط به 10 أقراص (20 قرص للعلبة)
            int stripsPerBox = 2;
            int piecesPerStrip = 10;

            // رصيد 55 قرص:
            // 55 / 20 = 2 علب (40 قرص)
            // المتبقي 15 قرص: 15 / 10 = 1 شريط (10 أقراص)
            // المتبقي 5 أقراص
            var (boxes, strips, pieces) = UnitBreakdownHelper.BreakdownStock(55, stripsPerBox, piecesPerStrip);

            boxes.Should().Be(2);
            strips.Should().Be(1);
            pieces.Should().Be(5);
        }

        [Fact]
        public void FormatStockDetailed_ShouldOutputClearArabicString()
        {
            int stripsPerBox = 3;
            int piecesPerStrip = 10; // 30 قرص للعلبة

            // 65 قرص -> 2 علبة و 0 شريط و 5 قرص
            string formatted = UnitBreakdownHelper.FormatStockDetailed(65, stripsPerBox, piecesPerStrip);
            formatted.Should().Contain("2 علبة");
            formatted.Should().Contain("5 قرص");
        }

        [Fact]
        public void CalculateSmallestUnits_ShouldSupportAmpouleSachetAndUnit()
        {
            // أمبول وكيس ووحدة تحسب كأصغر وحدة مباشرة
            UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel.Ampoule, 5, 2, 10).Should().Be(5);
            UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel.Sachet, 8, 1, 10).Should().Be(8);
            UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel.Unit, 12, 1, 1).Should().Be(12);

            UnitBreakdownHelper.UnitLevelName(UnitLevel.Ampoule).Should().Be("أمبول");
            UnitBreakdownHelper.UnitLevelName(UnitLevel.Sachet).Should().Be("كيس");
            UnitBreakdownHelper.UnitLevelName(UnitLevel.Unit).Should().Be("وحدة");
        }
    }
}
