using System;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// توزيع عناصر ملصق الباركود داخل مقاس الملصق (كل الأبعاد بوحدة 1/100 بوصة زي PrintDocument،
    /// وارتفاع الباركود بالبكسل 96 DPI زي الصورة اللي بيولّدها Code128BarcodeGenerator).
    /// التوزيع القديم كان بيحط السطور بأبعاد ثابتة (ملصق 25 مم = 98 وحدة: السطر الأخير بيبدأ عند 92) فسطر
    /// السعر والصلاحية كان بيقع خارج الملصق؛ هنا الباركود هو اللي بياخد المساحة المتبقية بعد السطور الثابتة.
    /// </summary>
    public sealed class LabelLayout
    {
        public const int Margin = 2;
        public const int TitleLine = 12;     // خط 7pt
        public const int NameLine = 13;      // خط 8pt
        public const int FooterLine = 15;    // خط 9pt
        public const int Gap = 1;
        public const int BarcodeTextAreaPx = 20;   // مساحة الأرقام تحت الأعمدة داخل صورة الباركود
        public const int MinBarsPx = 16;
        public const int MaxBarcodeHeightParam = 60;
        public const int BarcodeQuietZonePx = 15;

        public int PageWidth { get; private set; }
        public int PageHeight { get; private set; }
        public bool HasTitle { get; private set; }
        public int TitleY { get; private set; }
        public int NameY { get; private set; }
        public int BarcodeY { get; private set; }
        /// <summary>قيمة بارامتر height في GenerateCode128Bitmap (الارتفاع بدون مساحة الأرقام)</summary>
        public int BarcodeHeightParam { get; private set; }
        public bool BarcodeIncludeText { get; private set; }
        /// <summary>ارتفاع الأعمدة السوداء الفعلي بالبكسل (المولّد بيحجز 5 بكسل فوق وتحت)</summary>
        public int BarsHeightPx => BarcodeHeightParam - 10;
        /// <summary>ارتفاع صورة الباركود الكلي بوحدات الصفحة (1/100 بوصة)</summary>
        public int BarcodeBitmapHeightUnits { get; private set; }
        public int FooterY { get; private set; }
        /// <summary>false = المساحة المتبقية للباركود أقل من حد القراءة (الصفحة بتتحط على الأقل 90 وحدة فمش بتحصل عملياً)</summary>
        public bool Fits { get; private set; }

        public static int MmToHundredths(double mm) => (int)(mm / 25.4 * 100);

        public static int PixelsToUnits(int px96) => (int)Math.Ceiling(px96 * 100.0 / 96.0);

        public static int UnitsToPixels(int units) => (int)Math.Floor(units * 96.0 / 100.0);

        public static LabelLayout Compute(double widthMm, double heightMm, bool wantTitle)
            => Build(widthMm, heightMm, wantTitle, halfBarcode: false);

        public static LabelLayout Compute(double widthMm, double heightMm, bool wantTitle, bool halfBarcode)
            => Build(widthMm, heightMm, wantTitle, halfBarcode);

        private static LabelLayout Build(double widthMm, double heightMm, bool title, bool halfBarcode = false)
        {
            int pageH = heightMm >= 20 ? Math.Max(90, MmToHundredths(heightMm)) : Math.Max(45, MmToHundredths(heightMm));
            bool isCompact = pageH < 80;

            int margin = isCompact ? 1 : Margin;
            int titleLine = isCompact ? 8 : TitleLine;
            int nameLine = isCompact ? 9 : NameLine;
            int footerLine = isCompact ? 10 : FooterLine;
            int gap = isCompact ? 0 : Gap;

            var l = new LabelLayout
            {
                PageWidth = Math.Max(120, MmToHundredths(widthMm)),
                PageHeight = pageH,
                HasTitle = title
            };

            int y = margin;
            if (title) { l.TitleY = y; y += titleLine; }
            l.NameY = y; y += nameLine;
            l.BarcodeY = y;
            l.FooterY = l.PageHeight - margin - footerLine;

            int availUnits = l.FooterY - l.BarcodeY - gap;
            int availPx = UnitsToPixels(Math.Max(0, availUnits));

            int heightParam = availPx - BarcodeTextAreaPx;
            if (halfBarcode && heightParam > MinBarsPx)
            {
                // تقليل ارتفاع الباركود إلى النصف تقريباً لتفادي تجاوزه الملصق وخروجه على ورقتين
                heightParam = Math.Max(MinBarsPx, heightParam / 2);
            }

            if (heightParam >= MinBarsPx && !isCompact)
            {
                l.BarcodeIncludeText = true;
                l.BarcodeHeightParam = Math.Min(heightParam, MaxBarcodeHeightParam);
            }
            else
            {
                l.BarcodeIncludeText = false;
                l.BarcodeHeightParam = Math.Min(Math.Max(availPx, 0), MaxBarcodeHeightParam);
            }

            int bitmapPx = l.BarcodeHeightParam + (l.BarcodeIncludeText ? BarcodeTextAreaPx : 0);
            l.BarcodeBitmapHeightUnits = PixelsToUnits(bitmapPx);
            l.Fits = l.BarsHeightPx >= MinBarsPx - 10
                     && l.BarcodeY + l.BarcodeBitmapHeightUnits + gap <= l.FooterY;
            return l;
        }

        /// <summary>عرض صورة الباركود بوحدات الصفحة لعدد وحدات (modules) معين بعرض 1 بكسل للوحدة</summary>
        public static int BarcodeWidthUnits(int totalModules, int moduleWidthPx = 1, int quietZonePx = BarcodeQuietZonePx)
            => PixelsToUnits(totalModules * moduleWidthPx + quietZonePx * 2);

        public bool BarcodeFitsWidth(int totalModules, int moduleWidthPx = 1, int quietZonePx = BarcodeQuietZonePx)
            => BarcodeWidthUnits(totalModules, moduleWidthPx, quietZonePx) <= PageWidth - Margin * 2;
    }
}
