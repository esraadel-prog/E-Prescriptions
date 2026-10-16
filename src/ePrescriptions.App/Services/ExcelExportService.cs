using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.Services
{
    public interface IExcelExportService
    {
        string ExportSalesReport(List<SalesReportRow> sales, SalesPeriodSummary summary, DateTime from, DateTime to, string pharmacyName);
        string ExportItemMovementReport(List<ItemMovementReportRow> movements, DateTime from, DateTime to, string pharmacyName);
        string ExportLowStockReport(List<LowStockReportRow> lowStock, string pharmacyName);
        string ExportExpiryReport(List<ExpiryReportRow> expiries, string pharmacyName);
        string ExportInventoryValuationReport(InventoryValuationSummary valuation, string pharmacyName);
        string ExportCashierShiftsReport(List<CashierShiftSummaryRow> shifts, List<CashierDiscrepancySummary> summaries, DateTime from, DateTime to, string pharmacyName);
        string ExportOpeningStockVoucher(OpeningStockVoucher voucher, string pharmacyName);
        string ExportOpeningStockTemplate();

        // خطوة 28: نسخ غير متزامنة (على خيط خلفي). المستدعي بيسلّم نسخة من البيانات مش مجموعات مربوطة بالواجهة.
        Task<string> ExportSalesReportAsync(List<SalesReportRow> sales, SalesPeriodSummary summary, DateTime from, DateTime to, string pharmacyName);
        Task<string> ExportItemMovementReportAsync(List<ItemMovementReportRow> movements, DateTime from, DateTime to, string pharmacyName);
        Task<string> ExportLowStockReportAsync(List<LowStockReportRow> lowStock, string pharmacyName);
        Task<string> ExportExpiryReportAsync(List<ExpiryReportRow> expiries, string pharmacyName);
        Task<string> ExportInventoryValuationReportAsync(InventoryValuationSummary valuation, string pharmacyName);
        Task<string> ExportCashierShiftsReportAsync(List<CashierShiftSummaryRow> shifts, List<CashierDiscrepancySummary> summaries, DateTime from, DateTime to, string pharmacyName);
        Task<string> ExportOpeningStockVoucherAsync(OpeningStockVoucher voucher, string pharmacyName);
    }

    public class ExcelExportService : IExcelExportService
    {
        public Task<string> ExportSalesReportAsync(List<SalesReportRow> sales, SalesPeriodSummary summary, DateTime from, DateTime to, string pharmacyName)
            => Task.Run(() => ExportSalesReport(sales, summary, from, to, pharmacyName));

        public Task<string> ExportItemMovementReportAsync(List<ItemMovementReportRow> movements, DateTime from, DateTime to, string pharmacyName)
            => Task.Run(() => ExportItemMovementReport(movements, from, to, pharmacyName));

        public Task<string> ExportLowStockReportAsync(List<LowStockReportRow> lowStock, string pharmacyName)
            => Task.Run(() => ExportLowStockReport(lowStock, pharmacyName));

        public Task<string> ExportExpiryReportAsync(List<ExpiryReportRow> expiries, string pharmacyName)
            => Task.Run(() => ExportExpiryReport(expiries, pharmacyName));

        public Task<string> ExportInventoryValuationReportAsync(InventoryValuationSummary valuation, string pharmacyName)
            => Task.Run(() => ExportInventoryValuationReport(valuation, pharmacyName));

        public Task<string> ExportCashierShiftsReportAsync(List<CashierShiftSummaryRow> shifts, List<CashierDiscrepancySummary> summaries, DateTime from, DateTime to, string pharmacyName)
            => Task.Run(() => ExportCashierShiftsReport(shifts, summaries, from, to, pharmacyName));

        private static string GetExportDirectory()
        {
            return ePrescriptions.Core.Helpers.AppPaths.EnsureDirectory(ePrescriptions.Core.Helpers.AppPaths.ExportsDirectory);
        }

        public string ExportSalesReport(List<SalesReportRow> sales, SalesPeriodSummary summary, DateTime from, DateTime to, string pharmacyName)
        {
            string fileName = $"تقرير_المبيعات_{DbDate.Stamp(DateTime.Now)}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("تقرير المبيعات");
            ws.RightToLeft = true;

            // 1) العنوان والترويسة
            ws.Cell(1, 1).Value = $"{pharmacyName} - تقرير المبيعات والأرباح التفصيلي";
            ws.Range(1, 1, 1, 8).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 15;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(1, 85, 165); // Brand Blue (dark)
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"الفترة من: {DbDate.Slash(from)}  إلى: {DbDate.Slash(to)}  |  تاريخ التصدير: {DbDate.SlashMinutes(DateTime.Now)}";
            ws.Range(2, 1, 2, 8).Merge();
            ws.Cell(2, 1).Style.Font.FontSize = 11;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromArgb(71, 85, 105);
            ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // 2) ترويسة الأعمدة
            string[] headers = { "رقم الفاتورة", "التاريخ والوقت", "العميل", "طريقة الدفع", "المجموع قبل الضريبة", "ضريبة القيمة المضافة", "الإجمالي النهائي", "الربح التقديري" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 131, 203);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            // 3) صفوف البيانات
            int row = 5;
            foreach (var item in sales)
            {
                ws.Cell(row, 1).Value = item.InvoiceNumber;
                ws.Cell(row, 2).Value = item.Date.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                ws.Cell(row, 3).Value = item.CustomerName;
                ws.Cell(row, 4).Value = item.PaymentMethodName;

                ws.Cell(row, 5).Value = item.SubtotalPiastres / 100.0;
                ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                ws.Cell(row, 6).Value = item.VatPounds;
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                ws.Cell(row, 7).Value = item.TotalPounds;
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                ws.Cell(row, 8).Value = item.ProfitPounds;
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                for (int col = 1; col <= 8; col++)
                {
                    ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
                }

                row++;
            }

            // 4) سطر الإجمالي
            ws.Cell(row, 1).Value = $"الإجمالي ({sales.Count} فاتورة)";
            ws.Range(row, 1, row, 4).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // فترة بدون مبيعات: صف الإجمالي هو الصف 5 نفسه، و SUM(E5:E4) بتشمل الخلية نفسها (مرجع دائري في Excel)، فنكتب صفر ثابت
            bool hasSales = sales.Count > 0;
            if (hasSales) ws.Cell(row, 5).FormulaA1 = $"SUM(E5:E{row - 1})"; else ws.Cell(row, 5).Value = 0;
            ws.Cell(row, 5).Style.Font.Bold = true;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            if (hasSales) ws.Cell(row, 6).FormulaA1 = $"SUM(F5:F{row - 1})"; else ws.Cell(row, 6).Value = 0;
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            if (hasSales) ws.Cell(row, 7).FormulaA1 = $"SUM(G5:G{row - 1})"; else ws.Cell(row, 7).Value = 0;
            ws.Cell(row, 7).Style.Font.Bold = true;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            if (hasSales) ws.Cell(row, 8).FormulaA1 = $"SUM(H5:H{row - 1})"; else ws.Cell(row, 8).Value = 0;
            ws.Cell(row, 8).Style.Font.Bold = true;
            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            var totalRange = ws.Range(row, 1, row, 8);
            totalRange.Style.Fill.BackgroundColor = XLColor.FromArgb(241, 245, 249);
            totalRange.Style.Border.TopBorder = XLBorderStyleValues.Medium;
            totalRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;

            // 5) ملخص طرق الدفع في الأسفل
            int summaryRow = row + 3;
            ws.Cell(summaryRow, 1).Value = "ملخص المبيعات وطرق الدفع للفترة:";
            ws.Cell(summaryRow, 1).Style.Font.Bold = true;

            ws.Cell(summaryRow + 1, 1).Value = "المبيعات النقدية:";
            ws.Cell(summaryRow + 1, 2).Value = summary.CashSalesPounds;
            ws.Cell(summaryRow + 1, 2).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Cell(summaryRow + 2, 1).Value = "مبيعات الشبكة / البطاقات:";
            ws.Cell(summaryRow + 2, 2).Value = summary.NetworkSalesPounds;
            ws.Cell(summaryRow + 2, 2).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Cell(summaryRow + 3, 1).Value = "المبيعات الآجلة:";
            ws.Cell(summaryRow + 3, 2).Value = summary.OnAccountSalesPounds;
            ws.Cell(summaryRow + 3, 2).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Cell(summaryRow + 4, 1).Value = "صافي أرباح المبيعات:";
            ws.Cell(summaryRow + 4, 2).Value = summary.NetProfitPounds;
            ws.Cell(summaryRow + 4, 2).Style.Font.Bold = true;
            ws.Cell(summaryRow + 4, 2).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }

        public string ExportItemMovementReport(List<ItemMovementReportRow> movements, DateTime from, DateTime to, string pharmacyName)
        {
            string fileName = $"تقرير_حركة_الأصناف_{DbDate.Stamp(DateTime.Now)}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("حركة ومبيعات الأصناف");
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = $"{pharmacyName} - تقرير حركة ومبيعات الأصناف (الأكثر طلباً وركوداً)";
            ws.Range(1, 1, 1, 7).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(1, 85, 165);
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            string[] headers = { "كود الصنف", "الباركود", "اسم الدواء / المستحضر", "الكمية المباعة (وحدة)", "المخزون الحالي", "إجمالي الإيراد", "صافي الربح" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 131, 203);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = 4;
            foreach (var item in movements)
            {
                ws.Cell(row, 1).Value = item.ItemCode;
                ws.Cell(row, 2).Value = item.Barcode;
                ws.Cell(row, 3).Value = item.NameAr;
                ws.Cell(row, 4).Value = item.TotalSoldSmallestUnits;
                ws.Cell(row, 5).Value = item.CurrentStockSmallestUnits;

                ws.Cell(row, 6).Value = item.TotalRevenuePounds;
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                ws.Cell(row, 7).Value = item.TotalProfitPounds;
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                for (int col = 1; col <= 7; col++)
                {
                    ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
                }
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }

        public string ExportLowStockReport(List<LowStockReportRow> lowStock, string pharmacyName)
        {
            string fileName = $"تقرير_النواقص_وإعادة_الطلب_{DbDate.Stamp(DateTime.Now)}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("النواقص وإعادة الطلب");
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = $"{pharmacyName} - قائمة النواقص وحد إعادة الطلب المقترح";
            ws.Range(1, 1, 1, 7).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(185, 28, 28); // Red
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            string[] headers = { "كود الصنف", "الباركود", "اسم الدواء", "الرصيد الفعلي الحالي", "حد الطلب الأدنى", "مقدار العجز", "الكمية المقترحة للطلب" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(239, 68, 68);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = 4;
            foreach (var item in lowStock)
            {
                ws.Cell(row, 1).Value = item.ItemCode;
                ws.Cell(row, 2).Value = item.Barcode;
                ws.Cell(row, 3).Value = item.NameAr;
                ws.Cell(row, 4).Value = item.CurrentStockSmallestUnits;
                ws.Cell(row, 5).Value = item.MinStockAlertSmallestUnits;
                ws.Cell(row, 6).Value = item.DeficitUnits;
                ws.Cell(row, 7).Value = item.SuggestedOrderUnits;

                for (int col = 1; col <= 7; col++)
                {
                    ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
                }
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }

        public string ExportExpiryReport(List<ExpiryReportRow> expiries, string pharmacyName)
        {
            string fileName = $"تقرير_صلاحية_الأدوية_{DbDate.Stamp(DateTime.Now)}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("صلاحيات الأدوية");
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = $"{pharmacyName} - تقرير الأدوية قريبة الصلاحية والمنتهية (FEFO)";
            ws.Range(1, 1, 1, 7).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(217, 119, 6); // Amber
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            string[] headers = { "كود الصنف", "اسم الدواء", "رقم التشغيلة (Batch)", "تاريخ الانتهاء", "الأيام المتبقية", "الكمية المتبقية", "إجمالي قيمة التكلفة" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 158, 11);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = 4;
            foreach (var item in expiries)
            {
                ws.Cell(row, 1).Value = item.ItemCode;
                ws.Cell(row, 2).Value = item.NameAr;
                ws.Cell(row, 3).Value = item.BatchNumber;
                ws.Cell(row, 4).Value = item.ExpiryDate.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture);
                ws.Cell(row, 5).Value = item.DaysRemaining;
                ws.Cell(row, 6).Value = item.RemainingSmallestUnits;
                ws.Cell(row, 7).Value = item.TotalCostPounds;
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";

                if (item.IsExpired)
                {
                    ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.FromArgb(254, 226, 226); // Light red
                }

                for (int col = 1; col <= 7; col++)
                {
                    ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
                }
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }

        public string ExportInventoryValuationReport(InventoryValuationSummary valuation, string pharmacyName)
        {
            string fileName = $"تقرير_تقييم_المخزون_{DbDate.Stamp(DateTime.Now)}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("تقييم المخزون المالي");
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = $"{pharmacyName} - التقييم المالي الشامل للمخزون";
            ws.Range(1, 1, 1, 3).Merge();
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(16, 185, 129); // Emerald
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(3, 1).Value = "البند الإحصائي";
            ws.Cell(3, 2).Value = "القيمة";
            ws.Range(3, 1, 3, 2).Style.Font.Bold = true;
            ws.Range(3, 1, 3, 2).Style.Fill.BackgroundColor = XLColor.FromArgb(5, 150, 105);
            ws.Range(3, 1, 3, 2).Style.Font.FontColor = XLColor.White;

            string[] labels = {
                "إجمالي عدد الأصناف المعرفة:",
                "إجمالي عدد التشغيلات (Batches) النشطة:",
                "إجمالي عدد الوحدات الصغرى المتوفرة:",
                "إجمالي قيمة المخزون بسعر الشراء (التكلفة):",
                "إجمالي قيمة المخزون بسعر البيع (التجزئة):",
                "صافي الأرباح المتوقعة عند البيع الكامل:"
            };

            object[] values = {
                valuation.TotalItemsCount,
                valuation.TotalBatchesCount,
                valuation.TotalSmallestUnitsCount,
                valuation.TotalValuationCostPounds,
                valuation.TotalValuationRetailPounds,
                valuation.ExpectedProfitPounds
            };

            for (int i = 0; i < labels.Length; i++)
            {
                int r = 4 + i;
                ws.Cell(r, 1).Value = labels[i];
                ws.Cell(r, 2).Value = Convert.ToDouble(values[i]);
                if (i >= 3)
                {
                    ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00 \"ج.م\"";
                    ws.Cell(r, 2).Style.Font.Bold = true;
                }
                ws.Cell(r, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(r, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }

        public string ExportCashierShiftsReport(List<CashierShiftSummaryRow> shifts, List<CashierDiscrepancySummary> summaries, DateTime from, DateTime to, string pharmacyName)
        {
            string fileName = $"تقرير_الورديات_{DbDate.Stamp(DateTime.Now)}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            workbook.RightToLeft = true;

            var ws = workbook.Worksheets.Add("تفصيل الورديات");
            ws.RightToLeft = true;

            ws.Cell(1, 1).Value = $"{pharmacyName} - تقرير الورديات والصناديق";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;

            ws.Cell(2, 1).Value = $"الفترة من: {DbDate.Date(from)} إلى: {DbDate.Date(to)} | تاريخ الاستخراج: {DbDate.Full(DateTime.Now)}";
            ws.Cell(2, 1).Style.Font.FontSize = 10;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;

            string[] headers = {
                "رقم الوردية", "الكاشير", "بدء الوردية", "إغلاق الوردية", "الحالة",
                "الافتتاحي", "مبيعات نقدي", "مبيعات شبكة", "مبيعات آجل", "مبيعات تأمين",
                "إجمالي المبيعات", "مرتجع نقدي", "المتوقع بالدرج", "الفعلي المعدود", "الفارق (عجز/زيادة)"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 243, 246);
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = 5;
            foreach (var s in shifts)
            {
                ws.Cell(row, 1).Value = s.ShiftId;
                ws.Cell(row, 2).Value = s.CashierName;
                ws.Cell(row, 3).Value = DbDate.Full(s.StartTime);
                ws.Cell(row, 4).Value = s.EndTime.HasValue ? DbDate.Full(s.EndTime.Value) : "مفتوحة";
                ws.Cell(row, 5).Value = s.StatusDisplay;

                ws.Cell(row, 6).Value = (double)s.OpeningBalancePounds;
                ws.Cell(row, 7).Value = (double)s.CashSalesPounds;
                ws.Cell(row, 8).Value = (double)s.NetworkSalesPounds;
                ws.Cell(row, 9).Value = (double)s.OnAccountSalesPounds;
                ws.Cell(row, 10).Value = (double)s.InsuranceSalesPounds;
                ws.Cell(row, 11).Value = (double)s.TotalSalesPounds;
                ws.Cell(row, 12).Value = (double)s.CashReturnsPounds;
                ws.Cell(row, 13).Value = (double)s.ExpectedCashPounds;
                ws.Cell(row, 14).Value = s.ActualCashPounds.HasValue ? (double)s.ActualCashPounds.Value : 0;
                ws.Cell(row, 15).Value = s.DifferencePounds.HasValue ? (double)s.DifferencePounds.Value : 0;

                for (int c = 6; c <= 15; c++)
                {
                    ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";
                }

                if (s.DifferencePounds.HasValue && s.DifferencePounds.Value < 0)
                {
                    ws.Cell(row, 15).Style.Font.FontColor = XLColor.Red;
                    ws.Cell(row, 15).Style.Font.Bold = true;
                }
                else if (s.DifferencePounds.HasValue && s.DifferencePounds.Value > 0)
                {
                    ws.Cell(row, 15).Style.Font.FontColor = XLColor.DarkGreen;
                    ws.Cell(row, 15).Style.Font.Bold = true;
                }

                for (int c = 1; c <= headers.Length; c++)
                    ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                row++;
            }

            ws.Columns().AdjustToContents();

            var wsSum = workbook.Worksheets.Add("ملخص الكاشيرات");
            wsSum.RightToLeft = true;

            wsSum.Cell(1, 1).Value = $"{pharmacyName} - ملخص عجز وزيادة الصناديق لكل كاشير";
            wsSum.Cell(1, 1).Style.Font.Bold = true;
            wsSum.Cell(1, 1).Style.Font.FontSize = 13;

            string[] sumHeaders = {
                "اسم الكاشير", "عدد الورديات", "الورديات المغلقة", "إجمالي المبيعات",
                "إجمالي النقد المتوقع", "إجمالي النقد الفعلي", "إجمالي العجز", "إجمالي الزيادة", "صافي الفارق"
            };

            for (int i = 0; i < sumHeaders.Length; i++)
            {
                var cell = wsSum.Cell(3, i + 1);
                cell.Value = sumHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(230, 238, 248);
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int sumRow = 4;
            foreach (var sm in summaries)
            {
                wsSum.Cell(sumRow, 1).Value = sm.CashierName;
                wsSum.Cell(sumRow, 2).Value = sm.ShiftsCount;
                wsSum.Cell(sumRow, 3).Value = sm.ClosedShiftsCount;
                wsSum.Cell(sumRow, 4).Value = (double)sm.TotalSalesPounds;
                wsSum.Cell(sumRow, 5).Value = (double)sm.TotalExpectedCashPounds;
                wsSum.Cell(sumRow, 6).Value = (double)sm.TotalActualCashPounds;
                wsSum.Cell(sumRow, 7).Value = (double)sm.TotalShortagePounds;
                wsSum.Cell(sumRow, 8).Value = (double)sm.TotalSurplusPounds;
                wsSum.Cell(sumRow, 9).Value = (double)sm.NetDifferencePounds;

                for (int c = 4; c <= 9; c++)
                    wsSum.Cell(sumRow, c).Style.NumberFormat.Format = "#,##0.00";

                if (sm.TotalShortagePounds > 0)
                    wsSum.Cell(sumRow, 7).Style.Font.FontColor = XLColor.Red;
                if (sm.TotalSurplusPounds > 0)
                    wsSum.Cell(sumRow, 8).Style.Font.FontColor = XLColor.DarkGreen;

                for (int c = 1; c <= sumHeaders.Length; c++)
                    wsSum.Cell(sumRow, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                sumRow++;
            }

            wsSum.Columns().AdjustToContents();

            workbook.SaveAs(filePath);
            return filePath;
        }

        public Task<string> ExportOpeningStockVoucherAsync(OpeningStockVoucher voucher, string pharmacyName)
            => Task.Run(() => ExportOpeningStockVoucher(voucher, pharmacyName));

        public string ExportOpeningStockVoucher(OpeningStockVoucher voucher, string pharmacyName)
        {
            string fileName = $"سند_رصيد_أول_المدة_{voucher.VoucherNumber}_{DbDate.Stamp(DateTime.Now)}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("رصيد أول المدة");
            ws.RightToLeft = true;

            // العنوان الرئيسي
            ws.Cell(1, 1).Value = pharmacyName;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 16;
            ws.Range(1, 1, 1, 12).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(2, 1).Value = $"سند حصر رصيد أول المدة - رقم: {voucher.VoucherNumber} | التاريخ: {voucher.VoucherDate:yyyy-MM-dd}";
            ws.Cell(2, 1).Style.Font.Bold = true;
            ws.Cell(2, 1).Style.Font.FontSize = 12;
            ws.Range(2, 1, 2, 12).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // كروت الإجماليات
            ws.Cell(4, 1).Value = "عدد الأصناف:";
            ws.Cell(4, 2).Value = voucher.TotalItemsCount;
            ws.Cell(4, 4).Value = "إجمالي الوحدات:";
            ws.Cell(4, 5).Value = voucher.TotalSmallestUnits;
            ws.Cell(4, 7).Value = "إجمالي التكلفة:";
            ws.Cell(4, 8).Value = (double)voucher.TotalCostPounds;
            ws.Cell(4, 8).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(4, 10).Value = "إجمالي سعر الجمهور:";
            ws.Cell(4, 11).Value = (double)voucher.TotalRetailPounds;
            ws.Cell(4, 11).Style.NumberFormat.Format = "#,##0.00";

            for (int col = 1; col <= 11; col += 3)
            {
                ws.Cell(4, col).Style.Font.Bold = true;
                ws.Cell(4, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
            }

            // ترويسة الجدول
            string[] headers = new[]
            {
                "م", "كود الصنف", "الباركود", "اسم الصنف الدوائي", "المادة الفعالة",
                "رقم التشغيلة", "تاريخ الصلاحية", "الكمية بالعلب", "أشرطة", "أقراص/وحدات",
                "إجمالي الوحدات", "سعر التكلفة (علبة)", "إجمالي التكلفة", "سعر البيع (علبة)", "إجمالي البيع"
            };

            int headerRow = 6;
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0083CB");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int dataRow = 7;
            int idx = 1;
            foreach (var line in voucher.Lines)
            {
                ws.Cell(dataRow, 1).Value = idx++;
                ws.Cell(dataRow, 2).Value = line.ItemCode;
                ws.Cell(dataRow, 3).Value = line.ItemBarcode;
                ws.Cell(dataRow, 4).Value = line.ItemNameAr;
                ws.Cell(dataRow, 5).Value = line.ActiveIngredient;
                ws.Cell(dataRow, 6).Value = line.BatchNumber;
                ws.Cell(dataRow, 7).Value = line.ExpiryDate.ToString("yyyy-MM-dd");
                ws.Cell(dataRow, 8).Value = line.QuantityBoxes;
                ws.Cell(dataRow, 9).Value = line.QuantityStrips;
                ws.Cell(dataRow, 10).Value = line.QuantityUnits;
                ws.Cell(dataRow, 11).Value = line.TotalSmallestUnits;
                ws.Cell(dataRow, 12).Value = (double)line.CostPriceBoxPounds;
                ws.Cell(dataRow, 13).Value = (double)line.TotalCostPounds;
                ws.Cell(dataRow, 14).Value = (double)line.RetailPriceBoxPounds;
                ws.Cell(dataRow, 15).Value = (double)line.TotalRetailPounds;

                ws.Cell(dataRow, 12).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, 13).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, 14).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(dataRow, 15).Style.NumberFormat.Format = "#,##0.00";

                for (int c = 1; c <= headers.Length; c++)
                {
                    ws.Cell(dataRow, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }

                dataRow++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }

        public string ExportOpeningStockTemplate()
        {
            string fileName = $"نموذج_إدخال_رصيد_أول_المدة_{DbDate.Stamp(DateTime.Now)}.xlsx";
            string filePath = Path.Combine(GetExportDirectory(), fileName);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("نموذج بضاعة أول المدة");
            ws.RightToLeft = true;

            string[] headers = new[]
            {
                "الباركود", "اسم الصنف العربي *", "الاسم الإنجليزي", "المادة الفعالة",
                "عدد الشرائط بالعلبة", "عدد الأقراص بالشريط", "رقم التشغيلة",
                "تاريخ الصلاحية (yyyy-MM-dd)", "عدد العلب *", "أشرطة إضافية", "أقراص إضافية",
                "سعر شراء العلبة (ج.م) *", "سعر بيع العلبة للجمهور (ج.م) *"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#32992B");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            // سطر تجريبي إرشادي
            ws.Cell(2, 1).Value = "6221234567890";
            ws.Cell(2, 2).Value = "أوجمنتين 1 جم 14 قرص";
            ws.Cell(2, 3).Value = "Augmentin 1g 14 tab";
            ws.Cell(2, 4).Value = "Amoxicillin + Clavulanic Acid";
            ws.Cell(2, 5).Value = 2;
            ws.Cell(2, 6).Value = 7;
            ws.Cell(2, 7).Value = "B12345";
            ws.Cell(2, 8).Value = "2027-12-31";
            ws.Cell(2, 9).Value = 10;
            ws.Cell(2, 10).Value = 0;
            ws.Cell(2, 11).Value = 0;
            ws.Cell(2, 12).Value = 85.50;
            ws.Cell(2, 13).Value = 110.00;

            ws.Columns().AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        }
    }
}
