using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    public class PurchaseItemRow : ObservableObject
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;
        public int StripsPerBox { get; set; } = 1;
        public int PiecesPerStrip { get; set; } = 10;

        private string _batchNumber = string.Empty;
        public string BatchNumber { get => _batchNumber; set => SetProperty(ref _batchNumber, value); }

        private DateTime _expiryDate = DateTime.Today.AddYears(2);
        public DateTime ExpiryDate { get => _expiryDate; set => SetProperty(ref _expiryDate, value); }

        private UnitLevel _unitLevel = UnitLevel.Box;
        public UnitLevel UnitLevel
        {
            get => _unitLevel;
            set
            {
                if (SetProperty(ref _unitLevel, value))
                {
                    Recalculate();
                }
            }
        }

        private int _quantityPurchased = 1;
        public int QuantityPurchased
        {
            get => _quantityPurchased;
            set
            {
                if (value < 1) value = 1;
                if (SetProperty(ref _quantityPurchased, value))
                {
                    Recalculate();
                }
            }
        }

        private decimal _unitCostRiyals;
        public decimal UnitCostRiyals
        {
            get => _unitCostRiyals;
            set
            {
                if (SetProperty(ref _unitCostRiyals, value))
                {
                    Recalculate();
                }
            }
        }

        public long UnitCostPiastres => Money.FromPounds(UnitCostRiyals).Piastres;

        /// <summary>
        /// نسبة الضريبة لهذا البند (0 = معفى). المستخدم بيحددها وقت إدخال فاتورة الشراء؛
        /// القيمة الابتدائية من إعداد الصنف نفسه (معفى / نسبة خاصة / الافتراضية).
        /// </summary>
        private int _vatPercent;
        public int VatPercent
        {
            get => _vatPercent;
            set
            {
                if (value < 0) value = 0;
                if (value > 100) value = 100;
                if (SetProperty(ref _vatPercent, value))
                {
                    Recalculate();
                }
            }
        }

        private long _vatPiastres;
        public long VatPiastres { get => _vatPiastres; private set => SetProperty(ref _vatPiastres, value); }

        private long _totalPiastres;
        public long TotalPiastres { get => _totalPiastres; private set => SetProperty(ref _totalPiastres, value); }

        public int SmallestUnitsCount => UnitBreakdownHelper.CalculateSmallestUnits(UnitLevel, QuantityPurchased, StripsPerBox, PiecesPerStrip);

        public string FormattedTotal => Money.FromPiastres(TotalPiastres).ToFormattedString();
        public string FormattedVat => Money.FromPiastres(VatPiastres).ToFormattedString();

        public PurchaseItemRow(Item item, int defaultVatPercent = 14)
        {
            ItemId = item.Id;
            ItemCode = item.Code;
            ItemNameAr = item.NameAr;
            StripsPerBox = item.StripsPerBox;
            PiecesPerStrip = item.PiecesPerStrip;
            BatchNumber = $"BN-{DbDate.Compact(DateTime.Now)}";
            ExpiryDate = DateTime.Today.AddYears(2);
            UnitCostRiyals = Money.FromPiastres(item.CostPriceBoxPiastres).ToPounds();
            _vatPercent = item.GetEffectiveVatRate(defaultVatPercent);
            Recalculate();
        }

        public void Recalculate()
        {
            long taxable = UnitCostPiastres * QuantityPurchased;
            VatPiastres = Money.CalculateVat(taxable, VatPercent).Piastres;
            TotalPiastres = taxable + VatPiastres;
            OnPropertyChanged(nameof(SmallestUnitsCount));
            OnPropertyChanged(nameof(FormattedTotal));
            OnPropertyChanged(nameof(FormattedVat));
        }
    }

    public class PurchasesViewModel : ViewModelBase
    {
        private readonly PurchaseRepository _purchaseRepository;
        private readonly SupplierRepository _supplierRepository;
        private readonly ItemRepository _itemRepository;
        private readonly SettingsRepository? _settingsRepository;
        private readonly Services.IPdfReportService? _pdfReportService;

        // فاتورة الشراء الجديدة
        private string _invoiceNumber = string.Empty;
        public string InvoiceNumber { get => _invoiceNumber; set => SetProperty(ref _invoiceNumber, value); }

        private DateTime _invoiceDate = DateTime.Today;
        public DateTime InvoiceDate { get => _invoiceDate; set => SetProperty(ref _invoiceDate, value); }

        private Supplier? _selectedSupplier;
        public Supplier? SelectedSupplier
        {
            get => _selectedSupplier;
            set => SetProperty(ref _selectedSupplier, value);
        }

        private string _invoiceNotes = string.Empty;
        public string InvoiceNotes { get => _invoiceNotes; set => SetProperty(ref _invoiceNotes, value); }

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

        public ObservableCollection<PurchaseItemRow> CartItems { get; } = new();
        public ObservableCollection<Supplier> SuppliersList { get; } = new();
        public ObservableCollection<PurchaseInvoice> InvoicesHistory { get; } = new();
        public ObservableCollection<Item> AvailableItems { get; } = new();
        public ObservableCollection<Item> SearchResults { get; } = new();

        // إدخال وبحث الأصناف في فاتورة المشتريات
        private string _itemSearchQuery = string.Empty;
        public string ItemSearchQuery
        {
            get => _itemSearchQuery;
            set
            {
                if (SetProperty(ref _itemSearchQuery, value))
                {
                    if (TryAutoBarcode(value)) return;
                    PerformItemSearch(value);
                }
            }
        }

        private Item? _selectedItem;
        public Item? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    OnSelectedItemChanged(value);
                }
            }
        }

        private UnitLevel _inputUnitLevel = UnitLevel.Box;
        public UnitLevel InputUnitLevel { get => _inputUnitLevel; set => SetProperty(ref _inputUnitLevel, value); }

        private int _inputQuantity = 1;
        public int InputQuantity { get => _inputQuantity; set => SetProperty(ref _inputQuantity, value < 1 ? 1 : value); }

        private decimal _inputCostPounds;
        public decimal InputCostPounds { get => _inputCostPounds; set => SetProperty(ref _inputCostPounds, value); }

        private string _inputBatchNumber = string.Empty;
        public string InputBatchNumber { get => _inputBatchNumber; set => SetProperty(ref _inputBatchNumber, value); }

        private DateTime _inputExpiryDate = DateTime.Today.AddYears(2);
        public DateTime InputExpiryDate { get => _inputExpiryDate; set => SetProperty(ref _inputExpiryDate, value); }

        private int _inputVatPercent = 14;
        public int InputVatPercent { get => _inputVatPercent; set => SetProperty(ref _inputVatPercent, value); }

        // بيانات إضافة مورد جديد
        private string _newSupplierName = string.Empty;
        public string NewSupplierName { get => _newSupplierName; set => SetProperty(ref _newSupplierName, value); }

        private string _newSupplierPhone = string.Empty;
        public string NewSupplierPhone { get => _newSupplierPhone; set => SetProperty(ref _newSupplierPhone, value); }

        private string _newSupplierTaxNo = string.Empty;
        public string NewSupplierTaxNo { get => _newSupplierTaxNo; set => SetProperty(ref _newSupplierTaxNo, value); }

        // سند صرف مورد
        private decimal _voucherAmountRiyals;
        public decimal VoucherAmountRiyals { get => _voucherAmountRiyals; set => SetProperty(ref _voucherAmountRiyals, value); }

        private string _voucherNotes = "سداد دفعة للمورد";
        public string VoucherNotes { get => _voucherNotes; set => SetProperty(ref _voucherNotes, value); }

        // مجاميع الفاتورة
        private long _subtotalPiastres;
        public long SubtotalPiastres { get => _subtotalPiastres; set => SetProperty(ref _subtotalPiastres, value); }

        private long _vatPiastres;
        public long VatPiastres { get => _vatPiastres; set => SetProperty(ref _vatPiastres, value); }

        private long _totalPiastres;
        public long TotalPiastres { get => _totalPiastres; set => SetProperty(ref _totalPiastres, value); }

        private long _remainingPiastres;
        public long RemainingPiastres { get => _remainingPiastres; set => SetProperty(ref _remainingPiastres, value); }

        public decimal SubtotalRiyals => Money.FromPiastres(SubtotalPiastres).ToPounds();
        public decimal VatRiyals => Money.FromPiastres(VatPiastres).ToPounds();
        public decimal TotalRiyals => Money.FromPiastres(TotalPiastres).ToPounds();
        public decimal RemainingRiyals => Money.FromPiastres(RemainingPiastres).ToPounds();

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _lastSavedInvoiceId;
        public int LastSavedInvoiceId { get => _lastSavedInvoiceId; set => SetProperty(ref _lastSavedInvoiceId, value); }

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

        // الأوامر
        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand NewInvoiceCommand { get; }
        public IRelayCommand SaveInvoiceCommand { get; }
        public IRelayCommand UndoLastInvoiceCommand { get; }
        public IRelayCommand<PurchaseItemRow> RemoveCartItemCommand { get; }
        public IRelayCommand RemoveSelectedItemCommand { get; }
        public IRelayCommand AddItemToCartCommand { get; }
        public IRelayCommand BarcodeEnterCommand { get; }
        public IRelayCommand AddSupplierCommand { get; }
        public IRelayCommand PaySupplierVoucherCommand { get; }
        public IRelayCommand OpenItemSearchCommand { get; }
        public IRelayCommand CloseSearchPopupCommand { get; }
        public IRelayCommand<Item> SelectSearchResultCommand { get; }
        public IRelayCommand PreviousPurchaseInvoiceCommand { get; }
        public IRelayCommand NextPurchaseInvoiceCommand { get; }
        public IRelayCommand OpenPurchaseHistoryCommand { get; }
        public IRelayCommand ClosePurchaseHistoryCommand { get; }
        public IRelayCommand<PurchaseInvoice> LoadSpecificPurchaseInvoiceCommand { get; }

        private PurchaseItemRow? _selectedCartItem;
        public PurchaseItemRow? SelectedCartItem
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

        private bool _isPurchaseHistoryOpen;
        public bool IsPurchaseHistoryOpen
        {
            get => _isPurchaseHistoryOpen;
            set => SetProperty(ref _isPurchaseHistoryOpen, value);
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

        public ObservableCollection<PurchaseInvoice> RecentPurchaseInvoices { get; } = new();

        private readonly Data.Repositories.OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly Data.Repositories.StaffAuditLogRepository? _staffAuditLogRepository;
        private readonly Services.IUserSessionService? _sessionService;
        private readonly Services.IBarcodeLabelService? _barcodeLabelService;

        public PurchasesViewModel(
            PurchaseRepository purchaseRepository,
            SupplierRepository supplierRepository,
            ItemRepository itemRepository,
            SettingsRepository? settingsRepository = null,
            Services.IPdfReportService? pdfReportService = null,
            Data.Repositories.OwnerAuditLogRepository? ownerAuditLogRepository = null,
            Data.Repositories.StaffAuditLogRepository? staffAuditLogRepository = null,
            Services.IUserSessionService? sessionService = null,
            Services.IBarcodeLabelService? barcodeLabelService = null)
        {
            Title = "المشتريات والموردون";
            _barcodeLabelService = barcodeLabelService;
            _purchaseRepository = purchaseRepository;
            _supplierRepository = supplierRepository;
            _itemRepository = itemRepository;
            _settingsRepository = settingsRepository;
            _pdfReportService = pdfReportService;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _sessionService = sessionService;

            RefreshCommand = new RelayCommand(LoadData);
            NewInvoiceCommand = new RelayCommand(StartNewInvoice);
            SaveInvoiceCommand = new RelayCommand(SaveInvoice);
            AddItemToCartCommand = new RelayCommand(AddItemFromInput);
            BarcodeEnterCommand = new RelayCommand(HandleBarcodeEnter);
            RemoveCartItemCommand = new RelayCommand<PurchaseItemRow>(row =>
            {
                if (row != null && CartItems.Contains(row))
                {
                    CartItems.Remove(row);
                    RecalculateTotals();
                }
            });
            RemoveSelectedItemCommand = new RelayCommand(RemoveSelectedItem);
            AddSupplierCommand = new RelayCommand(AddSupplier);
            PaySupplierVoucherCommand = new RelayCommand(PaySupplierVoucher);
            UndoLastInvoiceCommand = new RelayCommand(UndoLastInvoice);

            OpenItemSearchCommand = new RelayCommand(OpenItemSearch);
            CloseSearchPopupCommand = new RelayCommand(() => IsSearchPopupOpen = false);
            SelectSearchResultCommand = new RelayCommand<Item>(SelectSearchResult);
            PreviousPurchaseInvoiceCommand = new RelayCommand(LoadPreviousPurchaseInvoice);
            NextPurchaseInvoiceCommand = new RelayCommand(LoadNextPurchaseInvoice);
            OpenPurchaseHistoryCommand = new RelayCommand(OpenPurchaseHistory);
            ClosePurchaseHistoryCommand = new RelayCommand(() => IsPurchaseHistoryOpen = false);
            LoadSpecificPurchaseInvoiceCommand = new RelayCommand<PurchaseInvoice>(inv =>
            {
                if (inv != null)
                {
                    LoadPurchaseInvoice(inv);
                    IsPurchaseHistoryOpen = false;
                }
            });

            LoadData();
            StartNewInvoice();
        }

        public void UndoLastInvoice()
        {
            if (string.IsNullOrWhiteSpace(LastSavedInvoiceNumber) || LastSavedInvoiceId <= 0)
            {
                var latest = _purchaseRepository.GetLatestPurchaseInvoice();
                if (latest != null)
                {
                    LastSavedInvoiceId = latest.Id;
                    LastSavedInvoiceNumber = latest.InvoiceNumber;
                }
            }

            if (LastSavedInvoiceId <= 0)
            {
                StatusMessage = "لا توجد فاتورة مشتريات حديثة للتراجع عنها.";
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                $"هل أنت متأكد من التراجع عن فاتورة المشتريات رقم '{LastSavedInvoiceNumber}'؟\nسيتم خصم الكميات من المخزون وعكس رصيد المورد وقيود اليومية.",
                "تأكيد التراجع عن فاتورة المشتريات",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No,
                System.Windows.MessageBoxOptions.RtlReading | System.Windows.MessageBoxOptions.RightAlign);

            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            if (_purchaseRepository.CancelPurchaseInvoice(LastSavedInvoiceId, "تراجع من شاشة المشتريات", out string err))
            {
                StatusMessage = $"تم بنجاح التراجع عن فاتورة المشتريات '{LastSavedInvoiceNumber}' وإلغاؤها واسترجاع أرصدة المخزون.";
                LastSavedInvoiceNumber = string.Empty;
                LastSavedInvoiceId = 0;
                LoadData();
            }
            else
            {
                StatusMessage = $"تعذر التراجع عن فاتورة المشتريات: {err}";
            }
        }

        public void LoadData()
        {
            SuppliersList.Clear();
            var suppliers = _supplierRepository.GetAll();
            foreach (var s in suppliers) SuppliersList.Add(s);

            InvoicesHistory.Clear();
            var history = _purchaseRepository.GetAllPurchases(50);
            foreach (var inv in history) InvoicesHistory.Add(inv);

            AvailableItems.Clear();
            try
            {
                var allItems = _itemRepository.GetAllItems();
                foreach (var itm in allItems) AvailableItems.Add(itm);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PurchasesViewModel.LoadData.Items");
            }

            StatusMessage = "تم تحديث بيانات المشتريات والموردين.";
        }

        public void StartNewInvoice()
        {
            InvoiceNumber = _purchaseRepository.GenerateNextPurchaseInvoiceNumber();
            InvoiceDate = DateTime.Today;
            CartItems.Clear();
            PaidRiyals = 0;
            InvoiceNotes = string.Empty;
            ItemSearchQuery = string.Empty;
            SearchResults.Clear();
            SelectedItem = null;
            InputBatchNumber = $"BN-{DbDate.Compact(DateTime.Now)}";
            InputExpiryDate = DateTime.Today.AddYears(2);
            InputQuantity = 1;
            InputCostPounds = 0;
            InputVatPercent = GetDefaultVat();
            IsViewingHistoricalInvoice = false;
            HistoricalInvoiceStatusText = string.Empty;
            IsSearchPopupOpen = false;
            IsPurchaseHistoryOpen = false;
            SelectedCartItem = null;

            RecalculateTotals();
        }

        private void OnSelectedItemChanged(Item? item)
        {
            if (item == null) return;
            InputCostPounds = Money.FromPiastres(item.CostPriceBoxPiastres).ToPounds();
            InputBatchNumber = $"BN-{DbDate.Compact(DateTime.Now)}";
            InputExpiryDate = DateTime.Today.AddYears(2);
            InputVatPercent = item.GetEffectiveVatRate(GetDefaultVat());
            InputQuantity = 1;
            InputUnitLevel = UnitLevel.Box;
        }

        private void PerformItemSearch(string query)
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
                Logger.LogException(ex, "PurchasesViewModel.PerformItemSearch");
            }
        }

        public void OpenItemSearch()
        {
            SearchResults.Clear();
            string q = (ItemSearchQuery ?? string.Empty).Trim();
            try
            {
                var list = string.IsNullOrEmpty(q)
                    ? _itemRepository.GetAllItems().Take(30).ToList()
                    : _itemRepository.Search(q, 30);
                foreach (var itm in list) SearchResults.Add(itm);
                IsSearchPopupOpen = SearchResults.Count > 0;
                if (SearchResults.Count == 0)
                {
                    StatusMessage = "لم يتم العثور على أي صنف مطابق للبحث";
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PurchasesViewModel.OpenItemSearch");
            }
        }

        public void SelectSearchResult(Item? item)
        {
            if (item == null) return;
            SelectedItem = item;
            AddItemFromInput();
            ItemSearchQuery = string.Empty;
            SearchResults.Clear();
            IsSearchPopupOpen = false;
        }

        public void RemoveSelectedItem()
        {
            if (SelectedCartItem != null && CartItems.Contains(SelectedCartItem))
            {
                CartItems.Remove(SelectedCartItem);
                SelectedCartItem = null;
                RecalculateTotals();
            }
            else if (CartItems.Count > 0)
            {
                CartItems.RemoveAt(CartItems.Count - 1);
                RecalculateTotals();
            }
        }

        public void LoadPurchaseInvoice(PurchaseInvoice invoice)
        {
            if (invoice == null) return;
            var fullInv = _purchaseRepository.GetFullPurchaseInvoice(invoice.InvoiceNumber) ?? invoice;
            InvoiceNumber = fullInv.InvoiceNumber;
            SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == fullInv.SupplierId);
            InvoiceDate = fullInv.InvoiceDate;
            InvoiceNotes = fullInv.Notes ?? string.Empty;

            CartItems.Clear();
            foreach (var line in fullInv.Items)
            {
                var baseItem = _itemRepository.GetById(line.ItemId) ?? new Item
                {
                    Id = line.ItemId,
                    Code = line.ItemCode,
                    NameAr = line.ItemNameAr,
                    CostPriceBoxPiastres = line.UnitCostPiastres
                };
                int vat = line.VatPercent ?? 0;
                var row = new PurchaseItemRow(baseItem, vat)
                {
                    UnitLevel = line.UnitLevel,
                    QuantityPurchased = line.QuantityPurchased,
                    UnitCostRiyals = Money.FromPiastres(line.UnitCostPiastres).ToPounds(),
                    BatchNumber = line.BatchNumber,
                    ExpiryDate = line.ExpiryDate,
                    VatPercent = vat
                };
                CartItems.Add(row);
            }

            PaidRiyals = Money.FromPiastres(fullInv.PaidPiastres).ToPounds();
            RecalculateTotals();
            IsViewingHistoricalInvoice = true;
            HistoricalInvoiceStatusText = $"فاتورة مشتريات سابقة رقم [{fullInv.InvoiceNumber}] | التاريخ: {DbDate.Display(fullInv.InvoiceDate)} | المورد: {fullInv.SupplierName} | الإجمالي: {Money.FromPiastres(fullInv.TotalPiastres).ToPounds():N2} ج.م";
            StatusMessage = $"تم استعراض فاتورة المشتريات رقم {fullInv.InvoiceNumber}";
        }

        public void LoadPreviousPurchaseInvoice()
        {
            string? prevNum = _purchaseRepository.GetPreviousPurchaseInvoiceNumber(InvoiceNumber);
            if (!string.IsNullOrEmpty(prevNum))
            {
                var inv = _purchaseRepository.GetFullPurchaseInvoice(prevNum);
                if (inv != null)
                {
                    LoadPurchaseInvoice(inv);
                    return;
                }
            }
            StatusMessage = "لا توجد فواتير مشتريات سابقة قبل هذه الفاتورة";
        }

        public void LoadNextPurchaseInvoice()
        {
            string? nextNum = _purchaseRepository.GetNextPurchaseInvoiceNumber(InvoiceNumber);
            if (!string.IsNullOrEmpty(nextNum))
            {
                var inv = _purchaseRepository.GetFullPurchaseInvoice(nextNum);
                if (inv != null)
                {
                    LoadPurchaseInvoice(inv);
                    return;
                }
            }
            StatusMessage = "أنت في أحدث فاتورة مشتريات مسجلة بالفعل";
        }

        public void OpenPurchaseHistory()
        {
            RecentPurchaseInvoices.Clear();
            try
            {
                var list = _purchaseRepository.GetRecentPurchaseInvoices(40);
                foreach (var inv in list) RecentPurchaseInvoices.Add(inv);
                IsPurchaseHistoryOpen = true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PurchasesViewModel.OpenPurchaseHistory");
            }
        }

        public void AddItemFromInput()
        {
            Item? targetItem = SelectedItem;
            if (targetItem == null && !string.IsNullOrWhiteSpace(ItemSearchQuery))
            {
                string code = ItemSearchQuery.Trim();
                targetItem = _itemRepository.GetByBarcodeOrCode(code);
                if (targetItem == null)
                {
                    var search = _itemRepository.Search(code, 1);
                    if (search.Count > 0) targetItem = search[0];
                }
            }

            if (targetItem == null)
            {
                StatusMessage = "يرجى اختيار الصنف أو مسح باركوده أولاً!";
                return;
            }

            decimal cost = InputCostPounds > 0 ? InputCostPounds : Money.FromPiastres(targetItem.CostPriceBoxPiastres).ToPounds();
            string batch = string.IsNullOrWhiteSpace(InputBatchNumber) ? $"BN-{DbDate.Compact(DateTime.Now)}" : InputBatchNumber.Trim();
            int vat = InputVatPercent >= 0 ? InputVatPercent : targetItem.GetEffectiveVatRate(GetDefaultVat());

            var row = new PurchaseItemRow(targetItem, vat)
            {
                UnitLevel = InputUnitLevel,
                QuantityPurchased = Math.Max(1, InputQuantity),
                UnitCostRiyals = cost,
                BatchNumber = batch,
                ExpiryDate = InputExpiryDate,
                VatPercent = vat
            };
            row.Recalculate();
            row.PropertyChanged += (s, e) => RecalculateTotals();

            CartItems.Add(row);
            RecalculateTotals();

            StatusMessage = $"تمت إضافة الصنف '{targetItem.NameAr}' إلى فاتورة الشراء.";
            ItemSearchQuery = string.Empty;
            SearchResults.Clear();
            SelectedItem = null;
            InputBatchNumber = $"BN-{DbDate.Compact(DateTime.Now)}";
            InputQuantity = 1;
        }

        public void HandleBarcodeEnter()
        {
            string code = ItemSearchQuery.Trim();
            if (string.IsNullOrEmpty(code)) return;

            var itm = _itemRepository.GetByBarcodeOrCode(code);
            if (itm != null)
            {
                SelectedItem = itm;
                AddItemFromInput();
            }
            else
            {
                StatusMessage = $"لم يتم العثور على صنف بالباركود: {code}";
            }
        }

        private bool TryAutoBarcode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string code = text.Trim();
            if (code.Length >= 5)
            {
                var item = _itemRepository.GetByBarcodeOrCode(code);
                if (item != null && (string.Equals(item.Barcode?.Trim(), code, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(item.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectedItem = item;
                    AddItemFromInput();
                    return true;
                }
            }
            return false;
        }

        /// <summary>نسبة الضريبة الافتراضية للصيدلية من الإعدادات (14 لو الإعدادات مش متاحة)</summary>
        private int GetDefaultVat()
        {
            try
            {
                return _settingsRepository?.GetSettings().DefaultVatPercent ?? 14;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PurchasesViewModel.GetDefaultVat");
                return 14;
            }
        }

        public void AddItemToCart(Item item)
        {
            var row = new PurchaseItemRow(item, GetDefaultVat());
            row.PropertyChanged += (s, e) => RecalculateTotals();
            CartItems.Add(row);
            RecalculateTotals();
        }

        public void RecalculateTotals()
        {
            long sub = 0;
            long vat = 0;
            long tot = 0;

            foreach (var row in CartItems)
            {
                sub += row.UnitCostPiastres * row.QuantityPurchased;
                vat += row.VatPiastres;
                tot += row.TotalPiastres;
            }

            SubtotalPiastres = sub;
            VatPiastres = vat;
            TotalPiastres = tot;

            long paidPiastres = Money.FromPounds(PaidRiyals).Piastres;
            RemainingPiastres = Math.Max(0, TotalPiastres - paidPiastres);

            OnPropertyChanged(nameof(SubtotalRiyals));
            OnPropertyChanged(nameof(VatRiyals));
            OnPropertyChanged(nameof(TotalRiyals));
            OnPropertyChanged(nameof(RemainingRiyals));
        }

        public void SaveInvoice()
        {
            if (SelectedSupplier == null)
            {
                StatusMessage = "يرجى تحديد المورد أولاً قبل حفظ الفاتورة!";
                return;
            }

            if (CartItems.Count == 0)
            {
                StatusMessage = "لا يمكن حفظ فاتورة مشتريات خالية من الأصناف!";
                return;
            }

            var inv = new PurchaseInvoice
            {
                InvoiceNumber = InvoiceNumber,
                SupplierId = SelectedSupplier.Id,
                SupplierName = SelectedSupplier.Name,
                InvoiceDate = InvoiceDate,
                SubtotalPiastres = SubtotalPiastres,
                VatPiastres = VatPiastres,
                TotalPiastres = TotalPiastres,
                PaidPiastres = Money.FromPounds(PaidRiyals).Piastres,
                RemainingPiastres = RemainingPiastres,
                Notes = InvoiceNotes
            };

            foreach (var row in CartItems)
            {
                inv.Items.Add(new PurchaseInvoiceItem
                {
                    ItemId = row.ItemId,
                    ItemCode = row.ItemCode,
                    ItemNameAr = row.ItemNameAr,
                    BatchNumber = string.IsNullOrWhiteSpace(row.BatchNumber) ? $"BN-{DbDate.Compact(DateTime.Now)}" : row.BatchNumber,
                    ExpiryDate = row.ExpiryDate,
                    UnitLevel = row.UnitLevel,
                    QuantityPurchased = row.QuantityPurchased,
                    SmallestUnitsCount = row.SmallestUnitsCount,
                    UnitCostPiastres = row.UnitCostPiastres,
                    VatPiastres = row.VatPiastres,
                    VatPercent = row.VatPercent,
                    TotalPiastres = row.TotalPiastres
                });
            }

            if (_purchaseRepository.SavePurchaseInvoice(inv, out string err))
            {
                LastSavedInvoiceId = inv.Id;
                LastSavedInvoiceNumber = inv.InvoiceNumber;
                string saved = $"تم حفظ فاتورة الشراء '{inv.InvoiceNumber}' بنجاح وزيادة أرصدة المخزون (يمكنك التراجع عنها بالضغط على زر التراجع).";
                LoadData();
                StartNewInvoice();
                StatusMessage = saved;

                // ملصقات الصلاحية: ملصق لكل علبة بباركود يعرّف التشغيلة
                try
                {
                    var labelLines = new System.Collections.Generic.List<(Item Item, ItemBatch Batch, int Units)>();
                    foreach (var line in inv.Items)
                    {
                        var itm = _itemRepository.GetById(line.ItemId);
                        var batch = line.BatchId > 0 ? _itemRepository.GetBatchById(line.BatchId) : null;
                        if (itm != null && batch != null)
                            labelLines.Add((itm, batch, line.SmallestUnitsCount));
                    }
                    string? printMsg = Services.BatchLabelPrintHelper.OfferAndPrint(_barcodeLabelService, labelLines);
                    if (printMsg != null) StatusMessage = $"{saved} {printMsg}";
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "PurchasesViewModel.PrintBatchLabels");
                }
            }
            else
            {
                StatusMessage = $"فشل حفظ فاتورة المشتريات: {err}";
            }
        }

        public void AddSupplier()
        {
            if (string.IsNullOrWhiteSpace(NewSupplierName))
            {
                StatusMessage = "يرجى كتابة اسم المورد!";
                return;
            }

            var s = new Supplier
            {
                Name = NewSupplierName,
                Phone = NewSupplierPhone,
                TaxNumber = NewSupplierTaxNo,
                BalancePiastres = 0,
                IsActive = true
            };

            _supplierRepository.SaveSupplier(s);
            StatusMessage = $"تمت إضافة المورد '{s.Name}' بنجاح.";
            NewSupplierName = string.Empty;
            NewSupplierPhone = string.Empty;
            NewSupplierTaxNo = string.Empty;
            LoadData();
        }

        public void PaySupplierVoucher()
        {
            if (SelectedSupplier == null)
            {
                StatusMessage = "يرجى اختيار مورد لإصدار سند الصرف له!";
                return;
            }

            if (VoucherAmountRiyals <= 0)
            {
                StatusMessage = "يرجى إدخال مبلغ صحيح لسند الصرف!";
                return;
            }

            long amtPiastres = Money.FromPounds(VoucherAmountRiyals).Piastres;
            string vNum = _purchaseRepository.GenerateNextSupplierVoucherNumber();

            var voucher = new SupplierVoucher
            {
                VoucherNumber = vNum,
                SupplierId = SelectedSupplier.Id,
                SupplierName = SelectedSupplier.Name,
                Date = DateTime.Now,
                AmountPiastres = amtPiastres,
                PaymentMethod = PaymentMethod.Cash,
                Notes = VoucherNotes
            };

            if (_purchaseRepository.AddSupplierVoucher(voucher))
            {
                LogSupplierVoucherAudit(SelectedSupplier, amtPiastres, vNum);
                StatusMessage = $"تم تسجيل سند الصرف رقم {vNum} بمبلغ {VoucherAmountRiyals:N2} ج.م وخصمه من حساب المورد.";

                if (_pdfReportService != null && _settingsRepository != null)
                {
                    try
                    {
                        var settings = _settingsRepository.GetSettings();
                        string pdfPath = _pdfReportService.GenerateVoucherPdf(vNum, DateTime.Now, SelectedSupplier.Name, amtPiastres, "سند صرف لمورد", VoucherNotes, settings);
                        StatusMessage += $" (تم إنشاء السند PDF: {System.IO.Path.GetFileName(pdfPath)})";
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "PurchasesViewModel (إنشاء سند الصرف PDF)");
                        StatusMessage += " (تعذّر إنشاء ملف PDF للسند)";
                    }
                }

                VoucherAmountRiyals = 0;
                LoadData();
            }
            else
            {
                StatusMessage = "فشل تسجيل سند الصرف.";
            }
        }
        /// <summary>
        /// تسجيل سند صرف لمورد (تسوية رصيد مورد): لصاحب الصيدلية في سجله الخاص المخفي عن الجميع،
        /// ولأي دور آخر (مدير/صيدلي/كاشير) في سجل الموظفين العام المرئي لـ Admin وOwner فقط.
        /// </summary>
        private void LogSupplierVoucherAudit(Supplier? supplier, long amountPiastres, string voucherNumber)
        {
            var user = _sessionService?.CurrentUser;
            if (user == null || supplier == null) return;

            string field = $"سند صرف رقم {voucherNumber} (تسوية رصيد مورد)";
            string oldVal = supplier.BalancePiastres.ToString();
            string newVal = (supplier.BalancePiastres - amountPiastres).ToString();

            if (user.Role == UserRole.Owner)
            {
                _ownerAuditLogRepository?.LogChange(user.Id, "Supplier", supplier.Id.ToString(), supplier.Name, field, oldVal, newVal);
            }
            else
            {
                _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "Supplier", supplier.Id.ToString(), supplier.Name, field, oldVal, newVal);
            }
        }
    }
}