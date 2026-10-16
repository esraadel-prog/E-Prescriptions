using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;

namespace ePrescriptions.App
{
    public partial class App : Application
    {
        private SingleInstanceMutex? _singleInstanceMutex;
        private IServiceProvider? _serviceProvider;

        public static IServiceProvider ServiceProvider => ((App)Current)._serviceProvider!;

        protected override void OnStartup(StartupEventArgs e)
        {
            // منع إغلاق البرنامج تلقائياً عند غلق نافذة الدخول قبل ظهور النافذة الرئيسية
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 1) قفل نسخة واحدة بـ Mutex
            _singleInstanceMutex = new SingleInstanceMutex();
            if (!_singleInstanceMutex.IsSingleInstance)
            {
                MessageBox.Show(
                    "برنامج e_prescriptions يعمل بالفعل على هذا الجهاز في الخلفية.",
                    "تنبيه - نسخة مكررة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                Shutdown();
                return;
            }

            // 2) معالجة الأخطاء غير المتوقعة وتسجيلها في ملف داخل ProgramData
            SetupGlobalExceptionHandling();

            // 3) ضبط الثقافة: مصر (تقويم ميلادي) حتى لا تُكتب تواريخ قاعدة البيانات بالسنة الهجرية، مع أرقام لاتينية
            var culture = new CultureInfo("ar-EG", false);
            culture.NumberFormat.DigitSubstitution = DigitShapes.None;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            // الخيوط الخلفية (Task.Run/async) ترث نفس الثقافة بدل ثقافة ويندوز
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            base.OnStartup(e);

            // 4) إعداد حقن التبعيات (DI)
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            _serviceProvider = serviceCollection.BuildServiceProvider();

            // 5) تهيئة قاعدة بيانات SQLite وتطبيق الـ Migrations والبيانات التجريبية
            try
            {
                // ترحيل بيانات التثبيتات القديمة (%AppData%) إلى %ProgramData% قبل فتح القاعدة
                try
                {
                    var migration = ePrescriptions.Core.Helpers.LegacyDataMigrator.MigrateIfNeeded();
                    if (migration == ePrescriptions.Core.Helpers.LegacyMigrationResult.Migrated)
                        Logger.LogInfo("تم نسخ قاعدة البيانات القديمة من AppData إلى ProgramData.", "App.OnStartup");
                }
                catch (Exception migEx)
                {
                    Logger.LogException(migEx, "App.LegacyDataMigration");
                }

                var dbContext = _serviceProvider.GetRequiredService<DatabaseContext>();
                // بدون بيانات تجريبية ولا حساب Owner افتراضي: ده كله بيتم من معالج الإعداد الأول
                dbContext.InitializeAndMigrate(seedData: false);
                Logger.LogInfo("تم تشغيل التطبيق بنجاح وتهيئة قاعدة البيانات.", "App.OnStartup");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "App.InitializeDatabase");
                MessageBox.Show(
                    $"حدث خطأ أثناء تهيئة قاعدة البيانات:\n{ex.Message}\nيرجى مراجعة سجل الأخطاء في C:\\ProgramData\\ePrescriptions\\logs.",
                    "خطأ فادح في قاعدة البيانات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            // 5.5) الإعداد الأول: لو مفيش حساب Owner يبقى ده أول تشغيل
            var setupService = _serviceProvider.GetRequiredService<ePrescriptions.Data.Setup.FirstRunSetupService>();
            if (setupService.IsFirstRun())
            {
                var setupVm = _serviceProvider.GetRequiredService<FirstRunSetupViewModel>();
                var setupWin = new Views.FirstRunSetupWindow(setupVm);
                if (setupWin.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            // 6) تسجيل الدخول الأولي لحماية نقطة البيع
            var sessionService = _serviceProvider.GetRequiredService<IUserSessionService>();
            var loginVm = _serviceProvider.GetRequiredService<LoginViewModel>();
            var loginWin = new Views.LoginWindow(loginVm);
            if (loginWin.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            // 6.5) تنبيهات ذكية عند فتح البرنامج (لو توفرت تنبيهات نشطة):
            var alertsService = _serviceProvider.GetRequiredService<ISmartAlertsService>();
            var alertsSummary = alertsService.GetAlerts();
            if (alertsSummary.HasAnyAlerts)
            {
                var alertsVm = _serviceProvider.GetRequiredService<SmartAlertsViewModel>();
                alertsVm.LoadAlerts(alertsSummary);
                var alertsWin = new Views.SmartAlertsWindow(alertsVm);
                alertsWin.ShowDialog();
            }

            // 7) تعيين الشاشة الافتراضية عند الفتح وهي المبيعات (SalesViewModel)
            var navService = _serviceProvider.GetRequiredService<INavigationService>();
            navService.NavigateTo<SalesViewModel>();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

            // 8) جدولة النسخ الاحتياطي اليومي واختبار الاستعادة الدوري (أول فحص بعد دقيقة من التشغيل)
            _serviceProvider.GetRequiredService<BackupSchedulerHost>().Start();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // Database
            services.AddSingleton<DatabaseContext>();

            // Repositories
            services.AddSingleton<ItemRepository>();
            services.AddSingleton<InvoiceRepository>();
            services.AddSingleton<CustomerRepository>();
            services.AddSingleton<UserRepository>();
            services.AddSingleton<ShiftRepository>();
            services.AddSingleton<SettingsRepository>();
            services.AddSingleton<PurchaseRepository>();
            services.AddSingleton<SupplierRepository>();
            services.AddSingleton<SupplierReturnRepository>();
            services.AddSingleton<PurchaseOrderRepository>();
            services.AddSingleton<ePrescriptions.Data.Setup.FirstRunSetupService>();
            services.AddSingleton<FinanceRepository>();
            services.AddSingleton<ReportRepository>();
            services.AddSingleton<OwnerAuditLogRepository>();
            services.AddSingleton<StaffAuditLogRepository>();
            services.AddSingleton<OpeningStockRepository>();

            // Services & Export
            services.AddSingleton<IUserSessionService, UserSessionService>();
            services.AddSingleton<IdleLockMonitor>();
            services.AddSingleton<IBarcodeLabelService, BarcodeLabelService>();
            services.AddSingleton<IExcelExportService, ExcelExportService>();
            services.AddSingleton<IPriceListFileReader, ExcelPriceListReader>();
            // خطوة 41: ملف حالة النسخ + نسخ احتياطي بيسجّل نتيجته + أتمتة يومية واختبار استعادة + مؤقّت التشغيل
            services.AddSingleton<BackupStatusStore>(_ => new BackupStatusStore());
            services.AddSingleton<IBackupService>(sp => new BackupService(
                sp.GetRequiredService<DatabaseContext>(),
                sp.GetRequiredService<BackupStatusStore>()));
            services.AddSingleton<IBackupAutomationService, BackupAutomationService>();
            services.AddSingleton<BackupSchedulerHost>(sp => new BackupSchedulerHost(sp.GetRequiredService<IBackupAutomationService>()));
            services.AddSingleton<IPdfReportService, PdfReportService>();
            services.AddSingleton<ISmartAlertsService, SmartAlertsService>();

            // Navigation & Window Management
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IWindowManager, WindowManager>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<LoginViewModel>();
            services.AddTransient<FirstRunSetupViewModel>();
            services.AddTransient<SmartAlertsViewModel>();
            services.AddTransient<SalesViewModel>();
            services.AddTransient<InventoryViewModel>();
            services.AddTransient<PurchasesViewModel>();
            services.AddTransient<SupplierReturnsViewModel>();
            services.AddTransient<PurchaseOrdersViewModel>();
            services.AddTransient<StocktakeViewModel>();
            services.AddTransient<OpeningStockViewModel>();
            services.AddTransient<PriceListImportViewModel>();
            services.AddTransient<CustomersViewModel>();
            services.AddTransient<FinanceViewModel>();
            services.AddTransient<StaffViewModel>();
            services.AddTransient<ZakatViewModel>();
            services.AddTransient<ReportsViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<HelpViewModel>();
            services.AddTransient<OwnerLogViewModel>();
            services.AddTransient<StaffAuditLogViewModel>();

            // Views & Main Window
            services.AddSingleton<MainWindow>();
        }

        private void SetupGlobalExceptionHandling()
        {
            DispatcherUnhandledException += (s, args) =>
            {
                Logger.LogException(args.Exception, "DispatcherUnhandledException");
                MessageBox.Show(
                    $"حدث خطأ أثناء تنفيذ العملية:\n{args.Exception.Message}\nتم تسجيل التفاصيل في السجل دون إغلاق البرنامج.",
                    "تنبيه - خطأ غير معالج",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                args.Handled = true; // منع إغلاق البرنامج فجأة
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    Logger.LogException(ex, "AppDomain.UnhandledException");
                }
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
