using System;
using System.Collections.Generic;
using System.Linq;

namespace ePrescriptions.Domain.Entities
{
    /// <summary>بند في أمر شراء مقترح (صنف تحت حد النواقص)</summary>
    public class SuggestedOrderLine
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;

        /// <summary>الرصيد الحالي (ممكن يكون سالب لو في كمية مباعة بالسالب)</summary>
        public int CurrentStockSmallestUnits { get; set; }
        public int MinStockAlertSmallestUnits { get; set; }
        public int SmallestUnitsPerBox { get; set; } = 1;

        /// <summary>العجز عن الحد الأدنى</summary>
        public int DeficitUnits => Math.Max(0, MinStockAlertSmallestUnits - CurrentStockSmallestUnits);

        /// <summary>
        /// الكمية المقترحة بأصغر وحدة: نفس قاعدة تقرير النواقص الموجود (العجز + ضعف الحد الأدنى)،
        /// فالرصيد بعد وصول الطلب = 3 × الحد الأدنى.
        /// </summary>
        public int SuggestedUnits => DeficitUnits + (MinStockAlertSmallestUnits * 2);

        /// <summary>الكمية المقترحة بالعلب الكاملة (بتتقرّب لأعلى لأن الطلب من الشركة بالعلبة)</summary>
        public int SuggestedBoxes => SmallestUnitsPerBox <= 0 ? SuggestedUnits : (SuggestedUnits + SmallestUnitsPerBox - 1) / SmallestUnitsPerBox;

        /// <summary>كمية الطلب الفعلية بأصغر وحدة بعد التقريب للعلب</summary>
        public int OrderUnits => SuggestedBoxes * Math.Max(1, SmallestUnitsPerBox);

        /// <summary>بيانات آخر شراء للصنف (لتقدير التكلفة فقط): إجمالي البند قبل الضريبة وعدد وحداته</summary>
        public bool HasPurchaseHistory => LastPurchaseUnits > 0;
        public long LastPurchaseTaxablePiastres { get; set; }
        public int LastPurchaseUnits { get; set; }

        /// <summary>تكلفة تقديرية بسعر آخر شراء (قبل الضريبة)؛ 0 لو الصنف عمره ما اتشترى</summary>
        public long EstimatedCostPiastres =>
            !HasPurchaseHistory || LastPurchaseTaxablePiastres <= 0
                ? 0
                : (LastPurchaseTaxablePiastres * OrderUnits + LastPurchaseUnits / 2) / LastPurchaseUnits;
    }

    /// <summary>
    /// أمر شراء مقترح لمورد واحد (مورد آخر شراء لكل صنف). SupplierId = null تعني أصناف
    /// عمرها ما اتشترت أو موردها الأخير اتعطّل؛ لازم المستخدم يختار لها مورد.
    /// </summary>
    public class SuggestedPurchaseOrder
    {
        public int? SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public List<SuggestedOrderLine> Lines { get; set; } = new();

        public bool HasSupplier => SupplierId.HasValue;
        public long EstimatedTotalPiastres => Lines.Sum(l => l.EstimatedCostPiastres);
        public bool HasUnpricedLines => Lines.Any(l => !l.HasPurchaseHistory);
    }
}
