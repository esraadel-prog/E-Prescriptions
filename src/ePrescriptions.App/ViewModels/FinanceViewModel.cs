using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.ViewModels
{
    public class ExpenseDisplayItem
    {
        public string ExpenseNumber { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string PaidTo { get; set; } = string.Empty;
        public long AmountPiastres { get; set; }
        public string? Notes { get; set; }

        public string FormattedDate => Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        public string FormattedAmount => Money.FromPiastres(AmountPiastres).ToFormattedString();
    }

    public class FinanceViewModel : ViewModelBase
    {
        private readonly FinanceRepository _financeRepository;

        // نطاق التاريخ للتقارير
        private DateTime _startDate = DateTime.Today.AddMonths(-1);
        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                {
                    LoadFinancialReports();
                }
            }
        }

        private DateTime _endDate = DateTime.Today;
        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value))
                {
                    LoadFinancialReports();
                }
            }
        }

        // قوائم البيانات
        public ObservableCollection<ExpenseDisplayItem> ExpensesList { get; } = new();
        public ObservableCollection<JournalEntry> JournalEntriesList { get; } = new();

        // إقرار ضريبة القيمة المضافة
        private VatReturnStatement _vatStatement = new();
        public VatReturnStatement VatStatement
        {
            get => _vatStatement;
            set => SetProperty(ref _vatStatement, value);
        }

        public string FormattedSalesVat => Money.FromPiastres(VatStatement.OutputVatPiastres).ToFormattedString();
        public string FormattedPurchasesVat => Money.FromPiastres(VatStatement.InputVatPiastres).ToFormattedString();
        public string FormattedNetVat => Money.FromPiastres(VatStatement.NetVatPayablePiastres).ToFormattedString();
        public string FormattedTaxableSales => Money.FromPiastres(VatStatement.TaxableSalesPiastres).ToFormattedString();
        public string FormattedTaxablePurchases => Money.FromPiastres(VatStatement.TaxablePurchasesPiastres).ToFormattedString();

        // ملخص التدفق النقدي
        private CashFlowSummary _cashFlow = new();
        public CashFlowSummary CashFlow
        {
            get => _cashFlow;
            set => SetProperty(ref _cashFlow, value);
        }

        public string FormattedInflow => Money.FromPiastres(CashFlow.TotalInflowPiastres).ToFormattedString();
        public string FormattedOutflow => Money.FromPiastres(CashFlow.TotalOutflowPiastres).ToFormattedString();
        public string FormattedNetCashFlow => Money.FromPiastres(CashFlow.NetCashFlowPiastres).ToFormattedString();

        // حقول إضافة مصروف جديد
        private string _newExpenseCategory = "إيجار ومرافق";
        public string NewExpenseCategory { get => _newExpenseCategory; set => SetProperty(ref _newExpenseCategory, value); }

        private decimal _newExpenseAmountPounds;
        public decimal NewExpenseAmountPounds { get => _newExpenseAmountPounds; set => SetProperty(ref _newExpenseAmountPounds, value); }

        private string _newExpensePaidTo = string.Empty;
        public string NewExpensePaidTo { get => _newExpensePaidTo; set => SetProperty(ref _newExpensePaidTo, value); }

        private string _newExpenseNotes = string.Empty;
        public string NewExpenseNotes { get => _newExpenseNotes; set => SetProperty(ref _newExpenseNotes, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        // الأوامر
        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand AddExpenseCommand { get; }

        private readonly Data.Repositories.OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly Data.Repositories.StaffAuditLogRepository? _staffAuditLogRepository;
        private readonly Services.IUserSessionService? _sessionService;

        public FinanceViewModel(
            FinanceRepository financeRepository,
            Data.Repositories.OwnerAuditLogRepository? ownerAuditLogRepository = null,
            Data.Repositories.StaffAuditLogRepository? staffAuditLogRepository = null,
            Services.IUserSessionService? sessionService = null)
        {
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _sessionService = sessionService;

            Title = "الحسابات والمالية والضرائب";
            _financeRepository = financeRepository;

            RefreshCommand = new RelayCommand(LoadFinancialReports);
            AddExpenseCommand = new RelayCommand(AddExpense);

            LoadFinancialReports();
        }

        public void LoadFinancialReports()
        {
            // 1) المصروفات
            ExpensesList.Clear();
            var expenses = _financeRepository.GetExpenses(StartDate, EndDate);
            foreach (var exp in expenses)
            {
                ExpensesList.Add(new ExpenseDisplayItem
                {
                    ExpenseNumber = exp.ExpenseNumber,
                    Category = exp.Category,
                    Date = exp.Date,
                    PaidTo = exp.PaidTo,
                    AmountPiastres = exp.AmountPiastres,
                    Notes = exp.Notes
                });
            }

            // 2) إقرار الضريبة (بنسب البنود الفعلية)
            VatStatement = _financeRepository.GetVatReturnReport(StartDate, EndDate);
            OnPropertyChanged(nameof(FormattedSalesVat));
            OnPropertyChanged(nameof(FormattedPurchasesVat));
            OnPropertyChanged(nameof(FormattedNetVat));
            OnPropertyChanged(nameof(FormattedTaxableSales));
            OnPropertyChanged(nameof(FormattedTaxablePurchases));

            // 3) التدفق النقدي
            CashFlow = _financeRepository.GetCashFlowSummary(StartDate, EndDate);
            OnPropertyChanged(nameof(FormattedInflow));
            OnPropertyChanged(nameof(FormattedOutflow));
            OnPropertyChanged(nameof(FormattedNetCashFlow));

            // 4) القيود اليومية
            JournalEntriesList.Clear();
            var jvs = _financeRepository.GetJournalEntries(StartDate, EndDate);
            foreach (var j in jvs) JournalEntriesList.Add(j);

            StatusMessage = "تم تحديث التقارير المالية والضريبية بنجاح.";
        }

        public void AddExpense()
        {
            if (NewExpenseAmountPounds <= 0)
            {
                StatusMessage = "يرجى إدخال مبلغ صحيح للمصروف!";
                return;
            }

            long amtPiastres = Money.FromPounds(NewExpenseAmountPounds).Piastres;
            string expNum = _financeRepository.GenerateNextExpenseNumber();

            var expense = new Expense
            {
                ExpenseNumber = expNum,
                Category = NewExpenseCategory,
                AmountPiastres = amtPiastres,
                Date = DateTime.Now,
                PaidTo = string.IsNullOrWhiteSpace(NewExpensePaidTo) ? "جهة الصرف" : NewExpensePaidTo,
                Notes = NewExpenseNotes
            };

            _financeRepository.AddExpense(expense);
            LogExpenseAudit(expense);
            StatusMessage = $"تم تسجيل المصروف رقم {expNum} بمبلغ {NewExpenseAmountPounds:N2} ج.م وإنشاء القيد اليومي التلقائي.";
            NewExpenseAmountPounds = 0;
            NewExpensePaidTo = string.Empty;
            NewExpenseNotes = string.Empty;

            LoadFinancialReports();
        }

        /// <summary>
        /// يسجّل إضافة مصروف (إجراء مالي يُخرج نقدية من الصيدلية) حتى لو كان "إضافة جديدة" وليس تعديلاً،
        /// لأن المصروف الوهمي وسيلة شائعة لتهريب نقدية. لصاحب الصيدلية في سجله الخاص، ولأي دور آخر في سجل الموظفين العام.
        /// </summary>
        private void LogExpenseAudit(Expense expense)
        {
            var user = _sessionService?.CurrentUser;
            if (user == null) return;

            string field = $"إضافة مصروف رقم {expense.ExpenseNumber} ({expense.Category} - {expense.PaidTo})";
            string newVal = Money.FromPiastres(expense.AmountPiastres).ToFormattedString();

            if (user.Role == ePrescriptions.Domain.Enums.UserRole.Owner)
                _ownerAuditLogRepository?.LogChange(user.Id, "Expense", expense.ExpenseNumber, field, "المبلغ", null, newVal);
            else
                _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "Expense", expense.ExpenseNumber, field, "المبلغ", null, newVal);
        }
    }
}
