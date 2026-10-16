using System;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.App.Services;
using ePrescriptions.App.Views;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly INavigationService _navigationService;
        private readonly IUserSessionService _sessionService;
        private readonly IServiceProvider _serviceProvider;
        private readonly DispatcherTimer _clockTimer;
        private readonly BackupSchedulerHost? _backupHost;
        private readonly IWindowManager _windowManager;

        private ViewModelBase? _currentView;
        public ViewModelBase? CurrentView
        {
            get => _currentView;
            set
            {
                if (SetProperty(ref _currentView, value))
                {
                    OnPropertyChanged(nameof(CurrentViewTitle));
                }
            }
        }

        public string CurrentViewTitle => _currentView == null ? string.Empty : WindowManager.GetDefaultTitleForViewModel(_currentView);

        private string _liveDateTimeString = string.Empty;
        public string LiveDateTimeString
        {
            get => _liveDateTimeString;
            set => SetProperty(ref _liveDateTimeString, value);
        }

        private string _currentUserDisplay = string.Empty;
        public string CurrentUserDisplay
        {
            get => _currentUserDisplay;
            set => SetProperty(ref _currentUserDisplay, value);
        }

        private string _shiftStatusDisplay = "الوردية رقم 1 [مفتوحة]";
        public string ShiftStatusDisplay
        {
            get => _shiftStatusDisplay;
            set => SetProperty(ref _shiftStatusDisplay, value);
        }

        /// <summary>خطوة 41: تنبيه النسخ الاحتياطي (فشل نسخة / فشل اختبار استعادة / نسخة قديمة). بيظهر للمدير والمالك بس لأنهم اللي يقدروا يصلحوه.</summary>
        public bool HasBackupAlert => IsAdminOrOwner && (_backupHost?.HasAlerts ?? false);
        public string BackupAlertText => _backupHost?.AlertText ?? string.Empty;
        public IRelayCommand OpenBackupSettingsCommand { get; }

        // أوامر التنقل للشريط الجانبي
        public IRelayCommand NavigateSalesCommand { get; }
        public IRelayCommand NavigateInventoryCommand { get; }
        public IRelayCommand NavigatePurchasesCommand { get; }
        public IRelayCommand NavigateSupplierReturnsCommand { get; }
        public IRelayCommand NavigatePurchaseOrdersCommand { get; }
        public IRelayCommand NavigateStocktakeCommand { get; }
        public IRelayCommand NavigateOpeningStockCommand { get; }
        public IRelayCommand NavigatePriceListImportCommand { get; }
        public IRelayCommand NavigateCustomersCommand { get; }
        public IRelayCommand NavigateFinanceCommand { get; }
        public IRelayCommand NavigateStaffCommand { get; }
        public IRelayCommand NavigateZakatCommand { get; }
        public IRelayCommand NavigateReportsCommand { get; }
        public IRelayCommand NavigateSettingsCommand { get; }
        public IRelayCommand NavigateHelpCommand { get; }
        public IRelayCommand NavigateOwnerLogCommand { get; }
        public IRelayCommand NavigateStaffAuditLogCommand { get; }

        /// <summary>
        /// true فقط لحساب صاحب الصيدلية؛ تتحكم في ظهور زر "سجل نشاطي" (المخفي عن الجميع) في الشريط الجانبي.
        /// لا تُستخدم كآلية حماية وحيدة: شاشة OwnerLogViewModel نفسها لا تُستدعى إلا عبر هذا الزر.
        /// </summary>
        public bool IsOwner => _sessionService.CurrentUser?.Role == ePrescriptions.Domain.Enums.UserRole.Owner;

        /// <summary>
        /// true للمدير وصاحب الصيدلية؛ تتحكم في ظهور زر "سجل نشاط الموظفين" (لا يحتوي على تعديلات Owner).
        /// </summary>
        public bool IsAdminOrOwner =>
            _sessionService.CurrentUser?.Role == ePrescriptions.Domain.Enums.UserRole.Admin || IsOwner;

        // ===== صلاحيات التنقل حسب الدور والصلاحيات المخصصة للموظف =====
        private ePrescriptions.Domain.Enums.UserRole? CurrentRole => _sessionService.CurrentUser?.Role;
        private ePrescriptions.Domain.Entities.User? CurrentUser => _sessionService.CurrentUser;

        public bool CanAccessSales => CurrentUser != null && (CurrentUser.EffectivePermissions.IsCustom ? CurrentUser.EffectivePermissions.CanAccessSales : (CurrentRole == ePrescriptions.Domain.Enums.UserRole.Cashier || CurrentRole == ePrescriptions.Domain.Enums.UserRole.Pharmacist || IsAdminOrOwner));
        public bool CanAccessInventory => CurrentUser != null && (CurrentUser.EffectivePermissions.IsCustom ? CurrentUser.EffectivePermissions.CanAccessInventory : (CurrentRole == ePrescriptions.Domain.Enums.UserRole.Pharmacist || IsAdminOrOwner));
        public bool CanAccessPurchases => CurrentUser != null && (CurrentUser.EffectivePermissions.IsCustom ? CurrentUser.EffectivePermissions.CanAccessPurchases : (CurrentRole == ePrescriptions.Domain.Enums.UserRole.Pharmacist || IsAdminOrOwner));
        public bool CanAccessCustomers => CurrentUser != null && (CurrentUser.EffectivePermissions.IsCustom ? CurrentUser.EffectivePermissions.CanAccessCustomers : (CurrentRole == ePrescriptions.Domain.Enums.UserRole.Pharmacist || IsAdminOrOwner));
        public bool CanAccessReports => CurrentUser != null && (CurrentUser.EffectivePermissions.IsCustom ? CurrentUser.EffectivePermissions.CanAccessReports : (CurrentRole == ePrescriptions.Domain.Enums.UserRole.Pharmacist || IsAdminOrOwner));
        public bool CanAccessFinance => CurrentUser != null && (CurrentUser.EffectivePermissions.IsCustom ? CurrentUser.EffectivePermissions.CanAccessFinance : IsAdminOrOwner);
        public bool CanAccessStaff => CurrentUser != null && (CurrentUser.EffectivePermissions.IsCustom ? CurrentUser.EffectivePermissions.CanAccessStaff : IsAdminOrOwner);
        public bool CanAccessZakat => CurrentUser != null && (CurrentUser.EffectivePermissions.IsCustom ? CurrentUser.EffectivePermissions.CanAccessFinance : IsAdminOrOwner);
        public bool CanAccessSettings => CurrentUser != null && (CurrentUser.EffectivePermissions.IsCustom ? CurrentUser.EffectivePermissions.CanAccessSettings : IsAdminOrOwner);
        public bool CanAccessPriceListImport => IsAdminOrOwner;
        public bool CanAccessOpeningStock => CanAccessInventory;


        // أوامر الأمان والجلسة
        public IRelayCommand LockScreenCommand { get; }
        public IRelayCommand LogoutCommand { get; }

        // أوامر فتح الشاشات في نوافذ منفصلة (Multi-Window)
        public IRelayCommand OpenCurrentInSeparateWindowCommand { get; }
        public IRelayCommand OpenSalesInSeparateWindowCommand { get; }
        public IRelayCommand OpenPurchasesInSeparateWindowCommand { get; }
        public IRelayCommand OpenInventoryInSeparateWindowCommand { get; }
        public IRelayCommand OpenOpeningStockInSeparateWindowCommand { get; }
        public IRelayCommand OpenCustomersInSeparateWindowCommand { get; }
        public IRelayCommand OpenReportsInSeparateWindowCommand { get; }

        public MainViewModel(
            INavigationService navigationService,
            IUserSessionService sessionService,
            IServiceProvider serviceProvider,
            IWindowManager? windowManager = null)
        {
            _navigationService = navigationService;
            _sessionService = sessionService;
            _serviceProvider = serviceProvider;
            _windowManager = windowManager ?? (serviceProvider.GetService(typeof(IWindowManager)) as IWindowManager) ?? new WindowManager(serviceProvider);

            _navigationService.CurrentViewModelChanged += vm => CurrentView = vm;
            _sessionService.SessionChanged += UpdateSessionDisplay;

            // جدولة النسخ الاحتياطي (اختيارية: لو مش مسجّلة في DI الشريط ببساطة ما يظهرش)
            _backupHost = serviceProvider.GetService(typeof(BackupSchedulerHost)) as BackupSchedulerHost;
            if (_backupHost != null)
            {
                _backupHost.AlertsChanged += () =>
                {
                    OnPropertyChanged(nameof(HasBackupAlert));
                    OnPropertyChanged(nameof(BackupAlertText));
                };
            }

            // تحديث شريط التاريخ والوقت كل ثانية باستخدام UmAlQura والأرقام اللاتينية
            UpdateLiveClock();
            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (s, e) => UpdateLiveClock();
            _clockTimer.Start();

            // كل أمر تنقل محروس بـ CanExecute حسب الدور (دفاع إضافي بجانب إخفاء الزر في XAML،
            // فحتى لو ظهر الزر بطريقة ما، الأمر نفسه يرفض التنفيذ).
            NavigateSalesCommand = new RelayCommand(() => _navigationService.NavigateTo<SalesViewModel>(), () => CanAccessSales);
            NavigateInventoryCommand = new RelayCommand(() => _navigationService.NavigateTo<InventoryViewModel>(), () => CanAccessInventory);
            NavigatePurchasesCommand = new RelayCommand(() => _navigationService.NavigateTo<PurchasesViewModel>(), () => CanAccessPurchases);
            NavigateSupplierReturnsCommand = new RelayCommand(() => _navigationService.NavigateTo<SupplierReturnsViewModel>(), () => CanAccessPurchases);
            NavigatePurchaseOrdersCommand = new RelayCommand(() => _navigationService.NavigateTo<PurchaseOrdersViewModel>(), () => CanAccessPurchases);
            NavigateStocktakeCommand = new RelayCommand(() => _navigationService.NavigateTo<StocktakeViewModel>(), () => CanAccessInventory);
            NavigateOpeningStockCommand = new RelayCommand(() => _navigationService.NavigateTo<OpeningStockViewModel>(), () => CanAccessOpeningStock);
            NavigatePriceListImportCommand = new RelayCommand(() => _navigationService.NavigateTo<PriceListImportViewModel>(), () => CanAccessPriceListImport);
            NavigateCustomersCommand = new RelayCommand(() => _navigationService.NavigateTo<CustomersViewModel>(), () => CanAccessCustomers);
            NavigateFinanceCommand = new RelayCommand(() => _navigationService.NavigateTo<FinanceViewModel>(), () => CanAccessFinance);
            NavigateStaffCommand = new RelayCommand(() => _navigationService.NavigateTo<StaffViewModel>(), () => CanAccessStaff);
            NavigateZakatCommand = new RelayCommand(() => _navigationService.NavigateTo<ZakatViewModel>(), () => CanAccessZakat);
            NavigateReportsCommand = new RelayCommand(() => _navigationService.NavigateTo<ReportsViewModel>(), () => CanAccessReports);
            NavigateSettingsCommand = new RelayCommand(() => _navigationService.NavigateTo<SettingsViewModel>(), () => CanAccessSettings);
            NavigateHelpCommand = new RelayCommand(() => _navigationService.NavigateTo<HelpViewModel>());
            NavigateOwnerLogCommand = new RelayCommand(() => _navigationService.NavigateTo<OwnerLogViewModel>(), () => IsOwner);
            NavigateStaffAuditLogCommand = new RelayCommand(() => _navigationService.NavigateTo<StaffAuditLogViewModel>(), () => IsAdminOrOwner);

            OpenBackupSettingsCommand = new RelayCommand(() => _navigationService.NavigateTo<SettingsViewModel>(), () => CanAccessSettings);

            // أوامر النوافذ المنفصلة
            OpenCurrentInSeparateWindowCommand = new RelayCommand(() =>
            {
                if (_currentView != null)
                {
                    _windowManager.OpenInSeparateWindow(_currentView);
                }
            });
            OpenSalesInSeparateWindowCommand = new RelayCommand(() => _windowManager.OpenInSeparateWindow<SalesViewModel>(), () => CanAccessSales);
            OpenPurchasesInSeparateWindowCommand = new RelayCommand(() => _windowManager.OpenInSeparateWindow<PurchasesViewModel>(), () => CanAccessPurchases);
            OpenInventoryInSeparateWindowCommand = new RelayCommand(() => _windowManager.OpenInSeparateWindow<InventoryViewModel>(), () => CanAccessInventory);
            OpenOpeningStockInSeparateWindowCommand = new RelayCommand(() => _windowManager.OpenInSeparateWindow<OpeningStockViewModel>(), () => CanAccessOpeningStock);
            OpenCustomersInSeparateWindowCommand = new RelayCommand(() => _windowManager.OpenInSeparateWindow<CustomersViewModel>(), () => CanAccessCustomers);
            OpenReportsInSeparateWindowCommand = new RelayCommand(() => _windowManager.OpenInSeparateWindow<ReportsViewModel>(), () => CanAccessReports);

            LockScreenCommand = new RelayCommand(ExecuteLockScreen);
            LogoutCommand = new RelayCommand(ExecuteLogout);

            UpdateSessionDisplay();
        }

        private void UpdateLiveClock()
        {
            LiveDateTimeString = DateHelper.FormatFullBanner(DateTime.Now);
        }

        private void UpdateSessionDisplay()
        {
            OnPropertyChanged(nameof(IsOwner));
            OnPropertyChanged(nameof(IsAdminOrOwner));
            OnPropertyChanged(nameof(CanAccessSales));
            OnPropertyChanged(nameof(CanAccessInventory));
            OnPropertyChanged(nameof(CanAccessPurchases));
            OnPropertyChanged(nameof(CanAccessCustomers));
            OnPropertyChanged(nameof(CanAccessReports));
            OnPropertyChanged(nameof(CanAccessFinance));
            OnPropertyChanged(nameof(CanAccessStaff));
            OnPropertyChanged(nameof(CanAccessZakat));
            OnPropertyChanged(nameof(CanAccessSettings));
            OnPropertyChanged(nameof(CanAccessPriceListImport));
            OnPropertyChanged(nameof(CanAccessOpeningStock));
            OnPropertyChanged(nameof(HasBackupAlert));

            // إعادة تقييم CanExecute لكل أوامر التنقل بعد تغيّر الجلسة (دخول/خروج/قفل)
            NavigateSalesCommand.NotifyCanExecuteChanged();
            NavigateInventoryCommand.NotifyCanExecuteChanged();

            NavigatePurchasesCommand.NotifyCanExecuteChanged();
            NavigateSupplierReturnsCommand.NotifyCanExecuteChanged();
            NavigatePurchaseOrdersCommand.NotifyCanExecuteChanged();
            NavigateStocktakeCommand.NotifyCanExecuteChanged();
            NavigateOpeningStockCommand.NotifyCanExecuteChanged();
            NavigatePriceListImportCommand.NotifyCanExecuteChanged();
            NavigateCustomersCommand.NotifyCanExecuteChanged();
            NavigateReportsCommand.NotifyCanExecuteChanged();
            NavigateFinanceCommand.NotifyCanExecuteChanged();
            NavigateStaffCommand.NotifyCanExecuteChanged();
            NavigateZakatCommand.NotifyCanExecuteChanged();
            NavigateSettingsCommand.NotifyCanExecuteChanged();
            OpenBackupSettingsCommand.NotifyCanExecuteChanged();
            NavigateOwnerLogCommand.NotifyCanExecuteChanged();
            NavigateStaffAuditLogCommand.NotifyCanExecuteChanged();

            // أوامر النوافذ المستقلة
            OpenPurchasesInSeparateWindowCommand.NotifyCanExecuteChanged();
            OpenInventoryInSeparateWindowCommand.NotifyCanExecuteChanged();
            OpenOpeningStockInSeparateWindowCommand.NotifyCanExecuteChanged();
            OpenCustomersInSeparateWindowCommand.NotifyCanExecuteChanged();
            OpenReportsInSeparateWindowCommand.NotifyCanExecuteChanged();

            if (_sessionService.CurrentUser != null)
            {
                CurrentUserDisplay = $"{_sessionService.CurrentUser.FullName} ({_sessionService.CurrentUser.Role})";
            }
            else
            {
                CurrentUserDisplay = string.Empty;
            }

            if (_sessionService.CurrentShift != null)
            {
                ShiftStatusDisplay = $"الوردية رقم {_sessionService.CurrentShift.Id} [مفتوحة]";
            }
            else
            {
                ShiftStatusDisplay = "لا توجد وردية مفتوحة";
            }
        }

        private void ExecuteLockScreen()
        {
            _windowManager.CloseAll();
            _sessionService.Lock();
            var loginVm = (LoginViewModel)_serviceProvider.GetService(typeof(LoginViewModel))!;
            loginVm.SetMode(lockMode: true);

            var loginWin = new LoginWindow(loginVm)
            {
                Owner = Application.Current.MainWindow
            };
            loginWin.ShowDialog();
        }

        private void ExecuteLogout()
        {
            _windowManager.CloseAll();
            _sessionService.Logout();
            var loginVm = (LoginViewModel)_serviceProvider.GetService(typeof(LoginViewModel))!;
            loginVm.SetMode(lockMode: false);

            var loginWin = new LoginWindow(loginVm)
            {
                Owner = Application.Current.MainWindow
            };
            if (loginWin.ShowDialog() != true)
            {
                Application.Current.Shutdown();
            }
        }
    }
}
