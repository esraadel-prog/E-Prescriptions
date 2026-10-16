namespace ePrescriptions.Domain.Enums
{
    public enum PaymentMethod
    {
        Cash = 1,        // نقدي
        Network = 2,     // شبكة / مدى
        OnAccount = 3,   // آجل (على الحساب)
        Insurance = 4,   // تأمين
        Mixed = 5        // دفع مقسّم (أكتر من طريقة في نفس الفاتورة، التفاصيل في InvoicePayments)
    }

    public enum PriceCategory
    {
        Retail = 1,      // قطاعي
        Wholesale = 2    // جملة
    }

    public enum InvoiceType
    {
        Sales = 1,       // فاتورة مبيعات
        Return = 2       // مرتجع مبيعات
    }

    public enum InvoiceStatus
    {
        Completed = 1,
        Suspended = 2,
        Cancelled = 3
    }

    public enum UnitLevel
    {
        Box = 1,         // علبة (الوحدة الكبرى)
        Strip = 2,       // شريط (الوحدة المتوسطة)
        Piece = 3,       // قرص (أصغر وحدة مخزنية)
        Ampoule = 4,     // أمبول
        Sachet = 5,      // كيس
        Unit = 6         // وحدة
    }

    public enum UserRole
    {
        Admin = 1,       // مدير إدارة الصيدلية (صلاحيات كاملة على الموظفين والتشغيل)
        Pharmacist = 2,  // صيدلي
        Cashier = 3,     // كاشير

        /// <summary>
        /// صاحب الصيدلية: صلاحيات تفوق المدير، وحسابه ونشاطه غير مرئيين لأي دور آخر بما فيه المدير.
        /// لا يُعرض هذا الدور في أي شاشة إدارة مستخدمين أو قائمة اختيار؛ يُستثنى دائماً من UserRepository.GetAll()
        /// ولا يظهر إلا لصاحبه بعد تسجيل الدخول بنفس بيانات حسابه (لا توجد قائمة مستخدمين في شاشة الدخول أصلاً).
        /// </summary>
        Owner = 99
    }

    public enum ShiftStatus
    {
        Open = 1,
        Closed = 2
    }
}
