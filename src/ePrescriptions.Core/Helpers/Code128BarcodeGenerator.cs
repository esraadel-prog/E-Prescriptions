using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// مولد باركود Code 128 (مجموعتي B وC) بلغة C# نقية وبدون أي مكتبات خارجية.
    /// يرسم الخطوط والفراغات بدقة مع حساب خانة التحقق (Modulo 103 Checksum).
    /// </summary>
    public static class Code128BarcodeGenerator
    {
        /// <summary>
        /// توليد صورة الباركود بصيغة Bitmap بدقة محددة مع نص الصنف وأرقام الباركود أسفله
        /// </summary>
        public static Bitmap GenerateCode128Bitmap(string text, int height = 80, int moduleWidth = 2, bool includeText = true, int quietZone = 15)
        {
            if (string.IsNullOrEmpty(text)) text = "000000";

            // الترميز في Code128Encoder (Set C للأرقام: EAN-13 بيصغر من 178 لـ 123 وحدة فيتسع في ملصق 50 مم)
            string patternString = Code128Encoder.Pattern(Code128Encoder.Encode(text));

            int totalUnits = 0;
            foreach (char ch in patternString)
            {
                totalUnits += (ch - '0');
            }

            int totalWidth = (totalUnits * moduleWidth) + (quietZone * 2);
            int textHeight = includeText ? 20 : 0;
            int totalHeight = height + textHeight;

            var bmp = new Bitmap(totalWidth, totalHeight, PixelFormat.Format32bppArgb);
            bmp.SetResolution(96f, 96f); // مقاس الصورة على الورق ثابت (LabelLayout بيحسب على 96 DPI)
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                int currentX = quietZone;
                bool isBar = true;

                foreach (char ch in patternString)
                {
                    int width = (ch - '0') * moduleWidth;
                    if (isBar)
                    {
                        g.FillRectangle(Brushes.Black, currentX, 5, width, height - 10);
                    }
                    currentX += width;
                    isBar = !isBar;
                }

                if (includeText)
                {
                    using var font = new Font("Arial", 9, FontStyle.Bold);
                    using var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    var textRect = new RectangleF(0, height - 5, totalWidth, textHeight);
                    g.DrawString(text, font, Brushes.Black, textRect, sf);
                }
            }

            return bmp;
        }
    }
}
