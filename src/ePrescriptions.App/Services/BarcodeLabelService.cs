using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Printing;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.Services
{
    public interface IBarcodeLabelService
    {
        bool PrintBarcodeLabel(Item item, ItemBatch? batch, int labelCount, out string errorMessage);
    }

    public class BarcodeLabelService : IBarcodeLabelService
    {
        private readonly SettingsRepository _settingsRepository;

        public BarcodeLabelService(SettingsRepository settingsRepository)
        {
            _settingsRepository = settingsRepository;
        }

        public bool PrintBarcodeLabel(Item item, ItemBatch? batch, int labelCount, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (labelCount <= 0) labelCount = 1;

            try
            {
                var settings = _settingsRepository.GetSettings();
                string printerName = string.IsNullOrWhiteSpace(settings.LabelPrinterName) || settings.LabelPrinterName.Contains("الافتراضية")
                    ? string.Empty
                    : settings.LabelPrinterName;

                // لا اسم شخصي افتراضي: لو اسم الصيدلية فاضي سطر العنوان يتشال (أو يفضل رقم التشغيلة بس)
                string pharmacyName = string.IsNullOrWhiteSpace(settings.PharmacyNameAr) ? string.Empty : settings.PharmacyNameAr;
                // ملصق تشغيلة: الباركود يعرّف التشغيلة (ومنها تاريخ الصلاحية)، وغير ذلك باركود الصنف العادي
                bool isBatchLabel = batch != null && batch.Id > 0;
                string barcodeToPrint = isBatchLabel
                    ? BatchLabelCode.Encode(batch!.Id)
                    : (string.IsNullOrWhiteSpace(item.Barcode) ? item.Code : item.Barcode);
                decimal retailPounds = item.RetailPriceBoxPiastres / 100m;
                string priceText = $"{retailPounds:N2} ج.م";
                string expText = batch != null ? $"صلاحية: {DbDate.Slash(batch.ExpiryDate)}" : "";

                // 1) اسم الصيدلية (ورقم التشغيلة على ملصق التشغيلة)
                string batchPart = isBatchLabel && !string.IsNullOrEmpty(batch!.BatchNumber) ? $"ت: {batch.BatchNumber}" : string.Empty;
                string titleLine = pharmacyName.Length > 0 && batchPart.Length > 0
                    ? $"{pharmacyName} | {batchPart}"
                    : (pharmacyName.Length > 0 ? pharmacyName : batchPart);
                string displayName = item.NameAr.Length > 26 ? item.NameAr.Substring(0, 24) + ".." : item.NameAr;
                string footerText = expText.Length > 0 ? $"{priceText}  |  {expText}" : priceText;

                // التوزيع محسوب على مقاس الملصق مع تقليل ارتفاع الباركود للنصف لمنع خروجه على ورقتين
                var layout = LabelLayout.Compute(settings.LabelWidthMm, settings.LabelHeightMm, wantTitle: titleLine.Length > 0, halfBarcode: true);

                // طباعة مهمة واحدة لكل الملصقات (صفحة لكل ملصق) بدل مهمة طباعة منفصلة لكل ملصق
                using var printDoc = new PrintDocument();
                if (!string.IsNullOrEmpty(printerName))
                {
                    printDoc.PrinterSettings.PrinterName = printerName;
                }

                printDoc.DefaultPageSettings.PaperSize = new PaperSize("CustomLabel", layout.PageWidth, layout.PageHeight);
                printDoc.DefaultPageSettings.Margins = new Margins(LabelLayout.Margin, LabelLayout.Margin, LabelLayout.Margin, LabelLayout.Margin);

                int pagesPrinted = 0;
                printDoc.PrintPage += (s, e) =>
                {
                    var g = e.Graphics;
                    if (g == null) return;

                    bool isCompact = layout.PageHeight < 80;
                    float titlePt = isCompact ? 5.5f : 7f;
                    float itemPt = isCompact ? 6.5f : 8f;
                    float pricePt = isCompact ? 7f : 9f;

                    using var fontTitle = new Font("Tahoma", titlePt, FontStyle.Bold);
                    using var fontItem = new Font("Tahoma", itemPt, FontStyle.Bold);
                    using var fontPrice = new Font("Tahoma", pricePt, FontStyle.Bold);
                    using var brush = new SolidBrush(Color.Black);
                    using var sfCenter = new StringFormat { Alignment = StringAlignment.Center };

                    int totalWidth = e.PageBounds.Width > 0 ? e.PageBounds.Width : layout.PageWidth;
                    int titleH = isCompact ? 8 : LabelLayout.TitleLine;
                    int nameH = isCompact ? 9 : LabelLayout.NameLine;
                    int footerH = isCompact ? 10 : LabelLayout.FooterLine;

                    if (layout.HasTitle)
                    {
                        g.DrawString(titleLine, fontTitle, brush, new RectangleF(0, layout.TitleY, totalWidth, titleH), sfCenter);
                    }

                    // 2) اسم الدواء
                    g.DrawString(displayName, fontItem, brush, new RectangleF(0, layout.NameY, totalWidth, nameH), sfCenter);

                    // 3) رسم الباركود Code128 (Set C للأرقام)
                    try
                    {
                        if (!layout.BarcodeFitsWidth(Code128Encoder.TotalModules(barcodeToPrint)))
                            throw new InvalidOperationException("الباركود أعرض من الملصق");

                        using var barcodeBmp = Code128BarcodeGenerator.GenerateCode128Bitmap(
                            barcodeToPrint, height: layout.BarcodeHeightParam, moduleWidth: 1, includeText: layout.BarcodeIncludeText);
                        int bmpWidthUnits = LabelLayout.PixelsToUnits(barcodeBmp.Width);
                        int bmpX = Math.Max(0, (totalWidth - bmpWidthUnits) / 2);

                        // من غير تنعيم: الأعمدة بتفضل حادة على طابعة 203 DPI
                        var oldInterpolation = g.InterpolationMode;
                        var oldPixelOffset = g.PixelOffsetMode;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(barcodeBmp, new Rectangle(bmpX, layout.BarcodeY, bmpWidthUnits, layout.BarcodeBitmapHeightUnits));
                        g.InterpolationMode = oldInterpolation;
                        g.PixelOffsetMode = oldPixelOffset;
                    }
                    catch (Exception barcodeEx)
                    {
                        Logger.LogInfo($"تعذر رسم الباركود، طُبع النص بدلاً منه: {barcodeEx.Message}", "BarcodeLabelService");
                        g.DrawString(barcodeToPrint, fontPrice, brush, new RectangleF(0, layout.BarcodeY, totalWidth, 18), sfCenter);
                    }

                    // 4) السعر وتاريخ الصلاحية (ثابت أسفل الملصق)
                    g.DrawString(footerText, fontPrice, brush, new RectangleF(0, layout.FooterY, totalWidth, footerH), sfCenter);

                    pagesPrinted++;
                    e.HasMorePages = pagesPrinted < labelCount;
                };

                printDoc.Print();

                Logger.LogInfo($"تمت طباعة {labelCount} ملصق باركود للصنف {item.Code} بنجاح.", "BarcodeLabelService");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Logger.LogException(ex, "BarcodeLabelService.PrintBarcodeLabel");
                return false;
            }
        }
    }
}
