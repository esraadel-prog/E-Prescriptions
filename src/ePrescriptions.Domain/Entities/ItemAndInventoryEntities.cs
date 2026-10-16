using System;
using System.Collections.Generic;

namespace ePrescriptions.Domain.Entities
{
    public class Item
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string ActiveIngredient { get; set; } = string.Empty;

        // معاملات تجزئة الوحدات
        // 1 علبة = StripsPerBox أشرطة
        // 1 شريط = PiecesPerStrip أقراص/وحدات
        // إجمالي أصغر الوحدات في العلبة = StripsPerBox * PiecesPerStrip
        public int StripsPerBox { get; set; } = 1;
        public int PiecesPerStrip { get; set; } = 10;

        // أسعار العلبة بالقرش (الوحدة الصغرى long)
        public long RetailPriceBoxPiastres { get; set; }
        public long WholesalePriceBoxPiastres { get; set; }
        public long CostPriceBoxPiastres { get; set; }

        public int MinStockAlertSmallestUnits { get; set; } = 30; // تنبيه عند القرب من النفاد
        public bool IsPrescriptionRequired { get; set; }
        public bool IsVatExempt { get; set; }

        /// <summary>
        /// نسبة الضريبة المخصصة للصنف في السوق المصري (0% أو 5% أو 14%).
        /// -1 تعني استخدام النسبة الافتراضية المحددة في إعدادات الصيدلية.
        /// </summary>
        public int CustomVatPercent { get; set; } = -1;

        public bool IsActive { get; set; } = true;

        // إجمالي المخزون الحالي بأصغر وحدة
        public int TotalStockSmallestUnits { get; set; }

        public List<ItemBatch> Batches { get; set; } = new();

        /// <summary>
        /// إجمالي عدد أصغر الوحدات في العلبة الواحدة
        /// </summary>
        public int SmallestUnitsPerBox => Math.Max(1, StripsPerBox * PiecesPerStrip);

        /// <summary>
        /// استخراج نسبة الضريبة الفعلية للصنف مع مراعاة الإعفاء ونسبة الصيدلية الافتراضية
        /// </summary>
        public int GetEffectiveVatRate(int defaultPharmacyVatPercent = 14)
        {
            if (IsVatExempt) return 0;
            if (CustomVatPercent >= 0) return CustomVatPercent;
            return defaultPharmacyVatPercent;
        }

        /// <summary>
        /// حساب سعر بيع الوحدة بناءً على المستوى (علبة / شريط / قرص) وفئة السعر بالقروش (مع التقريب الصحيح)
        /// </summary>
        public long GetPriceForUnit(Enums.UnitLevel unitLevel, Enums.PriceCategory category)
        {
            long boxPrice = category == Enums.PriceCategory.Wholesale
                ? (WholesalePriceBoxPiastres > 0 ? WholesalePriceBoxPiastres : RetailPriceBoxPiastres)
                : RetailPriceBoxPiastres;

            if (unitLevel == Enums.UnitLevel.Box)
                return boxPrice;

            if (unitLevel == Enums.UnitLevel.Strip)
            {
                int strips = Math.Max(1, StripsPerBox);
                return (boxPrice + (strips / 2)) / strips;
            }

            // Piece / قرص
            int totalUnits = SmallestUnitsPerBox;
            return (boxPrice + (totalUnits / 2)) / totalUnits;
        }
    }

    public class ItemBatch
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public int RemainingSmallestUnits { get; set; }

        public long CostPricePerSmallestUnitPiastres { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
