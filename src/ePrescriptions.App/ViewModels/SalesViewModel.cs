using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Linq;
using System.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using ePrescriptions.Domain.Helpers;

namespace ePrescriptions.App.ViewModels
{
    public class SalesViewModel : ViewModelBase
    {
        private readonly ItemRepository _itemRepository;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly CustomerRepository _customerRepository;
        private readonly SettingsRepository _settingsRepository;
        private readonly Services.IPdfReportService? _pdfReportService;
        private readonly Services.IUserSessionService? _sessionService;

        // المستخدم والوردية الحاليان من جلسة الدخول (القيم الافتراضية للتوافق عند غياب الجلسة)
        private int CurrentUserId => _sessionService?.CurrentUser?.Id ?? 0;
        private string CurrentUserName => _sessionService?.CurrentUser?.FullName ?? string.Empty;

        /// <summary>اسم المستخدم الحالي (المعروض في شريط المبيعات)</summary>
        public string CurrentUserDisplayName => CurrentUserName;

        /// <summary>رقم الوردية المفتوحة الحالية، أو 0 لو مفيش وردية مفتوحة (البيع مرفوض ساعتها)</summary>
        private int CurrentShiftId =>
            _sessionService?.CurrentShift != null && _sessionService.CurrentShift.Status == ShiftStatus.Open
                ? _sessionService.CurrentShift.Id
                : 0;
        public bool HasOpenShift => CurrentShiftId > 0;
        public bool NoOpenShift => !HasOpenShift;

        private readonly ShiftRepository? _shiftRepository;
        private readonly UserRepository? _userRepository;
        private readonly OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly StaffAuditLogRepository? _staffAuditLogRepository;

        private decimal _openingBalancePounds;
        /// <summary>رصيد الافتتاح (بالجنيه) عند فتح وردية من شاشة البيع</summary>
        public decimal OpeningBalancePounds
        {
            get => _openingBalancePounds;
            set => SetProperty(ref _openingBalancePounds, value < 0 ? 0 : value);
        }

        public IRelayCommand OpenShiftCommand { get; }

        /// <summary>
        /// تأكيد تجاوز حد الائتمان (للمدير فقط). الافتراضي نافذة سؤال، ويمكن استبداله في الاختبارات.
        /// </summary>
        public Func<string, bool> ConfirmCreditLimitOverride { get; set; } = message =>
            System.Windows.MessageBox.Show(
                message + "\n\nتوافق على تجاوز الحد وتسجيل الفاتورة؟",
                "تجاوز حد الائتمان",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No,
                System.Windows.MessageBoxOptions.RtlReading | System.Windows.MessageBoxOptions.RightAlign) == System.Windows.MessageBoxResult.Yes;

        // ===== صلاحيات على مستوى الإجراء: سقف خصم لكل دور + موافقة Admin/Owner =====
        private string _approverUsername = string.Empty;
        /// <summary>اسم مستخدم المدير/الصاحب الموافق (يتكتب لما الإجراء يحتاج موافقة)</summary>
        public string ApproverUsername
        {
            get => _approverUsername;
            set => SetProperty(ref _approverUsername, value ?? string.Empty);
        }

        private string _approverPassword = string.Empty;
        /// <summary>كلمة مرور الموافق. بتتمسح بعد كل محاولة ولا بتتخزّن.</summary>
        public string ApproverPassword
        {
            get => _approverPassword;
            set => SetProperty(ref _approverPassword, value ?? string.Empty);
        }

        private bool _needsManagerApproval;
        /// <summary>بيتفعّل لما الحفظ يترفض لأنه محتاج موافقة (بيظهر لوحة الموافقة في الواجهة)</summary>
        public bool NeedsManagerApproval
        {
            get => _needsManagerApproval;
            set => SetProperty(ref _needsManagerApproval, value);
        }

        private string _approvalReason = string.Empty;
        public string ApprovalReason
        {
            get => _approvalReason;
            set => SetProperty(ref _approvalReason, value);
        }

        private sealed class ManagerApproval
        {
            public User Approver { get; set; } = null!;
            public string Reason { get; set; } = string.Empty;
        }

