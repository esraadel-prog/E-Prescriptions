using System;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// أدوات تفكيك وتجزئة الوحدات الدوائية (علبة / شريط / قرص) وحسابات المخزون
    /// </summary>
    public static class UnitBreakdownHelper
    {
        /// <summary>
        /// حساب عدد أصغر الوحدات (أقراص/أمبولات) بناءً على المستوى والكمية
        /// </summary>
        public static int CalculateSmallestUnits(UnitLevel unitLevel, int quantity, int stripsPerBox, int piecesPerStrip)
        {
            if (quantity <= 0) return 0;

            int piecesPerBox = Math.Max(1, stripsPerBox * piecesPerStrip);
            int piecesPerSingleStrip = Math.Max(1, piecesPerStrip);

            return unitLevel switch
            {
                UnitLevel.Box => quantity * piecesPerBox,
                UnitLevel.Strip => quantity * piecesPerSingleStrip,
                UnitLevel.Piece => quantity,
                UnitLevel.Ampoule => quantity,
                UnitLevel.Sachet => quantity,
                UnitLevel.Unit => quantity,
                _ => quantity
            };
        }

        /// <summary>
        /// الاسم العربي المعتمد لمستوى الوحدة
        /// </summary>
        public static string UnitLevelName(UnitLevel unit) => unit switch
        {
            UnitLevel.Box => "علبة",
            UnitLevel.Strip => "شريط",
            UnitLevel.Piece => "قرص",
            UnitLevel.Ampoule => "أمبول",
            UnitLevel.Sachet => "كيس",
            UnitLevel.Unit => "وحدة",
            _ => "وحدة"
        };

        /// <summary>
        /// تفكيك رصيد المخزون الكلي بأصغر وحدة إلى وصف مقروء: X علبة و Y شريط و Z قرص
        /// </summary>
        public static (int Boxes, int Strips, int Pieces) BreakdownStock(int totalSmallestUnits, int stripsPerBox, int piecesPerStrip)
        {
            if (totalSmallestUnits <= 0) return (0, 0, 0);

            int piecesPerBox = Math.Max(1, stripsPerBox * piecesPerStrip);
            int piecesPerSingleStrip = Math.Max(1, piecesPerStrip);

            int boxes = totalSmallestUnits / piecesPerBox;
            int remainingAfterBoxes = totalSmallestUnits % piecesPerBox;

            int strips = remainingAfterBoxes / piecesPerSingleStrip;
            int pieces = remainingAfterBoxes % piecesPerSingleStrip;

            return (boxes, strips, pieces);
        }

        /// <summary>
        /// نص عربي منسق يوضح رصيد المخزون بالتفصيل
        /// </summary>
        public static string FormatStockDetailed(int totalSmallestUnits, int stripsPerBox, int piecesPerStrip)
        {
            if (totalSmallestUnits <= 0) return "نفد من المخزون (0)";

            var (boxes, strips, pieces) = BreakdownStock(totalSmallestUnits, stripsPerBox, piecesPerStrip);

            if (stripsPerBox <= 1 && piecesPerStrip <= 1)
            {
                return $"{totalSmallestUnits} وحدة";
            }

            string result = "";
            if (boxes > 0) result += $"{boxes} علبة ";
            if (strips > 0) result += $"و {strips} شريط ";
            if (pieces > 0) result += $"و {pieces} قرص";

            return string.IsNullOrWhiteSpace(result) ? $"{totalSmallestUnits} وحدة" : result.Trim();
        }
    }
}
