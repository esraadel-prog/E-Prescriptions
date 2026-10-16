# مهمة: حذف الاسم الشخصي القديم + فوتر "Designed with ❤️ by Maged Fayez"

طلب صاحب المشروع (2026-10-15):
1. احذف الاسم الشخصي القديم من **كل** مكان (كود، XAML، SQL، مثبّت، build.bat، README، اختبارات، توثيق) **إلا** اسم حساب `pharmacy_owner`.
2. أضف في **أسفل** البرنامج نص الفوتر بالظبط: `Designed with ❤️ by Maged Fayez`

## الحالة: ✅ تمت بالكامل (2026-10-15)
- [x] 1) بحث عن كل الورود
- [x] 2) حذف/استبدال: publish.ps1، build.bat، README، المثبّت (الناشر = e_prescriptions)، افتراضي الصيدلي المسؤول (فاضي)، الاسم الإنجليزي (فاضي)، `MainViewModel` (فاضي)، `SalesView` (اسم المستخدم الحالي)، DemoData.sql وmigration 005 (قيم محايدة)، الاختبارات، FIXES.md وPROGRESS.md
- [x] 3) الفوتر: `MainWindow.xaml` (صف سفلي ثابت) و`LoginWindow.xaml` (تحت زر الإغلاق)
- [x] 4) `NoPersonalNameAndFooterTests`: يفحص كل الملفات النصية + وجود الفوتر بالنص بالظبط
- [x] 5) الاختبارات: 494 → 496 (كلها ناجحة) + تحديث FIXES.md/PROGRESS.md + zip

## ملاحظات
- ❤️ في WPF على Windows 7 ممكن يظهر مربع/رمز أبيض وأسود (خط Segoe UI Emoji من Windows 8.1+).
- الفوتر في الواجهة فقط (مش في الإيصالات/الـ PDF الموجهة للعملاء).
