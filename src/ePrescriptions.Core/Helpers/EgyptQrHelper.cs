using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// أدوات الفاتورة ورمز QR البسيط والتحقق من شكل الرقم الضريبي المصري.
    /// <para>
    /// <b>تنبيه (خطوة 43):</b> نص الـ QR هنا صيغة نصية بسيطة خاصة بهذا البرنامج لقراءة بيانات الفاتورة بالكاميرا فقط.
    /// <b>مش</b> صيغة مصلحة الضرائب المصرية (ETA) للإيصال/الفاتورة الإلكترونية، ولا صيغة ZATCA السعودية، ولا أي صيغة معتمدة،
    /// ومش وعد بالمطابقة مع أي منظومة. ما بيتبعتش لأي جهة ومفيش توقيع إلكتروني ولا UUID (قرار صاحب المشروع، خطوة 20: لا تكامل).
    /// </para>
    /// </summary>
    public static class EgyptQrHelper
    {
        /// <summary>
        /// توليد نص QR بسيط غير مشفر يحمل رقم الفاتورة والتاريخ والإجمالي والرقم الضريبي (وقبلهم اسم الصيدلية لو اتبعت).
        /// الصيغة الفعلية: <c>[الاسم | ]فاتورة: رقم | التاريخ: تاريخ | الإجمالي: 115.50 ج.م | س.ض: الرقم الضريبي</c>.
        /// الإجمالي بصيغة ثابتة الثقافة (نقطة عشرية، بدون فاصل آلاف، رقمين عشريين) عشان النص يطلع نفسه على أي جهاز
        /// مهما كانت ثقافة ويندوز (في ar-EG الفاصل العشري "٫" والآلاف "٬").
        /// </summary>
        public static string GenerateSimpleInvoiceQrText(
            string invoiceNumber,
            DateTime invoiceDate,
            decimal totalWithVat,
            string taxRegistrationNumber,
            string sellerName = "")
        {
            string cleanSeller = string.IsNullOrWhiteSpace(sellerName) ? string.Empty : $"{sellerName.Trim()} | ";
            return $"{cleanSeller}فاتورة: {invoiceNumber} | التاريخ: {DbDate.Minutes(invoiceDate)} | الإجمالي: {totalWithVat.ToString("0.00", CultureInfo.InvariantCulture)} ج.م | س.ض: {taxRegistrationNumber}";
        }

        /// <summary>
        /// توليد نص QR بسيط للفاتورة بقيمة الإجمالي بالقروش
        /// </summary>
        public static string BuildSimpleQrText(
            string invoiceNumber,
            DateTime invoiceDate,
            long totalPiastres,
            string taxRegistrationNumber,
            string sellerName = "")
        {
            decimal totalPounds = totalPiastres / 100m;
            return GenerateSimpleInvoiceQrText(invoiceNumber, invoiceDate, totalPounds, taxRegistrationNumber, sellerName);
        }

        /// <summary>
        /// التحقق من رقم التسجيل الضريبي المصري: أرقام فقط مع مطابقة الطول المطلوب (افتراضياً 9 أرقام).
        /// </summary>
        public static bool ValidateTaxRegistrationNumber(string? taxNumber, int expectedLength = 9)
        {
            if (string.IsNullOrWhiteSpace(taxNumber))
                return false;

            string clean = taxNumber.Trim();
            if (expectedLength > 0 && clean.Length != expectedLength)
                return false;

            return Regex.IsMatch(clean, @"^\d+$");
        }
    }
}
