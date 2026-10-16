using System;
using System.Collections.Generic;
using ePrescriptions.Core.Financial;

namespace ePrescriptions.Domain.Entities
{
    /// <summary>
    /// سند / إذن رصيد أول المدة (بضاعة أول المدة):
    /// يُستخدم لحصر وإدخال كافة الأصناف المتوفرة بالصيدلية مع كمياتها وتشغيلاتها وأسعارها
    /// قبل بدء التشغيل الفعلي على البرنامج أو كأرصدة افتتاحية لفترة محاسبية جديدة.
    /// </summary>
    public class OpeningStockVoucher
    {
        public int Id { get; set; }
        public string VoucherNumber { get; set; } = string.Empty;
        public DateTime VoucherDate { get; set; } = DateTime.Today;
        public string Notes { get; set; } = string.Empty;
        public int CreatedByUserId { get; set; } = 1;
        public string CreatedByUserName { get; set; } = string.Empty;
        public int TotalItemsCount { get; set; }
        public int TotalSmallestUnits { get; set; }
        public long TotalCostPiastres { get; set; }
        public long TotalRetailPiastres { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<OpeningStockVoucherLine> Lines { get; set; } = new();

        public decimal TotalCostPounds => TotalCostPiastres / 100m;
        public decimal TotalRetailPounds => TotalRetailPiastres / 100m;
        public long TotalProfitPiastres => Math.Max(0, TotalRetailPiastres - TotalCostPiastres);
        public decimal TotalProfitPounds => TotalProfitPiastres / 100m;

        public decimal ProfitMarginPercent => TotalCostPiastres > 0
            ? Math.Round(((decimal)TotalProfitPiastres / TotalCostPiastres) * 100m, 2)
            : 0m;

        public string FormattedTotalCost => Money.FromPiastres(TotalCostPiastres).ToFormattedString();
        public string FormattedTotalRetail => Money.FromPiastres(TotalRetailPiastres).ToFormattedString();
        public string FormattedTotalProfit => Money.FromPiastres(TotalProfitPiastres).ToFormattedString();

        public void RecalculateTotals()
        {
            TotalItemsCount = Lines.Count;
            int totalUnits = 0;
            long totalCost = 0;
            long totalRetail = 0;

            foreach (var line in Lines)
            {
                line.RecalculateLine();
                totalUnits += line.TotalSmallestUnits;
                totalCost += line.TotalCostPiastres;
                totalRetail += line.TotalRetailPiastres;
            }

            TotalSmallestUnits = totalUnits;
            TotalCostPiastres = totalCost;
            TotalRetailPiastres = totalRetail;
        }
    }

    /// <summary>
    /// بند / سطر في إذن رصيد أول المدة
    /// </summary>
    public class OpeningStockVoucherLine
    {
        public int Id { get; set; }
        public int VoucherId { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemBarcode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;
        public string ItemNameEn { get; set; } = string.Empty;
        public string ActiveIngredient { get; set; } = string.Empty;

        // معاملات تجزئة العلبة
        public int StripsPerBox { get; set; } = 1;
        public int PiecesPerStrip { get; set; } = 10;

        // بيانات التشغيلة وتاريخ الصلاحية
        public string BatchNumber { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; } = DateTime.Today.AddYears(2);

        // الكميات المدخلة (علب + أشرطة + أقراص)
        public int QuantityBoxes { get; set; }
        public int QuantityStrips { get; set; }
        public int QuantityUnits { get; set; }

        // إجمالي الكمية بأصغر وحدة
        public int TotalSmallestUnits { get; set; }

        // أسعار العلبة بالقروش
        public long CostPriceBoxPiastres { get; set; }
        public long RetailPriceBoxPiastres { get; set; }

        // إجماليات السطر
        public long TotalCostPiastres { get; set; }
        public long TotalRetailPiastres { get; set; }

        public int SmallestUnitsPerBox => Math.Max(1, StripsPerBox * PiecesPerStrip);

        public decimal CostPriceBoxPounds
        {
            get => CostPriceBoxPiastres / 100m;
            set => CostPriceBoxPiastres = (long)Math.Round(value * 100m);
        }

        public decimal RetailPriceBoxPounds
        {
            get => RetailPriceBoxPiastres / 100m;
            set => RetailPriceBoxPiastres = (long)Math.Round(value * 100m);
        }

        public decimal TotalCostPounds => TotalCostPiastres / 100m;
        public decimal TotalRetailPounds => TotalRetailPiastres / 100m;

        public string FormattedCostPrice => Money.FromPiastres(CostPriceBoxPiastres).ToFormattedString();
        public string FormattedRetailPrice => Money.FromPiastres(RetailPriceBoxPiastres).ToFormattedString();
        public string FormattedTotalCost => Money.FromPiastres(TotalCostPiastres).ToFormattedString();
        public string FormattedTotalRetail => Money.FromPiastres(TotalRetailPiastres).ToFormattedString();

        public string FormattedQuantity
        {
            get
            {
                var parts = new List<string>();
                if (QuantityBoxes > 0) parts.Add($"{QuantityBoxes} علبة");
                if (QuantityStrips > 0) parts.Add($"{QuantityStrips} شريط");
                if (QuantityUnits > 0) parts.Add($"{QuantityUnits} قرص");
                return parts.Count > 0 ? string.Join(" + ", parts) : "0";
            }
        }

        public void RecalculateLine()
        {
            int perBox = SmallestUnitsPerBox;
            int piecesPerStrip = Math.Max(1, PiecesPerStrip);

            TotalSmallestUnits = (QuantityBoxes * perBox) + (QuantityStrips * piecesPerStrip) + QuantityUnits;

            if (perBox > 0)
            {
                // تكلفة الوحدة الصغرى بالقروش مع التقريب
                long costPerUnit = (CostPriceBoxPiastres + (perBox / 2)) / perBox;
                long retailPerUnit = (RetailPriceBoxPiastres + (perBox / 2)) / perBox;

                TotalCostPiastres = (long)Math.Round(((decimal)TotalSmallestUnits * CostPriceBoxPiastres) / perBox);
                TotalRetailPiastres = (long)Math.Round(((decimal)TotalSmallestUnits * RetailPriceBoxPiastres) / perBox);
            }
        }
    }
}
