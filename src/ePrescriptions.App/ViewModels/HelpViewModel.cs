using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ePrescriptions.App.ViewModels
{
    public class ShortcutItem
    {
        public string Key { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class HelpViewModel : ViewModelBase
    {
        public ObservableCollection<ShortcutItem> Shortcuts { get; } = new();

        public HelpViewModel()
        {
            Title = "الاختصارات السريعة ودليل التشغيل";

            Shortcuts.Add(new ShortcutItem { Key = "F1", Action = "فاتورة جديدة", Description = "بدء فاتورة مبيعات جديدة وتفريغ الحقول وضبط المؤشر في خانة البحث" });
            Shortcuts.Add(new ShortcutItem { Key = "F2", Action = "حفظ الفاتورة", Description = "حفظ الفاتورة وخصم المخزون بنظام FEFO وترحيل المبلغ لدرج النقدية" });
            Shortcuts.Add(new ShortcutItem { Key = "F3", Action = "طباعة الفاتورة", Description = "طباعة إيصال حراري فوري 80mm (مع رمز QR بسيط لو مفعّل)" });
            Shortcuts.Add(new ShortcutItem { Key = "F4", Action = "البحث والباركود", Description = "نقل مؤشر الكتابة فوراً إلى حقل إدخال الباركود والاسم السريع" });
            Shortcuts.Add(new ShortcutItem { Key = "F8", Action = "تعليق الفاتورة", Description = "حفظ الفاتورة مؤقتاً لخدمة عميل آخر والعودة إليها لاحقاً" });
            Shortcuts.Add(new ShortcutItem { Key = "F9", Action = "وضع المرتجع", Description = "التبديل بين وضع البيع العادي ووضع مرتجع المبيعات" });
            Shortcuts.Add(new ShortcutItem { Key = "F10", Action = "فتح درج النقدية", Description = "إرسال نبضة إلكترونية ESC/POS لفتح درج الكاشير فوراً" });
            Shortcuts.Add(new ShortcutItem { Key = "Enter", Action = "إضافة الصنف", Description = "إدراج الصنف المختار بالكمية الافتراضية إلى جدول بنود الفاتورة" });
            Shortcuts.Add(new ShortcutItem { Key = "Tab", Action = "التنقل السلس", Description = "التنقل بين حقول الكمية، الخصم، والمبلغ المدفوع بسرعة" });
            Shortcuts.Add(new ShortcutItem { Key = "Esc", Action = "إلغاء أو إغلاق", Description = "إلغاء العملية الحالية أو مسح صندوق البحث" });
        }
    }
}
