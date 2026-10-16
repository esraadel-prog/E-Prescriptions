using System;

namespace ePrescriptions.Domain.Entities
{
    public class Expense
    {
        public int Id { get; set; }
        public string ExpenseNumber { get; set; } = string.Empty;
        public string Category { get; set; } = "تشغيلي";

        public long AmountPiastres { get; set; }

        public DateTime Date { get; set; } = DateTime.Now;
        public string PaidTo { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class Income
    {
        public int Id { get; set; }
        public string IncomeNumber { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;

        public long AmountPiastres { get; set; }

        public DateTime Date { get; set; } = DateTime.Now;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class JournalEntry
    {
        public int Id { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;
        public string Description { get; set; } = string.Empty;
        public string DebitAccount { get; set; } = string.Empty;  // حساب المدين (من حـ/)
        public string CreditAccount { get; set; } = string.Empty; // حساب الدائن (إلى حـ/)

        public long AmountPiastres { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class VatReturnStatement
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        // المبيعات (المخرجات الضريبية)
        public long TaxableSalesPiastres { get; set; }
        /// <summary>مبيعات بنود معفاة (نسبة ضريبتها 0) — خارج الوعاء الضريبي</summary>
        public long ExemptSalesPiastres { get; set; }
        public long OutputVatPiastres { get; set; }

        // المشتريات (المدخلات الضريبية)
        public long TaxablePurchasesPiastres { get; set; }
        /// <summary>مشتريات بنود معفاة (نسبة ضريبتها 0)</summary>
        public long ExemptPurchasesPiastres { get; set; }
        public long InputVatPiastres { get; set; }

        // صافي الضريبة الواجبة للسداد (أو الاسترداد)
        public long NetVatPayablePiastres => OutputVatPiastres - InputVatPiastres;
    }

    public class CashFlowSummary
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        // التدفقات الداخلة (المقبوضات)
        public long CashSalesPiastres { get; set; }
        public long CustomerReceiptsPiastres { get; set; }
        public long OtherIncomesPiastres { get; set; }

        public long TotalInflowPiastres => CashSalesPiastres + CustomerReceiptsPiastres + OtherIncomesPiastres;

        // التدفقات الخارجة (المدفوعات)
        public long CashPurchasesPiastres { get; set; }
        public long SupplierPaymentsPiastres { get; set; }
        public long OperatingExpensesPiastres { get; set; }
        /// <summary>مرتجعات مبيعات نقدية: تخرج من الدرج (خطوة 47: كانت بتتعدّ كمقبوضات بالغلط)</summary>
        public long CashReturnsPiastres { get; set; }

        public long TotalOutflowPiastres => CashPurchasesPiastres + SupplierPaymentsPiastres + OperatingExpensesPiastres + CashReturnsPiastres;

        // صافي التدفق النقدي
        public long NetCashFlowPiastres => TotalInflowPiastres - TotalOutflowPiastres;
    }

    /// <summary>سعر جرام الذهب عيار 24 المحفوظ لحاسبة الزكاة</summary>
    public class ZakatGoldPrice
    {
        public long GramPricePiastres { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ZakatCalculation
    {
        public bool EvaluateAtCost { get; set; } = true;

        // عناصر وعاء الزكاة
        public long StockValuePiastres { get; set; }
        public long CashAndBankPiastres { get; set; }
        public long GoodReceivablesPiastres { get; set; }
        public long DuePayablesPiastres { get; set; }

        // وعاء الزكاة الشرعي: (مخزون + نقدية + ديون مرجوة) - التزامات حالة
        public long ZakatBasePiastres => Math.Max(0, (StockValuePiastres + CashAndBankPiastres + GoodReceivablesPiastres) - DuePayablesPiastres);

        // النصاب الشرعي: 85 جرام ذهب عيار 24 بالجنيه المصري
        public long GoldGramPricePiastres { get; set; } // لازم يتدخل ويتحفظ (خطوة 29): مفيش قيمة افتراضية

        public long NisabPiastres => 85 * GoldGramPricePiastres;

        // بدون سعر ذهب صالح مفيش نصاب، فالزكاة ما تُعتبرش واجبة (بدل ما النصاب يطلع صفر وكل وعاء يبان واجب)
        public bool IsZakatDue => GoldGramPricePiastres > 0 && ZakatBasePiastres >= NisabPiastres;

        // الحول الهجري (2.5%) أو الميلادي (2.577%)
        public bool IsHijriYear { get; set; } = true;

        /// <summary>
        /// احتساب مقدار الزكاة بالقروش مع التقريب الصحيح:
        /// هجري: وعاء * 25 / 1000 = وعاء * 2.5%
        /// ميلادي: وعاء * 2577 / 100000 = وعاء * 2.577%
        /// </summary>
        public long ZakatAmountPiastres
        {
            get
            {
                if (!IsZakatDue || ZakatBasePiastres <= 0) return 0;

                if (IsHijriYear)
                {
                    // 2.5% = (Base * 25 + 500) / 1000
                    return ((ZakatBasePiastres * 25) + 500) / 1000;
                }
                else
                {
                    // 2.577% = (Base * 2577 + 50000) / 100000
                    return ((ZakatBasePiastres * 2577) + 50000) / 100000;
                }
            }
        }
    }
}
