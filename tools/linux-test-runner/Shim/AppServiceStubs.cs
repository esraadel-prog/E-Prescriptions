// أغلفة تجميع فقط للمشغّل البديل (لا تُستخدم في بناء التطبيق الحقيقي): تسمح بترجمة PurchasesViewModel هنا
using System.Collections.Generic;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.Services
{
    public interface IBarcodeLabelService
    {
        bool PrintBarcodeLabel(Item item, ItemBatch? batch, int labelCount, out string errorMessage);
    }

    public static class BatchLabelPrintHelper
    {
        public static string? OfferAndPrint(IBarcodeLabelService? labelService, IReadOnlyList<(Item Item, ItemBatch Batch, int Units)> lines) => null;
    }
}
