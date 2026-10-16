using System;
using System.Globalization;
using System.IO;
using System.Linq;
using ePrescriptions.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class EgyptQrTests
    {
        [Fact]
        public void BuildSimpleQrText_ShouldContainInvoiceNumberTotalAndTaxNumber()
        {
            string text = EgyptQrHelper.BuildSimpleQrText("INV-2026-00001", new DateTime(2026, 10, 5, 14, 30, 0), 11550, "123456789", "صيدلية");

            text.Should().Contain("INV-2026-00001");
            text.Should().Contain("115.50");
            text.Should().Contain("123456789");
        }

        [Theory]
        [InlineData("123456789", 9, true)]
        [InlineData("12345678", 9, false)]
        [InlineData("12345678A", 9, false)]
        [InlineData("", 9, false)]
        [InlineData("300987654300003", 15, true)]
        public void ValidateTaxRegistrationNumber_ShouldMatchDigitsAndLength(string number, int length, bool expected)
        {
            EgyptQrHelper.ValidateTaxRegistrationNumber(number, length).Should().Be(expected);
        }

        // ===== خطوة 43: صيغة QR ثابتة الثقافة، وما فيش ادعاء اعتماد/ZATCA/ETA في أي نص ظاهر للمستخدم =====

        /// <summary>ثقافة بتحاكي ar-EG على ويندوز: فاصل عشري "٫" وفاصل آلاف "٬" (بتشتغل حتى لو الجهاز بدون ICU)</summary>
        private static CultureInfo ArabicSeparatorsCulture()
        {
            var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            c.NumberFormat.NumberDecimalSeparator = "\u066B";
            c.NumberFormat.NumberGroupSeparator = "\u066C";
            return c;
        }

        [Fact]
        public void QrText_ShouldFormatTotalIdenticallyOnAnyCulture_DotDecimalNoGroupingTwoDigits()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = ArabicSeparatorsCulture();
                string text = EgyptQrHelper.BuildSimpleQrText("INV-1", new DateTime(2026, 10, 5, 14, 30, 0), 123450, "123456789");

                text.Should().Contain("1234.50 ج.م");
                text.Contains("\u066B").Should().BeFalse();
                text.Contains("\u066C").Should().BeFalse();
                text.Contains(",").Should().BeFalse();

                // نفس النص بالظبط على الثقافة الثابتة
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                EgyptQrHelper.BuildSimpleQrText("INV-1", new DateTime(2026, 10, 5, 14, 30, 0), 123450, "123456789").Should().Be(text);
            }
            finally
            {
                CultureInfo.CurrentCulture = old;
            }
        }

        [Fact]
        public void QrText_ShouldKeepTwoDecimalsForWholeAndSmallAmounts()
        {
            EgyptQrHelper.BuildSimpleQrText("A", new DateTime(2026, 1, 1), 10000, "1").Should().Contain("100.00 ج.م");
            EgyptQrHelper.BuildSimpleQrText("A", new DateTime(2026, 1, 1), 5, "1").Should().Contain("0.05 ج.م");
        }

        [Fact]
        public void UserFacingTexts_ShouldNotClaimTaxAuthorityCompliance_ForTheSimpleQr()
        {
            string root = RepoRoot();
            string[] files =
            {
                "README.md",
                Path.Combine("src", "ePrescriptions.App", "ViewModels", "HelpViewModel.cs"),
                Path.Combine("src", "ePrescriptions.App", "Views", "SettingsView.xaml"),
                Path.Combine("installer", "e_prescriptions_installer.iss"),
            };
            string[] forbidden = { "ZATCA QR", "QR Code المعتمد", "القياسي للفواتير الإلكترونية", "ويدعم الربط الإلكتروني" };

            foreach (string rel in files)
            {
                string text = File.ReadAllText(Path.Combine(root, rel));
                foreach (string bad in forbidden)
                {
                    text.Contains(bad).Should().BeFalse($"{rel} فيه عبارة بتوحي باعتماد/تكامل ضريبي: {bad}");
                }
            }
        }

        [Fact]
        public void QrHelper_Documentation_ShouldStateItIsNotEtaOrZatcaFormat()
        {
            string src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "ePrescriptions.Core", "Helpers", "EgyptQrHelper.cs"));
            src.Contains("ETA").Should().BeTrue();
            src.Contains("ZATCA").Should().BeTrue();
            src.Contains("مش").Should().BeTrue();
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "e_prescriptions.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("e_prescriptions.sln");
        }
    }
}
