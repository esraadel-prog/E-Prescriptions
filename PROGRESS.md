# PROGRESS.md — سجل تقدم إكمال e_prescriptions

> **ملاحظة تشغيل الاختبارات (مهمة):** بيئة العمل Linux بدون Visual Studio ولا NuGet، فمش ممكن تشغيل `dotnet test` الحقيقي (net48 + WPF + xunit).
> بدلاً منه بُني مشغّل بديل (خارج المشروع) يترجم ملفات `tests/ePrescriptions.Tests/*.cs` الحقيقية مع طبقات Core/Domain/Data الحقيقية
> فوق SQLite حقيقي (libsqlite3)، مع بدائل صغيرة لـ xunit وFluentAssertions. **مش بديل عن تشغيل Visual Studio/CI على Windows (خطوة 42 و45).**
> الملف `ReportingAndExportTests.cs` (يحتاج ClosedXML/PdfSharp) غير مشمول في المشغّل البديل.
> الأعداد المسجلة تحت = عدد الاختبارات اللي اشتغلت فعلاً في المشغّل البديل.

## عمل سابق قبل هذا البرومبت (إصدار v10) — موثّق بالتفصيل في FIXES.md
نُفّذ قبل كتابة البرومبت، ويغطي جزءاً من خطوات 4 و5 و6 و7 و8 و9. حالة كل خطوة مقابل معيار قبولها:
- **خطوة 4 (منع بيع المنتهي + تحذير 90 يوم):** منفّذة ✅ (فلتر الصلاحية في `GetActiveBatches` + رفض البيع في `SaveInvoice` + تحذير في `SalesViewModel`). اختبارات: `Sale_ShouldSkipExpiredBatches_...` و`ExpiredBatch_EvenIfItIsTheOnlyOne_ShouldNeverBeSold_AndSaleGoesNegative`.
- **خطوة 5 (ربط المرتجع بالفاتورة الأصلية):** منفّذة ✅ (`Invoices.OriginalInvoiceId` + `InvoiceItems.OriginalInvoiceItemId` + التحقق + تحميل الفاتورة في الواجهة). اختبار: `Return_ShouldRequireOriginalInvoice_AndRespectSoldQuantity`.
- **خطوة 6 (شطب/مرتجع للمورد):** منفّذة ✅ (الشاشة + الخصم + التدقيق + حقل السبب بـ Migration 009). اختبارات: `SupplierReturn_ShouldDeductStock_...` و`SupplierReturn_ShouldStoreReason`.
- **خطوة 7 (الرصيد السالب):** منفّذة بتصميم **مختلف عن معيار القبول** — راجع "قرارات معلّقة" تحت. اختبار: `NegativeBalance_AfterPurchase_ShouldKeepTotalEqualToSumOfBatches` (الإجمالي 15 والتشغيلة 15).
- **خطوة 8 (باركود التشغيلة):** منفّذة بصيغة مختلفة (رقمية `99`+رقم التشغيلة بدل `ITEM|id|batch|date`) + تحذير التشغيلة الأقدم — راجع "قرارات معلّقة". اختبار: `BatchLabel_ShouldResolveToSameItemBatchAndExpiry_AndOlderBatchShouldBeDetected`.
- **خطوة 9 (تتبع التشغيلات):** منفّذة ✅ (جدول `InvoiceItemBatches` بأعمدة `BatchId/Units` بدل `BatchNumber/QuantitySmallestUnits`). اختبار: `SaleAcrossTwoBatches_ShouldRecordTwoAllocationRowsWithCorrectQuantities`.

## قرارات معلّقة تحتاج موافقة صاحب المشروع
1. **خطوة 7:** معيار القبول بيقول إن فاتورة شراء 20 وحدة على رصيد -5 تنشئ تشغيلة فيها 20 وحدة والإجمالي 15. ده بيخلي مجموع التشغيلات (20) ≠ إجمالي الصنف (15) ويظهر 5 وحدات وهمية قابلة للبيع.
   المنفّذ حالياً: الإجمالي 15 **والتشغيلة 15** (الـ 5 المباعة بالسالب بتتخصم من التشغيلة الجديدة)، فمجموع التشغيلات = الإجمالي دايماً. لو عايز السلوك الحرفي للمعيار قول وأغيّره.
2. **خطوة 8:** الباركود الرقمي القصير اتختار عشان قارئ الباركود بيكتب كأنه كيبورد وحروف `ITEM|` بتتلخبط لو لغة الكيبورد عربي. التاريخ والصنف بيتجابوا من القاعدة بالتشغيلة.

---

## خطوة 1 — إصلاح حسابات التشغيل الافتراضية الباظة
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: src/ePrescriptions.Data/Migrations/002_SeedData.sql، tests/ePrescriptions.Tests/SessionAndLabelTests.cs، tests/ePrescriptions.Tests/DatabaseMigrationsTests.cs، tests/ePrescriptions.Tests/SetupAndSecurityTests.cs (جديد)
عدد الاختبارات قبل/بعد: 44 (43 ناجح + 1 فاشل = اختبار دخول admin/admin123 اللي كان بيكشف المشكلة) → 45 (كلها ناجحة)
ملخص سطرين: حُذف إدخالا `admin` و`pharmacist` من 002_SeedData.sql (الـ hash المخزّن ما كانش بيطابق أي كلمة مرور — اتأكد بحساب PBKDF2-SHA1 فعلي). أُضيف اختبار `SeedData_ShouldNotInsertAnyUserRows` يتأكد إن الملف مفيهوش أي INSERT في Users.
ملاحظات/قرارات اتخذتها: (1) اختبار `UserSessionService_LoginAndLock` كان بيعتمد على حساب admin من البذرة، فبقى بينشئ المدير بنفسه عبر `SaveStaffUser` قبل الدخول (الاختبار بيتحقق من نفس السلوك). (2) اختبار `DatabaseMigrationsTests` كان بيشترط ≥2 مستخدم؛ بقى يشترط مستخدم واحد بالظبط (Owner) لأن الحذف هو الهدف من الخطوة. (3) حساب Owner لسه بيتنشأ بكلمة مرور ثابتة لحد خطوة 2.

## خطوة 2 — شاشة إعداد أول تشغيل (First-Run Setup Wizard)
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/DatabaseContext.cs، Data/Setup/FirstRunSetupService.cs (جديد)، Data/Migrations/002_SeedData.sql (أصبح فاضياً)، Data/Migrations/DemoData.sql (جديد، مصري على الهيكل النهائي)، App/App.xaml.cs، App/ViewModels/FirstRunSetupViewModel.cs (جديد)، App/Views/FirstRunSetupWindow.xaml(.cs) (جديد)، tests/.../SetupAndSecurityTests.cs
عدد الاختبارات قبل/بعد: 45 → 50 (كلها ناجحة)
ملخص سطرين: التطبيق بقى يستدعي `InitializeAndMigrate(seedData:false)` فمفيش بيانات تجريبية ولا حساب Owner افتراضي. لو مفيش Owner تظهر شاشة تطلب اسم الصيدلية/الرقم الضريبي (اختياريين) وكلمة مرور Owner (8+ وغير الافتراضية وتتأكد مرتين) وخيار "بيانات تجريبية" غير مفعّل افتراضياً. العميل النقدي (رقم 1) بيتنشأ دايماً لأنه سجل أساسي.
ملاحظات/قرارات اتخذتها: (1) نُفّذت معها **خطوة 22**: البيانات التجريبية بقت في `DemoData.sql` مصرية ومكتوبة على الهيكل النهائي (الـ 002 القديم كان بأعمدة Halalas ومينفعش يتحمّل بعد الـ migrations). (2) `InitializeAndMigrate(seedData:true)` فضلت للاختبارات فقط (بيانات تجريبية + Owner بكلمة افتراضية). (3) التثبيتات القديمة اللي فيها Owner بكلمة `ChangeMe@Owner1` لسه محتاجة خطوة 46. (4) في التثبيت النظيف بدون بيانات تجريبية، إعدادات الصيدلية بتتكتب فاضية (بدل القيم الافتراضية الشخصية اللي كانت هتظهر على الفواتير). (5) الواجهة (XAML) ما اتعرضتش على Windows، اتأكدت من سلامتها نحوياً فقط.

## خطوة 3 — فرض وجود وردية مفتوحة قبل أي بيع
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/Repositories/InvoiceRepository.cs، App/ViewModels/SalesViewModel.cs، App/Views/SalesView.xaml، App/ViewModels/StaffViewModel.cs، App/ViewModels/MainViewModel.cs، App/Services/UserSessionService.cs، tests/.../SalesShiftTests.cs (جديد)
عدد الاختبارات قبل/بعد: 50 → 52 (كلها ناجحة)
ملخص سطرين: شلت `?? 1`؛ `SaveInvoice` بيرفض أي بيع/مرتجع لو الوردية مش موجودة أو مقفولة (فمفيش فاتورة ولا حركة مخزون)، وشاشة البيع بتعرض شريط "افتح وردية" برصيد افتتاح. 
ملاحظات/قرارات اتخذتها: (1) اكتشفت إن الكاشير مالوش صلاحية شاشة الموظفين، فكان هيبقى مش قادر يفتح وردية بعد المنع؛ فأضفت زر فتح الوردية في شاشة البيع نفسها. (2) `StaffViewModel` كان بيفتح/يقرأ الوردية للمستخدم رقم 1 وباسم "<الاسم الشخصي القديم>" ثابت؛ بقى يستخدم المستخدم المسجّل دخوله ويحدّث وردية الجلسة (`SetCurrentShift(Shift?)` بقت تقبل null عند الإغلاق). (3) شلت الاسم الشخصي الثابت من `MainViewModel` وبدائل `?? 1` للمستخدم في البيع. (4) المرتجع كمان بيحتاج وردية مفتوحة.

## إغلاق فجوات خطوات 4 و6 و7 و8 و9 (بعد الخطوة 3)
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/Migrations/009_SupplierReturnReason.sql (جديد، إصدار القاعدة = 9)، DatabaseContext.cs، SupplierReturnRepository.cs، ExpiryAndReturnEntities.cs، SupplierReturnsViewModel.cs، SupplierReturnsView.xaml، tests/.../ExpiryReturnsAndLabelsTests.cs
عدد الاختبارات قبل/بعد: 52 → 57 (كلها ناجحة)
ملخص سطرين: إضافة حقل السبب لمستند المرتجع/الشطب + اختبارات المعايير الناقصة لخطوات 4 و7 و8 و9.

## خطوة 10 — فرض الوصفة الطبية
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/Migrations/010_ItemPrescriptionDetails.sql (جديد، إصدار القاعدة = 10)، DatabaseContext.cs، InvoiceAndSalesEntities.cs (حقلا PrescriptionNumber/DoctorName على InvoiceItem)، InvoiceRepository.cs، SalesItemRow.cs، SalesViewModel.cs، InventoryViewModel.cs، InventoryView.xaml، tests/.../PrescriptionTests.cs (جديد)
عدد الاختبارات قبل/بعد: 57 → 60 (كلها ناجحة)
ملخص سطرين: الصنف اللي `IsPrescriptionRequired` بيتمنع بيعه من غير رقم وصفة واسم طبيب (الفحص في المستودع + رسالة واضحة في الواجهة + تنبيه على السطر)، والقيمتان بتتخزنا على بند الفاتورة.
ملاحظات/قرارات اتخذتها: (1) اكتشفت إن مفيش أي مكان في الواجهة يخلّي الصنف "يتطلب وصفة"، فأضفت خانة اختيار في نموذج تعديل الصنف في المخزون (وتتسجل في سجل التدقيق). (2) الرقم والطبيب بيتاخدوا من حقول الوصفة اللي في رأس الفاتورة وبيتنسخوا على كل بند يتطلب وصفة. (3) المرتجع مش بيتطلب وصفة.

## خطوة 11 — مراقبة حد الائتمان في البيع الآجل
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: InvoiceRepository.cs، InvoiceAndSalesEntities.cs (Invoice.CreditLimitOverridden غير مخزّن)، SalesViewModel.cs، tests/.../CreditLimitTests.cs (جديد)
عدد الاختبارات قبل/بعد: 60 → 64 (كلها ناجحة)
ملخص سطرين: (رصيد العميل + المتبقي من الفاتورة الآجلة) لو أكبر من الحد المسموح، البيع بيترفض لأي دور. Admin/Owner بس بيظهرلهم سؤال تأكيد وبعده بيتسجل البيع. حد الائتمان 0 معناه "لا ائتمان".
ملاحظات/قرارات اتخذتها: (1) الفحص في المستودع (داخل الـ Transaction وبرصيد حديث من القاعدة) مش على قائمة العملاء في الشاشة. (2) تجاوز الحد بيتسجّل في التدقيق — راجع الإضافة تحت. (3) الاختبارات بتحقن دالة التأكيد `ConfirmCreditLimitOverride` بدل نافذة MessageBox.

### إضافة على خطوة 11 — تسجيل تجاوز حد الائتمان في LogAudit (بطلب صاحب المشروع)
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: App/ViewModels/SalesViewModel.cs (مستودعا التدقيق كمعاملين اختياريين + `LogCreditLimitOverride`)، tests/.../CreditLimitTests.cs
عدد الاختبارات قبل/بعد: 64 → 66 (كلها ناجحة)
ملخص سطرين: عند موافقة Admin/Owner على تجاوز الحد وحفظ الفاتورة بنجاح، بيتسجل في التدقيق: اسم العميل، حد الائتمان، رصيده قبل وبعد، المبلغ الآجل ورقم الفاتورة، وعلى اسم اللي وافق. Owner في `OwnerAuditLog` وAdmin في `StaffAuditLog` (من غير خلط بين السجلين).
ملاحظات/قرارات اتخذتها: التسجيل بعد نجاح الحفظ فقط (لو فشل ما يتسجلش موافقة وهمية)، والمبالغ بصيغة ثابتة الثقافة (F2) عشان تطلع نفس الشكل على أي إعداد لغة. فاتورة اترفضت من كاشير/صيدلي ما بتتسجلش (مفيش موافقة).

