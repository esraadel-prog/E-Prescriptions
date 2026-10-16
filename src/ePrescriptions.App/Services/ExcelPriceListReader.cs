using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace ePrescriptions.App.Services
{
    /// <summary>
    /// قارئ قوائم الأسعار من Excel بـ ClosedXML (أول ورقة فيها بيانات).
    /// الخلايا الرقمية بتتقرا بقيمتها الخام (مش بالتنسيق المعروض) عشان تنسيق العملة ما يبوّظش السعر.
    /// </summary>
    public class ExcelPriceListReader : IPriceListFileReader
    {
        public IReadOnlyList<string[]> ReadGrid(string path)
        {
            PriceListFileGuard.CheckFile(path);

            try
            {
                // FileShare.ReadWrite: الملف ممكن يكون مفتوح في Excel
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var workbook = new XLWorkbook(stream);

                foreach (var sheet in workbook.Worksheets)
                {
                    var used = sheet.RangeUsed();
                    if (used == null) continue;

                    int firstRow = used.FirstRow().RowNumber();
                    int lastRow = used.LastRow().RowNumber();
                    int firstCol = used.FirstColumn().ColumnNumber();
                    int lastCol = used.LastColumn().ColumnNumber();

                    PriceListFileGuard.CheckRowCount(lastRow - firstRow + 1);

                    var grid = new List<string[]>(lastRow - firstRow + 1);
                    for (int r = firstRow; r <= lastRow; r++)
                    {
                        var line = new string[lastCol - firstCol + 1];
                        for (int c = firstCol; c <= lastCol; c++)
                            line[c - firstCol] = CellText(sheet.Cell(r, c));
                        grid.Add(line);
                    }
                    return grid;
                }

                throw new PriceListFileException("الملف فاضي (مفيش ورقة فيها بيانات).");
            }
            catch (PriceListFileException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ePrescriptions.Core.Helpers.Logger.LogException(ex, "ExcelPriceListReader.ReadGrid");
                throw new PriceListFileException("تعذر قراءة ملف Excel (ممكن يكون تالف أو محمي بكلمة مرور): " + ex.Message);
            }
        }

        private static string CellText(IXLCell cell)
        {
            try
            {
                if (cell.IsEmpty()) return string.Empty;
                if (cell.DataType == XLDataType.Number)
                    return ((decimal)cell.GetDouble()).ToString(CultureInfo.InvariantCulture);
                return cell.GetString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                // خلية فيها خطأ (#N/A مثلاً) بتتعامل كفاضية، وما تبوّظش باقي الملف
                ePrescriptions.Core.Helpers.Logger.LogException(ex, "ExcelPriceListReader.CellText");
                return string.Empty;
            }
        }
    }
}
