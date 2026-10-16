using ePrescriptions.Core.Financial;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class FinancialCalculationsTests
    {
        [Fact]
        public void Money_FromPounds_ShouldCorrectlyRoundHalfUpToPiastres()
        {
            // Arrange & Act
            var m1 = Money.FromPounds(10.55m);
            var m2 = Money.FromPounds(10.555m); // 1055.5 -> 1056 piastres (AwayFromZero)
            var m3 = Money.FromPounds(10.554m); // 1055.4 -> 1055 piastres

            // Assert
            m1.Piastres.Should().Be(1055);
            m2.Piastres.Should().Be(1056);
            m3.Piastres.Should().Be(1055);
        }

        [Theory]
        [InlineData(10000, 15, 1500)]   // 100.00 EGP -> 15.00 EGP (1500 piastres)
        [InlineData(100, 15, 15)]       // 1.00 EGP -> 0.15 EGP (15 piastres)
        [InlineData(33, 15, 5)]         // (33 * 15 + 50) / 100 = 545 / 100 = 5 piastres
        [InlineData(37, 15, 6)]         // (37 * 15 + 50) / 100 = 605 / 100 = 6 piastres
        [InlineData(2550, 15, 383)]     // 25.50 EGP -> 382.5 + 0.5 = 383 piastres (3.83 EGP)
        public void CalculateVat_ShouldApplyHalfUpRoundingCorrectly(long amountPiastres, int vatPercent, long expectedVatPiastres)
        {
            var vat = Money.CalculateVat(amountPiastres, vatPercent);
            vat.Piastres.Should().Be(expectedVatPiastres);
        }

        [Fact]
        public void Money_AdditionAndSubtraction_ShouldBeExactWithoutFloatingPointErrors()
        {
            var m1 = Money.FromPiastres(1250); // 12.50 EGP
            var m2 = Money.FromPiastres(375);  // 3.75 EGP

            var sum = m1 + m2;
            var diff = m1 - m2;

            sum.Piastres.Should().Be(1625);
            sum.ToFormattedString(false).Should().Be("16.25");
            sum.ToFormattedString(true).Should().Be("16.25 ج.م");

            diff.Piastres.Should().Be(875);
            diff.ToFormattedString(false).Should().Be("8.75");
        }

        [Fact]
        public void ExtractNetFromGross_ShouldCorrectlyExtractBaseBefore15PercentVat()
        {
            // 115.00 EGP inclusive of 15% VAT -> Base = 100.00 EGP (10000 piastres)
            long gross = 11500;
            var net = Money.ExtractNetFromGross(gross, 15);
            var vat = Money.CalculateVat(net.Piastres, 15);

            net.Piastres.Should().Be(10000);
            (net.Piastres + vat.Piastres).Should().Be(gross);
        }
    }
}
