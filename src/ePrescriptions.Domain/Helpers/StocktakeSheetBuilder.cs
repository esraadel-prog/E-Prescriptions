using System;
using System.Collections.Generic;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.Domain.Helpers
{
    /// <summary>سطر في قائمة الجرد المطبوعة (عمود "المعدود" بيتسيب فاضي للكتابة بالقلم)</summary>
    public class StocktakeSheetRow
    {
        public int Number { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public int BookSmallestUnits { get; set; }
        public string BookText { get; set; } = string.Empty;
    }

    /// <summary>
    /// خطوة 38 جزء ج: تجهيز بيانات قائمة الجرد المطبوعة. منطق بحت (بدون PDF) عشان يتختبر.
    /// الأصناف غير النشطة ما بتظهرش، والترتيب بالاسم العربي، والترقيم من 1.
    /// </summary>
    public static class StocktakeSheetBuilder
    {
        public static List<StocktakeSheetRow> Build(IEnumerable<Item> items, bool includeZeroStock = true)
        {
            var ordered = items
                .Where(i => i.IsActive && (includeZeroStock || i.TotalStockSmallestUnits > 0))
                .OrderBy(i => i.NameAr, StringComparer.Ordinal)
                .ThenBy(i => i.Code, StringComparer.Ordinal)
                .ToList();

            var rows = new List<StocktakeSheetRow>(ordered.Count);
            int n = 0;
            foreach (var i in ordered)
            {
                n++;
                rows.Add(new StocktakeSheetRow
                {
                    Number = n,
                    Code = i.Code,
                    NameAr = i.NameAr,
                    BookSmallestUnits = i.TotalStockSmallestUnits,
                    BookText = UnitBreakdownHelper.FormatStockDetailed(i.TotalStockSmallestUnits, i.StripsPerBox, i.PiecesPerStrip)
                });
            }
            return rows;
        }
    }
}
