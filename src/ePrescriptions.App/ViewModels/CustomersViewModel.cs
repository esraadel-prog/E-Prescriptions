using System;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    public class CustomerStatementItemDisplay
    {
        public DateTime Date { get; set; }
        public string ReferenceNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long DebitPiastres { get; set; }
        public long CreditPiastres { get; set; }
        public long RunningBalancePiastres { get; set; }

        public string FormattedDate => Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        public string FormattedDebit => DebitPiastres > 0 ? Money.FromPiastres(DebitPiastres).ToFormattedString() : "-";
        public string FormattedCredit => CreditPiastres > 0 ? Money.FromPiastres(CreditPiastres).ToFormattedString() : "-";
        public string FormattedRunningBalance => Money.FromPiastres(RunningBalancePiastres).ToFormattedString();
    }

    public class CustomersViewModel : ViewModelBase
    {
        private readonly CustomerRepository _customerRepository;
        private readonly SettingsRepository? _settingsRepository;
        private readonly Services.IPdfReportService? _pdfReportService;
        private readonly Data.Repositories.OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly Data.Repositories.StaffAuditLogRepository? _staffAuditLogRepository;
        private readonly Services.IUserSessionService? _sessionService;

        // قائمة العملاء والبحث
        public ObservableCollection<Customer> CustomersList { get; } = new();
        public ObservableCollection<CustomerStatementItemDisplay> StatementRows { get; } = new();

        private Customer? _selectedCustomer;
        public Customer? SelectedCustomer
        {
            get => _selectedCustomer;
            set
            {
                if (SetProperty(ref _selectedCustomer, value) && value != null)
                {
                    LoadCustomerToEdit(value);
                    LoadStatement(value.Id);
                }
            }
        }

        // حقول إضافة / تعديل عميل
        private int _editId;
        private string _editName = string.Empty;
        public string EditName { get => _editName; set => SetProperty(ref _editName, value); }

        private string _editPhone = string.Empty;
        public string EditPhone { get => _editPhone; set => SetProperty(ref _editPhone, value); }

        private string _editNationalId = string.Empty;
        public string EditNationalId { get => _editNationalId; set => SetProperty(ref _editNationalId, value); }

        private decimal _editCreditLimitRiyals;
        public decimal EditCreditLimitRiyals { get => _editCreditLimitRiyals; set => SetProperty(ref _editCreditLimitRiyals, value); }

        private PriceCategory _editCategory = PriceCategory.Retail;
        public PriceCategory EditCategory { get => _editCategory; set => SetProperty(ref _editCategory, value); }

        private string _editNotes = string.Empty;
        public string EditNotes { get => _editNotes; set => SetProperty(ref _editNotes, value); }

        // سند قبض عميل
        private decimal _voucherAmountRiyals;
        public decimal VoucherAmountRiyals { get => _voucherAmountRiyals; set => SetProperty(ref _voucherAmountRiyals, value); }

        private PaymentMethod _voucherPaymentMethod = PaymentMethod.Cash;
        public PaymentMethod VoucherPaymentMethod { get => _voucherPaymentMethod; set => SetProperty(ref _voucherPaymentMethod, value); }

        private string _voucherNotes = "سداد دفعة من الحساب الآجل";
        public string VoucherNotes { get => _voucherNotes; set => SetProperty(ref _voucherNotes, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        // الأوامر
        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand NewCustomerCommand { get; }
        public IRelayCommand SaveCustomerCommand { get; }
        public IRelayCommand ReceivePaymentCommand { get; }
        public IRelayCommand PrintStatementPdfCommand { get; }

        public CustomersViewModel(
            CustomerRepository customerRepository,
            SettingsRepository? settingsRepository = null,
            Services.IPdfReportService? pdfReportService = null,
            Data.Repositories.OwnerAuditLogRepository? ownerAuditLogRepository = null,
            Data.Repositories.StaffAuditLogRepository? staffAuditLogRepository = null,
            Services.IUserSessionService? sessionService = null)
        {
            Title = "حسابات العملاء والآجل";
            _customerRepository = customerRepository;
            _settingsRepository = settingsRepository;
            _pdfReportService = pdfReportService;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _sessionService = sessionService;

            RefreshCommand = new RelayCommand(LoadCustomers);
            NewCustomerCommand = new RelayCommand(ResetForm);
            SaveCustomerCommand = new RelayCommand(SaveCustomer);
            ReceivePaymentCommand = new RelayCommand(ReceivePayment);
            PrintStatementPdfCommand = new AsyncRelayCommand(PrintStatementPdfAsync);

            LoadCustomers();
        }

        /// <summary>خطوة 28: توليد كشف الحساب PDF على خيط خلفي</summary>
        private async Task PrintStatementPdfAsync()
        {
            if (IsBusy) return;
            if (SelectedCustomer == null)
            {
                StatusMessage = "يرجى اختيار عميل أولاً لطباعة كشف حسابه.";
                return;
            }

            if (_pdfReportService == null || _settingsRepository == null)
            {
                StatusMessage = "خدمة طباعة PDF غير مفعلة.";
                return;
            }

            IsBusy = true;
            try
            {
                var settings = _settingsRepository.GetSettings();
                var statement = _customerRepository.GetCustomerStatement(SelectedCustomer.Id);
                var customer = SelectedCustomer; // نسخة ثابتة قبل الانتقال للخيط الخلفي
                string pdfPath = await _pdfReportService.GenerateCustomerStatementPdfAsync(customer, statement, settings);

                StatusMessage = $"تم توليد كشف الحساب بصيغة PDF بنجاح: {System.IO.Path.GetFileName(pdfPath)}";
                System.Windows.MessageBox.Show(
                    $"تم إنشاء كشف حساب العميل بصيغة PDF بنجاح:\n{pdfPath}",
                    "طباعة كشف الحساب",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                StatusMessage = $"فشل توليد كشف الحساب: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void LoadCustomers()
        {
            CustomersList.Clear();
            var customers = _customerRepository.GetAll();
            foreach (var c in customers) CustomersList.Add(c);
            StatusMessage = "تم تحديث قائمة العملاء.";
        }

        private void LoadCustomerToEdit(Customer customer)
        {
            _editId = customer.Id;
            EditName = customer.Name;
            EditPhone = customer.Phone;
            EditNationalId = customer.NationalId ?? string.Empty;
            EditCreditLimitRiyals = Money.FromPiastres(customer.CreditLimitPiastres).ToPounds();
            EditCategory = customer.PriceCategory;
            EditNotes = customer.Notes ?? string.Empty;
        }

        private void LoadStatement(int customerId)
        {
            StatementRows.Clear();
            var rows = _customerRepository.GetCustomerStatement(customerId);
            foreach (var r in rows)
            {
                StatementRows.Add(new CustomerStatementItemDisplay
                {
                    Date = r.Date,
                    ReferenceNumber = r.ReferenceNumber,
                    Description = r.Description,
                    DebitPiastres = r.DebitPiastres,
                    CreditPiastres = r.CreditPiastres,
                    RunningBalancePiastres = r.RunningBalancePiastres
                });
            }
        }

        public void ResetForm()
        {
            _editId = 0;
            EditName = string.Empty;
            EditPhone = string.Empty;
            EditNationalId = string.Empty;
            EditCreditLimitRiyals = 0;
            EditCategory = PriceCategory.Retail;
            EditNotes = string.Empty;
            SelectedCustomer = null;
            StatementRows.Clear();
            StatusMessage = "جاهز لإدخال عميل جديد.";
        }

        public void SaveCustomer()
        {
            if (string.IsNullOrWhiteSpace(EditName))
            {
                StatusMessage = "يرجى إدخال اسم العميل!";
                return;
            }

            var previous = SelectedCustomer;

            var c = new Customer
            {
                Id = _editId,
                Name = EditName,
                Phone = EditPhone,
                NationalId = string.IsNullOrWhiteSpace(EditNationalId) ? null : EditNationalId,
                CreditLimitPiastres = Money.FromPounds(EditCreditLimitRiyals).Piastres,
                CurrentBalancePiastres = SelectedCustomer?.CurrentBalancePiastres ?? 0,
                PriceCategory = EditCategory,
                Notes = EditNotes,
                IsActive = true
            };

            if (_editId == 0)
            {
                _customerRepository.AddCustomer(c);
                StatusMessage = $"تمت إضافة العميل '{c.Name}' بنجاح.";
            }
            else
            {
                LogOwnerCustomerEditIfApplicable(previous, c);
                _customerRepository.UpdateCustomer(c);
                StatusMessage = $"تم تحديث بيانات العميل '{c.Name}' بنجاح.";
            }

            LoadCustomers();
        }

        public void ReceivePayment()
        {
            if (SelectedCustomer == null)
            {
                StatusMessage = "يرجى اختيار عميل أولاً لإصدار سند القبض!";
                return;
            }

            if (VoucherAmountRiyals <= 0)
            {
                StatusMessage = "يرجى كتابة مبلغ قبض صحيح!";
                return;
            }

            long amtPiastres = Money.FromPounds(VoucherAmountRiyals).Piastres;
            string vNum = _customerRepository.GenerateNextCustomerVoucherNumber();

            var voucher = new CustomerVoucher
            {
                VoucherNumber = vNum,
                CustomerId = SelectedCustomer.Id,
                CustomerName = SelectedCustomer.Name,
                Date = DateTime.Now,
                AmountPiastres = amtPiastres,
                PaymentMethod = VoucherPaymentMethod,
                Notes = VoucherNotes
            };

            if (_customerRepository.AddCustomerVoucher(voucher))
            {
                LogOwnerVoucherIfApplicable(SelectedCustomer, amtPiastres, vNum);
                StatusMessage = $"تم تسجيل سند القبض رقم {vNum} بمبلغ {VoucherAmountRiyals:N2} ج.م وخصمه من مديونية العميل.";
                VoucherAmountRiyals = 0;
                LoadCustomers();
                LoadStatement(SelectedCustomer.Id);
            }
            else
            {
                StatusMessage = "فشل تسجيل سند القبض.";
            }
        }
        /// <summary>
        /// إذا كان المنفّذ صاحب الصيدلية، نسجّل أي حقل تغيّر في بيانات العميل في سجله الخاص فقط.
        /// </summary>
        /// <summary>
        /// يسجّل أي حقل تغيّر عند تعديل عميل: لصاحب الصيدلية في سجله الخاص المخفي عن الجميع،
        /// ولأي دور آخر في سجل الموظفين العام المرئي لـ Admin وOwner فقط.
        /// </summary>
        private void LogOwnerCustomerEditIfApplicable(Customer? previous, Customer updated)
        {
            if (_sessionService?.CurrentUser == null || previous == null) return;

            string entityId = updated.Id.ToString();
            string label = updated.Name;

            void Log(string field, string? oldVal, string? newVal) =>
                LogAudit("Customer", entityId, label, field, oldVal, newVal);

            Log("Name", previous.Name, updated.Name);
            Log("Phone", previous.Phone, updated.Phone);
            Log("CreditLimitPiastres", previous.CreditLimitPiastres.ToString(), updated.CreditLimitPiastres.ToString());
            Log("PriceCategory", previous.PriceCategory.ToString(), updated.PriceCategory.ToString());
        }

        /// <summary>
        /// نقطة التوجيه الموحّدة: Owner → OwnerAuditLog (مخفي عن الجميع)، أي دور آخر → StaffAuditLog (مرئي لـ Admin/Owner).
        /// </summary>
        private void LogAudit(string entityType, string entityId, string entityLabel, string field, string? oldVal, string? newVal)
        {
            var user = _sessionService?.CurrentUser;
            if (user == null) return;

            if (user.Role == UserRole.Owner)
            {
                _ownerAuditLogRepository?.LogChange(user.Id, entityType, entityId, entityLabel, field, oldVal, newVal);
            }
            else
            {
                _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), entityType, entityId, entityLabel, field, oldVal, newVal);
            }
        }

        /// <summary>
        /// تسجيل سند قبض يدوي من صاحب الصيدلية (تصحيح رصيد عميل)، إن كان هو المنفّذ.
        /// </summary>
        private void LogOwnerVoucherIfApplicable(Customer? customer, long amountPiastres, string voucherNumber)
        {
            if (_sessionService?.CurrentUser == null || customer == null) return;

            LogAudit("Customer", customer.Id.ToString(), customer.Name, $"سند قبض رقم {voucherNumber} (تسوية رصيد)",
                customer.CurrentBalancePiastres.ToString(), (customer.CurrentBalancePiastres - amountPiastres).ToString());
        }
    }
}