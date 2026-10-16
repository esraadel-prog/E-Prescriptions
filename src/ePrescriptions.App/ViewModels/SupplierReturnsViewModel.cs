using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    /// <summary>تشغيلة منتهية أو قريبة الانتهاء يمكن إضافتها لمستند المرتجع/الإتلاف</summary>
    public class ExpiryCandidateRow
    {
        public Item Item { get; }
        public ItemBatch Batch { get; }

        public ExpiryCandidateRow(Item item, ItemBatch batch)
        {
            Item = item;
            Batch = batch;
        }

        public string ItemName => Item.NameAr;
        public string BatchNumber => Batch.BatchNumber;
        public string ExpiryText => Batch.ExpiryDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        public int DaysLeft => (int)(Batch.ExpiryDate.Date - DateTime.Today).TotalDays;
        public bool IsExpired => DaysLeft < 0;
        public string StatusText => IsExpired ? $"منتهي منذ {-DaysLeft} يوم" : $"ينتهي خلال {DaysLeft} يوم";
        public string RemainingText => UnitBreakdownHelper.FormatStockDetailed(Batch.RemainingSmallestUnits, Item.StripsPerBox, Item.PiecesPerStrip);
        public string CostValueText => Money.FromPiastres(Batch.CostPricePerSmallestUnitPiastres * Batch.RemainingSmallestUnits).ToFormattedString();
    }

    /// <summary>بند في مستند المرتجع/الإتلاف: تشغيلة + كمية + نسبة خصم الشركة</summary>
    public class SupplierReturnLineRow : ObservableObject
    {
        public Item Item { get; }
        public ItemBatch Batch { get; }

        public string ItemName => Item.NameAr;
        public string BatchNumber => Batch.BatchNumber;
        public string ExpiryText => Batch.ExpiryDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        public string AvailableText => UnitBreakdownHelper.FormatStockDetailed(Batch.RemainingSmallestUnits, Item.StripsPerBox, Item.PiecesPerStrip);

        private UnitLevel _unitLevel = UnitLevel.Box;
        public UnitLevel UnitLevel
        {
            get => _unitLevel;
            set { if (SetProperty(ref _unitLevel, value)) Recalculate(); }
        }

        private int _quantity = 1;
        public int Quantity
        {
            get => _quantity;
            set
            {
                if (value < 1) value = 1;
                if (SetProperty(ref _quantity, value)) Recalculate();
            }
        }

        private int _deductionPercent;
        public int DeductionPercent
        {
            get => _deductionPercent;
            set
            {
                value = Math.Max(0, Math.Min(100, value));
                if (SetProperty(ref _deductionPercent, value)) Recalculate();
            }
        }

        public int SmallestUnits => UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel, Quantity, Item.StripsPerBox, Item.PiecesPerStrip);
        public bool ExceedsAvailable => SmallestUnits > Batch.RemainingSmallestUnits;
        public long CostTotalPiastres => Batch.CostPricePerSmallestUnitPiastres * SmallestUnits;
        public long CreditPiastres => SupplierReturnRepository.CalculateCredit(CostTotalPiastres, DeductionPercent);
        public string CostText => Money.FromPiastres(CostTotalPiastres).ToFormattedString();
        public string CreditText => Money.FromPiastres(CreditPiastres).ToFormattedString();
        public string ErrorText => ExceedsAvailable ? "أكبر من المتاح بالتشغيلة" : string.Empty;

        public SupplierReturnLineRow(Item item, ItemBatch batch, int deductionPercent)
        {
            Item = item;
            Batch = batch;
            _deductionPercent = Math.Max(0, Math.Min(100, deductionPercent));

            // الافتراضي: كل المتاح بالتشغيلة، بالعلبة إن كان يقبل القسمة على عدد وحدات العلبة وإلا بأصغر وحدة
            int perBox = Math.Max(1, item.SmallestUnitsPerBox);
            if (batch.RemainingSmallestUnits >= perBox && batch.RemainingSmallestUnits % perBox == 0)
            {
                _unitLevel = UnitLevel.Box;
                _quantity = batch.RemainingSmallestUnits / perBox;
            }
            else
            {
                _unitLevel = UnitLevel.Piece;
                _quantity = Math.Max(1, batch.RemainingSmallestUnits);
            }
        }

        public void Recalculate()
        {
            OnPropertyChanged(nameof(SmallestUnits));
            OnPropertyChanged(nameof(ExceedsAvailable));
            OnPropertyChanged(nameof(CostTotalPiastres));
            OnPropertyChanged(nameof(CreditPiastres));
            OnPropertyChanged(nameof(CostText));
            OnPropertyChanged(nameof(CreditText));
            OnPropertyChanged(nameof(ErrorText));
        }
    }

    /// <summary>نتيجة بحث عن فاتورة شراء أصلية</summary>
    public class PurchaseInvoiceSearchRow
    {
        public PurchaseInvoice Invoice { get; }
        public PurchaseInvoiceSearchRow(PurchaseInvoice invoice) { Invoice = invoice; }

        public string InvoiceNumber => Invoice.InvoiceNumber;
        public string SupplierName => Invoice.SupplierName;
        public string DateText => Invoice.InvoiceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        public string TotalText => Money.FromPiastres(Invoice.TotalPiastres).ToFormattedString();
    }

    /// <summary>بند من فاتورة الشراء الأصلية (المشترى / سبق إرجاعه / المتاح للإرجاع الآن)</summary>
    public class PurchaseReturnSourceRow
    {
        public ReturnablePurchaseLine Source { get; }
        public Item? Item { get; }

        public PurchaseReturnSourceRow(ReturnablePurchaseLine source, Item? item)
        {
            Source = source;
            Item = item;
        }

        private string Format(int units) =>
            Item != null ? UnitBreakdownHelper.FormatStockDetailed(units, Item.StripsPerBox, Item.PiecesPerStrip) : units.ToString();

        public string ItemName => Source.Line.ItemNameAr;
        public string BatchNumber => Source.Line.BatchNumber;
        public string ExpiryText => Source.Line.ExpiryDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        public string PurchasedText => Format(Source.Line.SmallestUnitsCount);
        public string ReturnedText => Format(Source.ReturnedUnits);
        public string ReturnableText => Format(Source.ReturnableUnits);
        public bool CanReturn => Source.ReturnableUnits > 0;

        public string NoteText =>
            !Source.HasBatch ? "فاتورة قديمة غير مربوطة بتشغيلة - استخدم الشطب"
            : Source.ReturnableUnits <= 0 ? "لا يوجد متاح للإرجاع"
            : string.Empty;
    }

    /// <summary>بند في مستند مرتجع مرتبط بفاتورة شراء: الكمية محدودة بالمتاح، والسعر من الفاتورة الأصلية</summary>
    public class PurchaseReturnLineRow : ObservableObject
    {
        public ReturnablePurchaseLine Source { get; }
        public Item? Item { get; }

        public string ItemName => Source.Line.ItemNameAr;
        public string BatchNumber => Source.Line.BatchNumber;
        public string AvailableText =>
            Item != null ? UnitBreakdownHelper.FormatStockDetailed(Source.ReturnableUnits, Item.StripsPerBox, Item.PiecesPerStrip) : Source.ReturnableUnits.ToString();

        private UnitLevel _unitLevel = UnitLevel.Piece;
        public UnitLevel UnitLevel
        {
            get => _unitLevel;
            set { if (SetProperty(ref _unitLevel, value)) Recalculate(); }
        }

        private int _quantity = 1;
        public int Quantity
        {
            get => _quantity;
            set
            {
                if (value < 1) value = 1;
                if (SetProperty(ref _quantity, value)) Recalculate();
            }
        }

        private int _deductionPercent;
        public int DeductionPercent
        {
            get => _deductionPercent;
            set
            {
                value = Math.Max(0, Math.Min(100, value));
                if (SetProperty(ref _deductionPercent, value)) Recalculate();
            }
        }

        private int PerBox => Item != null ? Math.Max(1, Item.SmallestUnitsPerBox) : 1;

        public int SmallestUnits => Item != null
            ? UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel, Quantity, Item.StripsPerBox, Item.PiecesPerStrip)
            : Quantity;

        public bool ExceedsReturnable => SmallestUnits > Source.ReturnableUnits;

        // للعرض فقط؛ المستودع هو المرجع الفعلي وبيحسب نفس القيمة من فاتورة الشراء
        public long CostTotalPiastres =>
            SupplierReturnRepository.ProportionalAmount(Source.Line.TotalPiastres, Source.ReturnedUnits + SmallestUnits, Source.Line.SmallestUnitsCount)
            - SupplierReturnRepository.ProportionalAmount(Source.Line.TotalPiastres, Source.ReturnedUnits, Source.Line.SmallestUnitsCount);
        public long CreditPiastres => SupplierReturnRepository.CalculateCredit(CostTotalPiastres, DeductionPercent);
        public string CostText => Money.FromPiastres(CostTotalPiastres).ToFormattedString();
        public string CreditText => Money.FromPiastres(CreditPiastres).ToFormattedString();
        public string ErrorText => ExceedsReturnable ? "أكبر من المتاح للإرجاع" : string.Empty;

        public PurchaseReturnLineRow(ReturnablePurchaseLine source, Item? item, int deductionPercent)
        {
            Source = source;
            Item = item;
            _deductionPercent = Math.Max(0, Math.Min(100, deductionPercent));

            // الافتراضي: كل المتاح للإرجاع، بالعلبة لو بيقبل القسمة وإلا بأصغر وحدة
            int available = Math.Max(1, source.ReturnableUnits);
            if (PerBox > 1 && available >= PerBox && available % PerBox == 0)
            {
                _unitLevel = UnitLevel.Box;
                _quantity = available / PerBox;
            }
            else
            {
                _unitLevel = UnitLevel.Piece;
                _quantity = available;
            }
        }

        public void Recalculate()
        {
            OnPropertyChanged(nameof(SmallestUnits));
            OnPropertyChanged(nameof(ExceedsReturnable));
            OnPropertyChanged(nameof(CostTotalPiastres));
            OnPropertyChanged(nameof(CreditPiastres));
            OnPropertyChanged(nameof(CostText));
            OnPropertyChanged(nameof(CreditText));
            OnPropertyChanged(nameof(ErrorText));
        }
    }

    public class SupplierReturnHistoryRow
    {
        public SupplierReturnHistoryRow(SupplierReturn r)
        {
            ReturnNumber = r.ReturnNumber;
            DateText = r.Date.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            KindText = r.Kind == SupplierReturnKind.Destroy ? "إتلاف" : "مرتجع للشركة";
            ReasonText = r.Reason;
            PartyText = r.Kind == SupplierReturnKind.Destroy ? "-" : r.SupplierName;
            CostText = Money.FromPiastres(r.TotalCostPiastres).ToFormattedString();
            CreditText = Money.FromPiastres(r.CreditPiastres).ToFormattedString();
            LossText = Money.FromPiastres(r.LossPiastres).ToFormattedString();
            UserName = r.UserName;
            PurchaseInvoiceText = string.IsNullOrEmpty(r.PurchaseInvoiceNumber) ? "-" : r.PurchaseInvoiceNumber;
        }

        public string PurchaseInvoiceText { get; }

        public string ReturnNumber { get; }
        public string DateText { get; }
        public string KindText { get; }
        public string ReasonText { get; }
        public string PartyText { get; }
        public string CostText { get; }
        public string CreditText { get; }
        public string LossText { get; }
        public string UserName { get; }
    }

    /// <summary>
    /// شاشة مرتجع الأدوية المنتهية/القريبة من الانتهاء للشركة (بنسبة خصم) أو إتلافها.
    /// الحفظ يخصم الكمية من التشغيلة ومن رصيد الصنف، ويخفض رصيد المورد بقيمة الاسترداد.
    /// </summary>
    public class SupplierReturnsViewModel : ViewModelBase
    {
        private readonly ItemRepository _itemRepository;
        private readonly SupplierRepository _supplierRepository;
        private readonly SupplierReturnRepository _returnRepository;
        private readonly Services.IUserSessionService? _sessionService;
        private readonly OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly StaffAuditLogRepository? _staffAuditLogRepository;

        public ObservableCollection<ExpiryCandidateRow> Candidates { get; } = new();
        public ObservableCollection<SupplierReturnLineRow> Lines { get; } = new();
        public ObservableCollection<Supplier> Suppliers { get; } = new();
        public ObservableCollection<SupplierReturnHistoryRow> History { get; } = new();

        private Supplier? _selectedSupplier;
        public Supplier? SelectedSupplier
        {
            get => _selectedSupplier;
            set => SetProperty(ref _selectedSupplier, value);
        }

        private int _withinDays = 90;
        /// <summary>عرض التشغيلات التي تنتهي خلال هذا العدد من الأيام (والمنتهية بالفعل)</summary>
        public int WithinDays
        {
            get => _withinDays;
            set => SetProperty(ref _withinDays, Math.Max(0, value));
        }

        private bool _isDestroy;
        /// <summary>إتلاف بدون مورد ولا استرداد (الخسارة = كامل التكلفة)</summary>
        public bool IsDestroy
        {
            get => _isDestroy;
            set
            {
                if (SetProperty(ref _isDestroy, value))
                {
                    if (value && IsFromInvoice) IsFromInvoice = false;
                    OnPropertyChanged(nameof(IsReturnToSupplier));
                    OnPropertyChanged(nameof(IsSupplierSelectable));
                    foreach (var l in Lines) l.DeductionPercent = value ? 100 : DeductionPercent;
                    RecalculateTotals();
                }
            }
        }

        public bool IsReturnToSupplier => !IsDestroy;

        private int _deductionPercent = 10;
        /// <summary>نسبة الخصم التي تخصمها الشركة من قيمة المرتجع (تُطبق على كل البنود ويمكن تعديلها لكل بند)</summary>
        public int DeductionPercent
        {
            get => _deductionPercent;
            set
            {
                value = Math.Max(0, Math.Min(100, value));
                if (SetProperty(ref _deductionPercent, value))
                {
                    foreach (var l in Lines) l.DeductionPercent = value;
                    foreach (var l in InvoiceReturnLines) l.DeductionPercent = value;
                    RecalculateTotals();
                }
            }
        }

        public string[] Reasons { get; } = { "انتهاء صلاحية", "قرب انتهاء الصلاحية", "تلف", "عيب تصنيع", "خطأ في التوريد", "أخرى" };

        // ===== وضع "مرتجع من فاتورة شراء" (عيب/خطأ في التوريد، مرتبط بالفاتورة الأصلية) =====
        public ObservableCollection<PurchaseInvoiceSearchRow> InvoiceSearchResults { get; } = new();
        public ObservableCollection<PurchaseReturnSourceRow> InvoiceSourceLines { get; } = new();
        public ObservableCollection<PurchaseReturnLineRow> InvoiceReturnLines { get; } = new();
        private ReturnablePurchase? _currentPurchase;

        private bool _isFromInvoice;
        /// <summary>مرتجع لصنف مُشترى فعلاً: بيرتبط بفاتورة الشراء، والكمية لا تتجاوز المشترى ناقص ما سبق إرجاعه، والسعر من الفاتورة</summary>
        public bool IsFromInvoice
        {
            get => _isFromInvoice;
            set
            {
                if (!SetProperty(ref _isFromInvoice, value)) return;

                if (value && IsDestroy) IsDestroy = false;
                Lines.Clear();
                InvoiceReturnLines.Clear();
                if (!value)
                {
                    _currentPurchase = null;
                    InvoiceSourceLines.Clear();
                    InvoiceSearchResults.Clear();
                    SelectedInvoiceResult = null;
                    InvoiceInfoText = string.Empty;
                    DeductionPercent = 10;
                    Reason = "انتهاء صلاحية";
                    LoadCandidates();
                }
                else
                {
                    // العيب/الخطأ في التوريد بيتسترد بالكامل عادةً؛ المستخدم يعدّل النسبة لو الشركة هتخصم
                    DeductionPercent = 0;
                    Reason = "عيب تصنيع";
                    StatusMessage = "ابحث برقم فاتورة الشراء أو اسم الشركة، واختار الفاتورة، وأضف الأصناف المراد إرجاعها.";
                }

                OnPropertyChanged(nameof(IsFromExpiry));
                OnPropertyChanged(nameof(IsSupplierSelectable));
                RecalculateTotals();
            }
        }

        public bool IsFromExpiry => !IsFromInvoice;
        public bool IsSupplierSelectable => !IsDestroy && !IsFromInvoice;

        private string _invoiceSearchText = string.Empty;
        public string InvoiceSearchText
        {
            get => _invoiceSearchText;
            set => SetProperty(ref _invoiceSearchText, value);
        }

        private PurchaseInvoiceSearchRow? _selectedInvoiceResult;
        public PurchaseInvoiceSearchRow? SelectedInvoiceResult
        {
            get => _selectedInvoiceResult;
            set
            {
                if (SetProperty(ref _selectedInvoiceResult, value) && value != null)
                    LoadInvoice(value.Invoice.Id);
            }
        }

        private string _invoiceInfoText = string.Empty;
        public string InvoiceInfoText
        {
            get => _invoiceInfoText;
            set => SetProperty(ref _invoiceInfoText, value);
        }

        /// <summary>
        /// نافذة تأكيد الحفظ (الافتراضي MessageBox بـ Yes/No، الافتراضي "لا")؛ قابلة للاستبدال في الاختبارات.
        /// </summary>
        public Func<string, string, bool> ConfirmAction { get; set; } = (message, title) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == MessageBoxResult.Yes;

        public IRelayCommand SearchInvoicesCommand { get; }
        public IRelayCommand<PurchaseReturnSourceRow> AddInvoiceLineCommand { get; }
        public IRelayCommand<PurchaseReturnLineRow> RemoveInvoiceLineCommand { get; }

        private string _reason = "انتهاء صلاحية";
        /// <summary>سبب المرتجع/الشطب (يُسجَّل في المستند)</summary>
        public string Reason
        {
            get => _reason;
            set => SetProperty(ref _reason, value);
        }

        private string _notes = string.Empty;
        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        private string _statusMessage = "اختار التشغيلات المنتهية أو القريبة من الانتهاء وأضفها للمستند.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private long _totalCostPiastres;
        private long _creditPiastres;
        public string TotalCostText => Money.FromPiastres(_totalCostPiastres).ToFormattedString();
        public string CreditText => Money.FromPiastres(IsDestroy ? 0 : _creditPiastres).ToFormattedString();
        public string LossText => Money.FromPiastres(IsDestroy ? _totalCostPiastres : _totalCostPiastres - _creditPiastres).ToFormattedString();

        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand<ExpiryCandidateRow> AddToReturnCommand { get; }
        public IRelayCommand AddAllExpiredCommand { get; }
        public IRelayCommand<SupplierReturnLineRow> RemoveLineCommand { get; }
        public IRelayCommand SaveCommand { get; }

        public SupplierReturnsViewModel(
            ItemRepository itemRepository,
            SupplierRepository supplierRepository,
            SupplierReturnRepository returnRepository,
            Services.IUserSessionService? sessionService = null,
            OwnerAuditLogRepository? ownerAuditLogRepository = null,
            StaffAuditLogRepository? staffAuditLogRepository = null)
        {
            Title = "مرتجع / إتلاف المنتهي";
            _itemRepository = itemRepository;
            _supplierRepository = supplierRepository;
            _returnRepository = returnRepository;
            _sessionService = sessionService;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;

            RefreshCommand = new RelayCommand(LoadData);
            AddToReturnCommand = new RelayCommand<ExpiryCandidateRow>(AddToReturn);
            AddAllExpiredCommand = new RelayCommand(AddAllExpired);
            RemoveLineCommand = new RelayCommand<SupplierReturnLineRow>(RemoveLine);
            SaveCommand = new RelayCommand(Save);
            SearchInvoicesCommand = new RelayCommand(SearchInvoices);
            AddInvoiceLineCommand = new RelayCommand<PurchaseReturnSourceRow>(AddInvoiceLine);
            RemoveInvoiceLineCommand = new RelayCommand<PurchaseReturnLineRow>(RemoveInvoiceLine);

            LoadData();
        }

        public override void OnNavigatedTo() => LoadData();

        public void LoadData()
        {
            try
            {
                Suppliers.Clear();
                foreach (var s in _supplierRepository.GetAll()) Suppliers.Add(s);
                if (SelectedSupplier != null)
                    SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == SelectedSupplier.Id);

                LoadCandidates();

                History.Clear();
                foreach (var r in _returnRepository.GetRecent(30)) History.Add(new SupplierReturnHistoryRow(r));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SupplierReturnsViewModel.LoadData");
                StatusMessage = $"تعذر تحميل البيانات: {ex.Message}";
            }
        }

        private void LoadCandidates()
        {
            Candidates.Clear();
            var inDocument = Lines.Select(l => l.Batch.Id).ToHashSet();
            foreach (var (item, batch) in _itemRepository.GetNearExpiryBatches(WithinDays))
            {
                if (inDocument.Contains(batch.Id)) continue;
                Candidates.Add(new ExpiryCandidateRow(item, batch));
            }
        }

        public void AddToReturn(ExpiryCandidateRow? row)
        {
            if (row == null) return;
            if (Lines.Any(l => l.Batch.Id == row.Batch.Id)) return;

            var line = new SupplierReturnLineRow(row.Item, row.Batch, IsDestroy ? 100 : DeductionPercent);
            line.PropertyChanged += (s, e) => RecalculateTotals();
            Lines.Add(line);
            Candidates.Remove(row);
            RecalculateTotals();
            StatusMessage = $"تمت إضافة {row.ItemName} (تشغيلة {row.BatchNumber}) للمستند.";
        }

        public void AddAllExpired()
        {
            foreach (var row in Candidates.Where(c => c.IsExpired).ToList())
                AddToReturn(row);
        }

        public void RemoveLine(SupplierReturnLineRow? line)
        {
            if (line == null || !Lines.Contains(line)) return;
            Lines.Remove(line);
            LoadCandidates();
            RecalculateTotals();
        }

        public void SearchInvoices()
        {
            try
            {
                InvoiceSearchResults.Clear();
                foreach (var inv in _returnRepository.SearchPurchases(InvoiceSearchText ?? string.Empty, 20))
                    InvoiceSearchResults.Add(new PurchaseInvoiceSearchRow(inv));

                StatusMessage = InvoiceSearchResults.Count == 0
                    ? "مفيش فواتير شراء مطابقة."
                    : $"لقيت {InvoiceSearchResults.Count} فاتورة. اختار الفاتورة الأصلية.";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SupplierReturnsViewModel.SearchInvoices");
                StatusMessage = $"تعذر البحث: {ex.Message}";
            }
        }

        private void LoadInvoice(int purchaseInvoiceId)
        {
            try
            {
                _currentPurchase = _returnRepository.GetReturnablePurchase(purchaseInvoiceId);
                InvoiceReturnLines.Clear();
                InvoiceSourceLines.Clear();
                if (_currentPurchase == null)
                {
                    InvoiceInfoText = string.Empty;
                    StatusMessage = "فاتورة الشراء غير موجودة.";
                    RecalculateTotals();
                    return;
                }

                foreach (var line in _currentPurchase.Lines)
                    InvoiceSourceLines.Add(new PurchaseReturnSourceRow(line, _itemRepository.GetById(line.Line.ItemId)));

                InvoiceInfoText = $"فاتورة {_currentPurchase.Invoice.InvoiceNumber} - {_currentPurchase.Invoice.SupplierName} - " +
                                  $"{_currentPurchase.Invoice.InvoiceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}";
                StatusMessage = "أضف الأصناف اللي هترجعها (الكمية المتاحة للإرجاع بتتحدد من المشترى وما سبق إرجاعه).";
                RecalculateTotals();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SupplierReturnsViewModel.LoadInvoice");
                StatusMessage = $"تعذر تحميل الفاتورة: {ex.Message}";
            }
        }

        public void AddInvoiceLine(PurchaseReturnSourceRow? row)
        {
            if (row == null) return;
            if (!row.CanReturn)
            {
                StatusMessage = row.NoteText;
                return;
            }
            if (InvoiceReturnLines.Any(l => l.Source.Line.Id == row.Source.Line.Id)) return;

            var line = new PurchaseReturnLineRow(row.Source, row.Item, DeductionPercent);
            line.PropertyChanged += (s, e) => RecalculateTotals();
            InvoiceReturnLines.Add(line);
            RecalculateTotals();
            StatusMessage = $"تمت إضافة {row.ItemName} (تشغيلة {row.BatchNumber}) للمستند.";
        }

        public void RemoveInvoiceLine(PurchaseReturnLineRow? line)
        {
            if (line == null || !InvoiceReturnLines.Contains(line)) return;
            InvoiceReturnLines.Remove(line);
            RecalculateTotals();
        }

        private void SaveFromInvoice()
        {
            if (_currentPurchase == null)
            {
                StatusMessage = "اختار فاتورة الشراء الأصلية الأول.";
                return;
            }
            if (InvoiceReturnLines.Count == 0)
            {
                StatusMessage = "أضف بند واحد على الأقل للمستند.";
                return;
            }
            if (string.IsNullOrWhiteSpace(Reason))
            {
                StatusMessage = "اختار سبب المرتجع (عيب أو خطأ في التوريد).";
                return;
            }

            var tooMuch = InvoiceReturnLines.FirstOrDefault(l => l.ExceedsReturnable);
            if (tooMuch != null)
            {
                StatusMessage = $"الكمية المطلوبة من {tooMuch.ItemName} (تشغيلة {tooMuch.BatchNumber}) أكبر من المتاح للإرجاع.";
                return;
            }

            int totalUnits = InvoiceReturnLines.Sum(l => l.SmallestUnits);
            if (!ConfirmAction(
                $"تأكيد مرتجع للشركة {_currentPurchase.Invoice.SupplierName} من فاتورة {_currentPurchase.Invoice.InvoiceNumber}\nعدد البنود: {InvoiceReturnLines.Count}\nالتكلفة: {TotalCostText}\nالاسترداد: {CreditText}\nالخسارة: {LossText}\n\nالكميات هتتخصم من المخزون ومش هينفع تتراجع. تكمّل؟",
                "تأكيد المستند")) return;

            var user = _sessionService?.CurrentUser;
            var doc = new SupplierReturn
            {
                Kind = SupplierReturnKind.ReturnToSupplier,
                PurchaseInvoiceId = _currentPurchase.Invoice.Id,
                SupplierId = _currentPurchase.Invoice.SupplierId,
                SupplierName = _currentPurchase.Invoice.SupplierName,
                Reason = Reason ?? string.Empty,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                UserId = user?.Id ?? 0,
                UserName = user?.FullName ?? string.Empty
            };

            foreach (var l in InvoiceReturnLines)
            {
                doc.Items.Add(new SupplierReturnItem
                {
                    PurchaseInvoiceItemId = l.Source.Line.Id,
                    SmallestUnitsCount = l.SmallestUnits,
                    DeductionPercent = l.DeductionPercent
                });
            }

            if (!_returnRepository.Save(doc, out string err))
            {
                StatusMessage = $"فشل حفظ المستند: {err}";
                return;
            }

            doc.PurchaseInvoiceNumber = _currentPurchase.Invoice.InvoiceNumber;
            LogAudit(doc, totalUnits);

            StatusMessage = $"تم حفظ مستند المرتجع {doc.ReturnNumber} من فاتورة {_currentPurchase.Invoice.InvoiceNumber}. الاسترداد: {Money.FromPiastres(doc.CreditPiastres).ToFormattedString()} (اتخصم من رصيد المورد)، الخسارة: {Money.FromPiastres(doc.LossPiastres).ToFormattedString()}";

            int invoiceId = _currentPurchase.Invoice.Id;
            Notes = string.Empty;
            InvoiceReturnLines.Clear();
            LoadInvoice(invoiceId);           // يحدّث المتاح للإرجاع بعد الخصم
            RecalculateTotals();
            History.Clear();
            foreach (var r in _returnRepository.GetRecent(30)) History.Add(new SupplierReturnHistoryRow(r));
        }

        private void RecalculateTotals()
        {
            if (IsFromInvoice)
            {
                _totalCostPiastres = InvoiceReturnLines.Sum(l => l.CostTotalPiastres);
                _creditPiastres = InvoiceReturnLines.Sum(l => l.CreditPiastres);
                OnPropertyChanged(nameof(TotalCostText));
                OnPropertyChanged(nameof(CreditText));
                OnPropertyChanged(nameof(LossText));
                return;
            }

            _totalCostPiastres = Lines.Sum(l => l.CostTotalPiastres);
            _creditPiastres = Lines.Sum(l => l.CreditPiastres);
            OnPropertyChanged(nameof(TotalCostText));
            OnPropertyChanged(nameof(CreditText));
            OnPropertyChanged(nameof(LossText));
        }

        public void Save()
        {
            if (IsFromInvoice)
            {
                SaveFromInvoice();
                return;
            }

            if (Lines.Count == 0)
            {
                StatusMessage = "أضف تشغيلة واحدة على الأقل للمستند.";
                return;
            }

            if (!IsDestroy && SelectedSupplier == null)
            {
                StatusMessage = "اختار الشركة (المورد) اللي هترجعلها الأدوية.";
                return;
            }

            var tooMuch = Lines.FirstOrDefault(l => l.ExceedsAvailable);
            if (tooMuch != null)
            {
                StatusMessage = $"الكمية المطلوبة من {tooMuch.ItemName} (تشغيلة {tooMuch.BatchNumber}) أكبر من المتاح بالتشغيلة.";
                return;
            }

            int totalUnits = Lines.Sum(l => l.SmallestUnits);
            string what = IsDestroy ? "إتلاف" : $"مرتجع للشركة {SelectedSupplier!.Name} بخصم {DeductionPercent}%";
            if (!ConfirmAction(
                $"تأكيد {what}\nعدد البنود: {Lines.Count}\nالتكلفة: {TotalCostText}\nالاسترداد: {CreditText}\nالخسارة: {LossText}\n\nالكميات هتتخصم من المخزون ومش هينفع تتراجع. تكمّل؟",
                "تأكيد المستند")) return;

            var user = _sessionService?.CurrentUser;
            var doc = new SupplierReturn
            {
                Kind = IsDestroy ? SupplierReturnKind.Destroy : SupplierReturnKind.ReturnToSupplier,
                SupplierId = IsDestroy ? null : SelectedSupplier?.Id,
                SupplierName = IsDestroy ? string.Empty : SelectedSupplier?.Name ?? string.Empty,
                Reason = Reason ?? string.Empty,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                UserId = user?.Id ?? 0,
                UserName = user?.FullName ?? string.Empty
            };

            foreach (var l in Lines)
            {
                doc.Items.Add(new SupplierReturnItem
                {
                    BatchId = l.Batch.Id,
                    SmallestUnitsCount = l.SmallestUnits,
                    DeductionPercent = IsDestroy ? 100 : l.DeductionPercent
                });
            }

            if (!_returnRepository.Save(doc, out string err))
            {
                StatusMessage = $"فشل حفظ المستند: {err}";
                return;
            }

            LogAudit(doc, totalUnits);

            StatusMessage = IsDestroy
                ? $"تم إتلاف الأصناف وحفظ المستند {doc.ReturnNumber}. الخسارة: {Money.FromPiastres(doc.LossPiastres).ToFormattedString()}"
                : $"تم حفظ مستند المرتجع {doc.ReturnNumber}. الاسترداد: {Money.FromPiastres(doc.CreditPiastres).ToFormattedString()} (اتخصم من رصيد المورد)، الخسارة: {Money.FromPiastres(doc.LossPiastres).ToFormattedString()}";

            Lines.Clear();
            Notes = string.Empty;
            RecalculateTotals();
            LoadData();
        }

        private void LogAudit(SupplierReturn doc, int totalUnits)
        {
            try
            {
                var user = _sessionService?.CurrentUser;
                if (user == null) return;

                string field = doc.Kind == SupplierReturnKind.Destroy ? "إتلاف أدوية منتهية"
                    : string.IsNullOrEmpty(doc.PurchaseInvoiceNumber) ? "مرتجع أدوية للشركة"
                    : $"مرتجع أدوية للشركة (فاتورة شراء {doc.PurchaseInvoiceNumber})";
                string newVal = $"{doc.Items.Count} بند / {totalUnits} وحدة / تكلفة {doc.TotalCostPiastres} / استرداد {doc.CreditPiastres}";

                if (user.Role == UserRole.Owner)
                    _ownerAuditLogRepository?.LogChange(user.Id, "SupplierReturn", doc.Id.ToString(), doc.ReturnNumber, field, "-", newVal);
                else
                    _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "SupplierReturn", doc.Id.ToString(), doc.ReturnNumber, field, "-", newVal);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SupplierReturnsViewModel.LogAudit");
            }
        }
    }
}
