using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Security;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    public class StaffViewModel : ViewModelBase
    {
        private readonly UserRepository _userRepository;
        private readonly ShiftRepository _shiftRepository;
        private readonly Services.IUserSessionService? _sessionService;
        private readonly Services.IBackupService? _backupService;
        private readonly Func<BackupSettings> _backupSettingsProvider;
        private readonly ReportRepository? _reportRepository;
        private readonly SettingsRepository? _settingsRepository;
        private readonly Services.IPdfReportService? _pdfReportService;

        // قائمة الموظفين
        public ObservableCollection<User> UsersList { get; } = new();

        // الوردية الحالية
        private Shift? _currentShift;
        public Shift? CurrentShift
        {
            get => _currentShift;
            set
            {
                if (SetProperty(ref _currentShift, value))
                {
                    OnPropertyChanged(nameof(HasOpenShift));
                    OnPropertyChanged(nameof(FormattedOpeningBalance));
                    OnPropertyChanged(nameof(FormattedExpectedCash));
                    OnPropertyChanged(nameof(FormattedCashSales));
                    OnPropertyChanged(nameof(FormattedNetworkSales));
                }
            }
        }

        public bool HasOpenShift => CurrentShift != null && CurrentShift.Status == ShiftStatus.Open;

        public string FormattedOpeningBalance => CurrentShift != null ? Money.FromPiastres(CurrentShift.OpeningBalancePiastres).ToFormattedString() : "-";
        public string FormattedExpectedCash => CurrentShift != null ? Money.FromPiastres(CurrentShift.ExpectedCashPiastres).ToFormattedString() : "-";
        public string FormattedCashSales => CurrentShift != null ? Money.FromPiastres(CurrentShift.TotalSalesCashPiastres).ToFormattedString() : "-";
        public string FormattedNetworkSales => CurrentShift != null ? Money.FromPiastres(CurrentShift.TotalSalesNetworkPiastres).ToFormattedString() : "-";
        public string FormattedOnAccountSales => CurrentShift != null ? Money.FromPiastres(CurrentShift.TotalSalesOnAccountPiastres).ToFormattedString() : "-";
        public string FormattedCashReturns => CurrentShift != null ? Money.FromPiastres(CurrentShift.TotalReturnsCashPiastres).ToFormattedString() : "-";
        public string FormattedTotalSales => CurrentShift != null ? Money.FromPiastres(CurrentShift.TotalSalesCashPiastres + CurrentShift.TotalSalesNetworkPiastres + CurrentShift.TotalSalesOnAccountPiastres).ToFormattedString() : "-";

        // حقول إغلاق الوردية
        private decimal _actualClosingCashPounds;
        public decimal ActualClosingCashPounds
        {
            get => _actualClosingCashPounds;
            set
            {
                if (SetProperty(ref _actualClosingCashPounds, value))
                {
                    CalculateShiftDifference();
                }
            }
        }

        private decimal _shiftDifferencePounds;
        public decimal ShiftDifferencePounds
        {
            get => _shiftDifferencePounds;
            set
            {
                if (SetProperty(ref _shiftDifferencePounds, value))
                {
                    OnPropertyChanged(nameof(DifferenceStatusText));
                    OnPropertyChanged(nameof(DifferenceStatusColor));
                    OnPropertyChanged(nameof(IsDifferenceZero));
                    OnPropertyChanged(nameof(IsDifferenceSurplus));
                    OnPropertyChanged(nameof(IsDifferenceDeficit));
                }
            }
        }

        public string DifferenceStatusText => ShiftDifferencePounds == 0
            ? "الصندوق مطابق تماماً (0.00 ج.م)"
            : ShiftDifferencePounds > 0
                ? $"يوجد زيادة بالصندوق قدرها +{ShiftDifferencePounds:N2} ج.م"
                : $"يوجد عجز بالصندوق قدره {ShiftDifferencePounds:N2} ج.م";

        public string DifferenceStatusColor => ShiftDifferencePounds == 0
            ? "#059669"
            : ShiftDifferencePounds > 0
                ? "#D97706"
                : "#DC2626";

        public bool IsDifferenceZero => ShiftDifferencePounds == 0;
        public bool IsDifferenceSurplus => ShiftDifferencePounds > 0;
        public bool IsDifferenceDeficit => ShiftDifferencePounds < 0;

        private ShiftClosureReport? _lastClosedShiftReport;
        public ShiftClosureReport? LastClosedShiftReport
        {
            get => _lastClosedShiftReport;
            set
            {
                if (SetProperty(ref _lastClosedShiftReport, value))
                {
                    OnPropertyChanged(nameof(HasClosedShiftReport));
                }
            }
        }
        public bool HasClosedShiftReport => LastClosedShiftReport != null;

        private string _closingNotes = string.Empty;
        public string ClosingNotes
        {
            get => _closingNotes;
            set => SetProperty(ref _closingNotes, value);
        }

        // حقول فتح وردية جديدة
        private decimal _newOpeningBalancePounds = 200;
        public decimal NewOpeningBalancePounds
        {
            get => _newOpeningBalancePounds;
            set => SetProperty(ref _newOpeningBalancePounds, value);
        }

        // حقول إضافة موظف جديد
        private string _newUsername = string.Empty;
        public string NewUsername { get => _newUsername; set => SetProperty(ref _newUsername, value); }

        private string _newFullName = string.Empty;
        public string NewFullName { get => _newFullName; set => SetProperty(ref _newFullName, value); }

        private string _newPassword = string.Empty;
        public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }

        private UserRole _newRole = UserRole.Pharmacist;
        public UserRole NewRole { get => _newRole; set => SetProperty(ref _newRole, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        // ===== خطوة 32 + تخصيص الصلاحيات الفردية للموظفين =====
        private User? _selectedUser;
        /// <summary>الموظف المحدد في القائمة. اختياره بيملى حقول التعديل والصلاحيات بقيمه الحالية.</summary>
        public User? SelectedUser
        {
            get => _selectedUser;
            set
            {
                if (SetProperty(ref _selectedUser, value))
                {
                    EditFullName = value?.FullName ?? string.Empty;
                    EditUsername = value?.Username ?? string.Empty;
                    EditRole = value != null && value.Role != UserRole.Owner ? value.Role : UserRole.Pharmacist;
                    ResetPasswordText = string.Empty;
                    LoadPermissionsFromUser(value);
                    OnPropertyChanged(nameof(HasSelectedUser));
                    OnPropertyChanged(nameof(ToggleActiveButtonText));
                }
            }
        }

        public bool HasSelectedUser => SelectedUser != null;
        public string ToggleActiveButtonText => SelectedUser != null && !SelectedUser.IsActive ? "تفعيل الحساب" : "تعطيل الحساب";

        private string _editFullName = string.Empty;
        public string EditFullName { get => _editFullName; set => SetProperty(ref _editFullName, value); }

        private string _editUsername = string.Empty;
        public string EditUsername { get => _editUsername; set => SetProperty(ref _editUsername, value); }

        private UserRole _editRole = UserRole.Pharmacist;
        public UserRole EditRole
        {
            get => _editRole;
            set
            {
                if (SetProperty(ref _editRole, value))
                {
                    OnPropertyChanged(nameof(PermissionsModeText));
                    if (!IsCustomPermissions)
                    {
                        ApplyRoleDefaultPermissions(value);
                    }
                }
            }
        }

        private string _resetPasswordText = string.Empty;
        public string ResetPasswordText { get => _resetPasswordText; set => SetProperty(ref _resetPasswordText, value); }

        /// <summary>أقل طول مسموح لكلمة المرور الجديدة عند إعادة التعيين (نفس حد تغيير كلمة المرور الذاتي)</summary>
        public const int MinPasswordLength = 8;

        // ===== حقول الصلاحيات المخصصة للموظف المحدد =====
        private bool _isCustomPermissions;
        public bool IsCustomPermissions
        {
            get => _isCustomPermissions;
            set
            {
                if (SetProperty(ref _isCustomPermissions, value))
                {
                    OnPropertyChanged(nameof(PermissionsModeText));
                    if (!value)
                    {
                        ApplyRoleDefaultPermissions(EditRole);
                    }
                }
            }
        }

        public string PermissionsModeText => IsCustomPermissions
            ? "صلاحيات مخصصة مفعلة (استثناء مستقل عن الدور)"
            : $"تتبع الدور الافتراضي ({GetRoleArabic(EditRole)})";

        private bool _permCanAccessSales = true;
        public bool PermCanAccessSales { get => _permCanAccessSales; set => SetProperty(ref _permCanAccessSales, value); }

        private bool _permCanAccessInventory;
        public bool PermCanAccessInventory { get => _permCanAccessInventory; set => SetProperty(ref _permCanAccessInventory, value); }

        private bool _permCanAccessPurchases;
        public bool PermCanAccessPurchases { get => _permCanAccessPurchases; set => SetProperty(ref _permCanAccessPurchases, value); }

        private bool _permCanAccessCustomers;
        public bool PermCanAccessCustomers { get => _permCanAccessCustomers; set => SetProperty(ref _permCanAccessCustomers, value); }

        private bool _permCanAccessReports;
        public bool PermCanAccessReports { get => _permCanAccessReports; set => SetProperty(ref _permCanAccessReports, value); }

        private bool _permCanAccessFinance;
        public bool PermCanAccessFinance { get => _permCanAccessFinance; set => SetProperty(ref _permCanAccessFinance, value); }

        private bool _permCanAccessStaff;
        public bool PermCanAccessStaff { get => _permCanAccessStaff; set => SetProperty(ref _permCanAccessStaff, value); }

        private bool _permCanAccessSettings;
        public bool PermCanAccessSettings { get => _permCanAccessSettings; set => SetProperty(ref _permCanAccessSettings, value); }

        private bool _permCanAccessPriceListImport;
        public bool PermCanAccessPriceListImport { get => _permCanAccessPriceListImport; set => SetProperty(ref _permCanAccessPriceListImport, value); }

        private decimal _permMaxDiscountPercent = 5m;
        public decimal PermMaxDiscountPercent { get => _permMaxDiscountPercent; set => SetProperty(ref _permMaxDiscountPercent, value); }

        private bool _permCanPerformReturnsWithoutApproval;
        public bool PermCanPerformReturnsWithoutApproval { get => _permCanPerformReturnsWithoutApproval; set => SetProperty(ref _permCanPerformReturnsWithoutApproval, value); }

        private bool _permCanApproveOverrides;
        public bool PermCanApproveOverrides { get => _permCanApproveOverrides; set => SetProperty(ref _permCanApproveOverrides, value); }

        private bool _permCanManageStocktake;
        public bool PermCanManageStocktake { get => _permCanManageStocktake; set => SetProperty(ref _permCanManageStocktake, value); }

        private bool _permCanEditItemPrices;
        public bool PermCanEditItemPrices { get => _permCanEditItemPrices; set => SetProperty(ref _permCanEditItemPrices, value); }

        // الأوامر
        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand OpenShiftCommand { get; }
        public IRelayCommand CloseShiftCommand { get; }
        public IRelayCommand PrintShiftReportCommand { get; }
        public IRelayCommand GenerateShiftReportPdfCommand { get; }
        public IRelayCommand AddUserCommand { get; }
        public IRelayCommand SaveEditCommand { get; }
        public IRelayCommand ToggleActiveCommand { get; }
        public IRelayCommand ResetPasswordCommand { get; }
        public IRelayCommand SavePermissionsCommand { get; }
        public IRelayCommand ResetPermissionsToRoleDefaultCommand { get; }

        public StaffViewModel(UserRepository userRepository, ShiftRepository shiftRepository, Services.IUserSessionService? sessionService = null,
            Services.IBackupService? backupService = null, Func<BackupSettings>? backupSettingsProvider = null,
            ReportRepository? reportRepository = null, SettingsRepository? settingsRepository = null, Services.IPdfReportService? pdfReportService = null)
        {
            _backupService = backupService;
            _backupSettingsProvider = backupSettingsProvider ?? (() => BackupSettingsStore.Load());
            _reportRepository = reportRepository;
            _settingsRepository = settingsRepository;
            _pdfReportService = pdfReportService;
            Title = "الموظفون وإدارة الورديات والصندوق";
            _userRepository = userRepository;
            _shiftRepository = shiftRepository;
            _sessionService = sessionService;

            RefreshCommand = new RelayCommand(LoadData);
            OpenShiftCommand = new RelayCommand(OpenShift);
            CloseShiftCommand = new AsyncRelayCommand(CloseShiftAsync);
            PrintShiftReportCommand = new RelayCommand(PrintShiftReport);
            GenerateShiftReportPdfCommand = new AsyncRelayCommand(GenerateShiftReportPdfAsync);
            AddUserCommand = new RelayCommand(AddUser);
            SaveEditCommand = new RelayCommand(SaveEdit);
            ToggleActiveCommand = new RelayCommand(ToggleActive);
            ResetPasswordCommand = new RelayCommand(ResetPassword);
            SavePermissionsCommand = new RelayCommand(SavePermissions);
            ResetPermissionsToRoleDefaultCommand = new RelayCommand(ResetPermissionsToRoleDefault);

            LoadData();
        }


        public void LoadData()
        {
            // تحميل المستخدمين (مع الحفاظ على الموظف المحدد لو لسه موجود)
            int selectedId = SelectedUser?.Id ?? 0;
            UsersList.Clear();
            var users = _userRepository.GetAll();
            foreach (var u in users) UsersList.Add(u);
            SelectedUser = selectedId > 0 ? UsersList.FirstOrDefault(u => u.Id == selectedId) : null;

            // جلب الوردية المفتوحة للمستخدم المسجّل دخوله حالياً
            int currentUserId = _sessionService?.CurrentUser?.Id ?? 0;
            CurrentShift = currentUserId > 0 ? _shiftRepository.GetCurrentOpenShift(currentUserId) : null;
            _sessionService?.SetCurrentShift(CurrentShift);
            if (CurrentShift != null)
            {
                ActualClosingCashPounds = Money.FromPiastres(CurrentShift.ExpectedCashPiastres).ToPounds();
                CalculateShiftDifference();
            }

            StatusMessage = "تم تحديث بيانات الورديات والموظفين.";
        }

        private void CalculateShiftDifference()
        {
            if (CurrentShift == null) return;
            decimal expected = Money.FromPiastres(CurrentShift.ExpectedCashPiastres).ToPounds();
            ShiftDifferencePounds = ActualClosingCashPounds - expected;
        }

        public void OpenShift()
        {
            if (HasOpenShift)
            {
                StatusMessage = "توجد وردية مفتوحة بالفعل حالياً!";
                return;
            }

            var currentUser = _sessionService?.CurrentUser;
            if (currentUser == null)
            {
                StatusMessage = "سجّل الدخول الأول عشان تفتح وردية.";
                return;
            }

            long openPiastres = Money.FromPounds(NewOpeningBalancePounds).Piastres;
            int sId = _shiftRepository.OpenShift(currentUser.Id, currentUser.FullName, openPiastres);
            StatusMessage = $"تم فتح الوردية رقم {sId} برصيد افتتاحي {NewOpeningBalancePounds:N2} ج.م بنجاح.";
            LoadData();
        }

        /// <summary>النسخة المتزامنة (للاختبارات والاستدعاء المباشر): نفس الإغلاق + نسخ احتياطي متزامن</summary>
        public void CloseShift()
        {
            if (!CloseShiftCore()) return;

            // خطوة 13: نسخة احتياطية تلقائية بعد إغلاق الوردية (فشلها ما بيلغيش الإغلاق، بس بيتنبّه له المستخدم)
            string backupNote = RunAutoBackupAfterShift();
            if (backupNote.Length > 0) StatusMessage += " " + backupNote;
        }

        /// <summary>خطوة 28: الأمر المربوط بالزر. الإغلاق نفسه سريع (على خيط الواجهة)، والنسخ الاحتياطي على خيط خلفي فالشاشة ما تتجمدش.</summary>
        public async Task CloseShiftAsync()
        {
            if (IsBusy) return; // منع الضغط المزدوج أثناء النسخ
            IsBusy = true;
            try
            {
                if (!CloseShiftCore()) return;
                string backupNote = await RunAutoBackupAfterShiftAsync();
                if (backupNote.Length > 0) StatusMessage += " " + backupNote;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>إغلاق الوردية في القاعدة وتحديث الشاشة (بدون النسخ الاحتياطي). false لو مفيش وردية مفتوحة.</summary>
        private bool CloseShiftCore()
        {
            if (CurrentShift == null)
            {
                StatusMessage = "لا توجد وردية مفتوحة لإغلاقها!";
                return false;
            }

            long actualPiastres = Money.FromPounds(ActualClosingCashPounds).Piastres;
            _shiftRepository.CloseShift(CurrentShift.Id, actualPiastres, ClosingNotes);

            string diffText = ShiftDifferencePounds == 0
                ? "الصندوق مطابق تماماً بدون عجز أو زيادة."
                : ShiftDifferencePounds > 0 
                    ? $"يوجد زيادة بالصندوق قدرها +{ShiftDifferencePounds:N2} ج.م"
                    : $"يوجد عجز بالصندوق قدره {ShiftDifferencePounds:N2} ج.م";

            int closedShiftId = CurrentShift.Id;
            LastClosedShiftReport = _reportRepository?.GetShiftClosureReport(closedShiftId);
            LoadData();
            StatusMessage = $"تم إغلاق الوردية رقم {closedShiftId} بنجاح. {diffText}";
            return true;
        }

        /// <summary>طباعة إيصال تسليم الوردية وإغلاق الصندوق على الطابعة الحرارية (Z-Report)</summary>
        public void PrintShiftReport()
        {
            int sId = LastClosedShiftReport?.ShiftId ?? CurrentShift?.Id ?? 0;
            if (sId <= 0)
            {
                StatusMessage = "لا توجد وردية حالية أو مغلقة لطباعة تقريرها!";
                return;
            }

            var report = _reportRepository?.GetShiftClosureReport(sId);
            if (report == null)
            {
                StatusMessage = "تعذر استخراج بيانات تقرير الوردية.";
                return;
            }

            var settings = _settingsRepository?.GetSettings() ?? new PharmacySettings();
            if (Services.ThermalReceiptPrinter.PrintShiftReport(report, settings, out string err))
            {
                StatusMessage = $"تمت طباعة تقرير إغلاق الوردية #{sId} بنجاح.";
            }
            else
            {
                StatusMessage = $"تعذر طباعة تقرير الوردية: {err}";
            }
        }

        /// <summary>تصدير تقرير إغلاق الوردية وتسليم الصندوق إلى ملف PDF A4</summary>
        public async Task GenerateShiftReportPdfAsync()
        {
            int sId = LastClosedShiftReport?.ShiftId ?? CurrentShift?.Id ?? 0;
            if (sId <= 0)
            {
                StatusMessage = "لا توجد وردية حالية أو مغلقة لتصدير تقريرها!";
                return;
            }

            var report = _reportRepository?.GetShiftClosureReport(sId);
            if (report == null)
            {
                StatusMessage = "تعذر استخراج بيانات تقرير الوردية.";
                return;
            }

            if (_pdfReportService == null)
            {
                StatusMessage = "خدمة تصدير PDF غير متوفرة.";
                return;
            }

            try
            {
                var settings = _settingsRepository?.GetSettings() ?? new PharmacySettings();
                string path = await _pdfReportService.GenerateShiftClosurePdfAsync(report, settings);
                StatusMessage = $"تم تصدير تقرير الوردية إلى PDF بنجاح: {System.IO.Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StaffViewModel.GenerateShiftReportPdfAsync");
                StatusMessage = $"حدث خطأ أثناء تصدير PDF: {ex.Message}";
            }
        }

        /// <summary>طريقة تنبيه المستخدم لفشل النسخ التلقائي (الافتراضي نافذة تحذير؛ الاختبارات بتستبدلها)</summary>
        public Action<string> BackupWarningNotifier { get; set; } = msg =>
            System.Windows.MessageBox.Show(msg, "النسخ الاحتياطي التلقائي",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);

        /// <summary>يرجع نص للعرض للمستخدم (فاضي لو النسخ التلقائي معطّل أو الخدمة غير متاحة). لا يرمي استثناءات.</summary>
        public string RunAutoBackupAfterShift()
        {
            if (_backupService == null) return string.Empty;
            try
            {
                return DescribeAutoBackup(_backupService.RunAfterShiftBackup(_backupSettingsProvider()));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StaffViewModel.RunAutoBackupAfterShift");
                return "⚠ تعذر أخذ النسخة الاحتياطية التلقائية: " + ex.Message;
            }
        }

        /// <summary>خطوة 28: نفس <see cref="RunAutoBackupAfterShift"/> لكن النسخ نفسه على خيط خلفي. لا يرمي استثناءات.</summary>
        public async Task<string> RunAutoBackupAfterShiftAsync()
        {
            if (_backupService == null) return string.Empty;
            try
            {
                var settings = _backupSettingsProvider(); // على خيط الواجهة قبل الانتقال للخيط الخلفي
                var result = await _backupService.RunAfterShiftBackupAsync(settings);
                return DescribeAutoBackup(result);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StaffViewModel.RunAutoBackupAfterShiftAsync");
                return "⚠ تعذر أخذ النسخة الاحتياطية التلقائية: " + ex.Message;
            }
        }

        private string DescribeAutoBackup(Services.BackupRunResult? result)
        {
            if (result == null) return string.Empty;
            if (result.Success && !result.ExternalFailed) return "تم أخذ نسخة احتياطية تلقائية.";

            string warn = "⚠ " + result.Message;
            BackupWarningNotifier(warn);
            return warn;
        }

        public void AddUser()
        {
            string username = (NewUsername ?? string.Empty).Trim();
            string fullName = (NewFullName ?? string.Empty).Trim();
            string password = (NewPassword ?? string.Empty).Trim();

            // دعم تلقائي: إذا تم إدخال أحدهما وترك الآخر، يتم نسخه لتجنب خطأ تسجيل الدخول
            if (string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(fullName))
            {
                username = fullName;
            }
            else if (string.IsNullOrWhiteSpace(fullName) && !string.IsNullOrWhiteSpace(username))
            {
                fullName = username;
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                StatusMessage = "يرجى كتابة اسم المستخدم وكلمة المرور!";
                return;
            }

            try
            {
                // NewRole مقصورة في هذه الشاشة على Admin/Pharmacist/Cashier فقط؛
                // SaveStaffUser نفسها ترفض أي محاولة لإنشاء حساب بدور Owner كحماية إضافية.
                string cleanPassword = ArabicTextNormalizer.NormalizeDigits(password);
                _userRepository.SaveStaffUser(0, username, fullName, cleanPassword, NewRole, true);
                string savedName = fullName;
                NewUsername = string.Empty;
                NewFullName = string.Empty;
                NewPassword = string.Empty;
                LoadData();
                StatusMessage = $"تم حفظ حساب الموظف '{savedName}' بنجاح."; // بعد LoadData عشان رسالتها ما تمسحش النتيجة
            }
            catch (Exception ex)
            {
                StatusMessage = FriendlySaveError(ex);
            }
        }

        // ===== خطوة 32 =====

        /// <summary>رسالة مفهومة لفشل الحفظ (اسم مستخدم مكرر بيطلع كخطأ UNIQUE من SQLite)</summary>
        private static string FriendlySaveError(Exception ex)
        {
            if (ex.Message.IndexOf("UNIQUE", StringComparison.OrdinalIgnoreCase) >= 0)
                return "اسم المستخدم ده مستخدم قبل كده، اختار اسم تاني.";
            return $"تعذر حفظ الموظف: {ex.Message}";
        }

        /// <summary>
        /// فحص مشترك للأوامر التلاتة: لازم موظف محدد، مش Owner، والمستخدم الحالي Admin أو Owner.
        /// (لو مفيش جلسة أصلاً — اختبارات — بيتخطى فحص الدور زي AddUser.) بيرجّع false ويحط الرسالة لو مرفوض.
        /// </summary>
        private bool TryGetManageableTarget(out User target)
        {
            target = SelectedUser!;
            var actor = _sessionService?.CurrentUser;
            if (actor != null && actor.Role != UserRole.Admin && actor.Role != UserRole.Owner)
            {
                StatusMessage = "إدارة حسابات الموظفين للمدير أو صاحب الصيدلية فقط.";
                return false;
            }
            if (SelectedUser == null)
            {
                StatusMessage = "اختار موظف من القائمة الأول.";
                return false;
            }
            if (SelectedUser.Role == UserRole.Owner)
            {
                StatusMessage = "حساب صاحب الصيدلية لا يُدار من شاشة الموظفين.";
                return false;
            }
            return true;
        }

        private bool IsSelectedUserTheCurrentUser() =>
            SelectedUser != null && _sessionService?.CurrentUser != null && _sessionService.CurrentUser.Id == SelectedUser.Id;

        private void LoadPermissionsFromUser(User? user)
        {
            if (user == null)
            {
                ApplyRoleDefaultPermissions(UserRole.Pharmacist);
                IsCustomPermissions = false;
                return;
            }

            var eff = user.EffectivePermissions;
            _isCustomPermissions = eff.IsCustom;
            OnPropertyChanged(nameof(IsCustomPermissions));
            OnPropertyChanged(nameof(PermissionsModeText));

            PermCanAccessSales = eff.CanAccessSales;
            PermCanAccessInventory = eff.CanAccessInventory;
            PermCanAccessPurchases = eff.CanAccessPurchases;
            PermCanAccessCustomers = eff.CanAccessCustomers;
            PermCanAccessReports = eff.CanAccessReports;
            PermCanAccessFinance = eff.CanAccessFinance;
            PermCanAccessStaff = eff.CanAccessStaff;
            PermCanAccessSettings = eff.CanAccessSettings;
            PermCanAccessPriceListImport = eff.CanAccessPriceListImport;
            PermMaxDiscountPercent = eff.MaxDiscountPercent;
            PermCanPerformReturnsWithoutApproval = eff.CanPerformReturnsWithoutApproval;
            PermCanApproveOverrides = eff.CanApproveOverrides;
            PermCanManageStocktake = eff.CanManageStocktake;
            PermCanEditItemPrices = eff.CanEditItemPrices;
        }

        public void ApplyRoleDefaultPermissions(UserRole role)
        {
            var def = UserPermissions.CreateDefault(role);
            PermCanAccessSales = def.CanAccessSales;
            PermCanAccessInventory = def.CanAccessInventory;
            PermCanAccessPurchases = def.CanAccessPurchases;
            PermCanAccessCustomers = def.CanAccessCustomers;
            PermCanAccessReports = def.CanAccessReports;
            PermCanAccessFinance = def.CanAccessFinance;
            PermCanAccessStaff = def.CanAccessStaff;
            PermCanAccessSettings = def.CanAccessSettings;
            PermCanAccessPriceListImport = def.CanAccessPriceListImport;
            PermMaxDiscountPercent = def.MaxDiscountPercent;
            PermCanPerformReturnsWithoutApproval = def.CanPerformReturnsWithoutApproval;
            PermCanApproveOverrides = def.CanApproveOverrides;
            PermCanManageStocktake = def.CanManageStocktake;
            PermCanEditItemPrices = def.CanEditItemPrices;
        }

        public static string GetRoleArabic(UserRole role) => role switch
        {
            UserRole.Admin => "مدير",
            UserRole.Pharmacist => "صيدلي",
            UserRole.Cashier => "كاشير",
            UserRole.Owner => "صاحب الصيدلية",
            _ => "غير محدد"
        };

        private string? BuildCurrentCustomPermissionsConfig()
        {
            if (!IsCustomPermissions) return null;
            var p = new UserPermissions
            {
                IsCustom = true,
                CanAccessSales = PermCanAccessSales,
                CanAccessInventory = PermCanAccessInventory,
                CanAccessPurchases = PermCanAccessPurchases,
                CanAccessCustomers = PermCanAccessCustomers,
                CanAccessReports = PermCanAccessReports,
                CanAccessFinance = PermCanAccessFinance,
                CanAccessStaff = PermCanAccessStaff,
                CanAccessSettings = PermCanAccessSettings,
                CanAccessPriceListImport = PermCanAccessPriceListImport,
                MaxDiscountPercent = PermMaxDiscountPercent,
                CanPerformReturnsWithoutApproval = PermCanPerformReturnsWithoutApproval,
                CanApproveOverrides = PermCanApproveOverrides,
                CanManageStocktake = PermCanManageStocktake,
                CanEditItemPrices = PermCanEditItemPrices
            };
            return p.ToConfigString();
        }

        public void ResetPermissionsToRoleDefault()
        {
            IsCustomPermissions = false;
            ApplyRoleDefaultPermissions(EditRole);
            StatusMessage = $"تم ضبط الخيارات لتطابق صلاحيات دور ({GetRoleArabic(EditRole)}). اضغط 'حفظ التعديل' أو 'حفظ الصلاحيات' لتأكيد التغيير.";
        }

        public void SavePermissions()
        {
            if (!TryGetManageableTarget(out var target)) return;

            try
            {
                string? config = BuildCurrentCustomPermissionsConfig();
                _userRepository.SaveUserPermissions(target.Id, config);
                string name = target.FullName;
                LoadData();
                StatusMessage = IsCustomPermissions
                    ? $"تم حفظ الصلاحيات المخصصة للموظف '{name}' بنجاح."
                    : $"تمت استعادة الصلاحيات الافتراضية لدور ({GetRoleArabic(target.Role)}) للموظف '{name}'.";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StaffViewModel.SavePermissions");
                StatusMessage = $"تعذر حفظ الصلاحيات: {ex.Message}";
            }
        }

        /// <summary>حفظ تعديل الاسم / اسم المستخدم / الدور والصلاحيات للموظف المحدد (كلمة المرور بتفضل زي ما هي)</summary>
        public void SaveEdit()
        {
            if (!TryGetManageableTarget(out var target)) return;

            if (string.IsNullOrWhiteSpace(EditFullName) || string.IsNullOrWhiteSpace(EditUsername))
            {
                StatusMessage = "الاسم واسم المستخدم مطلوبين.";
                return;
            }
            if (EditRole == UserRole.Owner)
            {
                StatusMessage = "لا يمكن إسناد دور صاحب الصيدلية لأي موظف.";
                return;
            }
            if (EditRole != target.Role && IsSelectedUserTheCurrentUser())
            {
                StatusMessage = "لا يمكنك تغيير دور حسابك أنت بنفسك.";
                return;
            }

            try
            {
                int id = target.Id;
                string name = EditFullName.Trim();
                string? config = BuildCurrentCustomPermissionsConfig();
                _userRepository.SaveStaffUser(id, EditUsername, EditFullName, null, EditRole, target.IsActive, config);
                _userRepository.SaveUserPermissions(id, config);
                LoadData();
                StatusMessage = $"تم تعديل بيانات وصلاحيات الموظف '{name}'.";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StaffViewModel.SaveEdit");
                StatusMessage = FriendlySaveError(ex);
            }
        }


        /// <summary>تعطيل حساب الموظف المحدد (ما يقدرش يسجل دخول) أو تفعيله لو كان معطّل. بيحفظ بياناته كما هي.</summary>
        public void ToggleActive()
        {
            if (!TryGetManageableTarget(out var target)) return;

            if (target.IsActive && IsSelectedUserTheCurrentUser())
            {
                StatusMessage = "لا يمكنك تعطيل حسابك أنت بنفسك.";
                return;
            }

            try
            {
                bool newState = !target.IsActive;
                string name = target.FullName;
                // نحفظ القيم المخزّنة (مش حقول التعديل) عشان أي تعديل لسه ما اتحفظش ما يتسرّبش مع التعطيل
                _userRepository.SaveStaffUser(target.Id, target.Username, target.FullName, null, target.Role, newState);
                LoadData();
                StatusMessage = newState ? $"تم تفعيل حساب '{name}'." : $"تم تعطيل حساب '{name}'، ولن يستطيع تسجيل الدخول.";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StaffViewModel.ToggleActive");
                StatusMessage = FriendlySaveError(ex);
            }
        }

        /// <summary>إعادة تعيين كلمة مرور الموظف المحدد بكلمة جديدة يكتبها المدير (وبتفك أي قفل مؤقت عليه)</summary>
        public void ResetPassword()
        {
            if (!TryGetManageableTarget(out var target)) return;

            if (string.IsNullOrWhiteSpace(ResetPasswordText) || ResetPasswordText.Length < MinPasswordLength)
            {
                StatusMessage = $"كلمة المرور الجديدة يجب ألا تقل عن {MinPasswordLength} أحرف.";
                return;
            }

            try
            {
                string name = target.FullName;
                string cleanReset = ArabicTextNormalizer.NormalizeDigits(ResetPasswordText);
                _userRepository.SaveStaffUser(target.Id, target.Username, target.FullName, cleanReset, target.Role, target.IsActive);
                LoadData();
                StatusMessage = $"تم تعيين كلمة مرور جديدة للموظف '{name}'.";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StaffViewModel.ResetPassword");
                StatusMessage = $"تعذر إعادة تعيين كلمة المرور: {ex.Message}";
            }
        }
    }
}
