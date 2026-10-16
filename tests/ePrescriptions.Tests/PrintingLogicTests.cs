using System;
using System.Collections.Generic;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Enums;
using ePrescriptions.Domain.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// خطوة 44 (جزء أ): منطق الطباعة اللي ينفع يتختبر من غير طابعة — أمر فتح الدرج (ESC/POS)، ترميز Code128،
    /// توزيع الملصق جوه مقاسه، وعنوان الإيصال. رسم GDI نفسه والطابعة الفعلية لسه محتاجين تجربة على Windows.
    /// </summary>
    public class PrintingLogicTests
    {
        // ---------- أمر ESC/POS ----------

        [Fact]
        public void EscPos_EmptyText_ShouldUseStandardDrawerPulse()
        {
            EscPosCommand.TryParse("", out var bytes, out var err).Should().BeTrue();
            err.Should().BeEmpty();
            bytes.Should().Equal(27, 112, 0, 25, 250);
            EscPosCommand.TryParse(null, out var bytes2, out _).Should().BeTrue();
            bytes2.Should().Equal(27, 112, 0, 25, 250);
        }

        [Fact]
        public void EscPos_DefaultPulse_ShouldNotBeMutableThroughResult()
        {
            EscPosCommand.TryParse("", out var bytes, out _);
            bytes[0] = 99;
            EscPosCommand.DefaultDrawerPulse[0].Should().Be(27);
        }

        [Theory]
        [InlineData("27,112,0,25,250")]
        [InlineData("27 112 0 25 250")]
        [InlineData("27, 112, 0, 25, 250")]
        [InlineData("27;112;0;25;250")]
        [InlineData("0x1B 0x70 0x00 0x19 0xFA")]
        [InlineData("0x1b,0x70,0,25,0xfa")]
        [InlineData("٢٧،١١٢،٠،٢٥،٢٥٠")]
        public void EscPos_SupportedFormats_ShouldAllGiveSameBytes(string text)
        {
            EscPosCommand.TryParse(text, out var bytes, out var err).Should().BeTrue(err);
            bytes.Should().Equal(27, 112, 0, 25, 250);
        }

        [Theory]
        [InlineData("1B 70 00 19 FA", "1B")]        // hex بدون 0x كان بيتحول بصمت لـ 70,0,19 ويتبعت للطابعة
        [InlineData("27,112,0,25,300", "300")]       // خارج 0-255 كان بيتشال والباقي يتبعت ناقص
        [InlineData("27,112,-1,25,250", "-1")]
        [InlineData("27,112,x,25,250", "x")]
        [InlineData("27.5,112", "27.5")]
        public void EscPos_InvalidToken_ShouldRejectWholeCommand_AndNameTheToken(string text, string badToken)
        {
            EscPosCommand.TryParse(text, out var bytes, out var err).Should().BeFalse();
            bytes.Should().BeEmpty();
            err.Should().Contain(badToken);
        }

        [Fact]
        public void EscPos_TooLong_ShouldBeRejected()
        {
            string text = string.Join(",", Enumerable.Repeat("1", EscPosCommand.MaxLength + 1));
            EscPosCommand.TryParse(text, out _, out var err).Should().BeFalse();
            err.Should().NotBeEmpty();
        }

        // ---------- Code128 ----------

        [Fact]
        public void Code128Patterns_ShouldBeAValidTable()
        {
            Code128Encoder.Patterns.Length.Should().Be(107);
            for (int v = 0; v <= 105; v++)
            {
                var p = Code128Encoder.Patterns[v];
                p.Length.Should().Be(6, $"الرمز {v}");
                p.Sum(ch => ch - '0').Should().Be(11, $"الرمز {v} لازم يكون 11 وحدة");
            }
            Code128Encoder.Patterns[106].Sum(ch => ch - '0').Should().Be(13);
            Code128Encoder.Patterns.Distinct().Count().Should().Be(107, "أي تكرار معناه خطأ كتابة في الجدول");
            // قيم معروفة من المواصفة
            Code128Encoder.Patterns[0].Should().Be("212222");
            Code128Encoder.Patterns[Code128Encoder.StartB].Should().Be("211214");
            Code128Encoder.Patterns[Code128Encoder.StartC].Should().Be("211232");
            Code128Encoder.Patterns[Code128Encoder.Stop].Should().Be("2331112");
        }

        [Fact]
        public void Code128_KnownVectors_ShouldMatchSpec()
        {
            // Set C: 105 + 12*1 + 34*2 = 185 → 185 % 103 = 82
            Code128Encoder.Encode("1234").Should().Equal(105, 12, 34, 82, 106);
            // Set B: "Hi" → 104 + 40*1 + 73*2 = 290 → 290 % 103 = 84
            Code128Encoder.Encode("Hi").Should().Equal(104, 40, 73, 84, 106);
            // 3 أرقام (أقل من 4) تفضل Set B
            Code128Encoder.Encode("123")[0].Should().Be(Code128Encoder.StartB);
        }

        [Theory]
        [InlineData("9900000042")]        // ملصق تشغيلة (10 رقم)
        [InlineData("6221031234567")]     // EAN-13 فردي: Set C ثم B للرقم الأخير
        [InlineData("12345678")]
        [InlineData("12345")]
        [InlineData("1234")]
        [InlineData("123")]
        [InlineData("AB-12")]
        [InlineData("PARA500")]
        [InlineData("A1234567")]
        public void Code128_EncodeThenDecode_ShouldRoundTrip(string text)
        {
            var codes = Code128Encoder.Encode(text);
            Code128Encoder.TryDecode(Code128Encoder.Pattern(codes), out var decoded).Should().BeTrue();
            decoded.Should().Be(text);
        }

        [Fact]
        public void Code128_Decode_ShouldRejectCorruptedChecksum()
        {
            var codes = Code128Encoder.Encode("9900000042");
            codes[codes.Count - 2] = (codes[codes.Count - 2] + 1) % 103;
            Code128Encoder.TryDecode(Code128Encoder.Pattern(codes), out _).Should().BeFalse();
        }

        [Fact]
        public void Code128_NonAsciiCharacters_ShouldBeEncodedAsSpace_NotCrash()
        {
            var codes = Code128Encoder.Encode("AB\u0627\u0628");
            codes.Skip(1).Take(4).ToList().Should().Equal(33, 34, 0, 0);
        }

        [Fact]
        public void Code128_DigitsOnly_ShouldBeMuchNarrowerThanSetBOnly()
        {
            Code128Encoder.TotalModules("9900000042").Should().Be(90);
            // EAN-13: 11 (بداية) + 66 (6 أزواج) + 11 (تحويل B) + 11 (رقم) + 11 (checksum) + 13 (نهاية)
            Code128Encoder.TotalModules("6221031234567").Should().Be(123);
        }

        // ---------- توزيع الملصق ----------

        [Theory]
        [InlineData(50.0, 25.0)]
        [InlineData(40.0, 30.0)]
        [InlineData(58.0, 40.0)]
        [InlineData(50.0, 30.0)]
        [InlineData(38.0, 25.0)]
        [InlineData(60.0, 40.0)]
        [InlineData(30.0, 20.0)]
        public void LabelLayout_EveryElement_ShouldStayInsideTheLabel_AndNotOverlap(double w, double h)
        {
            var l = LabelLayout.Compute(w, h, wantTitle: true);

            l.Fits.Should().BeTrue();
            l.BarsHeightPx.Should().BeGreaterThanOrEqualTo(LabelLayout.MinBarsPx);
            if (l.HasTitle)
                l.TitleY.Should().BeGreaterThanOrEqualTo(LabelLayout.Margin);
            int nameBottom = l.NameY + LabelLayout.NameLine;
            nameBottom.Should().BeLessThanOrEqualTo(l.BarcodeY);
            (l.BarcodeY + l.BarcodeBitmapHeightUnits).Should().BeLessThan(l.FooterY, "الباركود ما يغطيش سطر السعر");
            (l.FooterY + LabelLayout.FooterLine).Should().BeLessThanOrEqualTo(l.PageHeight - LabelLayout.Margin, "سطر السعر والصلاحية جوه الملصق");
        }

        [Fact]
        public void LabelLayout_Default50x25_ShouldKeepPriceLineAndGiveBarcodeTheRest()
        {
            var l = LabelLayout.Compute(50, 25, wantTitle: true);
            l.PageHeight.Should().Be(98);
            l.HasTitle.Should().BeTrue();
            l.BarcodeIncludeText.Should().BeTrue();
            l.BarcodeHeightParam.Should().Be(30);
            l.FooterY.Should().Be(81);
        }

        [Fact]
        public void LabelLayout_HalfBarcode_ShouldReduceHeightToPreventDoublePageFeed()
        {
            var l = LabelLayout.Compute(50, 25, wantTitle: true, halfBarcode: true);
            l.PageHeight.Should().Be(98);
            l.HasTitle.Should().BeTrue();
            l.BarcodeHeightParam.Should().BeLessThan(30);
            l.BarcodeHeightParam.Should().Be(16);
            l.Fits.Should().BeTrue();
        }

        [Fact]
        public void LabelLayout_OldFixedLayout_WasOverflowingThe25mmLabel()
        {
            // القديم: y = 2 + 14 (عنوان) + 16 (اسم) + باركود 36+20 بكسل + 2، وبعدين سطر السعر بارتفاع 18
            int oldFooterBottom = 2 + 14 + 16 + LabelLayout.PixelsToUnits(36 + 20) + 2 + 18;
            oldFooterBottom.Should().BeGreaterThan(LabelLayout.Compute(50, 25, true).PageHeight);
        }

        [Fact]
        public void LabelLayout_ShortLabel_ShouldDropDigitsUnderBars_ButKeepBarsReadableAndPriceLine()
        {
            // الصفحة بتتحط على الأقل 90 وحدة (زي PaperSize في الخدمة) فملصق 20 مم بيتحسب على 90
            var l = LabelLayout.Compute(50, 20, wantTitle: true);
            l.PageHeight.Should().Be(90);
            l.Fits.Should().BeTrue();
            l.BarcodeIncludeText.Should().BeFalse();
            l.BarsHeightPx.Should().BeGreaterThanOrEqualTo(LabelLayout.MinBarsPx);
            (l.FooterY + LabelLayout.FooterLine).Should().BeLessThanOrEqualTo(l.PageHeight - LabelLayout.Margin);
        }

        [Fact]
        public void LabelLayout_BarcodeWidth_ShouldFitOnly_WhenUsingSetC()
        {
            var l = LabelLayout.Compute(50, 25, true);
            l.BarcodeFitsWidth(Code128Encoder.TotalModules("9900000042")).Should().BeTrue();
            l.BarcodeFitsWidth(Code128Encoder.TotalModules("6221031234567")).Should().BeTrue();
            // لو EAN-13 اترمّز Set B كامل (178 وحدة) كان عرضه أكبر من ملصق 50 مم فيتقص طرف الباركود
            l.BarcodeFitsWidth(178).Should().BeFalse();
        }

        [Fact]
        public void LabelLayout_MmConversion_ShouldMatchPrintDocumentUnits()
        {
            LabelLayout.MmToHundredths(25.4).Should().Be(100);
            LabelLayout.MmToHundredths(50).Should().Be(196);
            LabelLayout.MmToHundredths(80).Should().Be(314);
        }

        // ---------- عنوان الإيصال ----------

        [Theory]
        [InlineData(InvoiceType.Sales, PaymentMethod.Cash, "فاتورة مبيعات نقدية")]
        [InlineData(InvoiceType.Sales, PaymentMethod.OnAccount, "فاتورة مبيعات آجلة")]
        [InlineData(InvoiceType.Sales, PaymentMethod.Insurance, "فاتورة مبيعات تأمين")]
        [InlineData(InvoiceType.Sales, PaymentMethod.Network, "فاتورة مبيعات (شبكة / بطاقة)")]
        [InlineData(InvoiceType.Sales, PaymentMethod.Mixed, "فاتورة مبيعات")]
        [InlineData(InvoiceType.Return, PaymentMethod.Cash, "إشعار دائن (مرتجع مبيعات)")]
        [InlineData(InvoiceType.Return, PaymentMethod.OnAccount, "إشعار دائن (مرتجع مبيعات)")]
        public void ReceiptTitle_ShouldFollowTypeAndPaymentMethod(InvoiceType type, PaymentMethod method, string expected)
        {
            ReceiptText.InvoiceTitle(type, method).Should().Be(expected);
        }
    }
}
