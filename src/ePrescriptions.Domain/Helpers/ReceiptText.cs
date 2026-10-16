using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Domain.Helpers
{
    /// <summary>نصوص ثابتة في الإيصال الحراري (منطق بحت علشان يتختبر)</summary>
    public static class ReceiptText
    {
        /// <summary>
        /// عنوان الإيصال حسب النوع وطريقة الدفع. كان بيطبع "فاتورة مبيعات نقدية" لأي فاتورة بيع حتى لو آجل أو تأمين.
        /// </summary>
        public static string InvoiceTitle(InvoiceType type, PaymentMethod method)
        {
            if (type == InvoiceType.Return) return "إشعار دائن (مرتجع مبيعات)";
            switch (method)
            {
                case PaymentMethod.Cash: return "فاتورة مبيعات نقدية";
                case PaymentMethod.Network: return "فاتورة مبيعات (شبكة / بطاقة)";
                case PaymentMethod.OnAccount: return "فاتورة مبيعات آجلة";
                case PaymentMethod.Insurance: return "فاتورة مبيعات تأمين";
                default: return "فاتورة مبيعات";
            }
        }
    }
}
