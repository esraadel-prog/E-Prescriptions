using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.Services
{
    /// <summary>
    /// طباعة ملصقات التشغيلة (باركود يحدد التشغيلة وتاريخ الصلاحية) بعد استلام البضاعة: ملصق لكل علبة.
    /// </summary>
    public static class BatchLabelPrintHelper
    {
        public static int LabelsFor(Item item, int smallestUnits)
        {
            int perBox = Math.Max(1, item.SmallestUnitsPerBox);
            return Math.Max(1, (smallestUnits + perBox - 1) / perBox);
        }

        /// <summary>
        /// يسأل المستخدم مرة واحدة ثم يطبع ملصقات كل التشغيلات. يرجع رسالة حالة (أو null لو لم تتم الطباعة).
        /// </summary>
        public static string? OfferAndPrint(IBarcodeLabelService? labelService, IReadOnlyList<(Item Item, ItemBatch Batch, int Units)> lines)
        {
            if (labelService == null || lines.Count == 0) return null;

            int total = lines.Sum(l => LabelsFor(l.Item, l.Units));
            var answer = MessageBox.Show(
                $"تطبع ملصقات الصلاحية على العلب الآن؟\nعدد الملصقات: {total}\n\nكل ملصق عليه باركود بيعرّف التشغيلة وتاريخ الصلاحية، وعند البيع الجهاز بيقرأه ويحذّرك لو في تشغيلة أقدم على الرف.",
                "طباعة ملصقات الصلاحية",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

            if (answer != MessageBoxResult.Yes) return null;

            int printed = 0;
            foreach (var (item, batch, units) in lines)
            {
                int count = LabelsFor(item, units);
                if (!labelService.PrintBarcodeLabel(item, batch, count, out string error))
                    return $"تعذرت طباعة الملصقات: {error}";
                printed += count;
            }

            return $"تمت طباعة {printed} ملصق صلاحية.";
        }
    }
}
