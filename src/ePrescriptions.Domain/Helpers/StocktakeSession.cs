using System;
using System.Collections.Generic;
using System.Linq;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.Domain.Helpers
{
    /// <summary>حالة سطر في جلسة الجرد مقارنةً بالرصيد الدفتري</summary>
    public enum StocktakeStatus
    {
        Match = 0,
        Shortage = 1,
        Surplus = 2
    }

    /// <summary>سطر جرد: صنف واحد، رصيده الدفتري وقت أول مسح، والكمية المعدودة فعلياً (بأصغر وحدة)</summary>
    public class StocktakeLine
    {
        public int ItemId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public int UnitsPerBox { get; set; } = 1;
        public int BookSmallestUnits { get; set; }
        public int CountedSmallestUnits { get; set; }

        /// <summary>الفرق = المعدود - الدفتري (سالب = عجز، موجب = زيادة)</summary>
        public int DifferenceSmallestUnits => CountedSmallestUnits - BookSmallestUnits;

        public StocktakeStatus Status =>
            DifferenceSmallestUnits == 0 ? StocktakeStatus.Match
            : DifferenceSmallestUnits < 0 ? StocktakeStatus.Shortage
            : StocktakeStatus.Surplus;
    }

    /// <summary>
    /// جلسة جرد بالباركود (خطوة 38): كل مسح لعلبة = زيادة علبة واحدة (SmallestUnitsPerBox) في سطر الصنف.
    /// الكمية المعدودة قابلة للتعديل يدوياً (لأشرطة/أقراص سائبة). الأصناف التي لم تُمسح لا تدخل المقارنة
    /// (الجرد الجزئي مسموح)، إلا إذا طُلب صراحةً اعتبارها صفراً عبر <see cref="IncludeUncounted"/>.
    /// منطق بحت بدون قاعدة بيانات ليسهل اختباره.
    /// </summary>
    public class StocktakeSession
    {
        private readonly Dictionary<int, StocktakeLine> _lines = new Dictionary<int, StocktakeLine>();
        private readonly List<int> _order = new List<int>();

        public IReadOnlyList<StocktakeLine> Lines => _order.Select(id => _lines[id]).ToList();

        public int ScanCount { get; private set; }

        /// <summary>مسح علبة واحدة لصنف: يضيف SmallestUnitsPerBox للكمية المعدودة. يرجع السطر بعد التحديث.</summary>
        public StocktakeLine AddScannedBox(Item item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var line = GetOrCreate(item);
            line.CountedSmallestUnits += line.UnitsPerBox;
            ScanCount++;
            return line;
        }

        /// <summary>تعيين الكمية المعدودة يدوياً (أصغر وحدة). القيم السالبة مرفوضة.</summary>
        public StocktakeLine SetCounted(Item item, int countedSmallestUnits)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (countedSmallestUnits < 0) throw new ArgumentOutOfRangeException(nameof(countedSmallestUnits), "الكمية المعدودة لا يمكن أن تكون سالبة");
            var line = GetOrCreate(item);
            line.CountedSmallestUnits = countedSmallestUnits;
            return line;
        }

        /// <summary>إضافة صنف لم يُمسح بعد بكمية معدودة صفر (يظهر في المقارنة كعجز لو رصيده الدفتري > 0)</summary>
        public StocktakeLine IncludeUncounted(Item item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            return GetOrCreate(item);
        }

        public bool RemoveLine(int itemId)
        {
            if (!_lines.Remove(itemId)) return false;
            _order.Remove(itemId);
            return true;
        }

        public void Clear()
        {
            _lines.Clear();
            _order.Clear();
            ScanCount = 0;
        }

        /// <summary>السطور التي بها فرق فقط (هي التي ستُسوّى عند الاعتماد)</summary>
        public List<StocktakeLine> LinesWithDifference() =>
            Lines.Where(l => l.DifferenceSmallestUnits != 0).ToList();

        private StocktakeLine GetOrCreate(Item item)
        {
            if (_lines.TryGetValue(item.Id, out var existing)) return existing;
            var line = new StocktakeLine
            {
                ItemId = item.Id,
                Code = item.Code,
                NameAr = item.NameAr,
                UnitsPerBox = item.SmallestUnitsPerBox,
                BookSmallestUnits = item.TotalStockSmallestUnits,
                CountedSmallestUnits = 0
            };
            _lines[item.Id] = line;
            _order.Add(item.Id);
            return line;
        }
    }
}
