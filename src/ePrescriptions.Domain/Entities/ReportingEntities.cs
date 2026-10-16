using System;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Domain.Entities
{
    /// <summary>
    /// بند تقرير المبيعات التفصيلي
    /// </summary>
    public class SalesReportRow
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public PaymentMethod PaymentMethod { get; set; }
        public string PaymentMethodName => PaymentMethod switch
        {
            PaymentMethod.Cash => "نقدي",
            PaymentMethod.Network => "شبكة / فيزا",
            PaymentMethod.OnAccount => "آجل",
            PaymentMethod.Insurance => "تأمين",
            PaymentMethod.Mixed => "مقسّم",
            _ => "نقدي"
        };
        public long SubtotalPiastres { get; set; }
        public long DiscountPiastres { get; set; }
        public long VatPiastres { get; set; }
        public long TotalPiastres { get; set; }
        public long EstimatedCostPiastres { get; set; }
        // الربح = الصافي بدون الضريبة (الضريبة مستحقة للدولة مش ربح) ناقص التكلفة (خطوة 47)
        public long ProfitPiastres => TotalPiastres - VatPiastres - EstimatedCostPiastres;

        public decimal SubtotalPounds => SubtotalPiastres / 100m;
        public decimal TotalPounds => TotalPiastres / 100m;
        public decimal VatPounds => VatPiastres / 100m;
        public decimal ProfitPounds => ProfitPiastres / 100m;
    }

    /// <summary>
    /// بند تقرير حركة ومبيعات الأصناف (الأكثر مبيعاً وركوداً)
    /// </summary>
    public class ItemMovementReportRow
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public int TotalSoldSmallestUnits { get; set; }
        public int CurrentStockSmallestUnits { get; set; }

        public long TotalRevenuePiastres { get; set; }
        public long TotalCostPiastres { get; set; }
        public long TotalProfitPiastres => TotalRevenuePiastres - TotalCostPiastres;

        public decimal TotalRevenuePounds => TotalRevenuePiastres / 100m;
        public decimal TotalProfitPounds => TotalProfitPiastres / 100m;
    }

    /// <summary>
    /// بند تقرير النواقص وحد إعادة الطلب
    /// </summary>
    public class LowStockReportRow
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public int CurrentStockSmallestUnits { get; set; }
        public int MinStockAlertSmallestUnits { get; set; }
        public int DeficitUnits => Math.Max(0, MinStockAlertSmallestUnits - CurrentStockSmallestUnits);
        public int SuggestedOrderUnits => DeficitUnits + (MinStockAlertSmallestUnits * 2);

        public long RetailPriceBoxPiastres { get; set; }

        public decimal RetailPriceBoxPounds => RetailPriceBoxPiastres / 100m;
    }

    /// <summary>
    /// بند تقرير الأدوية قريبة الصلاحية والمنتهية
    /// </summary>
    public class ExpiryReportRow
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string BatchNumber { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public int RemainingSmallestUnits { get; set; }

        public long CostPricePerSmallestUnitPiastres { get; set; }
        public long TotalCostPiastres => RemainingSmallestUnits * CostPricePerSmallestUnitPiastres;

        public int DaysRemaining => (int)(ExpiryDate.Date - DateTime.Today).TotalDays;
        public bool IsExpired => DaysRemaining <= 0;
        public string StatusDisplay => IsExpired ? "منتهي الصلاحية" : $"متبقي {DaysRemaining} يوم";
        public decimal TotalCostPounds => TotalCostPiastres / 100m;
    }

    /// <summary>
    /// ملخص تقييم المخزون المالي
    /// </summary>
    public class InventoryValuationSummary
    {
        public int TotalItemsCount { get; set; }
        public int TotalBatchesCount { get; set; }
        public int TotalSmallestUnitsCount { get; set; }

        public long TotalValuationCostPiastres { get; set; }
        public long TotalValuationRetailPiastres { get; set; }
        public long ExpectedProfitPiastres => TotalValuationRetailPiastres - TotalValuationCostPiastres;

        public decimal TotalValuationCostPounds => TotalValuationCostPiastres / 100m;
        public decimal TotalValuationRetailPounds => TotalValuationRetailPiastres / 100m;
        public decimal ExpectedProfitPounds => ExpectedProfitPiastres / 100m;
    }

    /// <summary>
    /// ملخص المبيعات المالي للفترة
    /// </summary>
    public class SalesPeriodSummary
    {
        public int TotalInvoicesCount { get; set; }

        /// <summary>إجمالي فواتير البيع (شامل الضريبة، قبل المرتجعات)</summary>
        public long TotalSalesPiastres { get; set; }
        /// <summary>تكلفة الأصناف المباعة (قبل المرتجعات)</summary>
        public long TotalCostPiastres { get; set; }
        /// <summary>ضريبة فواتير البيع (قبل المرتجعات)</summary>
        public long TotalVatPiastres { get; set; }

        // مرتجعات المبيعات في نفس الفترة (خطوة 47): بتتخصم من المبيعات والضريبة والتكلفة
        public long ReturnsTotalPiastres { get; set; }
        public long ReturnsVatPiastres { get; set; }
        public long ReturnsCostPiastres { get; set; }

        /// <summary>صافي المبيعات بعد المرتجعات (شامل الضريبة)</summary>
        public long NetSalesAfterReturnsPiastres => TotalSalesPiastres - ReturnsTotalPiastres;
        /// <summary>صافي الضريبة بعد المرتجعات</summary>
        public long NetVatPiastres => TotalVatPiastres - ReturnsVatPiastres;
        /// <summary>صافي التكلفة بعد المرتجعات</summary>
        public long NetCostPiastres => TotalCostPiastres - ReturnsCostPiastres;
        /// <summary>صافي المبيعات بدون الضريبة وبعد المرتجعات (إيراد حقيقي للصيدلية)</summary>
        public long NetRevenueExVatPiastres => NetSalesAfterReturnsPiastres - NetVatPiastres;

        /// <summary>الربح = الإيراد بدون الضريبة وبعد المرتجعات - التكلفة بعد المرتجعات (الضريبة مش ربح)</summary>
        public long NetProfitPiastres => NetRevenueExVatPiastres - NetCostPiastres;

        public double ProfitMarginPercent => NetRevenueExVatPiastres > 0 ? (double)NetProfitPiastres / NetRevenueExVatPiastres * 100 : 0;

        public long CashSalesPiastres { get; set; }
        public long NetworkSalesPiastres { get; set; }
        public long OnAccountSalesPiastres { get; set; }
        public long InsuranceSalesPiastres { get; set; }

        public decimal TotalSalesPounds => TotalSalesPiastres / 100m;
        public decimal TotalCostPounds => TotalCostPiastres / 100m;
        public decimal TotalVatPounds => TotalVatPiastres / 100m;
        public decimal NetProfitPounds => NetProfitPiastres / 100m;
        public decimal NetSalesAfterReturnsPounds => NetSalesAfterReturnsPiastres / 100m;
        public decimal CashSalesPounds => CashSalesPiastres / 100m;
        public decimal NetworkSalesPounds => NetworkSalesPiastres / 100m;
        public decimal OnAccountSalesPounds => OnAccountSalesPiastres / 100m;
        public decimal InsuranceSalesPounds => InsuranceSalesPiastres / 100m;
    }

    /// <summary>
    /// سطر في كشف حساب (عميل / مورد): مدين ودائن ورصيد تراكمي بالقروش
    /// </summary>
    public class StatementRow
    {
        public DateTime Date { get; set; }
        public string ReferenceNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long DebitPiastres { get; set; }
        public long CreditPiastres { get; set; }
        public long RunningBalancePiastres { get; set; }

        public decimal DebitPounds => DebitPiastres / 100m;
        public decimal CreditPounds => CreditPiastres / 100m;
        public decimal RunningBalancePounds => RunningBalancePiastres / 100m;
    }

    /// <summary>
    /// تقرير تفصيلي لوردية واحدة (تسليم الدرج وإغلاق الوردية) مع مقارنة النقدية المتوقعة بالفعلية وتفصيل طرق الدفع
    /// </summary>
    public class ShiftClosureReport
    {
        public int ShiftId { get; set; }
        public int UserId { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public ShiftStatus Status { get; set; } = ShiftStatus.Open;
        public string? Notes { get; set; }

        // تفصيل الحركات
        public int InvoicesCount { get; set; }
        public int ReturnsCount { get; set; }

        // الرصيد الافتتاحي
        public long OpeningBalancePiastres { get; set; }

        // تفصيل طرق الدفع (المبيعات)
        public long CashSalesPiastres { get; set; }
        public long NetworkSalesPiastres { get; set; }
        public long OnAccountSalesPiastres { get; set; }
        public long InsuranceSalesPiastres { get; set; }
        public long TotalSalesPiastres => CashSalesPiastres + NetworkSalesPiastres + OnAccountSalesPiastres + InsuranceSalesPiastres;

        // المرتجعات
        public long CashReturnsPiastres { get; set; }
        public long NonCashReturnsPiastres { get; set; }
        public long TotalReturnsPiastres => CashReturnsPiastres + NonCashReturnsPiastres;

        // مقارنة النقدية: المتوقع = الافتتاحي + المبيعات النقدية - المرتجعات النقدية
        public long ExpectedCashPiastres { get; set; }
        public long? ActualCashPiastres { get; set; }
        public long? DifferencePiastres { get; set; } // الفارق = الفعلي - المتوقع

        // مؤشرات للعرض
        public bool IsClosed => Status == ShiftStatus.Closed;
        public bool HasDiscrepancy => DifferencePiastres.HasValue && DifferencePiastres.Value != 0;
        public bool IsSurplus => DifferencePiastres.HasValue && DifferencePiastres.Value > 0;
        public bool IsDeficit => DifferencePiastres.HasValue && DifferencePiastres.Value < 0;

        public string DifferenceLabel => !DifferencePiastres.HasValue
            ? "الوردية مفتوحة (لم تُغلق بعد)"
            : DifferencePiastres.Value == 0
                ? "الصندوق مطابق تماماً"
                : DifferencePiastres.Value > 0
                    ? $"زيادة بالصندوق: +{(DifferencePiastres.Value / 100m):N2} ج.م"
                    : $"عجز بالصندوق: {(DifferencePiastres.Value / 100m):N2} ج.م";

        // مقادير بالجنيه للعرض والتقارير
        public decimal OpeningBalancePounds => OpeningBalancePiastres / 100m;
        public decimal CashSalesPounds => CashSalesPiastres / 100m;
        public decimal NetworkSalesPounds => NetworkSalesPiastres / 100m;
        public decimal OnAccountSalesPounds => OnAccountSalesPiastres / 100m;
        public decimal InsuranceSalesPounds => InsuranceSalesPiastres / 100m;
        public decimal TotalSalesPounds => TotalSalesPiastres / 100m;
        public decimal CashReturnsPounds => CashReturnsPiastres / 100m;
        public decimal NonCashReturnsPounds => NonCashReturnsPiastres / 100m;
        public decimal TotalReturnsPounds => TotalReturnsPiastres / 100m;
        public decimal NetSalesPounds => (TotalSalesPiastres - TotalReturnsPiastres) / 100m;
        public decimal ExpectedCashPounds => ExpectedCashPiastres / 100m;
        public decimal? ActualCashPounds => ActualCashPiastres.HasValue ? ActualCashPiastres.Value / 100m : null;
        public decimal? DifferencePounds => DifferencePiastres.HasValue ? DifferencePiastres.Value / 100m : null;
    }

    /// <summary>
    /// سطر تقرير الورديات لكل كاشير للفترة المحددة
    /// </summary>
    public class CashierShiftSummaryRow
    {
        public int ShiftId { get; set; }
        public int CashierId { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public ShiftStatus Status { get; set; }

        public int InvoicesCount { get; set; }
        public int ReturnsCount { get; set; }
        public long OpeningBalancePiastres { get; set; }
        public long CashSalesPiastres { get; set; }
        public long NetworkSalesPiastres { get; set; }
        public long OnAccountSalesPiastres { get; set; }
        public long InsuranceSalesPiastres { get; set; }
        public long TotalSalesPiastres => CashSalesPiastres + NetworkSalesPiastres + OnAccountSalesPiastres + InsuranceSalesPiastres;
        public long CashReturnsPiastres { get; set; }

        public long ExpectedCashPiastres { get; set; }
        public long? ActualCashPiastres { get; set; }
        public long? DifferencePiastres { get; set; }

        public string StatusDisplay => Status == ShiftStatus.Open ? "مفتوحة" : "مغلقة";
        public string DifferenceDisplay => !DifferencePiastres.HasValue
            ? "—"
            : DifferencePiastres.Value == 0
                ? "مطابق"
                : DifferencePiastres.Value > 0
                    ? $"+{(DifferencePiastres.Value / 100m):N2} (زيادة)"
                    : $"{(DifferencePiastres.Value / 100m):N2} (عجز)";

        public decimal OpeningBalancePounds => OpeningBalancePiastres / 100m;
        public decimal CashSalesPounds => CashSalesPiastres / 100m;
        public decimal NetworkSalesPounds => NetworkSalesPiastres / 100m;
        public decimal OnAccountSalesPounds => OnAccountSalesPiastres / 100m;
        public decimal InsuranceSalesPounds => InsuranceSalesPiastres / 100m;
        public decimal TotalSalesPounds => TotalSalesPiastres / 100m;
        public decimal CashReturnsPounds => CashReturnsPiastres / 100m;
        public decimal ExpectedCashPounds => ExpectedCashPiastres / 100m;
        public decimal? ActualCashPounds => ActualCashPiastres.HasValue ? ActualCashPiastres.Value / 100m : null;
        public decimal? DifferencePounds => DifferencePiastres.HasValue ? DifferencePiastres.Value / 100m : null;
    }

    /// <summary>
    /// ملخص إجمالي عجز/زيادة لكل كاشير
    /// </summary>
    public class CashierDiscrepancySummary
    {
        public int CashierId { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public int ShiftsCount { get; set; }
        public int ClosedShiftsCount { get; set; }
        public long TotalSalesPiastres { get; set; }
        public long TotalExpectedCashPiastres { get; set; }
        public long TotalActualCashPiastres { get; set; }
        public long TotalShortagePiastres { get; set; } // مجموع العجز (بالقيمة الموجبة)
        public long TotalSurplusPiastres { get; set; }  // مجموع الزيادة
        public long NetDifferencePiastres => TotalSurplusPiastres - TotalShortagePiastres;

        public decimal TotalSalesPounds => TotalSalesPiastres / 100m;
        public decimal TotalExpectedCashPounds => TotalExpectedCashPiastres / 100m;
        public decimal TotalActualCashPounds => TotalActualCashPiastres / 100m;
        public decimal TotalShortagePounds => TotalShortagePiastres / 100m;
        public decimal TotalSurplusPounds => TotalSurplusPiastres / 100m;
        public decimal NetDifferencePounds => NetDifferencePiastres / 100m;
    }
}
