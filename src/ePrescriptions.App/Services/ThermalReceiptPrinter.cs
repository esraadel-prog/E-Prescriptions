using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Printing;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using ePrescriptions.Domain.Helpers;
using QRCoder;

namespace ePrescriptions.App.Services
{
    /// <summary>
    /// محرك طباعة الإيصالات الحرارية (58 مم / 80 مم) المتوافق مع طابعات إيصالات نقاط البيع في مصر
    /// يدعم الاتجاه العربي من اليمين لليسار، رمز الاستجابة السريع البسيط (اختياري حسب الإعدادات)، والعملة بالجنيه المصري (ج.م).
    /// </summary>
    public static class ThermalReceiptPrinter
    {
        public static bool PrintReceipt(Invoice invoice, PharmacySettings settings, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                using var printDoc = new PrintDocument();
                string printerName = string.IsNullOrWhiteSpace(settings.ReceiptPrinterName)
                    ? printDoc.PrinterSettings.PrinterName
                    : settings.ReceiptPrinterName;

                printDoc.PrinterSettings.PrinterName = printerName;
                if (!printDoc.PrinterSettings.IsValid)
                {
                    errorMessage = $"طابعة الإيصالات المحددة ({printerName}) غير متوفرة أو غير متصلة.";
                    return false;
                }

                int paperWidthHundredths = (int)Math.Round(settings.ReceiptPaperWidthMm * 100.0 / 25.4);
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", paperWidthHundredths, 2000);
                printDoc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 20);

                // هامش أمان جانبي أيمن (38 وحدة ~ 9.6 مم) لمنع قص أول حرفين من اليمين في الطابعات الحرارية ذات الرأس 72 مم
                float marginLeft = settings.ReceiptPaperWidthMm <= 58 ? 12f : 18f;
                float marginRight = settings.ReceiptPaperWidthMm <= 58 ? 28f : 38f;
                float marginX = marginLeft;
                float printWidth = Math.Max(100f, paperWidthHundredths - marginLeft - marginRight);

                printDoc.PrintPage += (sender, e) =>
                {
                    if (e.Graphics == null) return;
                    var g = e.Graphics;

                    float y = 10f;

                    using var fontTitle = new Font("Arial", 11, FontStyle.Bold);
                    using var fontSubTitle = new Font("Arial", 8.5f, FontStyle.Regular);
                    using var fontRegular = new Font("Arial", 8f, FontStyle.Regular);
                    using var fontBold = new Font("Arial", 8f, FontStyle.Bold);
                    using var fontLargeBold = new Font("Arial", 12f, FontStyle.Bold);

                    using var sfCenter = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.DirectionRightToLeft
                    };

                    using var sfRtl = new StringFormat
                    {
                        Alignment = StringAlignment.Near,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.DirectionRightToLeft
                    };

                    using var sfLtr = new StringFormat
                    {
                        Alignment = StringAlignment.Far,
                        LineAlignment = StringAlignment.Center
                    };

                    // 1) اسم الصيدلية والترويسة
                    g.DrawString(settings.PharmacyNameAr, fontTitle, Brushes.Black, new RectangleF(marginX, y, printWidth, 20), sfCenter);
                    y += 20;