## خطوة 12 — نقل قاعدة البيانات إلى ProgramData
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Core/Helpers/AppPaths.cs (جديد: مسارات + `LegacyDataMigrator`)، Data/DatabaseContext.cs، Core/Helpers/Logger.cs، App/Services/BackupService.cs، ExcelExportService.cs، PdfReportService.cs، App/App.xaml.cs، installer/e_prescriptions_installer.iss، README.md، tests/.../DataPathsTests.cs (جديد)
عدد الاختبارات قبل/بعد: 66 → 69 (كلها ناجحة)
ملخص سطرين: القاعدة والنسخ الاحتياطية والسجلات والتصدير كلها في `%ProgramData%\ePrescriptions` (`DefaultDatabasePath` بيشاور عليه). عند أول تشغيل بعد التحديث بيتم نسخ `%AppData%\e_prescriptions` (القاعدة + ملفا WAL/SHM + النسخ الاحتياطية) للمسار الجديد من غير حذف القديم ومن غير الكتابة فوق قاعدة جديدة موجودة، وبيتكتب ملف `MIGRATED_TO_PROGRAMDATA.txt` في القديم.
ملاحظات/قرارات اتخذتها: (1) المثبّت بقى بينشئ المجلد بصلاحية `users-modify` وبـ `uninsneveruninstall` (البيانات متتحذفش مع إلغاء التثبيت). (2) لو البرنامج اتشغّل من غير المثبّت، `AppPaths.EnsureDirectory` بيحاول يديّ مجموعة Users صلاحية تعديل وقت إنشاء المجلد (محاولة best-effort، لازم تتجرّب على Windows). (3) **ترحيل بيانات لأكتر من مستخدم ويندوز:** لو كل مستخدم كان عنده قاعدة منفصلة في AppData الخاص بيه، بيتم ترحيل قاعدة أول مستخدم يفتح البرنامج فقط؛ الباقي لازم يتدمج يدوياً (مفيش دمج تلقائي). (4) نسخة الاسترداد من القاعدة القديمة يدوية: انسخ الملف من `%AppData%\e_prescriptions` لو احتجت.

## خطوة 13 — تقوية النسخ الاحتياطي (تسليم على أجزاء)

### 13-أ ✅ الخدمة: VACUUM INTO + قرص خارجي + حد احتفاظ + استعادة مع migrations | تاريخ: 2026-10-11
الملفات المعدّلة: App/Services/BackupService.cs (إعادة كتابة)، Core/Helpers/BackupSettings.cs (جديد)، tests/.../BackupTests.cs (جديد)، tools/linux-test-runner/ (جديد)
عدد الاختبارات قبل/بعد: 69 → 81 (كلها ناجحة)
ملخص سطرين: `CreateBackup` بقى بـ `VACUUM INTO` (متّسق مع القاعدة المفتوحة وWAL) لملف مؤقت + فحص سلامة ثم تسمية؛ و`RunConfiguredBackup` بيكتب نسخة في المجلد الأساسي ونسخة مطابقة على قرص/USB منفصل (ويرفض إن الخارجي = نفس الأساسي)، و`PruneOldBackups` بيحتفظ بعدد محدد من الأحدث بنمط `e_prescriptions_backup_*.db` فقط. `RestoreBackup` بيرفض النسخة الأحدث من البرنامج، بياخد نسخة وقائية، بيستبدل الملف بعد تفريغ الاتصالات وحذف WAL/SHM، ثم `InitializeAndMigrate(seedData:false)` يطبّق الـ migrations الناقصة ويتحقق إن `user_version == LatestSchemaVersion`، وبيرجّع القاعدة السابقة تلقائياً لو فشل.
ملاحظات/قرارات اتخذتها: (1) إعدادات النسخ (مجلد أساسي/خارجي/عدد الاحتفاظ/تلقائي بعد الوردية) في ملف مستقل `backup_settings.ini` وليس جدول الإعدادات، فالاستعادة ما ترجّعهاش لقيم قديمة (هذا تفسيري لبند "عزل ملف الإعدادات"). (2) الحذف بالاسم (التاريخ في الاسم) مش بتاريخ الملف. (3) أقل عدد احتفاظ = 1، والافتراضي 14. (4) اسم النسخة لو اتكرر في نفس الثانية بياخد لاحقة `_01`. (5) بيئة هذه الجلسة بُني فيها مشغّل اختبارات جديد على .NET 8 فوق libsqlite3 (محفوظ في `tools/linux-test-runner`، شوف README فيه) لأن المشغّل القديم ضاع مع الجلسة السابقة؛ ما زال بديلاً عن Visual Studio/CI (خطوة 42 و45).

### 13-ب ✅ الواجهة + النسخ التلقائي بعد إغلاق الوردية + عزل ملف الإعدادات | تاريخ: 2026-10-11
الملفات المعدّلة: App/ViewModels/StaffViewModel.cs، App/ViewModels/SettingsViewModel.cs، App/Views/SettingsView.xaml، tools/linux-test-runner/runner.csproj، tests/.../AutoBackupAfterShiftTests.cs (جديد)
عدد الاختبارات قبل/بعد: 81 → 84 (كلها ناجحة)
ملخص سطرين: `StaffViewModel.CloseShift` بياخد نسخة تلقائية (`RunAfterShiftBackup`) بعد إغلاق الوردية؛ فشل النسخ ما بيلغيش الإغلاق بس بيظهر تنبيه ⚠. شاشة الإعدادات فيها: مجلد النسخ، مجلد القرص الخارجي، عدد الاحتفاظ، خانة النسخ التلقائي، وزر حفظ (بيكتب `backup_settings.ini`)، وزر "نسخة فورية" بقى بيستخدم نفس الإعدادات.
ملاحظات/قرارات اتخذتها: (1) `SettingsViewModel.cs` وملف الـ XAML **ما اتترجموش في المشغّل البديل** (بيعتمدوا على WPF/طابعات)، فتعديلاتهم متراجعة بالقراءة فقط وتحتاج بناء فعلي على Windows (خطوة 42). (2) مفيش زر "استعراض" للمجلد الخارجي (يحتاج WinForms)؛ المسار بيتكتب يدوياً. (3) تنبيه فشل النسخ بيعدي عبر `BackupWarningNotifier` عشان الاختبارات ما تفتحش MessageBox. (4) المتبقي بنص الخطوة 41 (جدولة يومية فعلية + اختبار استعادة دوري + تنبيه) مؤجل لخطوته.

## خطوة 13 — ✅ تمت بالكامل | تاريخ: 2026-10-11
معيار القبول (اختبار نسخ ثم استعادة يتأكد من سلامة البيانات وأن LatestSchemaVersion صحيح بعد الاستعادة): `BackupThenRestore_ShouldBringBackOriginalData_AndLatestSchemaVersion` و`Restore_OfOlderSchemaBackup_ShouldApplyMissingMigrations`.
عدد الاختبارات قبل/بعد الخطوة كلها: 69 → 84 (كلها ناجحة)