        private static string Pct(decimal v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// يحدد لو الفاتورة الحالية محتاجة موافقة مدير (مرتجع من كاشير/صيدلي، أو خصم فوق سقف دور المستخدم).
        /// true = كمّل الحفظ (approval فاضي لو مفيش حاجة تحتاج موافقة)، false = الحفظ مرفوض والرسالة اتضبطت.
        /// لو مفيش جلسة (اختبارات/توافق) بيتخطى الفحص زي باقي فحوص الصلاحيات.
        /// </summary>
        private bool TryResolveManagerApproval(out ManagerApproval? approval)
        {
            approval = null;
            var user = _sessionService?.CurrentUser;
            if (user == null) return true;

            var reasons = new System.Collections.Generic.List<string>();
            if (IsReturnMode)
            {
                if (ActionPermissionPolicy.ReturnRequiresApproval(user))
                    reasons.Add("مرتجع مبيعات");
            }
            else
            {
                foreach (var row in CartItems)
                {
                    long gross = row.UnitPricePiastres * row.Quantity;
                    if (ActionPermissionPolicy.DiscountExceedsUserCap(user, gross, row.DiscountPiastres))
                    {
                        reasons.Add($"خصم {Pct(ActionPermissionPolicy.DiscountPercent(gross, row.DiscountPiastres))}٪ على «{row.NameAr}» (أقصى خصم مسموح لك {Pct(ActionPermissionPolicy.MaxDiscountPercent(user))}٪)");
                    }
                }
            }

            if (reasons.Count == 0)
            {
                NeedsManagerApproval = false;
                ApprovalReason = string.Empty;
                return true;
            }

            string reasonText = string.Join("؛ ", reasons);
            ApprovalReason = reasonText;
            NeedsManagerApproval = true;

            string typedUser = (ApproverUsername ?? string.Empty).Trim();
            string typedPass = ApproverPassword ?? string.Empty;
            ApproverPassword = string.Empty; // ما بنحتفظش بكلمة المرور بعد القراءة

            if (typedUser.Length == 0 || typedPass.Length == 0)
            {
                SetStatus($"محتاج موافقة مدير النظام أو صاحب الصيدلية: {reasonText}. اكتب اسم مستخدمه وكلمة مروره في لوحة الموافقة وأعد الحفظ.", false);
                PlayWarningSound();
                return false;
            }

            var approver = _userRepository?.Authenticate(typedUser, typedPass);
            if (approver == null)
            {
                SetStatus("بيانات الموافقة غير صحيحة أو الحساب غير نشط/مقفول.", false);
                PlayWarningSound();
                return false;
            }

            if (!ActionPermissionPolicy.CanApprove(approver))
            {
                SetStatus("الموافقة لازم تكون من مدير النظام أو من يملك صلاحية الاعتماد.", false);
                PlayWarningSound();
                return false;
            }


            approval = new ManagerApproval { Approver = approver, Reason = reasonText };
            return true;
        }

        /// <summary>تسجيل الموافقة في نظام التدقيق: الصاحب في سجله الخاص، والمدير في سجل الموظفين.</summary>
        private void LogManagerApproval(Invoice invoice, ManagerApproval approval)
        {
            try
            {
                var requester = _sessionService?.CurrentUser;
                var approver = approval.Approver;
                string kind = invoice.Type == InvoiceType.Return ? "ReturnApproval" : "DiscountApproval";
                string label = invoice.Type == InvoiceType.Return ? "موافقة على مرتجع" : "موافقة على خصم";
                string oldValue = $"طلب من: {requester?.FullName} ({requester?.Role})";
                string newValue = $"فاتورة {invoice.InvoiceNumber} — {approval.Reason} — إجمالي {(invoice.TotalPiastres / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} ج.م / وافق: {approver.FullName}";

                if (approver.Role == UserRole.Owner)
                    _ownerAuditLogRepository?.LogChange(approver.Id, kind, invoice.InvoiceNumber, invoice.InvoiceNumber, label, oldValue, newValue);
                else
                    _staffAuditLogRepository?.LogChange(approver.Id, approver.FullName, approver.Role.ToString(), kind, invoice.InvoiceNumber, invoice.InvoiceNumber, label, oldValue, newValue);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.LogManagerApproval");
            }
        }

        // ===== إلغاء فاتورة (Admin/Owner فقط) =====
        public bool CanCancelInvoice
        {
            get
            {
                var role = _sessionService?.CurrentUser?.Role;
                return role == UserRole.Admin || role == UserRole.Owner;
            }
        }

        private string _cancelInvoiceNumber = string.Empty;
        public string CancelInvoiceNumber
        {
            get => _cancelInvoiceNumber;
            set => SetProperty(ref _cancelInvoiceNumber, value);
        }

        private string _cancelReason = string.Empty;
        public string CancelReason
        {
            get => _cancelReason;
            set => SetProperty(ref _cancelReason, value);
        }

        private string _lastSavedInvoiceNumber = string.Empty;
        public string LastSavedInvoiceNumber
        {
            get => _lastSavedInvoiceNumber;
            set
            {
                if (SetProperty(ref _lastSavedInvoiceNumber, value))
                    OnPropertyChanged(nameof(CanUndoLastInvoice));
            }
        }
        public bool CanUndoLastInvoice => !string.IsNullOrEmpty(_lastSavedInvoiceNumber);

        public IRelayCommand CancelInvoiceCommand { get; }
        public IRelayCommand UndoLastSavedInvoiceCommand { get; }

        /// <summary>تأكيد إلغاء الفاتورة. الافتراضي نافذة سؤال، ويمكن استبداله في الاختبارات.</summary>
        public Func<string, bool> ConfirmCancelInvoice { get; set; } = message =>
            System.Windows.MessageBox.Show(
                message + "\n\nتأكيد إلغاء الفاتورة؟ لا يمكن التراجع.",
                "إلغاء فاتورة",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No,
                System.Windows.MessageBoxOptions.RtlReading | System.Windows.MessageBoxOptions.RightAlign) == System.Windows.MessageBoxResult.Yes;

        public void UndoLastSavedInvoice()
        {
            string targetNumber = !string.IsNullOrWhiteSpace(LastSavedInvoiceNumber) ? LastSavedInvoiceNumber : CancelInvoiceNumber;
            if (string.IsNullOrWhiteSpace(targetNumber))
            {
                SetStatus("اكتب رقم الفاتورة أو احفظ فاتورة أولاً للتراجع عنها.", false);
                return;
            }

            var target = _invoiceRepository.FindByNumber(targetNumber);
            if (target == null)
            {
                SetStatus($"لم يتم العثور على فاتورة بالرقم: {targetNumber}", false);
                return;
            }

            if (!ConfirmCancelInvoice($"فاتورة رقم {target.InvoiceNumber} بإجمالي {(target.TotalPiastres / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} ج.م"))
            {
                SetStatus("تم إلغاء طلب التراجع.", false);
                return;
            }

            var user = _sessionService?.CurrentUser;
            int userId = user?.Id ?? 1;
            string userName = user?.FullName ?? "المستخدم الحالي";
            string reason = string.IsNullOrWhiteSpace(CancelReason) ? "تراجع عن حفظ الفاتورة" : CancelReason.Trim();

            if (_invoiceRepository.CancelInvoice(target.Id, userId, userName, reason, out string err))
            {
                if (user != null) LogInvoiceCancellation(target, reason, user);
                SetStatus($"تم بنجاح التراجع عن الفاتورة {target.InvoiceNumber} وإلغاؤها واسترجاع المخزون.", true);
                PlayConfirmSound();
                LastSavedInvoiceNumber = string.Empty;
                CancelInvoiceNumber = string.Empty;
                CancelReason = string.Empty;
            }
            else
            {
                SetStatus($"تعذر التراجع عن الفاتورة: {err}", false);
                PlayWarningSound();
            }
        }

        public void CancelInvoice()
        {
            var user = _sessionService?.CurrentUser;
            if (user == null || !CanCancelInvoice)
            {
                SetStatus("إلغاء الفاتورة متاح لمدير النظام أو صاحب الصيدلية فقط.", false);
                return;
            }

            var target = _invoiceRepository.FindByNumber(CancelInvoiceNumber);
            if (target == null)
            {
                SetStatus($"مفيش فاتورة بالرقم {CancelInvoiceNumber.Trim()}.", false);
                return;
            }
            if (string.IsNullOrWhiteSpace(CancelReason))
            {
                SetStatus("اكتب سبب إلغاء الفاتورة.", false);
                return;
            }
            if (!ConfirmCancelInvoice($"فاتورة {target.InvoiceNumber} بإجمالي {(target.TotalPiastres / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} ج.م"))
            {
                SetStatus("تم التراجع عن إلغاء الفاتورة.", false);
                return;
            }

            if (_invoiceRepository.CancelInvoice(target.Id, user.Id, user.FullName, CancelReason, out string err))
            {
                LogInvoiceCancellation(target, CancelReason.Trim(), user);
                SetStatus($"تم إلغاء الفاتورة {target.InvoiceNumber} وإرجاع المخزون.", true);
                CancelInvoiceNumber = string.Empty;
                CancelReason = string.Empty;
            }
            else
            {
                SetStatus($"تعذر إلغاء الفاتورة: {err}", false);
            }
        }

        /// <summary>تسجيل الإلغاء في التدقيق: Owner في سجله الخاص، وAdmin في سجل الموظفين (من غير خلط)</summary>
        private void LogInvoiceCancellation(Invoice invoice, string reason, User user)
        {
            try
            {
                string total = (invoice.TotalPiastres / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                string oldValue = $"فاتورة {invoice.InvoiceNumber} مكتملة بإجمالي {total} ج.م";
                string newValue = $"ملغاة - السبب: {reason} - ألغاها: {user.FullName}";
                if (user.Role == UserRole.Owner)
                    _ownerAuditLogRepository?.LogChange(user.Id, "InvoiceCancel", invoice.Id.ToString(), invoice.InvoiceNumber, "إلغاء فاتورة", oldValue, newValue);
                else
                    _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "InvoiceCancel", invoice.Id.ToString(), invoice.InvoiceNumber, "إلغاء فاتورة", oldValue, newValue);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.LogInvoiceCancellation");
            }
        }

        // رأس الفاتورة
        private string _invoiceNumber = string.Empty;
        public string InvoiceNumber
        {
            get => _invoiceNumber;
            set => SetProperty(ref _invoiceNumber, value);
        }

        private InvoiceType _currentInvoiceType = InvoiceType.Sales;
        public InvoiceType CurrentInvoiceType
        {
            get => _currentInvoiceType;
            set
            {
                if (SetProperty(ref _currentInvoiceType, value))
                {
                    OnPropertyChanged(nameof(IsReturnMode));
                    OnPropertyChanged(nameof(InvoiceTypeTitle));
                }
            }
        }

        public bool IsReturnMode => CurrentInvoiceType == InvoiceType.Return;

        // ===== مرتجع المبيعات: مرتبط بفاتورة بيع أصلية =====
        private ReturnableInvoice? _returnSource;

        private string _originalInvoiceNumber = string.Empty;
        public string OriginalInvoiceNumber
        {
            get => _originalInvoiceNumber;
            set => SetProperty(ref _originalInvoiceNumber, value);
        }

        public IRelayCommand LoadOriginalInvoiceCommand { get; }
        public string InvoiceTypeTitle
        {
            get
            {
                if (IsViewingPastInvoice) return $"عرض فاتورة سابقة ({InvoiceNumber})";
                return IsReturnMode ? "فاتورة مرتجع مبيعات" : "فاتورة مبيعات جديدة";
            }
        }

        // ===== خطوة 35: بدائل الصنف بالمادة الفعالة =====
        public ObservableCollection<ItemAlternativeRow> AlternativeItems { get; } = new();

        private bool _isAlternativesVisible;
        public bool IsAlternativesVisible
        {
            get => _isAlternativesVisible;
            set => SetProperty(ref _isAlternativesVisible, value);
        }

        private string _outOfStockItemName = string.Empty;
        public string OutOfStockItemName
        {
            get => _outOfStockItemName;
            set => SetProperty(ref _outOfStockItemName, value);
        }

        private string _outOfStockActiveIngredient = string.Empty;
        public string OutOfStockActiveIngredient
        {
            get => _outOfStockActiveIngredient;
            set => SetProperty(ref _outOfStockActiveIngredient, value);
        }

        public IRelayCommand<ItemAlternativeRow> AddAlternativeToCartCommand { get; }
        public IRelayCommand CloseAlternativesCommand { get; }

        // بيانات العميل والوصفة
        private string _customerName = "عميل نقدي";
        public string CustomerName
        {
            get => _customerName;
            set => SetProperty(ref _customerName, value);
        }

        private string _customerPhone = string.Empty;
        public string CustomerPhone
        {
            get => _customerPhone;
            set => SetProperty(ref _customerPhone, value);
        }

        private PriceCategory _selectedPriceCategory = PriceCategory.Retail;
        public PriceCategory SelectedPriceCategory
        {
            get => _selectedPriceCategory;
            set
            {
                if (SetProperty(ref _selectedPriceCategory, value))
                {
                    // تحديث الأسعار لجميع البنود الحالية
                    foreach (var item in CartItems)
                    {
                        item.UpdatePriceBasedOnUnit();
                    }
                    RecalculateTotals();
                }
            }
        }

        private PaymentMethod _selectedPaymentMethod = PaymentMethod.Cash;
        public PaymentMethod SelectedPaymentMethod
        {
            get => _selectedPaymentMethod;
            set
            {
                if (SetProperty(ref _selectedPaymentMethod, value))
                {
                    OnPropertyChanged(nameof(IsMixedPayment));
                    if (value == PaymentMethod.Mixed)
                    {
                        ResetSplitFields();
                    }
                    else if (value == PaymentMethod.Cash || value == PaymentMethod.Network)
                    {
                        PaidRiyals = TotalNetRiyals;
                    }
                    else if (value == PaymentMethod.OnAccount)
                    {
                        PaidRiyals = 0;
                    }
                    RecalculateTotals();
                }
            }
        }

        // ===== خطوة 36: الدفع المقسّم =====
        // النقدي هو "الباقي": المستخدم يدخل مبالغ الشبكة/الآجل/التأمين والباقي يتحسب نقدي تلقائياً.
        public bool IsMixedPayment => SelectedPaymentMethod == PaymentMethod.Mixed;

        private decimal _splitNetworkRiyals;
        public decimal SplitNetworkRiyals
        {
            get => _splitNetworkRiyals;
            set { if (SetProperty(ref _splitNetworkRiyals, value)) RecalculateTotals(); }
        }

        private decimal _splitOnAccountRiyals;
        public decimal SplitOnAccountRiyals
        {
            get => _splitOnAccountRiyals;
            set { if (SetProperty(ref _splitOnAccountRiyals, value)) RecalculateTotals(); }
        }

        private decimal _splitInsuranceRiyals;
        public decimal SplitInsuranceRiyals
        {
            get => _splitInsuranceRiyals;
            set { if (SetProperty(ref _splitInsuranceRiyals, value)) RecalculateTotals(); }
        }

        /// <summary>الجزء النقدي = إجمالي الفاتورة ناقص باقي الأجزاء (لا يقل عن صفر)</summary>
        public decimal SplitCashRiyals => Money.FromPiastres(SplitCashPiastres).ToPounds();

        private long SplitNetworkPiastres => Math.Max(0, Money.FromPounds(SplitNetworkRiyals).Piastres);
        private long SplitOnAccountPiastres => Math.Max(0, Money.FromPounds(SplitOnAccountRiyals).Piastres);
        private long SplitInsurancePiastres => Math.Max(0, Money.FromPounds(SplitInsuranceRiyals).Piastres);
        private long SplitCashPiastres => Math.Max(0, NetTotalPiastres - SplitNetworkPiastres - SplitOnAccountPiastres - SplitInsurancePiastres);

        private void ResetSplitFields()
        {
            _splitNetworkRiyals = 0; _splitOnAccountRiyals = 0; _splitInsuranceRiyals = 0;
            OnPropertyChanged(nameof(SplitNetworkRiyals));
            OnPropertyChanged(nameof(SplitOnAccountRiyals));
            OnPropertyChanged(nameof(SplitInsuranceRiyals));
        }

        /// <summary>أجزاء الدفع الحالية (فاضية لو الدفع مش مقسّم). الأجزاء الصفرية بتتشال.</summary>
        public List<InvoicePayment> BuildPaymentParts()
        {
            var parts = new List<InvoicePayment>();
            if (!IsMixedPayment) return parts;
            void Add(PaymentMethod m, long amt) { if (amt > 0) parts.Add(new InvoicePayment { Method = m, AmountPiastres = amt }); }
            Add(PaymentMethod.Cash, SplitCashPiastres);
            Add(PaymentMethod.Network, SplitNetworkPiastres);
            Add(PaymentMethod.OnAccount, SplitOnAccountPiastres);
            Add(PaymentMethod.Insurance, SplitInsurancePiastres);
            return parts;
        }

        /// <summary>تحقق الدفع المقسّم قبل الحفظ (رسالة عربية لو فيه مشكلة)</summary>
        private bool TryValidateSplit(out string error)
        {
            error = string.Empty;
            if (!IsMixedPayment) return true;
            if (IsReturnMode) { error = "المرتجع بيتعمل بطريقة دفع واحدة؛ اختر نقدي أو شبكة."; return false; }
            if (SplitNetworkRiyals < 0 || SplitOnAccountRiyals < 0 || SplitInsuranceRiyals < 0)
            { error = "مبالغ الدفع المقسّم ما تكونش سالبة."; return false; }
            long others = SplitNetworkPiastres + SplitOnAccountPiastres + SplitInsurancePiastres;
            if (others > NetTotalPiastres)
            { error = "مجموع الشبكة والآجل والتأمين أكبر من إجمالي الفاتورة."; return false; }
            if (BuildPaymentParts().Count < 2)
            { error = "الدفع المقسّم لازم يكون من طريقتين على الأقل (ادخل مبلغ في الشبكة أو الآجل أو التأمين)."; return false; }
            return true;
        }

        private string _prescriptionNumber = string.Empty;
        public string PrescriptionNumber
        {
            get => _prescriptionNumber;
            set => SetProperty(ref _prescriptionNumber, value);
        }

        private string _doctorName = string.Empty;
        public string DoctorName
        {
            get => _doctorName;
            set => SetProperty(ref _doctorName, value);
        }

        // البحث اللحظي والباركود
        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    if (TryAutoBarcode(value))
                    {
                        return;
                    }
                    PerformSearch(value);
                }
            }
        }

        private bool TryAutoBarcode(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || IsReturnMode) return false;
            string code = text.Trim();

            // 1) كود ملصق تشغيلة يبدأ بـ EPRX-
            if (BatchLabelCode.TryDecode(code, out int batchId))
            {
                var found = _itemRepository.GetBatchWithItem(batchId);
                if (found != null)
                {
                    AddItemFromBatchLabel(found.Value.Item, found.Value.Batch);
                    _searchQuery = string.Empty;
                    OnPropertyChanged(nameof(SearchQuery));
                    SearchResults.Clear();
                    return true;
                }
            }

            // 2) مسح الباركود بجهاز الباركود تلقائياً بدون الحاجة للضغط على Enter:
            // أجهزة الباركود تدخل أرقام الباركود (EAN-8, UPC-12, EAN-13, ITF-14 أو باركود الصنف)
            if (code.Length >= 5)
            {
                var item = _itemRepository.GetByBarcodeOrCode(code);
                if (item != null && (string.Equals(item.Barcode?.Trim(), code, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(item.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
                {
                    AddItem(item);
                    _searchQuery = string.Empty;
                    OnPropertyChanged(nameof(SearchQuery));
                    SearchResults.Clear();
                    return true;
                }
            }

            return false;
        }

        public ObservableCollection<Item> SearchResults { get; } = new();
        public ObservableCollection<Item> TopSellingItems { get; } = new();
        public ObservableCollection<SalesItemRow> CartItems { get; } = new();
        public ObservableCollection<Customer> CustomersList { get; } = new();

        // المجاميع بالقروش
        private long _subtotalPiastres;
        public long SubtotalPiastres
        {
            get => _subtotalPiastres;
            set => SetProperty(ref _subtotalPiastres, value);
        }

        private long _discountPiastres;
        public long DiscountPiastres
        {
            get => _discountPiastres;
            set => SetProperty(ref _discountPiastres, value);
        }

        private long _vatPiastres;
        public long VatPiastres
        {
            get => _vatPiastres;
            set => SetProperty(ref _vatPiastres, value);
        }

        private long _netTotalPiastres;
        public long NetTotalPiastres
        {
            get => _netTotalPiastres;
            set => SetProperty(ref _netTotalPiastres, value);
        }

        private decimal _paidRiyals;
        public decimal PaidRiyals
        {
            get => _paidRiyals;
            set
            {
                if (SetProperty(ref _paidRiyals, value))
                {
                    RecalculateTotals();
                }
            }
        }

        private long _remainingPiastres;
        public long RemainingPiastres
        {
            get => _remainingPiastres;
            set => SetProperty(ref _remainingPiastres, value);
        }

        // خواص العرض بالجنيه المصري (مع الحفاظ على الأسماء للتوافق الكامل)
        public decimal SubtotalRiyals => Money.FromPiastres(SubtotalPiastres).ToPounds();
        public decimal DiscountRiyals => Money.FromPiastres(DiscountPiastres).ToPounds();
        public decimal VatRiyals => Money.FromPiastres(VatPiastres).ToPounds();
        public decimal TotalNetRiyals => Money.FromPiastres(NetTotalPiastres).ToPounds();
        public decimal RemainingRiyals => Money.FromPiastres(RemainingPiastres).ToPounds();

        // مرادفات بالجنيه
        public decimal SubtotalPounds => SubtotalRiyals;
        public decimal VatPounds => VatRiyals;
        public decimal TotalNetPounds => TotalNetRiyals;
        public decimal RemainingPounds => RemainingRiyals;
        public decimal PaidPounds => PaidRiyals;

        // إشعارات الحالة
        private string _statusMessage = "جاهز لاستقبال عمليات البيع";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isStatusSuccess = true;
        public bool IsStatusSuccess
        {
            get => _isStatusSuccess;
            set => SetProperty(ref _isStatusSuccess, value);
        }

        // الأوامر (F1..F10)
        public IRelayCommand NewInvoiceCommand { get; }
        public IRelayCommand SaveInvoiceCommand { get; }
        public IRelayCommand PrintInvoiceCommand { get; }
        public IRelayCommand PrintA4InvoicePdfCommand { get; }
        public IRelayCommand SuspendInvoiceCommand { get; }
        public IRelayCommand ToggleReturnModeCommand { get; }
        public IRelayCommand OpenCashDrawerCommand { get; }
        public IRelayCommand<SalesItemRow> RemoveItemCommand { get; }
        public IRelayCommand RemoveSelectedItemCommand { get; }
        public IRelayCommand<Item> AddItemToCartCommand { get; }
        public IRelayCommand BarcodeEnterCommand { get; }
        public IRelayCommand OpenItemSearchCommand { get; }
        public IRelayCommand CloseSearchPopupCommand { get; }
        public IRelayCommand<Item> SelectSearchResultCommand { get; }
        public IRelayCommand PreviousInvoiceCommand { get; }
        public IRelayCommand NextInvoiceCommand { get; }
        public IRelayCommand OpenInvoiceHistoryCommand { get; }
        public IRelayCommand CloseInvoiceHistoryCommand { get; }
        public IRelayCommand<Invoice> LoadSpecificInvoiceCommand { get; }

        private SalesItemRow? _selectedCartItem;
        public SalesItemRow? SelectedCartItem
        {
            get => _selectedCartItem;
            set => SetProperty(ref _selectedCartItem, value);
        }

        private bool _isSearchPopupOpen;
        public bool IsSearchPopupOpen
        {
            get => _isSearchPopupOpen;
            set => SetProperty(ref _isSearchPopupOpen, value);
        }

        private bool _isInvoiceHistoryOpen;
        public bool IsInvoiceHistoryOpen
        {
            get => _isInvoiceHistoryOpen;
            set => SetProperty(ref _isInvoiceHistoryOpen, value);
        }

        private bool _isViewingHistoricalInvoice;
        public bool IsViewingHistoricalInvoice
        {
            get => _isViewingHistoricalInvoice;
            set => SetProperty(ref _isViewingHistoricalInvoice, value);
        }

        private string _historicalInvoiceStatusText = string.Empty;
        public string HistoricalInvoiceStatusText
        {
            get => _historicalInvoiceStatusText;
            set => SetProperty(ref _historicalInvoiceStatusText, value);
        }

        public ObservableCollection<Invoice> RecentInvoices { get; } = new();

        public SalesViewModel(
            ItemRepository itemRepository,
            InvoiceRepository invoiceRepository,
            CustomerRepository customerRepository,
            SettingsRepository settingsRepository,
            Services.IPdfReportService? pdfReportService = null,
            Services.IUserSessionService? sessionService = null,
            ShiftRepository? shiftRepository = null,
            OwnerAuditLogRepository? ownerAuditLogRepository = null,
            StaffAuditLogRepository? staffAuditLogRepository = null,
            UserRepository? userRepository = null)
        {
            _userRepository = userRepository;
            _shiftRepository = shiftRepository;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _sessionService = sessionService;
            if (_sessionService != null) _sessionService.SessionChanged += () => OnPropertyChanged(nameof(CurrentUserDisplayName));
            Title = "نقطة البيع - المبيعات";
            _itemRepository = itemRepository;
            _invoiceRepository = invoiceRepository;
            _customerRepository = customerRepository;
            _settingsRepository = settingsRepository;
            _pdfReportService = pdfReportService;

            NewInvoiceCommand = new RelayCommand(StartNewInvoice);
            SaveInvoiceCommand = new RelayCommand(SaveCurrentInvoice);
            PrintInvoiceCommand = new RelayCommand(PrintCurrentInvoice);
            PrintA4InvoicePdfCommand = new AsyncRelayCommand(PrintA4InvoicePdfAsync);
            SuspendInvoiceCommand = new RelayCommand(SuspendCurrentInvoice);
            ToggleReturnModeCommand = new RelayCommand(ToggleReturnMode);
            OpenCashDrawerCommand = new RelayCommand(OpenDrawer);
            RemoveItemCommand = new RelayCommand<SalesItemRow>(RemoveItem);
            RemoveSelectedItemCommand = new RelayCommand(RemoveSelectedItem);
            AddItemToCartCommand = new RelayCommand<Item>(AddItem);
            BarcodeEnterCommand = new RelayCommand(HandleBarcodeEnter);
            LoadOriginalInvoiceCommand = new RelayCommand(LoadOriginalInvoice);
            CancelInvoiceCommand = new RelayCommand(CancelInvoice);
            OpenShiftCommand = new RelayCommand(OpenShiftFromSales);
            AddAlternativeToCartCommand = new RelayCommand<ItemAlternativeRow>(OnAddAlternativeToCart);
            CloseAlternativesCommand = new RelayCommand(() => IsAlternativesVisible = false);
            UndoLastSavedInvoiceCommand = new RelayCommand(UndoLastSavedInvoice);

            OpenItemSearchCommand = new RelayCommand(OpenItemSearch);
            CloseSearchPopupCommand = new RelayCommand(() => IsSearchPopupOpen = false);
            SelectSearchResultCommand = new RelayCommand<Item>(SelectSearchResult);
            PreviousInvoiceCommand = new RelayCommand(LoadPreviousInvoice);
            NextInvoiceCommand = new RelayCommand(LoadNextInvoice);
            OpenInvoiceHistoryCommand = new RelayCommand(OpenInvoiceHistory);
            CloseInvoiceHistoryCommand = new RelayCommand(() => IsInvoiceHistoryOpen = false);
            LoadSpecificInvoiceCommand = new RelayCommand<Invoice>(inv =>
            {
                if (inv != null)
                {
                    LoadInvoice(inv);
                    IsInvoiceHistoryOpen = false;
                }
            });

            LoadInitialData();
            StartNewInvoice();
        }

        private void LoadInitialData()
        {
            try
            {
                var topItems = _itemRepository.GetTopSellingItems(8);
                TopSellingItems.Clear();
                foreach (var itm in topItems) TopSellingItems.Add(itm);

                var customers = _customerRepository.GetAll();
                CustomersList.Clear();
                foreach (var c in customers) CustomersList.Add(c);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.LoadInitialData");
            }
        }

        public void StartNewInvoice()
        {
            InvoiceNumber = _invoiceRepository.GenerateNextInvoiceNumber(CurrentInvoiceType);
            _returnSource = null;
            OriginalInvoiceNumber = string.Empty;
            CustomerName = "عميل نقدي";
            CustomerPhone = string.Empty;
            PrescriptionNumber = string.Empty;
            DoctorName = string.Empty;
            SelectedPaymentMethod = PaymentMethod.Cash;
            ResetSplitFields();
            SelectedPriceCategory = PriceCategory.Retail;
            CartItems.Clear();
            SearchQuery = string.Empty;
            SearchResults.Clear();
            IsAlternativesVisible = false;
            AlternativeItems.Clear();
            OutOfStockItemName = string.Empty;
            OutOfStockActiveIngredient = string.Empty;
            PaidRiyals = 0;
            IsViewingHistoricalInvoice = false;
            HistoricalInvoiceStatusText = string.Empty;
            IsSearchPopupOpen = false;
            IsInvoiceHistoryOpen = false;
            SelectedCartItem = null;
            RecalculateTotals();
            SetStatus("تم تجهيز فاتورة جديدة جاهزة للبيع", true);
        }

        private void PerformSearch(string query)
        {
            SearchResults.Clear();
            if (string.IsNullOrWhiteSpace(query))
            {
                IsSearchPopupOpen = false;
                return;
            }

            try
            {
                var list = _itemRepository.Search(query.Trim(), 25);
                foreach (var itm in list) SearchResults.Add(itm);
                IsSearchPopupOpen = SearchResults.Count > 0;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.PerformSearch");
            }
        }

        public void OpenItemSearch()
        {
            SearchResults.Clear();
            string q = (SearchQuery ?? string.Empty).Trim();
            try
            {
                var list = string.IsNullOrEmpty(q)
                    ? _itemRepository.GetAllItems().Take(30).ToList()
                    : _itemRepository.Search(q, 30);
                if (list.Count == 0 && string.IsNullOrEmpty(q))
                {
                    list = _itemRepository.GetTopSellingItems(25);
                }
                foreach (var itm in list) SearchResults.Add(itm);
                IsSearchPopupOpen = SearchResults.Count > 0;
                if (SearchResults.Count == 0)
                {
                    SetStatus("لم يتم العثور على أي صنف مطابق للبحث", false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.OpenItemSearch");
            }
        }

        public void SelectSearchResult(Item? item)
        {
            if (item == null) return;
            AddItem(item);
            SearchQuery = string.Empty;
            SearchResults.Clear();
            IsSearchPopupOpen = false;
        }

        public void RemoveSelectedItem()
        {
            if (SelectedCartItem != null)
            {
                RemoveItem(SelectedCartItem);
                SelectedCartItem = null;
            }
            else if (CartItems.Count > 0)
            {
                RemoveItem(CartItems[CartItems.Count - 1]);
            }
        }

        public void LoadInvoice(Invoice invoice)
        {
            if (invoice == null) return;
            Invoice full = (invoice.Items != null && invoice.Items.Count > 0)
                ? invoice
                : (_invoiceRepository.FindByNumber(invoice.InvoiceNumber) ?? invoice);

            InvoiceNumber = full.InvoiceNumber;
            CustomerName = string.IsNullOrWhiteSpace(full.CustomerName) ? "عميل نقدي" : full.CustomerName;
            CustomerPhone = full.CustomerPhone ?? string.Empty;
            CurrentInvoiceType = full.Type;
            SelectedPaymentMethod = full.PaymentMethod;

            CartItems.Clear();
            foreach (var line in full.Items)
            {
                var baseItem = _itemRepository.GetById(line.ItemId);
                CartItems.Add(SalesItemRow.FromHistoricalItem(line, baseItem));
            }

            PaidRiyals = Money.FromPiastres(full.PaidPiastres).ToPounds();
            RecalculateTotals();
            IsViewingHistoricalInvoice = true;
            string statusStr = full.Status == InvoiceStatus.Cancelled ? "ملغاة" : (full.Type == InvoiceType.Return ? "مرتجع" : "مكتملة");
            HistoricalInvoiceStatusText = $"فاتورة سابقة رقم [{full.InvoiceNumber}] | التاريخ: {DbDate.Display(full.Date)} | الحالة: {statusStr} | العميل: {CustomerName} | الإجمالي: {Money.FromPiastres(full.TotalPiastres).ToPounds():N2} ج.م";
            SetStatus($"تم استعراض الفاتورة السابقة رقم {full.InvoiceNumber}", true);
        }

        public void LoadPreviousInvoice()
        {
            string? prevNum = _invoiceRepository.GetPreviousInvoiceNumber(InvoiceNumber);
            if (!string.IsNullOrEmpty(prevNum))
            {
                var inv = _invoiceRepository.FindByNumber(prevNum);
                if (inv != null)
                {
                    LoadInvoice(inv);
                    return;
                }
            }
            SetStatus("لا توجد فواتير سابقة قبل هذه الفاتورة", false);
        }

        public void LoadNextInvoice()
        {
            string? nextNum = _invoiceRepository.GetNextInvoiceNumber(InvoiceNumber);
            if (!string.IsNullOrEmpty(nextNum))
            {
                var inv = _invoiceRepository.FindByNumber(nextNum);
                if (inv != null)
                {
                    LoadInvoice(inv);
                    return;
                }
            }
            SetStatus("أنت في أحدث فاتورة مسجلة بالفعل", false);
        }

        public void OpenInvoiceHistory()
        {
            RecentInvoices.Clear();
            try
            {
                var list = _invoiceRepository.GetRecentInvoices(40);
                foreach (var inv in list) RecentInvoices.Add(inv);
                IsInvoiceHistoryOpen = true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.OpenInvoiceHistory");
            }
        }

        public void HandleBarcodeEnter()
        {
            string code = SearchQuery.Trim();
            if (string.IsNullOrEmpty(code)) return;

            if (IsReturnMode)
            {
                SetStatus("المرتجع بيتعمل من فاتورة بيع أصلية: اكتب رقم الفاتورة واضغط تحميل.", false);
                return;
            }

            // ملصق تشغيلة مطبوع على العلبة: يحدد الصنف والتشغيلة (ومنها تاريخ الصلاحية)
            if (BatchLabelCode.TryDecode(code, out int batchId))
            {
                var found = _itemRepository.GetBatchWithItem(batchId);
                if (found != null)
                {
                    AddItemFromBatchLabel(found.Value.Item, found.Value.Batch);
                    SearchQuery = string.Empty;
                    SearchResults.Clear();
                    return;
                }
            }

            var item = _itemRepository.GetByBarcodeOrCode(code);
            if (item != null)
            {
                AddItem(item);
                SearchQuery = string.Empty;
                SearchResults.Clear();
            }
            else
            {
                SetStatus($"لم يتم العثور على صنف بالباركود: {code}", false);
            }
        }

        private int GetDefaultVat()
        {
            try
            {
                return _settingsRepository.GetSettings().DefaultVatPercent;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "GetDefaultVat");
                return 14;
            }
        }

        private void OnAddAlternativeToCart(ItemAlternativeRow? alt)
        {
            if (alt?.Item == null) return;

            // إذا كان الصنف النافد الذي أظهر البدائل موجوداً بالسلة، يتم استبداله بالبديل المختار
            var outOfStockRow = CartItems.FirstOrDefault(x => x.NameAr == OutOfStockItemName && x.TotalStockSmallestUnits <= 0);
            if (outOfStockRow != null)
            {
                CartItems.Remove(outOfStockRow);
            }

            AddItem(alt.Item);
            IsAlternativesVisible = false;
        }

        /// <summary>
        /// خطوة 35: فحص وعرض بدائل الصنف بالمادة الفعالة إذا كان رصيده نافداً بالمخزون
        /// </summary>
        public bool LoadAlternativesForOutOfStockItem(Item item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ActiveIngredient))
            {
                IsAlternativesVisible = false;
                AlternativeItems.Clear();
                return false;
            }

            var alts = _itemRepository.GetAlternativesByActiveIngredient(item.ActiveIngredient, item.Id);
            AlternativeItems.Clear();
            foreach (var alt in alts)
            {
                AlternativeItems.Add(new ItemAlternativeRow(alt));
            }

            OutOfStockItemName = item.NameAr;
            OutOfStockActiveIngredient = item.ActiveIngredient;
            IsAlternativesVisible = AlternativeItems.Count > 0;

            if (IsAlternativesVisible)
            {
                SetStatus($"⚠ الصنف {item.NameAr} نافد من المخزون! يتوفر {AlternativeItems.Count} بديل بنفس المادة الفعالة ({item.ActiveIngredient})", false);
            }
            return IsAlternativesVisible;
        }

        public void AddItem(Item? item)
        {
            if (item == null) return;

            if (IsReturnMode)
            {
                SetStatus("المرتجع بيتعمل من فاتورة بيع أصلية: اكتب رقم الفاتورة واضغط تحميل.", false);
                return;
            }

            // خطوة 35: لو الصنف نفد من المخزون، اعرض بدائله بالمادة الفعالة
            if (item.TotalStockSmallestUnits <= 0)
            {
                LoadAlternativesForOutOfStockItem(item);
            }
            else
            {
                IsAlternativesVisible = false;
            }

            int defaultVat = GetDefaultVat();

            // التحقق مما إذا كان الصنف مضافاً مسبقاً بنفس الوحدة (بنود التشغيلة المحددة بالملصق لا تُدمج هنا)
            var existing = CartItems.FirstOrDefault(x => x.ItemId == item.Id && x.BatchId == null && x.SelectedUnitLevel == UnitLevel.Box);
            if (existing != null)
            {
                existing.Quantity++;
                SetStatus($"تمت إضافة: {item.NameAr}", true);
            }
            else
            {
                var row = new SalesItemRow(item, SelectedPriceCategory, defaultVat);
                string warning = BuildStockWarning(item, row);
                row.WarningText = warning;
                row.PropertyChanged += (s, e) => RecalculateTotals();
                CartItems.Add(row);

                if (item.IsPrescriptionRequired)
                    warning = ("يتطلب وصفة طبية (رقم الوصفة واسم الطبيب إلزاميان)" + (warning.Length > 0 ? " | " + warning : string.Empty));
                row.WarningText = warning;

                if (warning.Length > 0)
                {
                    SetStatus($"{item.NameAr}: {warning}", false);
                    PlayWarningSound();
                }
                else
                {
                    SetStatus($"تمت إضافة: {item.NameAr}", true);
                }
            }

            RecalculateTotals();
        }

        /// <summary>
        /// بيع بقراءة ملصق التشغيلة: التشغيلة المنتهية ممنوعة، وأي تشغيلة أقدم على الرف تظهر كتنبيه فوري.
        /// </summary>
        public void AddItemFromBatchLabel(Item item, ItemBatch batch)
        {
            if (ExpiryPolicy.IsExpired(batch.ExpiryDate))
            {
                SetStatus($"⛔ {item.NameAr}: التشغيلة {batch.BatchNumber} منتهية ({DbDate.Date(batch.ExpiryDate)}) — مينفعش تتباع، اسحبها من الرف.", false);
                PlayWarningSound();
                return;
            }

            var warnings = new System.Collections.Generic.List<string>();

            if (batch.RemainingSmallestUnits <= 0)
                warnings.Add("رصيد التشغيلة صفر بالنظام (راجع الجرد)");

            var older = _itemRepository.GetOlderSellableBatches(item.Id, batch.ExpiryDate, batch.Id);
            if (older.Count > 0)
            {
                var o = older[0];
                string qty = UnitBreakdownHelper.FormatStockDetailed(o.RemainingSmallestUnits, item.StripsPerBox, item.PiecesPerStrip);
                warnings.Add($"في تشغيلة أقدم على الرف: {o.BatchNumber} بتنتهي {DbDate.Date(o.ExpiryDate)} (الكمية {qty}) — الأفضل تبيعها الأول");
            }

            if (ExpiryPolicy.IsNearExpiry(batch.ExpiryDate))
                warnings.Add($"قريبة الانتهاء ({DbDate.Date(batch.ExpiryDate)})");

            string warning = string.Join(" | ", warnings);

            var existing = CartItems.FirstOrDefault(x => x.ItemId == item.Id && x.BatchId == batch.Id && x.SelectedUnitLevel == UnitLevel.Box);
            if (existing != null)
            {
                existing.Quantity++;
            }
            else
            {
                var row = new SalesItemRow(item, SelectedPriceCategory, GetDefaultVat())
                {
                    BatchId = batch.Id,
                    BatchNumber = batch.BatchNumber,
                    ExpiryDate = batch.ExpiryDate
                };
                row.WarningText = warning;
                row.PropertyChanged += (s, e) => RecalculateTotals();
                CartItems.Add(row);
            }

            RecalculateTotals();

            if (warning.Length > 0)
            {
                SetStatus($"{item.NameAr} (صلاحية {DbDate.Date(batch.ExpiryDate)}): {warning}", false);
                PlayWarningSound();
            }
            else
            {
                SetStatus($"تمت إضافة: {item.NameAr} — تشغيلة {batch.BatchNumber} صلاحية {DbDate.Date(batch.ExpiryDate)}", true);
            }
        }

        /// <summary>
        /// تنبيهات المخزون عند الإضافة العادية (بالباركود التجاري/البحث): منتهي على الرف، لا توجد تشغيلة سارية، قرب الانتهاء
        /// </summary>
        private string BuildStockWarning(Item item, SalesItemRow row)
        {
            var warnings = new System.Collections.Generic.List<string>();
            try
            {
                var expired = _itemRepository.GetExpiredBatchesWithStock(item.Id);
                if (expired.Count > 0)
                    warnings.Add($"فيه تشغيلة منتهية على الرف ({string.Join("، ", expired.Select(b => b.BatchNumber))}) — اسحبها ومتبعهاش");

                if (item.Batches == null || item.Batches.Count == 0)
                    warnings.Add("مفيش تشغيلة سارية — هتتسجل بالسالب لحد ما تدخل فاتورة شراء");
                else if (ExpiryPolicy.IsNearExpiry(item.Batches[0].ExpiryDate))
                    warnings.Add($"أقرب تشغيلة قريبة الانتهاء ({DbDate.Date(item.Batches[0].ExpiryDate)})");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.BuildStockWarning");
            }
            return string.Join(" | ", warnings);
        }

        /// <summary>
        /// فتح وردية للمستخدم الحالي من شاشة البيع نفسها برصيد افتتاح (الكاشير مالوش صلاحية شاشة الموظفين)
        /// </summary>
        public void OpenShiftFromSales()
        {
            var user = _sessionService?.CurrentUser;
            if (user == null || _shiftRepository == null)
            {
                SetStatus("سجّل الدخول الأول عشان تفتح وردية.", false);
                return;
            }

            if (HasOpenShift)
            {
                SetStatus("فيه وردية مفتوحة بالفعل.", true);
                return;
            }

            var existing = _shiftRepository.GetCurrentOpenShift(user.Id);
            if (existing == null)
            {
                long openingPiastres = Money.FromPounds(OpeningBalancePounds).Piastres;
                int shiftId = _shiftRepository.OpenShift(user.Id, user.FullName, openingPiastres);
                existing = _shiftRepository.GetCurrentOpenShift(user.Id);
                if (existing == null)
                {
                    SetStatus($"تعذر فتح الوردية رقم {shiftId}.", false);
                    return;
                }
            }

            _sessionService!.SetCurrentShift(existing);
            OnPropertyChanged(nameof(HasOpenShift));
            OnPropertyChanged(nameof(NoOpenShift));
            SetStatus($"تم فتح الوردية رقم {existing.Id} برصيد افتتاح {OpeningBalancePounds:N2} ج.م. تقدر تبدأ البيع.", true);
        }

        /// <summary>
        /// تحميل فاتورة البيع الأصلية لبدء المرتجع: الأصناف والأسعار والوحدات من الفاتورة،
        /// والكمية تبدأ بالمتبقي للإرجاع ويمكن تقليلها أو حذف البند.
        /// </summary>
        public void LoadOriginalInvoice()
        {
            if (!IsReturnMode) return;

            var source = _invoiceRepository.GetReturnableInvoice(OriginalInvoiceNumber, out string err);
            if (source == null)
            {
                SetStatus(err, false);
                PlayWarningSound();
                return;
            }

            _returnSource = source;
            CartItems.Clear();

            CustomerName = source.Invoice.CustomerName;
            CustomerPhone = source.Invoice.CustomerPhone;
            PrescriptionNumber = source.Invoice.PrescriptionNumber ?? string.Empty;
            DoctorName = source.Invoice.DoctorName ?? string.Empty;
            SelectedPriceCategory = source.Invoice.PriceCategory;
            // فاتورة مقسّمة الدفع: المرتجع بطريقة واحدة، نبدأ بنقدي وللكاشير يغيّرها
            SelectedPaymentMethod = source.Invoice.PaymentMethod == PaymentMethod.Mixed ? PaymentMethod.Cash : source.Invoice.PaymentMethod;

            int defaultVat = GetDefaultVat();
            foreach (var line in source.Lines.Where(l => l.RemainingQuantity > 0))
            {
                var item = _itemRepository.GetById(line.Line.ItemId);
                if (item == null) continue;

                var row = new SalesItemRow(item, source.Invoice.PriceCategory, defaultVat);
                row.ConfigureAsReturnLine(line.Line, line.RemainingQuantity);
                row.PropertyChanged += (s, e) => RecalculateTotals();
                CartItems.Add(row);
            }

            RecalculateTotals();
            SetStatus($"تم تحميل فاتورة {source.Invoice.InvoiceNumber}: عدّل الكميات المرتجعة أو احذف البنود اللي مش راجعة.", true);
        }

        public void RemoveItem(SalesItemRow? row)
        {
            if (row != null && CartItems.Contains(row))
            {
                CartItems.Remove(row);
                RecalculateTotals();
                SetStatus($"تم حذف البند: {row.NameAr}", true);
            }
        }

        public void RecalculateTotals()
        {
            long sub = 0;
            long disc = 0;
            long vat = 0;
            long net = 0;

            foreach (var row in CartItems)
            {
                long gross = row.UnitPricePiastres * row.Quantity;
                sub += gross;
                disc += row.DiscountPiastres;
                vat += row.VatPiastres;
                net += row.TotalPiastres;
            }

            SubtotalPiastres = sub;
            DiscountPiastres = disc;
            VatPiastres = vat;
            NetTotalPiastres = net;

            if (SelectedPaymentMethod == PaymentMethod.Cash || SelectedPaymentMethod == PaymentMethod.Network)
            {
                PaidRiyals = TotalNetRiyals;
            }
            else if (SelectedPaymentMethod == PaymentMethod.Mixed)
            {
                // المدفوع = الإجمالي ناقص جزء الآجل، والمتبقي = جزء الآجل فقط
                PaidRiyals = Money.FromPiastres(Math.Max(0, NetTotalPiastres - SplitOnAccountPiastres)).ToPounds();
            }

            long paidPiastres = Money.FromPounds(PaidRiyals).Piastres;
            RemainingPiastres = SelectedPaymentMethod == PaymentMethod.Mixed
                ? Math.Min(NetTotalPiastres, SplitOnAccountPiastres)
                : Math.Max(0, NetTotalPiastres - paidPiastres);
            OnPropertyChanged(nameof(SplitCashRiyals));

            OnPropertyChanged(nameof(SubtotalRiyals));
            OnPropertyChanged(nameof(DiscountRiyals));
            OnPropertyChanged(nameof(VatRiyals));
            OnPropertyChanged(nameof(TotalNetRiyals));
            OnPropertyChanged(nameof(RemainingRiyals));

            OnPropertyChanged(nameof(SubtotalPounds));
            OnPropertyChanged(nameof(VatPounds));
            OnPropertyChanged(nameof(TotalNetPounds));
            OnPropertyChanged(nameof(RemainingPounds));
            OnPropertyChanged(nameof(PaidPounds));
        }

        public void SaveCurrentInvoice()
        {
            if (CartItems.Count == 0)
            {
                SetStatus("لا يمكن حفظ فاتورة فارغة! أضف أصنافاً أولاً.", false);
                return;
            }

            if (!IsReturnMode)
            {
                var rxItems = CartItems.Where(r => r.RequiresPrescription).ToList();
                if (rxItems.Count > 0 && (string.IsNullOrWhiteSpace(PrescriptionNumber) || string.IsNullOrWhiteSpace(DoctorName)))
                {
                    SetStatus($"الأصناف دي بتتطلب وصفة طبية: {string.Join("، ", rxItems.Select(r => r.NameAr))} — اكتب رقم الوصفة واسم الطبيب الأول.", false);
                    PlayWarningSound();
                    return;
                }
            }

            if (!HasOpenShift)
            {
                SetStatus("لازم تفتح وردية الأول (من شاشة الموظفين والورديات برصيد افتتاح) قبل أي بيع أو مرتجع.", false);
                PlayWarningSound();
                return;
            }

            if (IsReturnMode && _returnSource == null)
            {
                SetStatus("حمّل فاتورة البيع الأصلية الأول: اكتب رقمها واضغط تحميل.", false);
                return;
            }

            if (!TryResolveManagerApproval(out var managerApproval)) return;

            try
            {
                // ربط الفاتورة بعميل مسجل (مطلوب لتحديث رصيد العميل في البيع الآجل)
                string typedName = (CustomerName ?? string.Empty).Trim();
                var matchedCustomer = CustomersList.FirstOrDefault(c => !c.IsDefaultCash &&
                    string.Equals(c.Name.Trim(), typedName, StringComparison.OrdinalIgnoreCase));

                if (!TryValidateSplit(out string splitError))
                {
                    SetStatus(splitError, false);
                    PlayWarningSound();
                    return;
                }

                bool needsRegisteredCustomer = SelectedPaymentMethod == PaymentMethod.OnAccount
                    || (IsMixedPayment && SplitOnAccountPiastres > 0);
                if (needsRegisteredCustomer && matchedCustomer == null)
                {
                    SetStatus("البيع الآجل يتطلب اختيار عميل مسجل بنفس الاسم من قائمة العملاء.", false);
                    return;
                }

                var settings = _settingsRepository.GetSettings();
                DateTime now = DateTime.Now;

                // إنشاء رمز QR بسيط إن كان مفعلاً في الإعدادات
                string? qrText = null;
                if (settings.EnableQrCode)
                {
                    qrText = EgyptQrHelper.GenerateSimpleInvoiceQrText(
                        InvoiceNumber,
                        now,
                        TotalNetRiyals,
                        settings.TaxNumber,
                        settings.PharmacyNameAr
                    );
                }

                var invoice = new Invoice
                {
                    InvoiceNumber = InvoiceNumber,
                    Type = CurrentInvoiceType,
                    Date = now,
                    CustomerId = matchedCustomer?.Id,
                    CustomerName = CustomerName,
                    CustomerPhone = CustomerPhone,
                    PriceCategory = SelectedPriceCategory,
                    PaymentMethod = SelectedPaymentMethod,
                    Payments = BuildPaymentParts(),
                    PrescriptionNumber = PrescriptionNumber,
                    DoctorName = DoctorName,
                    UserId = CurrentUserId,
                    UserName = CurrentUserName,
                    ShiftId = CurrentShiftId,
                    SubtotalPiastres = SubtotalPiastres,
                    DiscountPiastres = DiscountPiastres,
                    VatPiastres = VatPiastres,
                    TotalPiastres = NetTotalPiastres,
                    PaidPiastres = Money.FromPounds(PaidRiyals).Piastres,
                    RemainingPiastres = RemainingPiastres,
                    QrCodeText = qrText,
                    OriginalInvoiceId = IsReturnMode ? _returnSource?.Invoice.Id : null
                };

                foreach (var row in CartItems)
                {
                    invoice.Items.Add(new InvoiceItem
                    {
                        ItemId = row.ItemId,
                        ItemCode = row.Code,
                        ItemNameAr = row.NameAr,
                        BatchId = row.BatchId,
                        PrescriptionNumber = row.RequiresPrescription ? PrescriptionNumber : null,
                        DoctorName = row.RequiresPrescription ? DoctorName : null,
                        OriginalInvoiceItemId = row.OriginalInvoiceItemId,
                        BatchNumber = row.BatchNumber,
                        ExpiryDate = row.ExpiryDate,
                        UnitLevel = row.SelectedUnitLevel,
                        QuantitySold = row.Quantity,
                        SmallestUnitsCount = row.SmallestUnitsToDeduct,
                        UnitPricePiastres = row.UnitPricePiastres,
                        DiscountPiastres = row.DiscountPiastres,
                        VatRatePercent = row.VatRatePercent,
                        VatPiastres = row.VatPiastres,
                        TotalPiastres = row.TotalPiastres
                    });
                }

                bool saved = _invoiceRepository.SaveInvoice(invoice, out string err);

                // تجاوز حد الائتمان: Admin/Owner يقدر يوافق بعد تأكيد صريح، غيرهم بيتم رفض البيع
                if (!saved && err.StartsWith(InvoiceRepository.CreditLimitErrorPrefix, StringComparison.Ordinal))
                {
                    var role = _sessionService?.CurrentUser?.Role;
                    if (role == UserRole.Admin || role == UserRole.Owner)
                    {
                        // رصيد وحد العميل قبل البيع (من القاعدة) لتسجيلهما في سجل التدقيق عند الموافقة
                        var customerBefore = invoice.CustomerId.HasValue ? _customerRepository.GetById(invoice.CustomerId.Value) : null;

                        if (ConfirmCreditLimitOverride(err))
                        {
                            invoice.CreditLimitOverridden = true;
                            saved = _invoiceRepository.SaveInvoice(invoice, out err);
                            if (saved) LogCreditLimitOverride(invoice, customerBefore);
                        }
                    }
                    else
                    {
                        err += " — تجاوز الحد محتاج موافقة مدير النظام.";
                    }
                }

                if (saved)
                {
                    LastSavedInvoiceNumber = invoice.InvoiceNumber;
                    if (managerApproval != null) LogManagerApproval(invoice, managerApproval);
                    NeedsManagerApproval = false;
                    ApprovalReason = string.Empty;
                    ApproverUsername = string.Empty;
                    SetStatus($"تم حفظ الفاتورة بنجاح: {invoice.InvoiceNumber} (يمكنك التراجع عنها بـ F9)", true);
                    PlayConfirmSound();
                    StartNewInvoice();
                }
                else
                {
                    SetStatus($"فشل حفظ الفاتورة: {err}", false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SaveCurrentInvoice");
                SetStatus($"خطأ غير متوقع: {ex.Message}", false);
            }
        }

        /// <summary>
        /// تسجيل موافقة المدير على تجاوز حد الائتمان في نظام التدقيق: Owner في سجله الخاص، وAdmin في سجل الموظفين.
        /// </summary>
        private void LogCreditLimitOverride(Invoice invoice, Customer? customerBefore)
        {
            try
            {
                var user = _sessionService?.CurrentUser;
                if (user == null) return;

                long limit = customerBefore?.CreditLimitPiastres ?? 0;
                long balanceBefore = customerBefore?.CurrentBalancePiastres ?? 0;
                string name = customerBefore?.Name ?? invoice.CustomerName;

                string M(long piastres) => (piastres / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                string oldValue = $"حد الائتمان {M(limit)} ج.م / الرصيد قبل الفاتورة {M(balanceBefore)} ج.م";
                string newValue = $"فاتورة {invoice.InvoiceNumber} بمبلغ آجل {M(invoice.RemainingPiastres)} ج.م / الرصيد بعدها {M(balanceBefore + invoice.RemainingPiastres)} ج.م / وافق: {user.FullName}";
                string entityId = (invoice.CustomerId ?? 0).ToString();

                if (user.Role == UserRole.Owner)
                    _ownerAuditLogRepository?.LogChange(user.Id, "CreditLimitOverride", entityId, name, "تجاوز حد الائتمان", oldValue, newValue);
                else
                    _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "CreditLimitOverride", entityId, name, "تجاوز حد الائتمان", oldValue, newValue);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.LogCreditLimitOverride");
            }
        }

        public void PrintCurrentInvoice()
        {
            if (CartItems.Count == 0)
            {
                SetStatus("لا يمكن طباعة فاتورة فارغة!", false);
                return;
            }

            try
            {
                var settings = _settingsRepository.GetSettings();
                string? qrText = null;
                if (settings.EnableQrCode)
                {
                    qrText = EgyptQrHelper.GenerateSimpleInvoiceQrText(
                        InvoiceNumber,
                        DateTime.Now,
                        TotalNetRiyals,
                        settings.TaxNumber,
                        settings.PharmacyNameAr
                    );
                }

                var invoice = new Invoice
                {
                    InvoiceNumber = InvoiceNumber,
                    Date = DateTime.Now,
                    CustomerName = CustomerName,
                    CustomerPhone = CustomerPhone,
                    PaymentMethod = SelectedPaymentMethod,
                    Payments = BuildPaymentParts(),
                    SubtotalPiastres = SubtotalPiastres,
                    DiscountPiastres = DiscountPiastres,
                    VatPiastres = VatPiastres,
                    TotalPiastres = NetTotalPiastres,
                    PaidPiastres = Money.FromPounds(PaidRiyals).Piastres,
                    RemainingPiastres = RemainingPiastres,
                    QrCodeText = qrText
                };

                foreach (var row in CartItems)
                {
                    invoice.Items.Add(new InvoiceItem
                    {
                        ItemId = row.ItemId,
                        ItemCode = row.Code,
                        ItemNameAr = row.NameAr,
                        BatchNumber = row.BatchNumber,
                        ExpiryDate = row.ExpiryDate,
                        UnitLevel = row.SelectedUnitLevel,
                        QuantitySold = row.Quantity,
                        SmallestUnitsCount = row.SmallestUnitsToDeduct,
                        UnitPricePiastres = row.UnitPricePiastres,
                        DiscountPiastres = row.DiscountPiastres,
                        VatRatePercent = row.VatRatePercent,
                        VatPiastres = row.VatPiastres,
                        TotalPiastres = row.TotalPiastres
                    });
                }

                if (Services.ThermalReceiptPrinter.PrintReceipt(invoice, settings, out string err))
                {
                    SetStatus("تمت طباعة الإيصال الحراري بنجاح (F3)", true);
                }
                else
                {
                    SetStatus($"تعذر طباعة الإيصال: {err}", false);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"خطأ في الطباعة: {ex.Message}", false);
            }
        }

        /// <summary>خطوة 28: توليد PDF على خيط خلفي (الفاتورة بتتبني على خيط الواجهة قبله)</summary>
        public async Task PrintA4InvoicePdfAsync()
        {
            if (IsBusy) return;
            if (CartItems.Count == 0)
            {
                SetStatus("الرجاء إضافة أصناف أولاً قبل طباعة الفاتورة A4", false);
                return;
            }

            if (_pdfReportService == null)
            {
                SetStatus("خدمة طباعة PDF غير مفعلة.", false);
                return;
            }

            IsBusy = true;
            try
            {
                var settings = _settingsRepository.GetSettings();
                string? qrText = null;
                if (settings.EnableQrCode)
                {
                    qrText = EgyptQrHelper.GenerateSimpleInvoiceQrText(
                        InvoiceNumber,
                        DateTime.Now,
                        TotalNetRiyals,
                        settings.TaxNumber,
                        settings.PharmacyNameAr
                    );
                }

                var invoice = new Invoice
                {
                    InvoiceNumber = InvoiceNumber,
                    Date = DateTime.Now,
                    CustomerName = CustomerName,
                    CustomerPhone = CustomerPhone,
                    PaymentMethod = SelectedPaymentMethod,
                    Payments = BuildPaymentParts(),
                    SubtotalPiastres = SubtotalPiastres,
                    DiscountPiastres = DiscountPiastres,
                    VatPiastres = VatPiastres,
                    TotalPiastres = NetTotalPiastres,
                    PaidPiastres = Money.FromPounds(PaidRiyals).Piastres,
                    RemainingPiastres = RemainingPiastres,
                    QrCodeText = qrText
                };

                foreach (var row in CartItems)
                {
                    invoice.Items.Add(new InvoiceItem
                    {
                        ItemId = row.ItemId,
                        ItemCode = row.Code,
                        ItemNameAr = row.NameAr,
                        BatchNumber = row.BatchNumber,
                        ExpiryDate = row.ExpiryDate,
                        UnitLevel = row.SelectedUnitLevel,
                        QuantitySold = row.Quantity,
                        SmallestUnitsCount = row.SmallestUnitsToDeduct,
                        UnitPricePiastres = row.UnitPricePiastres,
                        DiscountPiastres = row.DiscountPiastres,
                        VatRatePercent = row.VatRatePercent,
                        VatPiastres = row.VatPiastres,
                        TotalPiastres = row.TotalPiastres
                    });
                }

                string pdfPath = await _pdfReportService.GenerateA4InvoicePdfAsync(invoice, settings);
                SetStatus($"تم توليد فاتورة A4 الضريبية بنجاح: {System.IO.Path.GetFileName(pdfPath)}", true);
                System.Windows.MessageBox.Show(
                    $"تم إنشاء فاتورة المبيعات الضريبية الرسمية A4 بصيغة PDF بنجاح:\n{pdfPath}",
                    "فاتورة ضريبية رسمية (A4)",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                SetStatus($"فشل توليد الفاتورة A4: {ex.Message}", false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void SuspendCurrentInvoice()
        {
            if (CartItems.Count == 0)
            {
                SetStatus("لا توجد فاتورة لتعليقها!", false);
                return;
            }
            SetStatus($"تم تعليق الفاتورة {InvoiceNumber} بنجاح (F8)", true);
            StartNewInvoice();
        }

        public void ToggleReturnMode()
        {
            CurrentInvoiceType = CurrentInvoiceType == InvoiceType.Sales ? InvoiceType.Return : InvoiceType.Sales;
            StartNewInvoice();
        }

        public void OpenDrawer()
        {
            try
            {
                var settings = _settingsRepository.GetSettings();
                if (Services.ThermalReceiptPrinter.OpenCashDrawer(settings, out string err))
                {
                    SetStatus("تم إرسال نبضة فتح درج النقدية (F10)", true);
                }
                else
                {
                    SetStatus($"تعذر فتح الدرج: {err}", false);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"تعذر فتح الدرج: {ex.Message}", false);
            }
        }

        private void SetStatus(string msg, bool success)
        {
            StatusMessage = msg;
            IsStatusSuccess = success;
        }

        private static void PlayWarningSound()
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.PlayWarningSound");
            }
        }

        private static void PlayConfirmSound()
        {
            try
            {
                SystemSounds.Beep.Play();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SalesViewModel.PlayConfirmSound");
            }
        }
    }
}