                    if (!string.IsNullOrWhiteSpace(settings.PharmacyNameEn))
                    {
                        g.DrawString(settings.PharmacyNameEn, fontSubTitle, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfCenter);
                        y += 16;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.ResponsiblePharmacist))
                    {
                        g.DrawString($"الصيدلي المسؤول: {settings.ResponsiblePharmacist}", fontSubTitle, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfCenter);
                        y += 16;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.Address))
                    {
                        g.DrawString(settings.Address, fontSubTitle, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfCenter);
                        y += 16;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.TaxNumber))
                    {
                        g.DrawString($"رقم التسجيل الضريبي: {settings.TaxNumber}", fontSubTitle, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfCenter);
                        y += 16;
                    }

                    if (!string.IsNullOrWhiteSpace(settings.CommercialRegister))
                    {
                        g.DrawString($"س.ت: {settings.CommercialRegister}", fontSubTitle, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfCenter);
                        y += 16;
                    }

                    y += 4;
                    DrawDottedLine(g, y, marginX, marginX + printWidth);
                    y += 8;

                    // 2) بيانات الفاتورة
                    string invoiceTypeStr = ReceiptText.InvoiceTitle(invoice.Type, invoice.PaymentMethod);
                    g.DrawString(invoiceTypeStr, fontBold, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfCenter);
                    y += 18;

                    g.DrawString($"رقم الفاتورة: {invoice.InvoiceNumber}", fontBold, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfRtl);
                    y += 16;

                    g.DrawString($"التاريخ: {DbDate.SlashMinutes(invoice.Date)}", fontRegular, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfRtl);
                    y += 16;

                    if (!string.IsNullOrWhiteSpace(invoice.CustomerName) && invoice.CustomerName != "عميل نقدي")
                    {
                        g.DrawString($"العميل: {invoice.CustomerName}", fontRegular, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfRtl);
                        y += 16;
                    }

                    y += 4;
                    DrawDottedLine(g, y, marginX, marginX + printWidth);
                    y += 8;

                    // 3) جدول الأصناف
                    g.DrawString("الصنف", fontBold, Brushes.Black, new RectangleF(marginX, y, printWidth * 0.45f, 16), sfRtl);
                    g.DrawString("الكمية", fontBold, Brushes.Black, new RectangleF(marginX + printWidth * 0.45f, y, printWidth * 0.20f, 16), sfCenter);
                    g.DrawString("السعر (ج.م)", fontBold, Brushes.Black, new RectangleF(marginX + printWidth * 0.65f, y, printWidth * 0.35f, 16), sfLtr);
                    y += 18;

                    foreach (var item in invoice.Items)
                    {
                        // اسم الصنف الطويل بيتلف على أكثر من سطر
                        float nameHeight = Math.Max(16f, g.MeasureString(item.ItemNameAr, fontBold, (int)printWidth, sfRtl).Height + 2f);
                        g.DrawString(item.ItemNameAr, fontBold, Brushes.Black, new RectangleF(marginX, y, printWidth, nameHeight), sfRtl);
                        y += nameHeight;

                        string qtyDetail = $"{item.QuantitySold} x {(item.UnitPricePiastres / 100.0):N2}";
                        g.DrawString(qtyDetail, fontRegular, Brushes.DarkSlateGray, new RectangleF(marginX, y, printWidth * 0.60f, 14), sfRtl);
                        g.DrawString($"{(item.TotalPiastres / 100.0):N2} ج.م", fontBold, Brushes.Black, new RectangleF(marginX + printWidth * 0.60f, y, printWidth * 0.40f, 14), sfLtr);
                        y += 16;
                    }

                    y += 4;
                    DrawDottedLine(g, y, marginX, marginX + printWidth);
                    y += 8;

                    // 4) المجاميع المالية
                    g.DrawString($"المجموع قبل الضريبة: {(invoice.SubtotalPiastres / 100.0):N2} ج.م", fontRegular, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfRtl);
                    y += 16;

                    if (invoice.DiscountPiastres > 0)
                    {
                        g.DrawString($"إجمالي الخصم: {(invoice.DiscountPiastres / 100.0):N2} ج.م", fontRegular, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfRtl);
                        y += 16;
                    }

                    g.DrawString($"ضريبة القيمة المضافة: {(invoice.VatPiastres / 100.0):N2} ج.م", fontRegular, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfRtl);
                    y += 18;

                    // الإجمالي النهائي
                    g.DrawString($"صافي المستحق: {(invoice.TotalPiastres / 100.0):N2} ج.م", fontLargeBold, Brushes.Black, new RectangleF(marginX, y, printWidth, 24), sfCenter);
                    y += 24;

                    g.DrawString($"المدفوع: {(invoice.PaidPiastres / 100.0):N2} ج.م   |   المتبقي: {(invoice.RemainingPiastres / 100.0):N2} ج.م", fontRegular, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfCenter);
                    y += 20;

                    // تفصيل الدفع المقسّم: سطر لكل طريقة بمبلغها
                    if (invoice.IsSplitPayment)
                    {
                        foreach (var part in invoice.Payments)
                        {
                            g.DrawString($"{InvoicePayment.MethodName(part.Method)}: {(part.AmountPiastres / 100.0):N2} ج.م", fontRegular, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfRtl);
                            y += 16;
                        }
                        y += 4;
                    }

                    // 5) رمز الاستجابة السريع البسيط (إن فُعّل من الإعدادات)
                    if (settings.EnableQrCode)
                    {
                        try
                        {
                            string qrText = !string.IsNullOrEmpty(invoice.QrCodeText)
                                ? invoice.QrCodeText
                                : EgyptQrHelper.BuildSimpleQrText(invoice.InvoiceNumber, invoice.Date, invoice.TotalPiastres, settings.TaxNumber);

                            using var qrGen = new QRCodeGenerator();
                            using var qrData = qrGen.CreateQrCode(qrText, QRCodeGenerator.ECCLevel.M);
                            using var qr = new QRCode(qrData);
                            using var qrBmp = qr.GetGraphic(3);

                            float qrSize = 90f;
                            float qrX = marginX + (printWidth - qrSize) / 2f;
                            g.DrawImage(qrBmp, qrX, y, qrSize, qrSize);
                            y += qrSize + 6;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException(ex, "ThermalReceiptPrinter (طباعة رمز QR)");
                        }
                    }

                    // تذييل الإيصال
                    g.DrawString("شكراً لزيارتكم - نتمنى لكم دوام الصحة والعافية", fontSubTitle, Brushes.Black, new RectangleF(marginX, y, printWidth, 16), sfCenter);
                    y += 18;
                    g.DrawString(AppBranding.ProductByline, fontRegular, Brushes.Gray, new RectangleF(marginX, y, printWidth, 14), sfCenter);
                    y += 14;
                    g.DrawString(AppBranding.Copyright, fontRegular, Brushes.Gray, new RectangleF(marginX, y, printWidth, 14), sfCenter);

                    e.HasMorePages = false;
                };

                printDoc.Print();

                // فتح درج النقدية إذا كان مفعلاً وكانت الفاتورة نقدية
                if (settings.EnableCashDrawerOnPrint && (invoice.PaymentMethod == Domain.Enums.PaymentMethod.Cash
                    || invoice.Payments.Any(p => p.Method == Domain.Enums.PaymentMethod.Cash)))
                {
                    if (!OpenCashDrawer(settings, out string drawerError))
                        Logger.LogInfo($"تعذر فتح درج النقدية بعد الطباعة: {drawerError}", "ThermalReceiptPrinter");
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"حدث خطأ أثناء الطباعة: {ex.Message}";
                return false;
            }
        }

        public static bool OpenCashDrawer(PharmacySettings settings, out string errorMessage)
        {
            return OpenCashDrawer(settings.ReceiptPrinterName, settings.CashDrawerEscPosBytes, out errorMessage);
        }

        public static bool OpenCashDrawer(string printerName, string escPosBytesStr, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(printerName))
                {
                    using var defaultDoc = new PrintDocument();
                    printerName = defaultDoc.PrinterSettings.PrinterName;
                }
                if (!EscPosCommand.TryParse(escPosBytesStr, out byte[] pulseBytes, out errorMessage))
                    return false;
                return RawPrinterHelper.SendBytesToPrinter(printerName, pulseBytes, out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static bool PrintTestTicket(string printerName, string pharmacyName, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                using var printDoc = new PrintDocument();
                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    printDoc.PrinterSettings.PrinterName = printerName;
                }
                if (!printDoc.PrinterSettings.IsValid)
                {
                    errorMessage = $"الطابعة المحددة ({printerName}) غير متوفرة.";
                    return false;
                }

                printDoc.PrintPage += (s, e) =>
                {
                    if (e.Graphics == null) return;
                    using var f = new Font("Arial", 10, FontStyle.Bold);
                    e.Graphics.DrawString($"اختبار نجاح الاتصال بطابعة الكاشير\n{pharmacyName}\n{DbDate.Full(DateTime.Now)}", f, Brushes.Black, new PointF(10, 10));
                };

                printDoc.Print();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static bool PrintShiftReport(ShiftClosureReport report, PharmacySettings settings, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                using var printDoc = new PrintDocument();
                string printerName = string.IsNullOrWhiteSpace(settings.ReceiptPrinterName)
                    ? printDoc.PrinterSettings.PrinterName
                    : settings.ReceiptPrinterName;

                printDoc.PrinterSettings.PrinterName = printerName;
                if (!printDoc.PrinterSettings.IsValid)
                {
                    errorMessage = $"طابعة الإيصالات المحددة ({printerName}) غير متوفرة أو غير متصلة.";
                    return false;
                }

                int paperWidthHundredths = (int)Math.Round(settings.ReceiptPaperWidthMm * 100.0 / 25.4);
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("ShiftReport", paperWidthHundredths, 2500);
                printDoc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 20);

                printDoc.PrintPage += (sender, e) =>
                {
                    if (e.Graphics == null) return;
                    var g = e.Graphics;

                    float marginLeft = settings.ReceiptPaperWidthMm <= 58 ? 12f : 18f;
                    float marginRight = settings.ReceiptPaperWidthMm <= 58 ? 28f : 38f;
                    float printWidth = Math.Max(100f, paperWidthHundredths - marginLeft - marginRight);
                    float y = 10f;

                    using var fontTitle = new Font("Arial", 11, FontStyle.Bold);
                    using var fontSubTitle = new Font("Arial", 8.5f, FontStyle.Regular);
                    using var fontRegular = new Font("Arial", 8f, FontStyle.Regular);
                    using var fontBold = new Font("Arial", 8f, FontStyle.Bold);
                    using var fontLargeBold = new Font("Arial", 11f, FontStyle.Bold);

                    using var sfCenter = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.DirectionRightToLeft
                    };
                    using var sfRtl = new StringFormat
                    {
                        Alignment = StringAlignment.Near,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.DirectionRightToLeft
                    };
                    using var sfLtr = new StringFormat
                    {
                        Alignment = StringAlignment.Far,
                        LineAlignment = StringAlignment.Center
                    };

                    g.DrawString(settings.PharmacyNameAr, fontTitle, Brushes.Black, new RectangleF(marginLeft, y, printWidth, 20), sfCenter);
                    y += 20;

                    g.DrawString("تقرير إغلاق الوردية وتسليم الصندوق", fontBold, Brushes.Black, new RectangleF(marginLeft, y, printWidth, 18), sfCenter);
                    y += 20;

                    DrawDottedLine(g, y, marginLeft, marginLeft + printWidth);
                    y += 6;

                    void DrawRow(string label, string val, Font? fVal = null)
                    {
                        g.DrawString(label, fontRegular, Brushes.Black, new RectangleF(marginLeft, y, printWidth * 0.55f, 16), sfRtl);
                        g.DrawString(val, fVal ?? fontRegular, Brushes.Black, new RectangleF(marginLeft + printWidth * 0.55f, y, printWidth * 0.45f, 16), sfLtr);
                        y += 16;
                    }

                    DrawRow("رقم الوردية:", $"#{report.ShiftId}", fontBold);
                    DrawRow("الكاشير:", report.CashierName, fontBold);
                    DrawRow("وقت البدء:", DbDate.Full(report.StartTime));
                    if (report.EndTime.HasValue)
                        DrawRow("وقت الإغلاق:", DbDate.Full(report.EndTime.Value));
                    DrawRow("حالة الوردية:", report.Status == ShiftStatus.Closed ? "مغلقة" : "مفتوحة");

                    DrawDottedLine(g, y, marginLeft, marginLeft + printWidth);
                    y += 6;

                    g.DrawString("تفصيل المبيعات وطرق الدفع", fontBold, Brushes.Black, new RectangleF(marginLeft, y, printWidth, 16), sfRtl);
                    y += 18;

                    DrawRow("عدد فواتير المبيعات:", report.InvoicesCount.ToString());
                    DrawRow("عدد المرتجعات:", report.ReturnsCount.ToString());
                    DrawRow("مبيعات نقدي:", $"{report.CashSalesPounds:N2} ج.م");
                    DrawRow("مبيعات شبكة / بطاقات:", $"{report.NetworkSalesPounds:N2} ج.م");
                    DrawRow("مبيعات آجل:", $"{report.OnAccountSalesPounds:N2} ج.م");
                    DrawRow("مبيعات تأمين:", $"{report.InsuranceSalesPounds:N2} ج.م");
                    DrawRow("إجمالي المبيعات:", $"{report.TotalSalesPounds:N2} ج.م", fontBold);
                    if (report.TotalReturnsPounds > 0)
                    {
                        DrawRow("المرتجعات النقدية:", $"-{report.CashReturnsPounds:N2} ج.م");
                        DrawRow("صافي المبيعات:", $"{report.NetSalesPounds:N2} ج.م", fontBold);
                    }

                    DrawDottedLine(g, y, marginLeft, marginLeft + printWidth);
                    y += 6;

                    g.DrawString("مقارنة النقدية وحركة الصندوق", fontBold, Brushes.Black, new RectangleF(marginLeft, y, printWidth, 16), sfRtl);
                    y += 18;

                    DrawRow("الرصيد الافتتاحي:", $"{report.OpeningBalancePounds:N2} ج.م");
                    DrawRow("(+) المقبوضات النقدية:", $"{report.CashSalesPounds:N2} ج.م");
                    DrawRow("(-) المرتجعات النقدية:", $"-{report.CashReturnsPounds:N2} ج.م");
                    DrawRow("(=) النقد المتوقع بالدرج:", $"{report.ExpectedCashPounds:N2} ج.م", fontBold);
                    if (report.ActualCashPounds.HasValue)
                    {
                        DrawRow("النقد الفعلي المعدود:", $"{report.ActualCashPounds.Value:N2} ج.م", fontLargeBold);
                        string diffStr = report.DifferencePounds.HasValue
                            ? (report.DifferencePounds.Value == 0
                                ? "مطابق تماماً (0.00 ج.م)"
                                : report.DifferencePounds.Value > 0
                                    ? $"+{report.DifferencePounds.Value:N2} ج.م (زيادة)"
                                    : $"{report.DifferencePounds.Value:N2} ج.م (عجز)")
                            : "—";
                        DrawRow("الفارق في الصندوق:", diffStr, fontLargeBold);
                    }

                    if (!string.IsNullOrWhiteSpace(report.Notes))
                    {
                        y += 4;
                        g.DrawString($"ملاحظات: {report.Notes}", fontRegular, Brushes.Black, new RectangleF(marginLeft, y, printWidth, 30), sfRtl);
                        y += 32;
                    }

                    DrawDottedLine(g, y, marginLeft, marginLeft + printWidth);
                    y += 10;

                    g.DrawString("توقيع الكاشير المسلّم: ........................", fontRegular, Brushes.Black, new RectangleF(marginLeft, y, printWidth, 16), sfRtl);
                    y += 20;
                    g.DrawString("توقيع الإدارة / المستلم: ........................", fontRegular, Brushes.Black, new RectangleF(marginLeft, y, printWidth, 16), sfRtl);
                    y += 22;

                    g.DrawString(AppBranding.ProductByline, fontSubTitle, Brushes.Gray, new RectangleF(marginLeft, y, printWidth, 14), sfCenter);
                    y += 14;
                    g.DrawString(AppBranding.Copyright, fontRegular, Brushes.Gray, new RectangleF(marginLeft, y, printWidth, 14), sfCenter);

                    e.HasMorePages = false;
                };

                printDoc.Print();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"حدث خطأ أثناء طباعة تقرير الوردية: {ex.Message}";
                Logger.LogException(ex, "ThermalReceiptPrinter.PrintShiftReport");
                return false;
            }
        }

        private static void DrawDottedLine(Graphics g, float y, float width)
        {
            using var pen = new Pen(Color.Black, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawLine(pen, 0, y, width, y);
        }
    }
}