## خطوة 14 — توحيد اسم الـ Mutex مع المثبّت
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Core/Helpers/SingleInstanceMutex.cs، tests/.../MutexNameTests.cs (جديد)
عدد الاختبارات قبل/بعد: 84 → 85 (كلها ناجحة)
ملخص سطرين: الاسم الموحّد `e_prescriptions_SingleInstance_Mutex_9F82A41C` بقى ثابت `MutexBaseName` في الكود، والمثبّت (`CheckForMutexes`) ما اتغيّرش لأنه كان بالاسم ده. الاختبار بيقرأ ملف `.iss` ويتأكد إن كل استدعاءات `CheckForMutexes` مطابقة حرفياً للثابت، وإن الاسم القديم اختفى.
ملاحظات/قرارات اتخذتها: التطبيق بيفتح الـ Mutex بالاسم `Global\` + الاسم الأساسي (عشان يغطي كل جلسات ويندوز)، والمثبّت بيفحص الاسم الأساسي بدون البادئة. **لازم يتأكد على Windows (خطوة 42)** إن Inno Setup فعلاً بيكتشف الـ Mutex ذي البادئة `Global\`؛ لو لا، يتغيّر اسم المثبّت لـ `Global\...`.

## خطوة 15 — توليد القيود المحاسبية فعلياً
✅ تمت | تاريخ: 2026-10-11
الملفات المعدّلة: Data/Repositories/JournalPosting.cs (جديد)، InvoiceRepository.cs، PurchaseAndSupplierRepositories.cs، tests/.../JournalPostingTests.cs (جديد)
عدد الاختبارات قبل/بعد: 85 → 93 (كلها ناجحة)
ملخص سطرين: `SaveInvoice` و`SavePurchaseInvoice` بيولّدوا قيود اليومية داخل نفس الـ Transaction (فشل القيد = إلغاء المستند كله). بيع: مدين نقدية/شبكة/تأمين (المدفوع) + عملاء (المتبقي)، دائن إيرادات المبيعات (الصافي) + ضريبة القيمة المضافة؛ مرتجع: العكس بحساب مردودات المبيعات؛ شراء: مدين مخزون (الإجمالي)، دائن نقدية (المدفوع) + موردين (المتبقي). المصروف كان بيولّد قيده أصلاً (اختبار تأكيد أضيف).
ملاحظات/قرارات اتخذتها: (1) جدول القيود صف واحد = مدين واحد/دائن واحد، فالقيد المتعدد الأطراف بيتفكّك لصفوف بأرقام `JV-<رقم المستند>-01`، `-02`..، والمجموعان لازم يتساويا وإلا استثناء. (2) المتبقي/الآجل بيُؤخذ من `RemainingPiastres`، والباقي من الإجمالي نقدي (فالباقي الزائد/الفكة ما بيدخلش في القيد). (3) **غير مشمول:** تكلفة البضاعة المباعة، وسندات القبض/الصرف للعملاء والموردين، وإلغاء الفاتورة (خطوة 16 لازم تعكس قيدها)، والقيود ما بتتولّدش بأثر رجعي للفواتير القديمة. (4) تقارير الأرباح/الزكاة لم تتغيّر ولا تقرأ من القيود حالياً (راجع خطوة 47).

## خطوة 16 — إمكانية إلغاء فاتورة
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Data/Migrations/011_InvoiceCancellation.sql (جديد، إصدار القاعدة = 11)، DatabaseContext.cs، InvoiceAndSalesEntities.cs (Invoice.Status)، InvoiceRepository.cs (`CancelInvoice` + `FindByNumber` + رفض مرتجع على ملغاة)، SalesViewModel.cs، SalesView.xaml، tests/.../InvoiceCancellationTests.cs (جديد)، tests/.../BackupTests.cs (تعديل اختبار الاستعادة لإصدار أقدم ليشمل migration 011)
عدد الاختبارات قبل/بعد: 93 → 102 (كلها ناجحة)
ملخص سطرين: عمود `Status` على Invoices (1 مكتملة / 3 ملغاة) + بيانات من ألغى ومتى والسبب. `CancelInvoice` في Transaction واحدة: يرجّع المخزون لنفس التشغيلات (أو يخفّض البيع بالسالب)، يعكس رصيد العميل الآجل ومجاميع الوردية، يولّد قيداً عكسياً `JV-CXL-...`، ويعلّم الفاتورة ملغاة. في شاشة المبيعات صندوق "إلغاء فاتورة" (رقم + سبب + تأكيد) ظاهر لـ Admin/Owner فقط، والتسجيل في التدقيق: Owner في OwnerAuditLog وAdmin في StaffAuditLog.
ملاحظات/قرارات اتخذتها: (1) الإلغاء لفواتير البيع فقط، ويُرفض لو عليها مرتجعات، أو ملغاة بالفعل، أو **وردية الفاتورة اتقفلت** (بعد الإقفال المسار الصحيح هو المرتجع؛ عشان ما نغيّرش أرقام وردية مقفولة). (2) السبب إلزامي. (3) مرتجع على فاتورة ملغاة بيترفض (في البحث وفي الحفظ). (4) استخدمت `InvoiceStatus.Cancelled = 3` الموجود أصلاً بدل Active/Cancelled جديدة. (5) `SalesView.xaml` و`SalesViewModel` الواجهة ما اتترجمتش على WPF فعلي في المشغّل (SalesViewModel بيترجم فيه؛ الـ XAML لا) — يحتاج بناء على Windows (خطوة 42). (6) الفواتير الملغاة اتستبعدت من تقرير المبيعات وملخص الفترة وحركة الأصناف وإقرار الضريبة وملخص النقدية وكشف حساب العميل (`Status <> 3`)، باختبار. تقارير تانية لو اتضافت لازم تعمل نفس الفلتر.

## خطوة 17 — تصحيح ترقيم الفواتير
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Data/Migrations/012_NumberCounters.sql (جديد، إصدار القاعدة = 12)، Data/Repositories/NumberSequence.cs (جديد)، DatabaseContext.cs، InvoiceRepository.cs، PurchaseAndSupplierRepositories.cs، tests/.../InvoiceNumberingTests.cs (جديد)
عدد الاختبارات قبل/بعد: 102 → 108 (كلها ناجحة)
ملخص سطرين: بطل `COUNT(*)+1` خارج الـ Transaction. جدول `Counters` وعدّاد لكل (نوع+سنة) بيتحجز داخل نفس Transaction الحفظ، وأول سطر فيه بياخد قفل الكتابة فمفيش حفظين متزامنين ياخدوا نفس الرقم. `GenerateNext...Number` بقت للعرض فقط (Peek)، و`SaveInvoice` بيكتب الرقم النهائي على الفاتورة (ويصحّح نص الـ QR لو الرقم اختلف عن المعروض). نفس الشيء لفواتير الشراء. فاتورة مرفوضة ما بتستهلكش رقم.
ملاحظات/قرارات اتخذتها: (1) لو العدّاد مش موجود (قاعدة اتحدّثت من النسخ القديمة) بيبدأ من أكبر رقم موجود فعلاً. (2) اختبار تزامن حقيقي: خيطان واتصالان على ملف قاعدة واحد، 12 فاتورة بدون تكرار (اتكرر التشغيل 3 مرات بنجاح). (3) **غير مشمول:** أرقام سندات العملاء/الموردين والمصروفات والقيود لسه `COUNT(*)+1` (مش مطلوبة بالخطوة؛ ممكن توحيدها بنفس `NumberSequence` لاحقاً). (4) لو الرقم النهائي اختلف عن اللي ظهر في الشاشة (حفظ متزامن من جهازين) الإيصال المطبوع بعد الحفظ بيستخدم الرقم النهائي، لكن زر الطباعة في `SalesViewModel` بيبني فاتورة من الشاشة بالرقم المعروض (ما راجعتش ترتيبه مع الحفظ)، فلو اتطبع قبل الحفظ في حالة التزامن ممكن يحمل الرقم القديم — يتراجع في خطوة 42.

## خطوة 18 — تقوية أمان كلمات المرور
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Core/Security/PasswordHasher.cs، Data/Migrations/013_LoginLockout.sql (جديد، إصدار القاعدة = 13)، DatabaseContext.cs، UserAndShiftRepositories.cs، App/Services/UserSessionService.cs (+`IdleLockMonitor`)، App/MainWindow.xaml.cs، App/App.xaml.cs، App/ViewModels/LoginViewModel.cs، tests/.../PasswordSecurityTests.cs (جديد)، tests/.../SessionAndLabelTests.cs (اختبار اسم المستخدم الافتراضي اتغيّر معناه)، tests/.../BackupTests.cs
عدد الاختبارات قبل/بعد: 108 → 117 (كلها ناجحة)
ملخص سطرين: التجزئة بقت PBKDF2-HMAC-SHA256 بـ 210000 تكرار (`sha256:iter:salt:hash`) مع استمرار قبول الصيغة القديمة SHA1 وترقيتها تلقائياً عند أول دخول ناجح (`NeedsRehash`). بعد 5 محاولات فاشلة متتالية الحساب يتقفل 5 دقائق (حتى بكلمة المرور الصحيحة) ويتصفّر العدّاد بالنجاح أو بإعادة تعيين كلمة المرور؛ وفك قفل الشاشة بعد 5 محاولات خاطئة بيعمل تسجيل خروج. قفل تلقائي بعد خمول 10 دقايق (`IdleLockMonitor` + مؤقّت في MainWindow). حقل اسم المستخدم في شاشة الدخول بقى فاضي (مش `admin`).
ملاحظات/قرارات اتخذتها: (1) **اكتشفت وأصلحت خطأ قديم:** `SaveStaffUser` عند تعديل حساب موجود (id ≠ 0) كان بيرجّع الـ id **من غير ما ينفّذ الـ UPDATE** أصلاً — يعني تعديل الموظف وإعادة تعيين كلمة مروره ما كانوش بيشتغلوا. اتصلّح باختبار (ده كمان بيخدم خطوة 32). (2) القفل بيتتبّع الحسابات الموجودة فقط (اسم غير موجود ما بيتعدّش)، ورسالة "الحساب مقفول" بتكشف إن الحساب موجود. (3) مدة الخمول 10 دقايق ثابتة في الكود (`IdleLockMonitor.DefaultTimeout`)؛ مفيش شاشة إعداد لها (صفر يعطّلها برمجياً). (4) 210000 تكرار بيزوّد وقت تسجيل الدخول؛ **لازم يتجرّب على جهاز الصيدلية الأضعف** (خطوة 42) ولو بطيء يتخفّض `IterationCount` (الحسابات القائمة بتتحدّث لحالها لو اتغيّر). (5) تعديلات `MainWindow.xaml.cs` (مؤقّت الخمول) لم تُترجم/تُشغّل في المشغّل البديل (WPF) — تحتاج بناء Windows.

## خطوة 19 — حماية إضافية لسجل التدقيق (سلسلة hash)
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Data/Migrations/014_AuditHashChain.sql (جديد، إصدار القاعدة = 14)، Data/Repositories/AuditHashChain.cs (جديد)، OwnerAuditLogRepository.cs، StaffAuditLogRepository.cs، DatabaseContext.cs، tests/.../AuditHashChainTests.cs (جديد)، tests/.../BackupTests.cs (تعديل اختبار الاستعادة لإصدار أقدم ليشمل migration 014)
عدد الاختبارات قبل/بعد: 117 → 133 (كلها ناجحة)
ملخص سطرين: كل سطر في OwnerAuditLog وStaffAuditLog بقى فيه `PrevHash` و`RowHash` (SHA-256 على محتوى السطر + Id + hash السابق + وسم الجدول)، بيتكتبوا في نفس Transaction الإدخال. `VerifyIntegrity()` في كل مستودع بيكتشف تعديل أي حقل، حذف سطر، إدخال سطر يدوي، أو فك الـ hash، ويحدد أول سطر مكسور.
ملاحظات/قرارات اتخذتها: (1) السطور القديمة قبل الترقية بدون hash وبتتعدّ كبادئة غير محمية (مش كسر)؛ السلسلة بتبدأ من أول سطر جديد. (2) السلسلتان مستقلتان ولا اختلاط بينهما (اختبار صريح). (3) **حدود موثّقة في FIXES.md:** حذف آخر السطور ما بيتكشفش، ومن يعرف الخوارزمية يقدر يعيد بناء السلسلة؛ SQLCipher/مرساة خارجية = الحل الأقوى وخارج نطاق الخطوة. (4) مفيش زر في الواجهة لعرض نتيجة الفحص (تعديل WPF ما اتعملش عشان ما يتعذّرش اختباره هنا). (5) اختبار تزامن (خيطان على ملف واحد، 20 سطر) اتكرر 3 مرات بنجاح.

## إصلاح إضافي: BusyTimeout
✅ تمت | تاريخ: 2026-10-12 (طلب مستقل، مش جزء من خطوة 19)
الملفات المعدّلة: Data/DatabaseContext.cs (`BusyTimeout=5000;` في الـ connection string للملف العادي و`:memory:`)، tools/linux-test-runner/Shim/Sqlite.cs (المشغّل بقى يقرأ المفتاح)
عدد الاختبارات قبل/بعد: 133 → 133 (كلها ناجحة؛ اختبارا التزامن والنسخ التلقائي اتكرروا 3 مرات بدون تعليق أو "database is locked")
ملاحظات: الاختبارات بتنجح بدون المفتاح كذلك (لأن `PRAGMA busy_timeout` بيتنفّذ بعد الفتح)، فالتعديل وقائي. التفاصيل في FIXES.md.

## خطوة 20 — تقييم الفاتورة الإلكترونية المصرية (بحث وتوصية فقط)
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: FIXES.md فقط (قسم "خطوة 20" بالمصادر والتاريخ والتوصية)
عدد الاختبارات قبل/بعد: 133 → 133 (كلها ناجحة، مفيش كود اتغيّر)
ملخص سطرين: الصيدليات مشمولة بالمرحلة السابعة (الفرعية الأولى من 15 مارس 2025، القاهرة الكبرى والإسكندرية) لكن الإلزام بقرار على كل ممول بالاسم، والاستعلام برقم التسجيل من بوابة ETA. البرنامج الحالي غير مربوط ولا متوافق.
ملاحظات/قرارات اتخذتها: (1) المصادر صحفية ناقلة عن المصلحة؛ ما قدرتش أفتح قوائم/قرارات الإلزام الرسمية، فمقدرش أجزم إن صيدلية بعينها ملزمة. (2) لم يُبنَ أي تكامل (مطلوب تأكيد صريح). (3) ترتبط بخطوة 43 (QrCodeText مش صيغة ETA).
**قرار صاحب المشروع (2026-10-12): الصيدلية غير ملزمة ولا يريد تكامل الإيصال الإلكتروني نهائياً — لا يُبنى أي تكامل ولا يُعاد فتح الموضوع.**

## خطوة 21 — تنظيف بقايا usePiastres وPRAGMA table_info المتكرر
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Data/Repositories/InvoiceRepository.cs، tests/.../SuspendedInvoiceTests.cs (جديد)
عدد الاختبارات قبل/بعد: 133 → 134 (كلها ناجحة)
ملخص سطرين: اتحذفت `TableHasColumn` وكل استدعاءاتها (3 مواضع) وبقايا `usePiastres ? "X" : "X"`؛ الأعمدة مضمونة بـ Migration 005. مفيش تغيير سلوك.
ملاحظات/قرارات اتخذتها: (1) البقايا كانت في دالتي الفواتير المعلّقة مش `SaveInvoice`. (2) أضفت اختباراً للفواتير المعلّقة لأنه ما كانش فيه تغطية. (3) سجلات `SettingsRepository.HasColumn` (قراءة قارئ، مش PRAGMA) لم تُمس لأنها خارج نص الخطوة.

## خطوة 22: ✅ تمت ضمن خطوة 2، تم التحقق
تاريخ التحقق: 2026-10-12 (تحقق فقط، بدون تعديل كود)
الفحص: `DemoData.sql` (50 سطر) و`002_SeedData.sql` (4 أسطر، بلا أي INSERT) اتفحصوا بحثاً عن ريال/SAR/halala/مدن سعودية/زاتكا/ضريبة 15%/جوالات 05x: **صفر نتائج**. البيانات بالجنيه والضريبة 14% (الأدوية معفاة CustomVatPercent=0) وعناوين القاهرة، على الهيكل النهائي (Piastres).
ملاحظات تجميلية (مش اتعدّلت، اختيارية): (1) السجل التجاري التجريبي `1010776655` بصيغة السجل السعودي (10 أرقام بتبدأ بـ 1010). (2) باركودات الأصناف التجريبية بتبدأ بـ `628` (بادئة GS1 السعودية؛ مصر 622) والاختبارات معتمدة عليها حرفياً فتغييرها محتاج تعديل اختبارات. (3) رقم الهاتف `0114567890` ناقص خانة (جوال مصري = 11 رقم).

## خطوة 23 — عمود IsDefaultCash في Customers
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Data/Migrations/015_DefaultCashCustomer.sql (جديد، إصدار القاعدة = 15)، DatabaseContext.cs (`EnsureCashCustomer` + migration)، Data/Migrations/DemoData.sql، Domain/Entities/UserAndPartyEntities.cs (`Customer.IsDefaultCash`)، Data/Repositories/CustomerRepository.cs، Data/Repositories/InvoiceRepository.cs، App/ViewModels/SalesViewModel.cs، tests/.../DefaultCashCustomerTests.cs (جديد)، tests/.../BackupTests.cs
عدد الاختبارات قبل/بعد: 134 → 138 (كلها ناجحة)
ملخص سطرين: العميل النقدي بقى معلّم بـ `IsDefaultCash` (فريد، والقواعد القائمة بتعلّم رقم 1 تلقائياً)؛ اتشالت 4 مواضع كانت بتفترض `Id > 1` (3 في InvoiceRepository وواحد في SalesViewModel).
ملاحظات/قرارات اتخذتها: (1) تغيير سلوك مقصود في حالة نادرة: عميل حقيقي برقم 1 مش نقدي بقى عميل عادي (حد ائتمان ورصيد) — اختبار صريح. (2) `SalesViewModel.cs` بيترجم في المشغّل البديل (اتجربت)، لكن مفيش اختبار لمسار `matchedCustomer` نفسه. (3) مسار إلغاء الفاتورة (`CancelInvoice`) اتعدّل بنفس الدالة ومفيش اختبار مخصص لحالة عميل نقدي غير رقم 1 فيه.

## خطوة 24 — توحيد البحث العربي
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Core/Helpers/ArabicTextNormalizer.cs (جديد)، Data/Migrations/016_ItemSearchText.sql (جديد، إصدار القاعدة = 16)، Data/DatabaseContext.cs، Data/Repositories/ItemRepository.cs، tests/.../ArabicSearchTests.cs (جديد)، tests/.../BackupTests.cs (اختبار الاستعادة لإصدار أقدم بيشيل العمود الجديد)، FIXES.md
عدد الاختبارات قبل/بعد: 138 → 145 (كلها ناجحة)
ملخص سطرين: عمود `Items.SearchText` مطبّع (أ/إ/ا، ة/ه، ى/ي، بدون تشكيل/تطويل، أرقام لاتينية، حروف صغيرة) بيتحدّث عند الحفظ، والبحث بيطبّع الكلمة ويطابق كل كلمة بأي ترتيب مع بقاء بحث الكود/الباركود. الأصناف القديمة والتجريبية بتتفهرس تلقائياً.
ملاحظات/قرارات اتخذتها: (1) عمود مطبّع بدل FTS5 (مش مضمون في المكتبة). (2) `%` و`_` حرفية في البحث. (3) البحث بالأصناف هو الموضع الوحيد في الكود حالياً. (4) واجهة XAML ما اتغيّرتش (الـ ViewModels بتنادي `Search` زي ما هي). (5) `SQLiteDataReader` في المشغّل البديل ما فيهوش `GetString/GetValue` فاستخدمت الـ indexer (متوافق مع المكتبة الحقيقية).

## خطوة 25 — ExpiryDate كنص ISO بدون date()
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Core/Helpers/BatchLabelCode.cs (`ExpiryPolicy.ToIso/TodayIso/ParseIso`)، Data/Migrations/017_ExpiryIsoText.sql (جديد، إصدار القاعدة = 17)، Data/DatabaseContext.cs (`NormalizeLegacyExpiryDates`)، Data/Repositories/ItemRepository.cs، ReportRepository.cs، InvoiceRepository.cs، SupplierReturnRepository.cs، PurchaseAndSupplierRepositories.cs، tests/.../ExpiryIsoTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 145 → 151 (كلها ناجحة)
ملخص سطرين: اتشالت كل `date(ExpiryDate)` من WHERE/ORDER BY والمقارنة بقت نصية ISO مباشرة (الفهرس بيشتغل)، والكتابة كلها بـ `ExpiryPolicy.ToIso` ثابتة الثقافة. Migration 017 بتنضّف القيم القديمة وبتضيف مشغّلات ترفض أي صيغة غير `yyyy-MM-dd` على ItemBatches.
ملاحظات/قرارات اتخذتها: (1) الحماية بمشغّلات على ItemBatches فقط. (2) صيغ قديمة غير مفهومة بتتسجّل في الـ Logger وبتتساب. (3) قراءات `DateTime.Parse` بثقافة الجهاز وباقي التواريخ تُوحَّد في خطوة 26. (4) ما اتجرّبش على WPF/Windows فعلي.

## خطوة 26 — توحيد التواريخ بصيغة ثابتة الثقافة
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Core/Helpers/DbDate.cs (جديد) + 31 ملف (كل repositories في Data، DatabaseContext، Core: BatchLabelCode/AppPaths/EgyptQrHelper/Logger، App: ViewModels وServices)، App.xaml.cs (ثقافة افتراضية للخيوط الخلفية)، tests/.../DateCultureTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 151 → 154 (كلها ناجحة؛ اتشغّلت مرتين)
ملخص سطرين: كل كتابة/قراءة/عرض للتواريخ بقى عبر `DbDate` بثقافة ثابتة (مفيش `DateTime.Parse` ولا `ToString("yyyy…")` ولا `{x:yyyy…}` بدون ثقافة في `src`، بفحص آلي). صيغ التخزين ما اتغيّرتش.
ملاحظات/قرارات اتخذتها: (1) مفيش Migration. (2) `StringFormat` في عمودين XAML لم يُمس (يتأكد في خطوة 42). (3) ملفات App غير المترجمة هنا اتعدّلت بالاستبدال النصي وتحتاج بناء Windows. (4) الاختبار بثقافة "عدائية" (فواصل `.`) بدل `ar-SA` لأن المشغّل على Linux قد يكون بدون ICU.

## خطوة 27 — استبدال كل catch فاضية بتسجيل الخطأ
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Data: SettingsRepository، ItemRepository، DatabaseContext؛ Core: AppPaths، SingleInstanceMutex، Logger؛ App: SalesViewModel، PurchasesViewModel، PdfReportService، ThermalReceiptPrinter، BackupService؛ tests/.../NoSilentCatchTests.cs (جديد)؛ FIXES.md
عدد الاختبارات قبل/بعد: 154 → 156 (كلها ناجحة)
ملخص سطرين: اتصلّحت كل الـ `catch` الفاضية في `src` (13 موضع فعلياً مش 9) بتسجيل عبر `Logger` (و`Trace` داخل الـ Logger نفسه). فحص آلي بيمنع رجوعها.
ملاحظات/قرارات اتخذتها: (1) العدد الفعلي 13 مش 9 (موثّق في FIXES.md). (2) سند الصرف لو فشل PDF بيظهر تنبيه في الحالة. (3) ملفات App غير المترجمة هنا اتعدّلت نصياً وتحتاج بناء Windows.

## خطوة 28 — async للتصدير والنسخ الاحتياطي (تسليم على أجزاء)
**جزء أ — خدمة النسخ الاحتياطي: ✅ تم | تاريخ: 2026-10-12**
الملفات المعدّلة: App/Services/BackupService.cs (`RunConfiguredBackupAsync` و`RunAfterShiftBackupAsync` و`RestoreBackupAsync` + `BackupRestoreResult` في الواجهة `IBackupService`)، tests/.../BackupAsyncTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 156 → 159 (كلها ناجحة)
ملخص سطرين: النسخ المتزامنة بقيت زي ما هي (الاختبارات والمسارات الحالية معتمدة عليها)، وأضفنا نسخاً غير متزامنة بتشغّلها على خيط خلفي بـ `Task.Run`. الواجهات (Settings/Staff) لسه ما اتربطتش بيها — ده الجزء ب.
**جزء ب — ربط الورديات والإعدادات: ✅ تم | تاريخ: 2026-10-12**
الملفات المعدّلة: App/ViewModels/StaffViewModel.cs (`CloseShiftAsync` و`RunAutoBackupAfterShiftAsync`، والأمر بقى `AsyncRelayCommand`)، App/ViewModels/SettingsViewModel.cs (نسخ واستعادة async مع `IsBusy`)، tools/linux-test-runner/Shim/Wpf.cs (بديل `AsyncRelayCommand` للمشغّل فقط)، tests/.../AutoBackupAfterShiftTests.cs (+5 اختبارات)، FIXES.md
عدد الاختبارات قبل/بعد: 159 → 164 (كلها ناجحة)
ملخص سطرين: إغلاق الوردية بيتم على خيط الواجهة (سريع) والنسخ التلقائي بعده على خيط خلفي مع `IsBusy` ومنع الضغط المزدوج؛ `CloseShift()` المتزامنة لسه موجودة. نسخ/استعادة الإعدادات نفس الفكرة.
ملاحظات: (1) `SettingsViewModel.cs` مش بيترجم في المشغّل البديل؛ اتفحص نحوياً فقط (صفر أخطاء صياغة) وياخد بناء Windows الحقيقي (خطوة 42). (2) أثناء الاستعادة الأزرار بتتعطّل (AsyncRelayCommand) لكن المستخدم ممكن يتنقل لشاشة تانية؛ يتأكد منه في خطوة 42. (3) الواجهة XAML ما اتغيّرتش.
**جزء ج — Excel وPDF: ✅ تم | تاريخ: 2026-10-12**
الملفات المعدّلة: App/Services/ExcelExportService.cs و PdfReportService.cs (نسخ `...Async` لكل دالة تصدير/توليد على الواجهة)، App/ViewModels/ReportsViewModel.cs (تصدير Excel)، CustomersViewModel.cs (كشف حساب PDF)، SalesViewModel.cs (فاتورة A4 PDF) — الأوامر الثلاثة بقت `AsyncRelayCommand` مع `IsBusy`، tools/linux-test-runner/Shim/Wpf.cs (واجهة PDF للمشغّل)، tests/.../ReportingAndExportTests.cs (+2 اختبار)، FIXES.md
عدد الاختبارات قبل/بعد (المشغّل البديل): 164 → 164 كلها ناجحة (`SalesViewModel` بيترجم هناك). الاختبارين الجداد (Excel async وPDF سند async) في ملف مستبعد من المشغّل (ClosedXML/PdfSharp) **ما اتشغّلوش** — اتفحصوا نحوياً فقط وينتظروا Windows (خطوة 42/45).
ملخص سطرين: كل توليد ملف ثقيل بقى على خيط خلفي بـ `Task.Run`، والواجهة بتبني البيانات على خيطها وبتسلّمها نسخة، وبعد الانتهاء بترجع تعرض النتيجة. الدوال المتزامنة لسه موجودة.
ملاحظات/قرارات اتخذتها: (1) سند صرف المورد PDF في `PurchasesViewModel` فضل متزامن عن قصد: ملف صغير جداً بعد حفظ السند مباشرة، وبعده `LoadData()` بيعتمد على نفس التسلسل؛ الخدمة فيها `GenerateVoucherPdfAsync` جاهزة لو احتجناها. (2) كشف حساب المورد PDF مفيش له زر في الواجهة حالياً فمفيش ربط. (3) `Customers/Reports/Settings` والخدمتين اتفحصوا نحوياً فقط (صفر أخطاء صياغة) وتنتظر بناء Windows حقيقي. (4) كائنات `SalesSummary`/`ValuationSummary` بتتمرر للخيط الخلفي مباشرة (قراءة فقط، مش مربوطة بالواجهة).

## خطوة 28 — ✅ تمت بالكامل (جزء أ + ب + ج) | تاريخ: 2026-10-12

## خطوة 29 — سعر الذهب في الزكاة إدخال إلزامي يُحفظ بدون قيمة افتراضية
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: Data/Migrations/018_ZakatGoldPrice.sql (جديد، إصدار القاعدة = 18)، Data/DatabaseContext.cs، Data/Repositories/FinanceRepository.cs (`GetZakatGoldPrice` و`SaveZakatGoldPrice`، و`CalculateZakat` بيرفض سعر <= 0)، Domain/Entities/FinanceAndZakatEntities.cs (شيل الافتراضي 380000 + `ZakatGoldPrice`)، App/ViewModels/ZakatViewModel.cs (إعادة كتابة كاملة)، App/Views/ZakatView.xaml (زر "حفظ السعر" + سطر آخر سعر محفوظ)، tests/.../ZakatGoldPriceTests.cs (جديد)، tests/.../BackupTests.cs (اختبار الاستعادة لإصدار أقدم بيمسح جدول 018)، tools/linux-test-runner/runner.csproj (ZakatViewModel بيترجم في المشغّل)، FIXES.md
عدد الاختبارات قبل/بعد: 164 → 174 (كلها ناجحة)
ملخص سطرين: السعر الافتراضي 3800 اتشال: الخانة بتبدأ فاضية لحد ما المستخدم يدخل سعر ويضغط "حفظ السعر" (بيتخزّن في جدول `ZakatSettings` مع تاريخ الحفظ، وبيتحمّل تلقائي المرة الجاية). بدون سعر صالح مفيش نصاب ولا زكاة: الشاشة بتعرض "—" ورسالة تطلب الإدخال، والمستودع بيرفض الحساب بسعر <= 0.
ملاحظات/قرارات اتخذتها: (1) جدول منفصل مش عمود في `PharmacySettings` لأن `SaveSettings` بيستخدم `INSERT OR REPLACE` وكان هيمسح السعر (اختبار صريح بيثبت إنه ما بيتمسحش). (2) إصلاح خطر لقيته: `IsZakatDue` كانت بترجع true لأي وعاء لما النصاب = صفر؛ دلوقتي بتتطلب سعر > 0. (3) الحفظ بزر مش تلقائي مع كل ضغطة مفتاح عشان ما يتخزّنش سعر ناقص (3 ثم 38 ثم 380). الحساب بيشتغل لحظياً بالسعر المدخل حتى قبل الحفظ، والشاشة بتبيّن إنه غير محفوظ (`HasUnsavedGoldPrice`)، لكن ما فيش تحذير بصري في XAML للحالة دي. (4) ما فيش تحذير لقدم السعر (التاريخ معروض بس). (5) `ZakatView.xaml` اتفحص كـ XML سليم فقط؛ الربط الفعلي (`TargetNullValue` على TextBox مربوط بـ `decimal?`) يتأكد في خطوة 42.

## خطوة 30 — build.bat يتوقف عند فشل أي اختبار
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: build.bat (كتلة الاختبارات)، tests/.../BuildScriptTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 174 → 177 (كلها ناجحة)
ملخص سطرين: قبل كده فشل الاختبارات كان بيطبع "[تحذير]" ويكمّل لحد "اكتمل البناء بنجاح!" وممكن يبني مثبّت من كود فيه اختبارات فاشلة. دلوقتي بيطبع "[خطأ]" وبيعمل `pause` و`exit /b 1` زي خطوتي الاستعادة والترجمة.
ملاحظات/قرارات اتخذتها: (1) الاختبار بيقرأ build.bat كنص (مش بيشغّله): بيتأكد إن كتلة فحص الاختبارات فيها `exit /b 1` وإن كل كتلة `if %errorlevel% neq 0` في الملف بتخرج. اتجرّب سلبياً: رجّعت التحذير القديم فالاختبارين فشلوا، ورجّعت الملف الصحيح. (2) السلوك الفعلي لـ cmd.exe ما اتجرّبش (مفيش Windows)؛ يتأكد بتشغيل build.bat مع اختبار فاشل عمداً في خطوة 42. (3) لو `vstest.console.exe` مش في PATH (فرع msbuild) البناء هيقف برضه، والرسالة بتذكر ده. (4) لاحظت ولم أعدّل (خارج الخطوة): فشل Inno Setup في آخر الملف بيطبع خطأ لكن الرسالة النهائية بتفضل "اكتمل البناء بنجاح!"، والملف بنهايات أسطر LF مش CRLF.

## خطوة 31 — تدوير ملفات الـ Logger
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: src/ePrescriptions.Core/Helpers/Logger.cs، tests/ePrescriptions.Tests/LoggerRotationTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 177 → 182 (كلها ناجحة)
ملخص سطرين: الملف اليومي بيتدوّر عند 5 ميجا (أجزاء `.1/.2/.3` والأقدم بيتحذف)، وسجلات `error_`/`info_` الأقدم من 30 يوم بتتحذف تلقائياً مرة كل يوم. أي ملف تاني في مجلد السجلات ما بيتلمسش.
ملاحظات/قرارات اتخذتها: (1) الأرقام (5 ميجا / 3 أجزاء / 30 يوم) قراري؛ ثوابت في `Logger` وسهلة التغيير. (2) تاريخ الملف بيتقرأ من اسمه مش من تاريخ التعديل. (3) ما فيش اختبار على تدوير فعلي بتزامن (عدة خيوط)؛ الكتابة كلها جوّا نفس الـ lock.

## خطوة 32 — إدارة الموظفين (تعديل / تعطيل / إعادة تعيين كلمة مرور)
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: src/ePrescriptions.App/ViewModels/StaffViewModel.cs، src/ePrescriptions.App/Views/StaffView.xaml، tests/ePrescriptions.Tests/StaffManagementTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 182 → 193 (كلها ناجحة)
ملخص سطرين: شاشة الموظفين بقى فيها تحديد موظف من القائمة وتعديل اسمه/اسم المستخدم/دوره، وزر تعطيل/تفعيل، وإعادة تعيين كلمة مروره (8 أحرف على الأقل وبتفك أي قفل مؤقت). كله عبر `UserRepository.SaveStaffUser` الموجودة بدون أي تعديل على المستودع.
ملاحظات/قرارات اتخذتها: (1) حساب Owner مرفوض من الأوامر التلاتة في الـ ViewModel (وكمان المستودع بيرفضه). (2) الأوامر الجديدة بترفض لو المستخدم الحالي مش Admin/Owner (لو مفيش جلسة، زي الاختبارات، بيتخطى الفحص زي AddUser). (3) المدير ما يقدرش يعطّل حسابه ولا يغيّر دور نفسه (منع قفل نفسه برّه). (4) تعطيل الحساب بيمنع تسجيل الدخول الجديد فقط؛ جلسة شغّالة حالياً لنفس الموظف بتكمل لحد ما يخرج. (5) أضفت اختيار الدور (مدير/صيدلي/كاشير) في استمارة موظف جديد لأنه ما كانش ليه خانة في الواجهة (كان دايماً صيدلي). (6) أصلحت `AddUser`: رسالة النجاح كانت بتتمسح برسالة `LoadData` فوراً. (7) `StaffView.xaml` اتفحص كـ XML سليم فقط؛ الربط الفعلي (SelectedItem/ComboBox بـ Tag من enum) يتأكد في خطوة 42. (8) ما اتضافش تسجيل تدقيق (LogAudit) لتعديلات الموظفين لأن الخطوة ما طلبتوش؛ مقترح لو حابب.

## خطوة 33 — صلاحيات على مستوى الإجراء (سقف خصم لكل دور + موافقة Admin/Owner)
✅ تمت | تاريخ: 2026-10-12
الملفات المعدّلة: src/ePrescriptions.Domain/Helpers/ActionPermissionPolicy.cs (جديد)، src/ePrescriptions.App/ViewModels/SalesViewModel.cs، src/ePrescriptions.App/Views/SalesView.xaml و SalesView.xaml.cs، tests/ePrescriptions.Tests/ActionPermissionTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 193 → 206 (كلها ناجحة)
ملخص سطرين: سقف خصم لكل دور (كاشير 5٪ / صيدلي 10٪ / Admin وOwner بلا سقف) وأي مرتجع من كاشير/صيدلي بيتطلب موافقة مدير أو صاحب بكتابة اسم مستخدمه وكلمة مروره في لوحة موافقة بالشاشة. الموافقة بتتسجّل في سجل التدقيق الصح باسم الموافق.
ملاحظات/قرارات اتخذتها: (1) الأرقام 5٪ و10٪ (الصيدلي اتعدّل من 15٪ إلى 10٪ بطلب صاحب المشروع، تعديل ثابت واحد في `ActionPermissionPolicy` بدون أي تغيير تاني). (2) الموافقة بإدخال بيانات المدير مش بتسجيل خروج/دخول، ومتحقق منها عبر `Authenticate` (فيها قفل المحاولات الفاشلة). (3) السقف لكل بند مش للفاتورة ككل. (4) `SalesView.xaml`/code-behind اتفحصوا كـ XML سليم فقط؛ ربط PasswordBox ومسح الخانة يتأكدوا في خطوة 42. (5) `SalesViewModel` بيترجم في المشغّل، والـ DI بيحقن `UserRepository` تلقائياً (معامل اختياري في الآخر). (6) خصم المرتجع بيتاخد من الفاتورة الأصلية فمش بيتفحص ضد السقف.

**تعديل بعد التسليم على خطوة 33 | 2026-10-12:** سقف خصم الصيدلي اتغيّر من 15٪ إلى 10٪ بطلب صاحب المشروع (ثابت واحد في `ActionPermissionPolicy.cs` + توثيق FIXES/PROGRESS). الاختبارات 206 → 206 (كلها ناجحة، بدون تعديل أي اختبار).

---

## خطوة 34 — تنبيهات ذكية عند فتح البرنامج (صلاحية، نواقص، تجاوز حد الائتمان)
✅ تمت | تاريخ: 2026-10-13
الملفات المعدّلة: src/ePrescriptions.Data/Repositories/CustomerRepository.cs، src/ePrescriptions.App/Services/SmartAlertsService.cs (جديد)، src/ePrescriptions.App/ViewModels/SmartAlertsViewModel.cs (جديد)، src/ePrescriptions.App/Views/SmartAlertsWindow.xaml و SmartAlertsWindow.xaml.cs (جديد)، src/ePrescriptions.App/App.xaml.cs، tests/ePrescriptions.Tests/SmartAlertsTests.cs (جديد)، tools/linux-test-runner/runner.csproj، FIXES.md
عدد الاختبارات قبل/بعد: 206 → 210 (كلها ناجح)
ملخص سطرين: شاشة ملخص ذكية (`SmartAlertsWindow`) بتفتح تلقائياً بعد تسجيل الدخول مباشرة لو وُجدت تنبيهات (صلاحيات قريبة خلال 90 يوم، نواقص مخزون تحت الحد الأدنى، أو عملاء متجاوزين لحد الائتمان مع استبعاد العميل النقدي). الشاشة بتعرض إحصائيات ملونة وتبويبات مفصلة مع زر متابعة لنقطة البيع.
ملاحظات/قرارات اتخذتها: (1) استعلام `CustomerRepository.GetCreditLimitExceededCustomers()` بيستبعد العميل النقدي العام (`IsDefaultCash = 0`). (2) نافذة التنبيهات لا تظهر إطلاقاً إذا لم يكن هناك أي تنبيهات نشطة، وتفتح بسلاسة على نقطة البيع مباشرة. (3) التواريخ منسقة بثقافة ثابتة `DbDate.Date` متوافقة مع متطلبات خطوة 26. (4) `SmartAlertsWindow.xaml` مفحوص كـ XML سليم ويتأكد بصرياً في خطوة 42.

## خطوة 35 — بدائل الصنف بالمادة الفعالة في شاشة المبيعات
✅ تمت | تاريخ: 2026-10-13 (التنفيذ اتعمل في جلسة سابقة وانقطع الحد قبل التوثيق؛ اتحقق منه وتوثّق في هذه الجلسة)
الملفات المعدّلة: src/ePrescriptions.Data/Repositories/ItemRepository.cs (`GetAlternativesByActiveIngredient`)، src/ePrescriptions.App/ViewModels/ItemAlternativeRow.cs (جديد)، src/ePrescriptions.App/ViewModels/SalesViewModel.cs، src/ePrescriptions.App/Views/SalesView.xaml، tools/linux-test-runner/runner.csproj، tests/ePrescriptions.Tests/ItemAlternativesTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 210 → 215 (كلها ناجحة؛ اتشغّلت فعلاً في هذه الجلسة بالمشغّل البديل: passed=215 failed=0)
ملخص سطرين: لما يتضاف للسلة صنف رصيده <= 0 بتظهر لوحة بدائل بنفس المادة الفعالة (المتاحة بالمخزون فقط، بدون الصنف نفسه) فيها السعر والرصيد المتاح وزر إضافة؛ اختيار بديل بيستبدل الصنف النافد في السلة. الترتيب: الأكبر رصيداً ثم الأرخص سعراً.
ملاحظات/قرارات اتخذتها: (1) المطابقة بالمادة الفعالة مطابقة تامة بعد TRIM وتجاهل حالة الأحرف الإنجليزية؛ مادة مكتوبة بصيغ مختلفة ("Paracetamol" مقابل "Paracetamol 500mg") مش بتتطابق ولا فيه تطبيع عربي لها — يستاهل قرارك لو بياناتك فيها اختلافات كتابة. (2) البيع بالرصيد السالب لسه مسموح (اللوحة تنبيه بس، مش منع). (3) `SalesView.xaml` اتفحص كـ XML سليم فقط؛ الربط الفعلي (WrapPanel/RelativeSource للأمر) يتأكد في خطوة 42. (4) لقيت فرق بسيط: PROGRESS كان مسجّل 210 بعد خطوة 34 والمشغّل بيعدّ 215 قبل أي تعديل مني، والفرق هو اختبارات خطوة 35 الخمسة.

**تعديل لاحق على خطوة 35 | 2026-10-13 (بطلب صاحب المشروع):** البدائل بقت بأسماء المواد الفعالة فقط (تجاهل التركيز والمواد المضافة؛ أي اسم مادة مشترك = بديل). الملفات: Core/Helpers/ActiveIngredientMatcher.cs (جديد)، Data/Repositories/ItemRepository.cs، tests/.../ActiveIngredientMatchingTests.cs (جديد)، FIXES.md. الاختبارات 215 → 230 (كلها ناجحة، اتشغّلت مرتين). ملاحظة: Paracetamol + Vitamin C بيظهر بديل لـ Vitamin C لوحده وبالعكس.


## خطوة 36 — الدفع المقسّم (طريقتا دفع أو أكثر لنفس الفاتورة)
✅ تمت | تاريخ: 2026-10-13
الملفات المعدّلة: Domain: Enums/DomainEnums.cs (`Mixed`)، Entities/InvoiceAndSalesEntities.cs (`InvoicePayment` + `Invoice.Payments`)، Entities/ReportingEntities.cs؛ Data: Migrations/019_InvoicePayments.sql (جديد)، DatabaseContext.cs (الإصدار 19)، Repositories/InvoiceRepository.cs و JournalPosting.cs و ReportRepository.cs؛ App: ViewModels/SalesViewModel.cs، Views/SalesView.xaml، Services/ThermalReceiptPrinter.cs و PdfReportService.cs؛ tests: SplitPaymentTests.cs (جديد)، BackupTests.cs (سطر محاكاة النسخة القديمة)؛ FIXES.md
عدد الاختبارات قبل/بعد: 230 → 245 (كلها ناجحة؛ اتشغّلت مرتين)
ملخص سطرين: فاتورة البيع ممكن تتدفع بجزأين أو أكتر (نقدي/شبكة/آجل/تأمين) ويتخزّن كل جزء بمبلغه في جدول `InvoicePayments`. الوردية والقيود المحاسبية ورصيد العميل والإلغاء والتقارير والإيصال كلها بتتعامل مع كل جزء لوحده، وفواتير الطريقة الواحدة سلوكها ما اتغيّرش.
ملاحظات/قرارات اتخذتها: (1) النقدي = الباقي تلقائياً في الواجهة. (2) الفاتورة المقسّمة بتتخزّن `PaymentMethod = Mixed`. (3) المرتجع بطريقة واحدة. (4) مفيش اختبار على ترجمة الإيصال/PDF/XAML (Windows)؛ التفاصيل في FIXES.md. (5) جزء التأمين بيتجمّع في عمود النقدي بالوردية زي فاتورة التأمين الحالية (سلوك قديم ما اتغيّرش).

---

## خطوة 37 — تقرير إغلاق وردية أوضح ومطابقة النقدية (المرحلة و)
✅ تمت | تاريخ: 2026-10-13
الملفات المعدّلة: src/ePrescriptions.Domain/Entities/ReportingEntities.cs، src/ePrescriptions.Data/Repositories/ReportRepository.cs، src/ePrescriptions.App/ViewModels/StaffViewModel.cs، src/ePrescriptions.App/ViewModels/ReportsViewModel.cs، src/ePrescriptions.App/Views/StaffView.xaml، src/ePrescriptions.App/Views/ReportsView.xaml، src/ePrescriptions.App/Services/ThermalReceiptPrinter.cs، src/ePrescriptions.App/Services/PdfReportService.cs، src/ePrescriptions.App/Services/ExcelExportService.cs، tests/ePrescriptions.Tests/ShiftClosureReportTests.cs (جديد)، tools/linux-test-runner/Shim/Wpf.cs، tools/linux-test-runner/runner.csproj، FIXES.md
عدد الاختبارات قبل/بعد: 245 → 252 (كلها ناجح)
ملخص سطرين: تقرير تفصيلي لإغلاق الوردية ومطابقة النقدية (الرصيد الافتتاحي + المبيعات النقدية - المرتجعات النقدية = النقد المتوقع بالدرج) مع مقارنة النقد الفعلي وإظهار العجز/الزيادة بالألوان وتفصيل طرق الدفع (نقدي/شبكة/آجل/تأمين/مقسم)، بالإضافة لتقرير ملخص عجز وزيادة الورديات مجمّعاً لكل كاشير في شاشة التقارير وتصديره لإكسل وطباعة إيصال حراري Z-Report وPDF.
ملاحظات/قرارات اتخذتها: (1) التقرير متاح في شاشة إدارة الورديات عبر طباعة إيصال حراري Z-Report أو تصدير PDF A4. (2) في شاشة التقارير أُضيف تبويب سادس تفصيلي لورديات الكاشيرات وعجز الصندوق مع تصدير شامل لملف إكسل بشيتين (التفصيلي + ملخص الكاشيرات). (3) الفواتير المقسمة يتم تفصيل أجزائها النقدية والشبكة في حسابات الوردية بدقة.

---

## خطوة 38 — جرد بالباركود (تسليم على أجزاء)
**ملاحظة تصحيح:** سطر "الخطوة التالية" القديم كان مكتوب فيه "خطوة 38 — تقرير ضريبة القيمة المضافة"، وده مش مطابق للبرومبت (خطوة 38 = جرد بالباركود، وكل خطوات المرحلة و التانية في الترتيب 39-41). اتّبعت البرومبت. تقرير الضريبة لو مطلوب يتضاف كطلب مستقل بعد باقي الخطوات.

### جزء أ — منطق الجرد والمستودع ✅ تم | تاريخ: 2026-10-14
الملفات: src/ePrescriptions.Domain/Helpers/StocktakeSession.cs (جديد)، src/ePrescriptions.Data/Repositories/ItemRepository.cs (`ResolveScannedCode` و`ApplyStocktake`)، tests/ePrescriptions.Tests/StocktakeTests.cs (جديد)
عدد الاختبارات قبل/بعد: 252 → 260 (كلها ناجحة)
ملخص: جلسة جرد (كل مسح = علبة، والكمية قابلة للتعديل يدوياً) بتقارن المعدود بالدفتري (مطابق/عجز/زيادة)، وفك الباركود بيقبل ملصق التشغيلة أو باركود/كود الصنف. الاعتماد بيسجّل تسوية جرد (`StockAdjustments`، أرقام `CNT-...-001`) لكل صنف فيه فرق فقط.
قرارات: (1) الجرد الجزئي مسموح: الصنف اللي ما اتمسحش مش بيدخل المقارنة إلا بطلب صريح (`IncludeUncounted`). (2) الاعتماد بيستخدم نفس `RecordStockAdjustment` الموجودة، فبيحدّث إجمالي الصنف فقط زي التسوية اليدوية القديمة (مش التشغيلات). (3) الرصيد الدفتري بيتسجّل وقت أول مسح؛ لو حصل بيع بين المسح والاعتماد، التسوية بتثبّت الرصيد على المعدود.

### جزء ب — شاشة الجرد ✅ تم | تاريخ: 2026-10-14
الملفات: App/ViewModels/StocktakeViewModel.cs، Views/StocktakeView.xaml و.xaml.cs (جديدة)، ViewModels/MainViewModel.cs (`NavigateStocktakeCommand`)، MainWindow.xaml (DataTemplate + زر جانبي)، App.xaml.cs (DI)، tools/linux-test-runner/runner.csproj، tests/.../StocktakeTests.cs (+4 اختبارات VM)
عدد الاختبارات قبل/بعد: 260 → 264 (كلها ناجحة)
ملخص: شاشة "الجرد بالباركود" (صلاحية المخزون: صيدلي/مدير/Owner): خانة مسح تستقبل Enter من القارئ، جدول (مسجّل/معدود/فرق) والمعدود قابل للتعديل، وزر اعتماد بتأكيد ثم تسوية الأصناف اللي فيها فرق وتفريغ الجلسة. التدقيق: Owner → OwnerAuditLog، غيره → StaffAuditLog (اختبار لكل حالة، والسجلين ما بيتخلطوش).
ملاحظات: (1) `StocktakeView.xaml` اتفحص كـ XML سليم فقط؛ تعديل الخلية (`CountedSmallestUnits` بـ LostFocus) والتركيز على خانة المسح يتأكدوا في خطوة 42 على Windows. (2) رسالة التأكيد (MessageBox) مش مغطاة باختبار؛ المنطق نفسه في `ApplyConfirmed` مختبر. (3) مفيش شاشة تعرض تاريخ جلسات الجرد القديمة؛ التسويات بتتخزّن في `StockAdjustments` برقم `CNT-...`.

### جزء ج — طباعة قائمة الجرد ✅ تم | تاريخ: 2026-10-14
الملفات: Domain/Helpers/StocktakeSheetBuilder.cs (جديد)، App/Services/PdfReportService.cs (`GenerateStocktakeSheetPdf` + Async + الواجهة)، App/ViewModels/StocktakeViewModel.cs (`PrintSheetCommand`)، Views/StocktakeView.xaml (زر)، tools/linux-test-runner/Shim/Wpf.cs، tests/.../StocktakeTests.cs (+4)
عدد الاختبارات قبل/بعد: 264 → 268 (كلها ناجحة)
ملخص: زر "طباعة قائمة جرد (PDF)" بيولّد ملف A4 متعدد الصفحات لكل الأصناف النشطة مرتبة بالاسم: م / الكود / الصنف / المسجّل / المعدود (فاضي للكتابة بالقلم) / ملاحظات، وتوقيعات في آخر صفحة. تجهيز السطور منطق بحت مختبر، والـ VM مختبر بخدمة PDF وهمية.

## خطوة 38 — جرد بالباركود
✅ تمت (أ + ب + ج) | تاريخ: 2026-10-14
الملفات المعدّلة: Domain/Helpers/StocktakeSession.cs و StocktakeSheetBuilder.cs، Data/Repositories/ItemRepository.cs، App/ViewModels/StocktakeViewModel.cs و MainViewModel.cs، Views/StocktakeView.xaml(.cs)، MainWindow.xaml، App.xaml.cs، Services/PdfReportService.cs، tools/linux-test-runner (runner.csproj + Shim/Wpf.cs)، tests/StocktakeTests.cs، FIXES.md
عدد الاختبارات قبل/بعد: 252 → 268 (كلها ناجحة)
ملخص سطرين: شاشة جرد بالباركود (مسح علبة = +علبة، مقارنة لحظية بالمسجّل، اعتماد يسوّي الأصناف اللي فيها فرق ويتسجّل في التدقيق الصح) مع طباعة قائمة جرد PDF.
ملاحظات/قرارات: (1) كود PDF والـ XAML ما بيتبنوش في المشغّل البديل (PdfSharp/WPF)؛ اتراجعوا نصياً ويتأكدوا فعلياً في خطوة 42/43. (2) العربي في PDF بنفس أسلوب باقي التقارير (بدون معالجة RTL خاصة)؛ ممكن يحتاج مراجعة بصرية في خطوة 43. (3) الاعتماد بيحدّث إجمالي الصنف فقط مش التشغيلات (زي التسوية اليدوية).

## خطوة 39 — مرتجعات للمورد لصنف مُشترى + أوامر شراء مقترحة (تسليم على أجزاء)

### 39-أ ✅ ربط مرتجع المورد بفاتورة الشراء الأصلية والتحقق من الكمية | تاريخ: 2026-10-14
الملفات المعدّلة: Data/Migrations/020_PurchaseLinkedSupplierReturns.sql (جديد، إصدار القاعدة = 20)، Data/DatabaseContext.cs، Data/Repositories/SupplierReturnRepository.cs، Data/Repositories/PurchaseAndSupplierRepositories.cs (تخزين `BatchId` على بند الشراء)، Domain/Entities/ExpiryAndReturnEntities.cs، tests/.../PurchaseLinkedReturnTests.cs (جديد)، tests/.../BackupTests.cs (اختبار الاستعادة لإصدار أقدم بيشيل أعمدة 020)، FIXES.md
عدد الاختبارات قبل/بعد: 268 → 278 (كلها ناجحة)
ملخص سطرين: المرتجع المرتبط بفاتورة شراء بيرفض أي كمية أكبر من (المشترى − ما سبق إرجاعه) أو من المتبقي بالتشغيلة، وبياخد البند والتشغيلة والمورد والسعر من الفاتورة نفسها (مش من الواجهة). مبلغ كل مرتجع جزئي بتقريب تراكمي فمجموعها = إجمالي البند بالظبط.
ملاحظات/قرارات اتخذتها: (1) السعر = إجمالي البند **شامل الضريبة** موزّع على وحداته (لأن رصيد المورد بيحمل الضريبة). (2) نسبة الخصم عند المورد اختيارية والافتراضي 0. (3) سبب المرتجع إلزامي، والإتلاف مش مسموح مرتبطاً بفاتورة. (4) بنود الشراء القديمة بتتربط بتشغيلتها تلقائياً بس لو التطابق غير ملتبس. (5) أصلحت ثغرة مرافقة: بندان على نفس التشغيلة في مستند واحد (فحص تراكمي). (6) **لا يوجد UI لهذا بعد** (39-ب).
⚠ نقاط مفتوحة اكتُشفت (مش اتعدّلت، محتاجة قرارك): **(أ)** تكلفة وحدة التشغيلة عند الشراء أقل من الحقيقي بمقدار الكمية (مثال مُجرَّب: 10 علب = 10 قروش للقطعة بدل 100) — بيأثّر على تقارير الأرباح والتقييم؛ **(ب)** شاشة المشتريات بتحسب ضريبة 15% ثابتة. التفاصيل في FIXES.md.

### 39-ج ✅ أوامر شراء مقترحة من النواقص (المنطق والمستودع) | تاريخ: 2026-10-14
الملفات المعدّلة: Domain/Entities/SuggestedOrderEntities.cs (جديد)، Data/Repositories/PurchaseAndSupplierRepositories.cs (`GetSuggestedPurchaseOrders`)، tests/.../SuggestedPurchaseOrderTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 278 → 284 (كلها ناجحة)
ملخص سطرين: الأصناف تحت الحد الأدنى بتتجمّع في أوامر شراء مقترحة بمورد آخر شراء لكل صنف، بنفس قاعدة الكمية في تقرير النواقص (العجز + ضعف الحد) مقرّبة لعلب كاملة وبتكلفة تقديرية من آخر شراء. الأصناف بدون مورد سابق (أو موردها متعطّل) في مجموعة منفصلة.
ملاحظات/قرارات اتخذتها: (1) للقراءة فقط (مقترحات مش مستندات محفوظة). (2) أعدت استخدام قاعدة الكمية الموجودة بدل ما أخترع قاعدة جديدة. (3) **لا يوجد UI لها بعد** (39-د).

### تعديل بطلب المستخدم ✅ ضريبة المشتريات لكل بند يحددها المستخدم | تاريخ: 2026-10-14
الملفات المعدّلة: App/ViewModels/PurchasesViewModel.cs، App/Views/PurchasesView.xaml، tests/.../PurchaseLinkedReturnTests.cs، FIXES.md
عدد الاختبارات قبل/بعد: 284 → 285 (كلها ناجحة)
ملخص سطرين: شالت نسبة 15% الثابتة؛ كل بند في فاتورة الشراء له خانة "الضريبة %" (0 = معفى) يحددها المستخدم، وقيمتها الابتدائية من إعداد الصنف. المرتجع المرتبط بياخد ضريبة كل بند كما اتسجّلت.
ملاحظة: كود WPF ما اتترجمش هنا؛ يُتأكد منه في خطوة 42.

### ✅ تصحيح الملاحظات الثمانية | تاريخ: 2026-10-14
الملفات المعدّلة: Data/Migrations/021_PurchaseCostAndVatPercent.sql و022_PurchaseOrders.sql (جديدين، إصدار القاعدة = 22)، Data/DatabaseContext.cs، Data/Repositories/{PurchaseAndSupplierRepositories,SupplierReturnRepository,FinanceRepository,JournalPosting}.cs، Data/Repositories/PurchaseOrderRepository.cs (جديد)، Domain/Entities/{PurchasesAndVoucherEntities,FinanceAndZakatEntities,PurchaseOrderEntities(جديد)}.cs، App/ViewModels/{PurchasesViewModel,SupplierReturnsViewModel,PurchaseOrdersViewModel(جديد),MainViewModel}.cs، App/Views/{PurchasesView,SalesView,FinanceView,SupplierReturnsView,PurchaseOrdersView(جديد)}.xaml، App/{MainWindow.xaml,App.xaml.cs}، tools/linux-test-runner (Shim + runner.csproj)، tests (VatAndPurchaseCostTests, SupplierReturnJournalTests, PurchaseOrderTests, SupplierReturnsViewModelLinkedTests, PurchaseOrdersViewModelTests + تعديل BackupTests/PurchaseLinkedReturnTests)، FIXES.md
عدد الاختبارات قبل/بعد: 285 → 306 (كلها ناجحة)
ملخص: (1) إصلاح تكلفة التشغيلة + تصحيح القديم (2) تخزين VatPercent (3) الضريبة الافتراضية من الإعدادات (4) إقرار الضريبة من البنود (خاضع/معفى) وإزالة 15% (5) أوامر شراء محفوظة (6) واجهتا 39-ب و39-د (7) قيد يومية لمرتجع/شطب المورد (8) تحقق Windows: **لم يُنفَّذ** (مستحيل هنا).
ملاحظات/قرارات: انظر FIXES.md "تصحيح الملاحظات الثمانية". تصحيح لتقدير سابق: الأرباح غير متأثرة بخطأ التكلفة (بتحسب من تكلفة علبة الصنف).

### 📝 المتبقي/ملاحظات للمستقبل
1. **[خطوة 42] تحقق Windows الفعلي لواجهات WPF الجديدة/المعدّلة:** شاشة "تحديث الأسعار (Excel)" (فتح ملف .xlsx حقيقي + قراءة ClosedXML للأرقام والنصوص + شكل الشاشة)، شاشة المشتريات (عمود الضريبة %)، شاشة مرتجع/إتلاف (وضع الفاتورة + التبديل بين الأوضاع)، شاشة أوامر الشراء + زر القائمة، نصوص المبيعات والمالية.
2. طباعة/PDF لأمر الشراء، وتحويل أمر شراء لفاتورة شراء (تعبئة السلة منه) وربط الاستلام بالفاتورة.
3. إقرار الضريبة: خصم مرتجعات المبيعات (Type=2)، وعرض المعفى في شاشة المالية.
4. مرتجعات المورد القديمة (قبل تصحيح التكلفة) ما اتعدّلتش.
5. نسخ الشاشات/المشغّل: `tools/linux-test-runner/Shim/AppServiceStubs.cs` للتجميع فقط ولا يدخل بناء التطبيق.

### ✅ تعديل المثبّت (ويندوز 7 + حذف البيانات اختيارياً) | تاريخ: 2026-10-14
الملفات المعدّلة: installer/e_prescriptions_installer.iss، installer/redist/README.txt (جديد)، .gitignore، README.md، tests/.../InstallerScriptTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 306 → 310 (كلها ناجحة)
ملخص: رسالة ويندوز 7 الصحيحة (4.8 مش 4.8.1 + KB4474419/KB4490628)، تضمين اختياري لمثبّت 4.8 الكامل، وسؤال "حذف بيانات الصيدلية؟" عند الإزالة (الافتراضي لا + تأكيد مزدوج + لا حذف في الصامتة).
⚠ سكريبت Inno ما اتترجمش ولا اتجرّب على ويندوز 7؛ لازم بناء وتجربة يدوية (ضمن خطوة 42).

---

## خطوة 40 — تحديث أسعار تلقائي من ملف Excel لقوائم أسعار الموردين (تسليم على أجزاء)

### جزء أ — القراءة والمطابقة وخطة التحديث والاعتماد على القاعدة ✅ تم | تاريخ: 2026-10-14
الملفات: Domain/Helpers/PriceListImport.cs (جديد: `PriceListParser` و`PriceListPlanner` والكيانات)، Data/Repositories/ItemRepository.cs (`ApplyPriceChanges`)، tests/ePrescriptions.Tests/PriceListImportTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 310 → 347 (كلها ناجحة)
ملخص سطرين: النظام بيقرأ جدول نصي (اللي هيسلّمه قارئ Excel) ويكتشف أعمدة الباركود/الكود/الاسم/سعر الجمهور/سعر الجملة/سعر التكلفة من عناوين عربي أو إنجليزي، ويطابق كل سطر بصنف (باركود ← كود ← اسم مطبّع بالتطابق التام) ويطلع خطة معاينة (سعر قديم/جديد/نسبة التغيير) من غير ما يكتب حاجة. الاعتماد (`ApplyPriceChanges`) بيحدّث الأسعار في Transaction واحدة ويتخطى أي تغيير السعر الحالي بتاعه اتغيّر بعد المعاينة.
ملاحظات/قرارات اتخذتها: (1) كل الأسعار بسعر **العلبة** بالقرش زي حقول الصنف. (2) سطر مش متطابق/غامض/تعارض/سعر غير صالح بيتعلّم ومبيتحدّثش (مفيش تخمين). (3) نفس الصنف مرتين بنفس السعر = تكرار بيتجاهل؛ بأسعار مختلفة = تعارض والاتنين بيتوقفوا. (4) تغيير بنسبة 30٪ أو أكتر، أو سعر جمهور أقل من التكلفة، بيتعلّم "يحتاج مراجعة" (قرار العرض والاستبعاد في الواجهة، جزء ب). (5) "السعر"/"Price" لوحدها تتفسّر سعر جمهور. (6) تسجيل التدقيق (LogAudit) في جزء ب لأنه على مستوى الـ ViewModel زي باقي الشاشات.
⚠ لسه (جزء ب): قارئ Excel الفعلي (ClosedXML)، شاشة المعاينة والاعتماد، تسجيل التدقيق (قديم/جديد/تاريخ/المورد/اسم الملف) وصلاحية الشاشة.

### جزء ب — قارئ Excel + الشاشة + التدقيق + الصلاحية ✅ تم | تاريخ: 2026-10-14
الملفات المعدّلة: App/Services/IPriceListFileReader.cs و ExcelPriceListReader.cs (جديدين)، App/ViewModels/PriceListImportViewModel.cs (جديد) و MainViewModel.cs، App/Views/PriceListImportView.xaml(.cs) (جديدين)، App/MainWindow.xaml، App/App.xaml.cs، tools/linux-test-runner/runner.csproj، tests/ePrescriptions.Tests/PriceListImportViewModelTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 347 → 386 (كلها ناجحة)
ملخص سطرين: شاشة تحديث الأسعار من Excel (Admin/Owner فقط): اختيار ملف .xlsx/.xlsm ← معاينة بالقديم/الجديد ← تعليم الصفوف (التغيير الكبير وتحت التكلفة غير معلّمين افتراضياً) ← اعتماد بتأكيد ← تسجيل كل تغيير سعر في التدقيق الصح (Owner → OwnerAuditLog، غيره → StaffAuditLog) بالقديم/الجديد/المورد/الملف/التاريخ. الصلاحية بتتفحص كمان وقت الاعتماد.
ملاحظات/قرارات: (1) النسخة الأولى من جزء ب ضاعت مع جلسة سابقة؛ اتعادت كتابتها من الصفر بنفس القرارات. (2) **كود ClosedXML والـ XAML وMainViewModel ما اتترجموش هنا** (فحص نصي فقط)؛ التحقق الفعلي في خطوة 42/43. (3) التدقيق بعد التحديث مش في نفس الـ Transaction، وفشله بيظهر تحذير. (4) Admin/Owner فقط قرار مني. (5) مفيش مكتبة NuGet جديدة (ClosedXML موجودة من قبل).
⚠ **خطوة 40 بالكامل ✅ تمت (أ + ب).**

---
## خطوة 41 — تكملة النسخ الاحتياطي التلقائي (تسليم على أجزاء)

### جزء أ — الجدولة اليومية + اختبار الاستعادة الدوري + حالة النسخ + تنبيه الفشل (المنطق والخدمات) ✅ تم | تاريخ: 2026-10-14
الملفات: Core/Helpers/BackupSchedule.cs (جديد: `BackupStatus` و`BackupStatusStore` و`BackupScheduler` و`BackupHealth`)، Core/Helpers/BackupSettings.cs (حقول الجدولة)، App/Services/BackupService.cs (`VerifyBackupRestorable` و`FindLatestBackup` وتسجيل النتيجة وقفل)، App/Services/BackupAutomationService.cs (جديد)، tools/linux-test-runner/runner.csproj، tests/ePrescriptions.Tests/BackupAutomationTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 386 → 415 (كلها ناجحة)
ملخص سطرين: النسخة اليومية بتتأخد لو ميعادها عدّى ومفيش نسخة ناجحة بعده (من أي مصدر، مع تعويض الجهاز المقفول)، وبعدها اختبار استعادة دوري بيفتح أحدث نسخة في قاعدة مؤقتة (سلامة + migrations + قراءة الجداول الأساسية) من غير ما يلمس القاعدة الحية. كل نتيجة (نجاح/فشل) بتتسجّل في `backup_status.ini` المستقل، و`BackupHealth` بيحوّلها لتنبيهات بتختفي أول ما يحصل نجاح أحدث.
ملاحظات/قرارات: (1) ملف حالة مستقل مش جدول قاعدة. (2) النسخة بعد الوردية بتعدّ كنسخة اليوم. (3) فاصل إعادة المحاولة: 30 دقيقة للنسخ و6 ساعات لاختبار الاستعادة. (4) مفيش مكتبة NuGet جديدة. (5) الاختبار كشف خلل حقيقي في تنظيف رسالة الفشل (`\r\n` ← مسافتين) واتصلّح في الكود. (6) ما اتضافتش خدمات في `App.xaml.cs` لسه (جزء ب).
⚠ لسه (جزء ب): مؤقّت التشغيل في التطبيق، شريط تنبيه في الواجهة، حقول الإعدادات وزر "اختبار الاستعادة الآن"، تسجيل الخدمات في DI. (WPF: فحص نصي فقط، التحقق الفعلي خطوة 42.)

### جزء ب — ربط الجدولة بالتطبيق: مؤقّت + شريط تنبيه + الإعدادات ✅ تم | تاريخ: 2026-10-14
الملفات المعدّلة: App/Services/BackupSchedulerHost.cs (جديد)، App/Services/BackupService.cs (الاستعادة تحت نفس قفل النسخ)، App/App.xaml.cs (DI + بدء المؤقّت)، App/ViewModels/MainViewModel.cs و App/MainWindow.xaml (شريط التنبيه)، App/ViewModels/SettingsViewModel.cs و App/Views/SettingsView.xaml (حقول الجدولة + زر اختبار الاستعادة + نص الحالة)، tools/linux-test-runner/runner.csproj، tests/ePrescriptions.Tests/BackupSchedulerHostTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 415 → 421 (كلها ناجحة)
ملخص سطرين: المؤقّت (أول فحص بعد دقيقة وبعدها كل 5 دقايق) بيشغّل `RunDueTasksAsync` على خيط خلفي وبيحدّث شريط أحمر للمدير/المالك فيه التنبيهات وزر يفتح إعدادات النسخ؛ الاستعادة بتوقف الجدولة وتشتغل تحت نفس قفل النسخ. شاشة الإعدادات فيها خانة النسخ اليومي وميعاده (HH:mm بيتفحص) واختبار الاستعادة الدوري وتكراره وزر "اختبار استعادة أحدث نسخة الآن" ونص حالة.
ملاحظات/قرارات: (1) **`MainViewModel` و`SettingsViewModel` و`App.xaml.cs` والـ XAML ما اتترجموش هنا**: فحص صياغة ناجح (مفيش Syntax errors) وXML سليم فقط؛ التحقق الفعلي في خطوة 42. (2) الشريط للمدير/المالك فقط. (3) تحديث الشريط بعد نسخة وردية خلال 5 دقايق (التحذير الفوري موجود من خطوة 13). (4) مفيش مكتبة NuGet جديدة.

## خطوة 41 — ✅ تمت بالكامل (أ + ب) | تاريخ: 2026-10-14
معيار الجدولة والاستعادة والتنبيه متغطي باختبارات `BackupAutomationTests` (29) و`BackupSchedulerHostTests` (6). عدد الاختبارات قبل/بعد الخطوة كلها: 386 → 421 (كلها ناجحة).

## خطوة 42 — التحقق الفعلي على Windows (تشغيل المشروع من الصفر) | تاريخ: 2026-10-15
الحالة: ✅ بناء وتشغيل ناجح بشهادة صاحب المشروع (نُفّذ على جهازه، مش في بيئة الـ AI).
المصدر: إفادة صاحب المشروع: "عملت بيلد للبرنامج وشغاله تمام". المشروع اتنقل على GitHub (ملف `E-Prescriptions-main.zip`) وهو المرجع من هنا ورايح (أي zip أقدم يتجاهل).
الملفات المعدّلة: لا يوجد (توثيق فقط).
عدد الاختبارات قبل/بعد: لم يتغيّر (421 في المشغّل البديل).
ملاحظات صاحب المشروع: **عنده ملاحظات على البرنامج بعد التشغيل الفعلي، وهيقولها بعد ما كل الخطوات تخلص.** تتسجّل هنا ككتلة "ملاحظات خطوة 42" وتتعالج في الآخر.
⚠ ما اتوثّقش تفصيلياً (مش مطلوب من صاحب المشروع دلوقتي): نتيجة كل شاشة على حدة، ونتيجة بنود 40 و41 الخاصة بالتحقق على Windows (شاشة تحديث الأسعار Excel، شريط تنبيه النسخ الاحتياطي، حقول الجدولة، زر اختبار الاستعادة، وأن الجدولة بتاخد نسخة فعلاً). لو الملاحظات الآتية فيها حاجة منهم يتسجّل هنا.

### ملاحظات خطوة 42 (من صاحب المشروع)
_(فاضية لحد دلوقتي: بتتكتب لما صاحب المشروع يبعتها، بعد اكتمال باقي الخطوات.)_

## خطوة 43 — مراجعة مخرجات PdfSharp وClosedXML وQRCoder وأن QR مش وعد بمطابقة ضريبية
✅ تمت جزئياً | تاريخ: 2026-10-15 | **التحقق الفعلي من المكتبات لسه محتاج تشغيل على Windows** (بيئة الـ AI مفيهاش NuGet، فمشفتش مخرجات حقيقية).
الملفات المعدّلة: Core/Helpers/EgyptQrHelper.cs، App/Services/ExcelExportService.cs، App/ViewModels/HelpViewModel.cs، App/Views/SettingsView.xaml، installer/e_prescriptions_installer.iss، README.md، tests/ePrescriptions.Tests/EgyptQrTests.cs و ReportingAndExportTests.cs، FIXES.md، PROGRESS.md
عدد الاختبارات قبل/بعد: 421 → 425 في المشغّل البديل (كلها ناجحة). + 5 اختبارات جديدة في `ReportingAndExportTests` (Excel/PDF/QR الحقيقيين) **مكتوبة وما اتشغّلتش** (فحص صياغة بس)، المتوقع على Windows: 425 + (ReportingAndExportTests الموجودة) + 5.
ملخص سطرين: نص الـ QR بقى ثابت الثقافة (`1234.50` بدل فواصل ar-EG)، واتشالت 4 عبارات بتدّعي اعتماد/ZATCA/ربط إلكتروني (README والمساعدة والإعدادات والمثبّت) مع تعليق يقول صراحة إنه مش صيغة ETA. وتصدير Excel لفترة بدون مبيعات كان بيطلّع مرجع دائري `SUM(E5:E4)`؛ اتصلّح.
ملاحظات/قرارات: (1) لا مكتبة جديدة؛ فك الـ QR بقارئ حقيقي مش متغطي (يحتاج مكتبة). (2) مهم: PdfSharp 1.50 على حد علمي مبيشكّلش حروف عربي في الـ PDF، فلازم حد يفتح فاتورة PDF فعلية ويقول شكلها. لو طلعت مكسورة تتصلّح بخطوة صغيرة منفصلة. (3) الـ QR مفيش زر لتفعيله في الإعدادات. (4) اشتباه إن الربح في تقرير المبيعات بيشمل الضريبة، يتأكد في خطوة 47. التفاصيل في FIXES.md.
⚠ **مطلوب من صاحب المشروع لإغلاق 43 بالكامل:** (أ) تشغيل الاختبارات على Windows (أو GitHub Actions) وإرسال أي اختبار فاشل. (ب) فتح PDF فاتورة وتصدير Excel وإرسال لقطة شاشة/وصف.

---
## خطوة 44 — اختبار الطباعة الحرارية وطباعة الباركود
✅ تمت جزئياً | تاريخ: 2026-10-15 | **التجربة على طابعة فعلية لسه مطلوبة** (مفيش طابعة ولا Windows في بيئة الـ AI).
الملفات المعدّلة: Core/Helpers/EscPosCommand.cs و Code128Encoder.cs و LabelLayout.cs (جديدة)، Core/Helpers/Code128BarcodeGenerator.cs، Core/Printing/RawPrinterHelper.cs، Domain/Helpers/ReceiptText.cs (جديد)، App/Services/ThermalReceiptPrinter.cs و BarcodeLabelService.cs، tests/ePrescriptions.Tests/PrintingLogicTests.cs (جديد)، tools/linux-test-runner/Shim/Fluent.cs، tools/printing-test-checklist.md (جديد)، FIXES.md، PROGRESS.md
عدد الاختبارات قبل/بعد: 425 → 473 في المشغّل البديل (كلها ناجحة).
ملخص سطرين: اكتشفت إن الإيصال والملصق بيترسموا بـ GDI+ مش ESC/POS (ESC/POS الخام للدرج بس)، فمحاكي ESC/POS مش بيشوفهم. اتصلّح بالحساب والاختبار: سطر السعر/الصلاحية كان خارج ملصق 25 مم، وباركود EAN-13 كان أعرض من ملصق 50 مم (Set C حلّها)، وأمر الدرج كان بيتحلّل بإسقاط صامت، وعنوان "نقدية" على الآجل، واسم شخصي ثابت على الملصق.
ملاحظات/قرارات: (1) لا مكتبة جديدة. (2) الأمر الغلط للدرج بقى بيترفض برسالة (مش بيتبعت). (3) كود GDI اترجم ضد stubs فقط (صياغة)، مش System.Drawing الحقيقية. (4) ما غيّرتش طول صفحة الإيصال الثابت (20 بوصة) ولا عرض الرسم: محتاجين نشوف سلوك الطابعة الفعلية الأول. التفاصيل في FIXES.md "خطوة 44".
⚠ **مطلوب من صاحب المشروع لإغلاق 44 بالكامل:** تنفيذ `tools/printing-test-checklist.md` (الأقسام 1 و2 بدون أي طابعة حرارية، و3 و4 على الطابعة الحقيقية) وإرسال النتيجة/لقطات. + تشغيل الاختبارات على Windows (تحتوي الآن `PrintingLogicTests`).

---
## خطوة 45 — GitHub Actions على windows-latest (بناء + اختبارات + فشل عند أي خطأ)
✅ تمت (كتابة + تحقق نصي واختباري) | تاريخ: 2026-10-15 | ⚠ **لسه ما اتشغّلتش على GitHub فعلياً** (مفيش Windows/GitHub في بيئة الـ AI).
الملفات المعدّلة: .github/workflows/build.yml (اتكتب من جديد)، tests/ePrescriptions.Tests/CiWorkflowTests.cs (جديد)، FIXES.md، PROGRESS.md
عدد الاختبارات قبل/بعد: 473 → 481 في المشغّل البديل (كلها ناجحة)
ملخص سطرين: كان فيه build.yml قديم غير موثّق وخطوة "SQLite Interop" فيه كانت هتفشل دايماً (بتدوّر في مجلد packages/ غير موجود في مشاريع SDK-style). اتعاد بناؤه: restore ← build للحل كامل ← فحص interop ← `dotnet test` ← حارس يفشل لو صفر اختبارات أو أي فاشل ← بعدها فقط التحزيم (Inno Setup) ورفع النتائج.
ملاحظات/قرارات: (1) `dotnet test` بدل vstest (نفس `build.bat`، وأقل نقاط فشل). (2) كل أمر native في خطوة مستقلة (pwsh بيبص على آخر exit code بس). (3) الاختبار السلبي: الـ workflow القديم فشّل 5 من 8 اختبارات جديدة. (4) مفيش مكتبة NuGet جديدة. (5) التحزيم لسه جوه نفس الـ job بعد الاختبارات؛ فشل Inno/choco هيفشل الـ workflow كله.
⚠ **مطلوب من صاحب المشروع لإغلاق 45 فعلياً:** ارفع المشروع على GitHub (أو Run workflow يدوياً) وابعت: حالة الـ workflow، وأي اختبار فاشل من artifact `test-results` أو سجل خطوة "Run all tests". **متوقع** ظهور فشل في أول تشغيل (الاختبارات ما اتشغّلتش على Windows قبل كده، و5 اختبارات مكتبات حقيقية مكتوبة بس).

## خطوة 46 — إجبار تغيير كلمة مرور Owner الافتراضية
✅ تمت | تاريخ: 2026-10-15 | ⚠ الواجهة (XAML) فحص نحوي فقط؛ التحقق الفعلي على Windows لسه.
الملفات المعدّلة: Core/Security/DefaultCredentials.cs (جديد)، Data/DatabaseContext.cs، Data/Repositories/UserAndShiftRepositories.cs، App/ViewModels/LoginViewModel.cs، App/Views/LoginWindow.xaml(.cs)، tests/ePrescriptions.Tests/ForcedPasswordChangeTests.cs (جديد)، FIXES.md، PROGRESS.md
عدد الاختبارات قبل/بعد: 481 → 490 (كلها ناجحة)
ملخص سطرين: أي دخول بكلمة المرور الافتراضية المعروفة (`ChangeMe@Owner1`، أي حساب) ما بيكمّلش للنظام: شاشة الدخول بتتحوّل لوضع تغيير إجباري، الخروج/إغلاق النافذة بيقفل الجلسة والبرنامج، وبعد التغيير بيدخل بالكلمة الجديدة. التغيير بيرفض الكلمة الافتراضية والتكرار.
ملاحظات/قرارات: (1) التثبيت الجديد أصلاً مفيهوش كلمة افتراضية (خطوة 2)، فالإجبار للتثبيتات القديمة ومسار الاختبارات/التجريبي. (2) بعد التغيير الجلسة بتتقفل ودخول بالجديدة (بسبب hash قديم في الذاكرة). (3) تغيير كلمة المرور من "سجل نشاطي" لسه فيه نفس مشكلة الـ hash القديم وقت فك القفل (خارج الخطوة، موثّق في FIXES.md). (4) مفيش مكتبة NuGet جديدة. (5) اختبار سلبي: تعطيل الإجبار فشّل 6 من 9.

### ✅ تعديل: اسم الصيدلية في شاشة الدخول من الإعدادات | تاريخ: 2026-10-15
الملفات المعدّلة: App/ViewModels/LoginViewModel.cs، App/Views/LoginWindow.xaml، App/Services/PdfReportService.cs (تذييل A4)، App/ViewModels/SettingsViewModel.cs و ReportsViewModel.cs، Domain/Entities/InvoiceAndSalesEntities.cs، Data/Repositories/SettingsRepository.cs، Data/Setup/FirstRunSetupService.cs، tests/.../LoginPharmacyNameTests.cs (جديد)، FIXES.md
عدد الاختبارات قبل/بعد: 490 → 494 (كلها ناجحة)
ملخص: شاشة الدخول بتعرض اسم الصيدلية من الإعدادات (= اللي اتكتب في أول شاشة)، والاحتياطي "الصيدلية" بدل أي اسم شخصي. لسه ثابت: "المستخدم: <اسم شخصي>" في SalesView وقيمة MainViewModel الابتدائية (مش اسم صيدلية، منتظر قرارك).

### ✅ تعديل: حذف الاسم الشخصي القديم + فوتر "Designed with ❤️ by Maged Fayez" | تاريخ: 2026-10-15
الملفات المعدّلة: MainWindow.xaml، LoginWindow.xaml، SalesView.xaml، SalesViewModel.cs، MainViewModel.cs، SettingsViewModel.cs، SettingsRepository.cs، InvoiceAndSalesEntities.cs، DemoData.sql، 005_EgyptLocalization.sql، installer .iss، build.bat، publish.ps1، README.md، اختبارات (أسماء)، NoPersonalNameAndFooterTests.cs (جديد)، TODO_NAME_REMOVAL_AND_FOOTER.md (سجل المهمة)، FIXES.md، PROGRESS.md
عدد الاختبارات قبل/بعد: 494 → 496 (كلها ناجحة)
ملخص: الاسم القديم اتشال من كل ملف نصي (حارس اختبار بيفحص ده)، والفوتر في أسفل النافذة الرئيسية وشاشة الدخول. ⚠ XAML ما اتجرّبش على Windows؛ قواعد بيانات قديمة فيها الاسم مخزّن هتفضل بيه لحد التعديل من الإعدادات.

### ✅ تعديل: هوية "E-Prescription by Maged Fayez" + حقوق النشر (فوتر/مثبّت/إيصال) | تاريخ: 2026-10-15
الملفات المعدّلة: Core/Helpers/AppBranding.cs (جديد)، App/Services/ThermalReceiptPrinter.cs (إيصال البيع + إيصال إغلاق الوردية)، App/Services/PdfReportService.cs (فاتورة A4)، App/MainWindow.xaml، App/Views/LoginWindow.xaml، installer/e_prescriptions_installer.iss، ePrescriptions.{App,Core,Domain,Data}.csproj، README.md، tests/.../BrandingTests.cs (جديد)، FIXES.md، PROGRESS.md
عدد الاختبارات قبل/بعد: 496 → 505 (كلها ناجحة)
ملخص: الاسم "E-Prescription by Maged Fayez" + سطر "© 2026 Maged Fayez. All rights reserved." بقوا في أسفل الإيصال الحراري (البيع والوردية) وتذييل PDF الفاتورة وشاشة الدخول (وتلميح على فوتر النافذة الرئيسية) وفي المثبّت (الناشر، حقوق النشر، عنوان Add/Remove، رسالة الترحيب، خصائص ملف الـ Setup) وخصائص ملفات الـ DLL/EXE. فوتر "Designed with ❤️ by Maged Fayez" في الواجهة زي ما هو.
ملاحظات/قرارات: (1) `AppId` واسم ملف الـ Setup ومجلد التثبيت ما اتغيروش (التحديث فوق نسخة قديمة يفضل شغّال). (2) في المثبّت استخدمت "(C)" بدل © (الملف بدون BOM). (3) السطر الإنجليزي على الإيصال ممكن يطلع صغير على ورق 58 مم: يتأكد بالطباعة الفعلية. (4) XAML والطباعة الفعلية فحص نصي بس؛ التحقق على Windows. (5) مفيش مكتبة NuGet جديدة. (6) بنود تقارير Excel/PDF الأخرى (غير فاتورة A4) ما اتعدّلتش.

## خطوة 47 — مراجعة حسابات الزكاة والأرباح وكشوف الحساب
✅ تمت | تاريخ: 2026-10-15 | ⚠ الربط بالواجهة (XAML/PDF/Excel) ما اتجرّبش على Windows.
الملفات المعدّلة: Domain/Entities/ReportingEntities.cs و FinanceAndZakatEntities.cs، Data/Repositories/ReportRepository.cs و FinanceRepository.cs و CustomerRepository.cs، tests/ePrescriptions.Tests/AccountingVerificationTests.cs (جديد) و ReportingAndExportTests.cs (رقم واحد)، FIXES.md، PROGRESS.md
عدد الاختبارات قبل/بعد: 505 → 515 في المشغّل البديل (كلها ناجحة)
ملخص سطرين: راجعت بسيناريو محسوب يدوياً. حساب الزكاة نفسه طلع صح، لكن اتأكد اشتباه إن الربح بيشمل الضريبة وطلعت 6 مشاكل تانية: المرتجعات مش بتتخصم من الربح، تكلفة الصنف بتتقصّ (9990 بدل 10000)، تقرير حركة الأصناف بيعدّ بنود برا الفترة والمرتجعات، كشف العميل بيحسب المرتجع مدين وما بيظهرش الرصيد الافتتاحي، التدفق النقدي بيعدّ المرتجع النقدي مقبوضات، وإقرار الضريبة ما بيخصمش المرتجعات. كلها اتصلّحت بأرقام مثبتة باختبارات.
ملاحظات/قرارات: (1) اختبار قديم في `ReportingAndExportTests` كان بيثبّت الربح شامل الضريبة (600) واتعدّل لـ 390 عن قصد، ومحتاج ClosedXML فما اتشغّلش هنا. (2) فشل سلبي: على الكود الأصلي 6 من 10 اختبارات جديدة فشلت. (3) لا مكتبة جديدة. (4) حدود ما اتغيّرتش وبتحتاج قرارك (النقدية في الزكاة للورديات المفتوحة بس ومفيش إدخال يدوي للبنك/الخزينة، التكلفة من تكلفة الصنف الحالية مش التشغيلة، مفيش كشف حساب مورد) موثّقة في FIXES.md "خطوة 47".
⚠ **مطلوب من صاحب المشروع:** (أ) تشغيل الاختبارات على Windows/GitHub Actions. (ب) قرار: عايز خانة إدخال يدوي لرصيد البنك/الخزينة في الزكاة؟ وعايز كشف حساب مورد؟ وعايز التكلفة من التشغيلة الفعلية؟

### ✅ تعديل: إصلاحات الطباعة (الباركود والإيصال) وخيارات التراجع وتنسيق الفاتورة والوحدات | تاريخ: 2026-10-16
الملفات المعدّلة: 
- `Core/Helpers/LabelLayout.cs` و `App/Services/BarcodeLabelService.cs`: تقليل ارتفاع الباركود إلى النصف تقريباً (`halfBarcode: true` بحد أقصى مريح) لمنع خروج الملصق على ورقتين، وضبط أبعاد الصفحة للطباعة داخل الملصق بدقة.
- `App/Services/ThermalReceiptPrinter.cs`: تعديل الهوامش الجانبية (هامش أمان أيمن 38 وحدة ~ 9.6 مم لطابعات 80 مم و28 وحدة لطابعات 58 مم) لمنع قص أول حرفين من اليمين في النصوص العربية وضبط عرض الطباعة الفعلي.
- `Data/Repositories/PurchaseAndSupplierRepositories.cs` و `App/ViewModels/PurchasesViewModel.cs` و `App/Views/PurchasesView.xaml`: إضافة زر وخاصية التراجع عن فاتورة المشتريات `UndoLastInvoiceCommand` مع عكس أثرها على المخزون وحساب المورد والقيود اليومية داخل Transaction آمنة.
- `App/ViewModels/SalesViewModel.cs` و `App/Views/SalesView.xaml`: إضافة زر وخاصية التراجع عن آخر فاتورة بيع `UndoLastSavedInvoiceCommand` وإلغاء أي فاتورة بيع برقمها مع استرجاع المخزون وتسجيل التدقيق.
- `App/Views/SalesView.xaml`: تبسيط وضغط مقطع بيانات العميل (حذف فئة السعر، واسم الطبيب، ورقم الوصفة، ودمج اسم العميل وموبايله وطريقة الدفع في صف واحد أنيق وموفر للمساحة).
- `SalesView.xaml`: إعادة ترتيب جدول بنود المبيعات بحيث يحظى "اسم الصنف الدوائي والمادة الفعالة" بأكبر مساحة مرنة (`Width="4*"`).
- `Domain/Enums/DomainEnums.cs` و `Domain/Helpers/UnitBreakdownHelper.cs` و `Domain/Entities/ItemAndInventoryEntities.cs` و `App/ViewModels/SalesItemRow.cs` و `SalesView.xaml` و `PurchasesView.xaml`: توسيع قائمة الوحدات لتشمل (علبة، شريط، قرص، أمبول، كيس، وحدة) مع حسابات التسعير والتجزئة الدقيقة في المبيعات والمشتريات.

### ✅ ميزة جديدة: شاشة ونظام "رصيد أول المدة" (بضاعة أول المدة) | تاريخ: 2026-10-16
الملفات المضافة والمعدّلة:
- `Domain/Entities/OpeningStockEntities.cs`: كيانات `OpeningStockVoucher` و `OpeningStockVoucherLine` مع حسابات التجزئة والتكلفة والبيع وهوامش الربح.
- `Data/Migrations/024_OpeningStockVouchers.sql`: إنشاء جدولي `OpeningStockVouchers` و `OpeningStockVoucherLines` مع الفهارس.
- `Data/DatabaseContext.cs`: ترقية `LatestSchemaVersion = 24` وإضافة تطبيق Migration 024.
- `Data/Repositories/JournalPosting.cs`: إضافة حساب `OpeningBalanceEquity` ("رأس المال / رصيد افتتاحي") للقيود الآلية المزدوجة.
- `Data/Repositories/OpeningStockRepository.cs`: مستودع حفظ سندات رصيد أول المدة، إنشاء الأصناف الجديدة تلقائياً، إدراج التشغيلات واستيعاب الرصيد السالب، تحديث الأرصدة وتسجيل القيود المحاسبية وسجلات التدقيق داخل Transaction آمنة.
- `App/Services/ExcelExportService.cs`: إضافة دوال تصدير سند رصيد أول المدة إلى Excel وتوليد نموذج استيراد فارغ.
- `App/ViewModels/OpeningStockViewModel.cs`: نموذج عرض متكامل يدعم المسح بالباركود، البحث بـ F4، إضافة أصناف جديدة على الطاير، إدخال العلب والأشرطة والأقراص، استيراد/تصدير Excel، استعراض السندات السابقة، وبطاقات المؤشرات المالية الفورية.
- `App/Views/OpeningStockView.xaml` و `.xaml.cs`: واجهة مستخدم متناسقة ومصممة بعناية مع جدول تفصيلي، كروت إحصائيات، ومربعات حوار منسدلة.
- `App/Resources/Icons.xaml`: إضافة أيقونة فيكتور جديدة `IconOpeningStock`.
- `App/MainWindow.xaml` و `ViewModels/MainViewModel.cs`: إضافة زر التنقل للشريط الجانبي، صلاحيات الوصول، ودعم النوافذ المستقلة.
- `App/Services/WindowManager.cs` و `App.xaml.cs`: تسجيل مستودع ونموذج العرض في DI وربط عنوان النافذة.
- `tests/ePrescriptions.Tests/OpeningStockTests.cs`: حزمة اختبارات شاملة تغطي حسابات الكميات والتكلفة والبيع، حفظ السندات للأصناف الموجودة والجديدة، قيود اليومية، وتسلسل الأرقام والصلاحيات.
ملخص: إتاحة إدخال وحصر المخزون الافتتاحي بالكامل قبل بدء العمل الفعلي بالصيدلية مع دعم الاستيراد من Excel والإدخال السريع بالباركود.

## الخطوة التالية: خطوة 48 — README.md نهائي شامل (متطلبات التشغيل، التثبيت الأول، بيانات الدخول لكل دور، النسخ الاحتياطي اليدوي، أي خطوة يدوية بعد أول تشغيل).

