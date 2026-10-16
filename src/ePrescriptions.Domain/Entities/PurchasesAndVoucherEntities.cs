using System;
using System.Collections.Generic;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Domain.Entities
{
    public class PurchaseInvoice
    {
        public int Id { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public DateTime InvoiceDate { get; set; } = DateTime.Today;

        public long SubtotalPiastres { get; set; }
        public long VatPiastres { get; set; }
        public long TotalPiastres { get; set; }
        public long PaidPiastres { get; set; }
        public long RemainingPiastres { get; set; }

        public decimal TotalPounds => TotalPiastres / 100m;
        public decimal PaidPounds => PaidPiastres / 100m;
        public decimal RemainingPounds => RemainingPiastres / 100m;

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<PurchaseInvoiceItem> Items { get; set; } = new();
    }

    public class PurchaseInvoiceItem
    {
        /// <summary>نسبة الضريبة المحددة وقت الإدخال (0 = معفى). null = بند قديم قبل تخزين النسبة.</summary>
        public int? VatPercent { get; set; }

        public int Id { get; set; }
        public int PurchaseInvoiceId { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;
        public string BatchNumber { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; } = DateTime.Today.AddYears(2);

        /// <summary>
        /// معرّف التشغيلة التي أُنشئت لهذا البند عند حفظ فاتورة الشراء (تُستخدم لطباعة ملصقات الصلاحية)
        /// </summary>
        public int BatchId { get; set; }

        public UnitLevel UnitLevel { get; set; } = UnitLevel.Box;
        public int QuantityPurchased { get; set; } = 1;
        public int SmallestUnitsCount { get; set; } = 1;

        public long UnitCostPiastres { get; set; }
        public long VatPiastres { get; set; }
        public long TotalPiastres { get; set; }
    }

    public class CustomerVoucher
    {
        public int Id { get; set; }
        public string VoucherNumber { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;

        public long AmountPiastres { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class SupplierVoucher
    {
        public int Id { get; set; }
        public string VoucherNumber { get; set; } = string.Empty;
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;

        public long AmountPiastres { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class StockAdjustment
    {
        public int Id { get; set; }
        public string AdjustmentNumber { get; set; } = string.Empty;
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;
        public int BookSmallestUnits { get; set; }
        public int ActualSmallestUnits { get; set; }
        public int DifferenceSmallestUnits => ActualSmallestUnits - BookSmallestUnits;
        public string Reason { get; set; } = "تسوية جردية";
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
