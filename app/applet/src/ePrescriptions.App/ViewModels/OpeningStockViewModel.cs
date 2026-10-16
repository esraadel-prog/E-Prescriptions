using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.App.Services;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.ViewModels
{
    /// <summary>
    /// نموذج عرض شاشة رصيد أول المدة (بضاعة أول المدة):
    /// يتيح للصيدلي إدخال جميع الأدوية والبضائع المتوفرة بالصيدلية مع كمياتها،
    /// أسعارها، تشغيلاتها وصلاحياتها قبل بدء التشغيل الفعلي.
    /// يدعم الإدخال السريع بالباركود، البحث بـ F4، الاستيراد والتصدير من/إلى Excel،
    /// والاعتماد المحاسبي مع سجل الحركات.
    /// </summary>
    public class OpeningStockViewModel : ViewModelBase
    {
        private readonly OpeningStockRepository _openingStockRepository;
        private readonly ItemRepository _itemRepository;
        private readonly IExcelExportService? _excelExportService;
        private readonly IUserSessionService? _sessionService;
        private readonly SettingsRepository? _settingsRepository;

        // ===== بيانات رأس السند =====
        private string _voucherNumber = string.Empty;
        public string VoucherNumber
        {
            get => _voucherNumber;
            set => SetProperty(ref _voucherNumber, value);
        }

        private DateTime _voucherDate = DateTime.Today;
        public DateTime VoucherDate
        {
            get => _voucherDate;
            set => SetProperty(ref _voucherDate, value);
        }

        private string _notes = "حصر رصيد أول المدة قبل بدء التشغيل الفعلي";
        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        // ===== حقول الإدخال السريع بالباركود =====
        private string _scanOrSearchText = string.Empty;
        public string ScanOrSearchText
        {
            get => _scanOrSearchText;
            set => SetProperty(ref _scanOrSearchText, value);
        }

        private int _entryItemId;
        public int EntryItemId
        {
            get => _entryItemId;
            set => SetProperty(ref _entryItemId, value);
        }

        private string _entryItemCode = string.Empty;
        public string EntryItemCode
        {
            get => _entryItemCode;
            set => SetProperty(ref _entryItemCode, value);
        }

        private string _entryItemBarcode = string.Empty;
        public string EntryItemBarcode
        {
            get => _entryItemBarcode;
            set => SetProperty(ref _entryItemBarcode, value);
        }

        private string _entryItemNameAr = string.Empty;
        public string EntryItemNameAr
        {
            get => _entryItemNameAr;
            set => SetProperty(ref _entryItemNameAr, value);
        }

        private string _entryItemNameEn = string.Empty;
        public string EntryItemNameEn
        {
            get => _entryItemNameEn;
            set => SetProperty(ref _entryItemNameEn, value);
        }

        private string _entryActiveIngredient = string.Empty;
        public string EntryActiveIngredient
        {
            get => _entryActiveIngredient;
            set => SetProperty(ref _entryActiveIngredient, value);
        }

        private int _entryStripsPerBox = 1;
        public int EntryStripsPerBox
        {
            get => _entryStripsPerBox;
            set
            {
                if (SetProperty(ref _entryStripsPerBox, Math.Max(1, value)))
                    UpdateUnitsPreview();
            }
        }

        private int _entryPiecesPerStrip = 10;
        public int EntryPiecesPerStrip
        {
            get => _entryPiecesPerStrip;
            set
            {
                if (SetProperty(ref _entryPiecesPerStrip, Math.Max(1, value)))
                    UpdateUnitsPreview();
            }
        }

        private string _entryBatchNumber = string.Empty;
        public string EntryBatchNumber
        {
            get => _entryBatchNumber;
            set => SetProperty(ref _entryBatchNumber, value);
        }

        private DateTime _entryExpiryDate = DateTime.Today.AddYears(2);
        public DateTime EntryExpiryDate
        {
            get => _entryExpiryDate;
            set => SetProperty(ref _entryExpiryDate, value);
        }

        private int _entryQuantityBoxes = 1;
        public int EntryQuantityBoxes
        {
            get => _entryQuantityBoxes;
            set
            {
                if (SetProperty(ref _entryQuantityBoxes, Math.Max(0, value)))
                    UpdateUnitsPreview();
            }
        }

        private int _entryQuantityStrips;
        public int EntryQuantityStrips
        {
            get => _entryQuantityStrips;
            set
            {
                if (SetProperty(ref _entryQuantityStrips, Math.Max(0, value)))
                    UpdateUnitsPreview();
            }
        }

        private int _entryQuantityUnits;
        public int EntryQuantityUnits
        {
            get => _entryQuantityUnits;
            set
            {
                if (SetProperty(ref _entryQuantityUnits, Math.Max(0, value)))
                    UpdateUnitsPreview();
            }
        }

        private decimal _entryCostPricePounds;
        public decimal EntryCostPricePounds
        {
            get => _entryCostPricePounds;
            set => SetProperty(ref _entryCostPricePounds, Math.Max(0m, value));
        }

        private decimal _entryRetailPricePounds;
        public decimal EntryRetailPricePounds
        {
            get => _entryRetailPricePounds;
            set => SetProperty(ref _entryRetailPricePounds, Math.Max(0m, value));
        }

        private string _entryTotalUnitsPreview = "10 قرص / وحدة";
        public string EntryTotalUnitsPreview
        {
            get => _entryTotalUnitsPreview;
            set => SetProperty(ref _entryTotalUnitsPreview, value);
        }

        private bool _isNewItemForm;
        public bool IsNewItemForm
        {
            get => _isNewItemForm;
            set => SetProperty(ref _isNewItemForm, value);
        }

        // ===== جدول بنود السند =====
        public ObservableCollection<OpeningStockVoucherLine> Lines { get; } = new();

        private OpeningStockVoucherLine? _selectedLine;
        public OpeningStockVoucherLine? SelectedLine
        {
            get => _selectedLine;
            set => SetProperty(ref _selectedLine, value);
        }

        // ===== بطاقات الإجماليات والمؤشرات (KPIs) =====
        private int _totalItemsCount;
        public int TotalItemsCount
        {
            get => _totalItemsCount;
            private set => SetProperty(ref _totalItemsCount, value);
        }

        private int _totalUnitsCount;
        public int TotalUnitsCount
        {
            get => _totalUnitsCount;
            private set => SetProperty(ref _totalUnitsCount, value);
        }

        private decimal _totalCostPounds;
        public decimal TotalCostPounds
        {
            get => _totalCostPounds;
            private set => SetProperty(ref _totalCostPounds, value);
        }

        private decimal _totalRetailPounds;
        public decimal TotalRetailPounds
        {
            get => _totalRetailPounds;
            private set => SetProperty(ref _totalRetailPounds, value);
        }

        private decimal _totalProfitPounds;
        public decimal TotalProfitPounds
        {
            get => _totalProfitPounds;
            private set => SetProperty(ref _totalProfitPounds, value);
        }

        private decimal _profitMarginPercent;
        public decimal ProfitMarginPercent
        {
            get => _profitMarginPercent;
            private set => SetProperty(ref _profitMarginPercent, value);
        }

        public string FormattedTotalCost => $"{TotalCostPounds:N2} ج.م";
        public string FormattedTotalRetail => $"{TotalRetailPounds:N2} ج.م";
        public string FormattedTotalProfit => $"{TotalProfitPounds:N2} ج.م";

        // ===== نافذة البحث المنسدلة (F4) =====
        private bool _isSearchPopupOpen;
        public bool IsSearchPopupOpen
        {
            get => _isSearchPopupOpen;
            set => SetProperty(ref _isSearchPopupOpen, value);
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                    PerformSearch(value);
            }
        }

        public ObservableCollection<Item> SearchResults { get; } = new();

        private Item? _selectedSearchResult;
        public Item? SelectedSearchResult
        {
            get => _selectedSearchResult;
            set => SetProperty(ref _selectedSearchResult, value);
        }

        // ===== سجل السندات السابقة =====
        private bool _isHistoryOpen;
        public bool IsHistoryOpen
        {
            get => _isHistoryOpen;
            set => SetProperty(ref _isHistoryOpen, value);
        }

        public ObservableCollection<OpeningStockVoucher> PastVouchers { get; } = new();

        private OpeningStockVoucher? _selectedPastVoucher;
        public OpeningStockVoucher? SelectedPastVoucher
        {
            get => _selectedPastVoucher;
            set => SetProperty(ref _selectedPastVoucher, value);
        }

        // ===== الأوامر =====
        public IRelayCommand HandleScanOrSearchCommand { get; }
        public IRelayCommand AddCurrentLineCommand { get; }
        public IRelayCommand RemoveSelectedLineCommand { get; }
        public IRelayCommand ClearAllLinesCommand { get; }
        public IAsyncRelayCommand SaveAndPostCommand { get; }
        public IAsyncRelayCommand ExportExcelCommand { get; }
        public IRelayCommand ExportTemplateCommand { get; }
        public IRelayCommand<string> ImportExcelFromPathCommand { get; }
        public IRelayCommand OpenItemSearchCommand { get; }
        public IRelayCommand SelectSearchResultCommand { get; }
        public IRelayCommand CloseSearchPopupCommand { get; }
        public IRelayCommand ToggleHistoryCommand { get; }
        public IRelayCommand LoadSelectedPastVoucherCommand { get; }

        public OpeningStockViewModel(
            OpeningStockRepository openingStockRepository,
            ItemRepository itemRepository,
            IExcelExportService? excelExportService = null,
            IUserSessionService? sessionService = null,
            SettingsRepository? settingsRepository = null)
        {
            Title = "رصيد أول المدة (بضاعة أول المدة)";
            _openingStockRepository = openingStockRepository;
            _itemRepository = itemRepository;
            _excelExportService = excelExportService;
            _sessionService = sessionService;
            _settingsRepository = settingsRepository;

            HandleScanOrSearchCommand = new RelayCommand(HandleScanOrSearch);
            AddCurrentLineCommand = new RelayCommand(AddCurrentLine);
            RemoveSelectedLineCommand = new RelayCommand(RemoveSelectedLine, () => SelectedLine != null);
            ClearAllLinesCommand = new RelayCommand(ClearAllLines);
            SaveAndPostCommand = new AsyncRelayCommand(SaveAndPostAsync, () => Lines.Count > 0 && !IsBusy);
            ExportExcelCommand = new AsyncRelayCommand(ExportExcelAsync, () => Lines.Count > 0 && !IsBusy);
            ExportTemplateCommand = new RelayCommand(ExportTemplate);
            ImportExcelFromPathCommand = new RelayCommand<string>(ImportFromExcelPath);
            OpenItemSearchCommand = new RelayCommand(OpenItemSearch);
            SelectSearchResultCommand = new RelayCommand(SelectSearchResult);
            CloseSearchPopupCommand = new RelayCommand(() => IsSearchPopupOpen = false);
            ToggleHistoryCommand = new RelayCommand(ToggleHistory);
            LoadSelectedPastVoucherCommand = new RelayCommand(LoadSelectedPastVoucher, () => SelectedPastVoucher != null);

            ResetVoucherHeader();
            ResetEntryFields();
        }

        private void ResetVoucherHeader()
        {
            try
            {
                VoucherNumber = _openingStockRepository.PeekNextVoucherNumber();
            }
            catch
            {
                VoucherNumber = $"OP-{DateTime.Now:yyyy}-00001";
            }
            VoucherDate = DateTime.Today;
            Notes = "حصر رصيد أول المدة قبل بدء التشغيل الفعلي";
        }

        private void ResetEntryFields()
        {
            ScanOrSearchText = string.Empty;
            EntryItemId = 0;
            EntryItemCode = string.Empty;
            EntryItemBarcode = string.Empty;
            EntryItemNameAr = string.Empty;
            EntryItemNameEn = string.Empty;
            EntryActiveIngredient = string.Empty;
            EntryStripsPerBox = 1;
            EntryPiecesPerStrip = 10;
            EntryBatchNumber = $"OP-{DateTime.Now:yyyyMMdd}";
            EntryExpiryDate = DateTime.Today.AddYears(2);
            EntryQuantityBoxes = 1;
            EntryQuantityStrips = 0;
            EntryQuantityUnits = 0;
            EntryCostPricePounds = 0;
            EntryRetailPricePounds = 0;
            IsNewItemForm = false;
            UpdateUnitsPreview();
        }

        private void UpdateUnitsPreview()
        {
            int perBox = Math.Max(1, EntryStripsPerBox * EntryPiecesPerStrip);
            int perStrip = Math.Max(1, EntryPiecesPerStrip);
            int total = (EntryQuantityBoxes * perBox) + (EntryQuantityStrips * perStrip) + EntryQuantityUnits;
            EntryTotalUnitsPreview = $"{total} قرص / وحدة";
        }

        public void HandleScanOrSearch()
        {
            string query = (ScanOrSearchText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                OpenItemSearch();
                return;
            }

            var resolved = _itemRepository.ResolveScannedCode(query);
            if (resolved != null)
            {
                PopulateFromItem(resolved.Value.Item, resolved.Value.Batch);
                StatusMessage = $"تم العثور على الصنف: {resolved.Value.Item.NameAr}";
                return;
            }

            // فحص بالباركود أو الكود مباشرة
            var item = _itemRepository.GetByBarcodeOrCode(query);
            if (item != null)
            {
                PopulateFromItem(item, null);
                StatusMessage = $"تم العثور على الصنف: {item.NameAr}";
                return;
            }

            // إذا لم يُعثر على صنف مطابق بالباركود، نفتح نموذج إضافة صنف جديد بهذا الباركود
            IsNewItemForm = true;
            EntryItemId = 0;
            EntryItemBarcode = query;
            EntryItemCode = $"MED-{DateTime.Now:yyyyMMdd}-{Lines.Count + 1:D3}";
            EntryItemNameAr = string.Empty;
            EntryItemNameEn = string.Empty;
            EntryActiveIngredient = string.Empty;
            EntryStripsPerBox = 1;
            EntryPiecesPerStrip = 10;
            EntryCostPricePounds = 0;
            EntryRetailPricePounds = 0;
            EntryQuantityBoxes = 1;
            EntryQuantityStrips = 0;
            EntryQuantityUnits = 0;
            EntryBatchNumber = $"OP-{DateTime.Now:yyyyMMdd}";
            EntryExpiryDate = DateTime.Today.AddYears(2);
            UpdateUnitsPreview();

            StatusMessage = $"باركود جديد ({query}) — أدخل اسم الصنف وأسعاره لإضافته لرصيد أول المدة.";
        }

        public void PopulateFromItem(Item item, ItemBatch? batch)
        {
            IsNewItemForm = false;
            EntryItemId = item.Id;
            EntryItemCode = item.Code;
            EntryItemBarcode = item.Barcode;
            EntryItemNameAr = item.NameAr;
            EntryItemNameEn = item.NameEn;
            EntryActiveIngredient = item.ActiveIngredient;
            EntryStripsPerBox = Math.Max(1, item.StripsPerBox);
            EntryPiecesPerStrip = Math.Max(1, item.PiecesPerStrip);
            EntryCostPricePounds = item.CostPriceBoxPiastres / 100m;
            EntryRetailPricePounds = item.RetailPriceBoxPiastres / 100m;

            if (batch != null)
            {
                EntryBatchNumber = batch.BatchNumber;
                EntryExpiryDate = batch.ExpiryDate;
            }
            else
            {
                EntryBatchNumber = $"OP-{DateTime.Now:yyyyMMdd}";
                EntryExpiryDate = DateTime.Today.AddYears(2);
            }

            EntryQuantityBoxes = 1;
            EntryQuantityStrips = 0;
            EntryQuantityUnits = 0;
            UpdateUnitsPreview();
        }

        public void AddCurrentLine()
        {
            if (string.IsNullOrWhiteSpace(EntryItemNameAr))
            {
                StatusMessage = "يرجى كتابة أو اختيار اسم الصنف الدوائي أولاً.";
                return;
            }

            int perBox = Math.Max(1, EntryStripsPerBox * EntryPiecesPerStrip);
            int totalUnits = (EntryQuantityBoxes * perBox) + (EntryQuantityStrips * Math.Max(1, EntryPiecesPerStrip)) + EntryQuantityUnits;

            if (totalUnits <= 0)
            {
                StatusMessage = "يرجى إدخال كمية صحيحة (علب أو أشرطة أو وحدات).";
                return;
            }

            if (EntryCostPricePounds < 0 || EntryRetailPricePounds < 0)
            {
                StatusMessage = "الأسعار لا يمكن أن تكون سالبة.";
                return;
            }

            var line = new OpeningStockVoucherLine
            {
                ItemId = EntryItemId,
                ItemCode = EntryItemCode,
                ItemBarcode = EntryItemBarcode,
                ItemNameAr = EntryItemNameAr.Trim(),
                ItemNameEn = EntryItemNameEn?.Trim() ?? string.Empty,
                ActiveIngredient = EntryActiveIngredient?.Trim() ?? string.Empty,
                StripsPerBox = Math.Max(1, EntryStripsPerBox),
                PiecesPerStrip = Math.Max(1, EntryPiecesPerStrip),
                BatchNumber = string.IsNullOrWhiteSpace(EntryBatchNumber) ? $"OP-{DateTime.Now:yyyyMMdd}" : EntryBatchNumber.Trim(),
                ExpiryDate = EntryExpiryDate,
                QuantityBoxes = EntryQuantityBoxes,
                QuantityStrips = EntryQuantityStrips,
                QuantityUnits = EntryQuantityUnits,
                CostPriceBoxPounds = EntryCostPricePounds,
                RetailPriceBoxPounds = EntryRetailPricePounds
            };

            line.RecalculateLine();

            // فحص التكرار: إذا كان نفس الصنف وبنفس التشغيلة موجوداً بالفعل نزيد الكمية
            var existing = Lines.FirstOrDefault(l =>
                (l.ItemId > 0 && l.ItemId == line.ItemId && l.BatchNumber == line.BatchNumber) ||
                (!string.IsNullOrWhiteSpace(l.ItemBarcode) && l.ItemBarcode == line.ItemBarcode && l.BatchNumber == line.BatchNumber));

            if (existing != null)
            {
                existing.QuantityBoxes += line.QuantityBoxes;
                existing.QuantityStrips += line.QuantityStrips;
                existing.QuantityUnits += line.QuantityUnits;
                existing.CostPriceBoxPounds = line.CostPriceBoxPounds;
                existing.RetailPriceBoxPounds = line.RetailPriceBoxPounds;
                existing.RecalculateLine();
                StatusMessage = $"تم تحديث كمية الصنف ({existing.ItemNameAr}) في السند.";
            }
            else
            {
                Lines.Insert(0, line);
                StatusMessage = $"تمت إضافة ({line.ItemNameAr}) إلى رصيد أول المدة.";
            }

            RecalculateTotals();
            ResetEntryFields();
        }

        public void RemoveSelectedLine()
        {
            if (SelectedLine == null) return;
            string name = SelectedLine.ItemNameAr;
            Lines.Remove(SelectedLine);
            SelectedLine = null;
            RecalculateTotals();
            StatusMessage = $"تم حذف ({name}) من القائمة.";
        }

        public void ClearAllLines()
        {
            Lines.Clear();
            SelectedLine = null;
            RecalculateTotals();
            ResetEntryFields();
            StatusMessage = "تم تفريغ قائمة رصيد أول المدة.";
        }

        public void RecalculateTotals()
        {
            TotalItemsCount = Lines.Count;
            int totalUnits = 0;
            decimal costPounds = 0m;
            decimal retailPounds = 0m;

            foreach (var l in Lines)
            {
                totalUnits += l.TotalSmallestUnits;
                costPounds += l.TotalCostPounds;
                retailPounds += l.TotalRetailPounds;
            }

            TotalUnitsCount = totalUnits;
            TotalCostPounds = costPounds;
            TotalRetailPounds = retailPounds;
            TotalProfitPounds = Math.Max(0m, retailPounds - costPounds);
            ProfitMarginPercent = costPounds > 0 ? Math.Round((TotalProfitPounds / costPounds) * 100m, 2) : 0m;

            OnPropertyChanged(nameof(FormattedTotalCost));
            OnPropertyChanged(nameof(FormattedTotalRetail));
            OnPropertyChanged(nameof(FormattedTotalProfit));

            SaveAndPostCommand.NotifyCanExecuteChanged();
            ExportExcelCommand.NotifyCanExecuteChanged();
            RemoveSelectedLineCommand.NotifyCanExecuteChanged();
        }

        public async Task SaveAndPostAsync()
        {
            if (Lines.Count == 0)
            {
                StatusMessage = "لا توجد بنود للاعتماد.";
                return;
            }

            var confirm = MessageBox.Show(
                $"هل أنت متأكد من حفظ واعتماد رصيد أول المدة؟\n\nعدد الأصناف: {TotalItemsCount}\nإجمالي الوحدات: {TotalUnitsCount}\nإجمالي التكلفة: {FormattedTotalCost}\n\nسيتم تحديث المخزون وإدراج التشغيلات والقيود المحاسبية التلقائية.",
                "تأكيد اعتماد رصيد أول المدة",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                var voucher = new OpeningStockVoucher
                {
                    VoucherDate = VoucherDate,
                    Notes = Notes,
                    Lines = Lines.ToList()
                };

                var user = _sessionService?.CurrentUser;
                await Task.Run(() => _openingStockRepository.SaveVoucher(voucher, user));

                StatusMessage = $"تم اعتماد وحفظ رصيد أول المدة بنجاح! رقم السند: {voucher.VoucherNumber}";
                MessageBox.Show(
                    $"تم حفظ واعتماد رصيد أول المدة بنجاح!\n\nرقم السند: {voucher.VoucherNumber}\nعدد الأصناف: {voucher.TotalItemsCount}\nالقيمة بسعر التكلفة: {voucher.FormattedTotalCost}",
                    "نجاح العملية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ClearAllLines();
                ResetVoucherHeader();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "OpeningStockViewModel.SaveAndPostAsync");
                StatusMessage = $"فشل حفظ رصيد أول المدة: {ex.Message}";
                MessageBox.Show($"تعذر الحفظ: {ex.Message}", "خطأ في الاعتماد", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ExportExcelAsync()
        {
            if (_excelExportService == null || Lines.Count == 0) return;

            IsBusy = true;
            try
            {
                var voucher = new OpeningStockVoucher
                {
                    VoucherNumber = VoucherNumber,
                    VoucherDate = VoucherDate,
                    Notes = Notes,
                    Lines = Lines.ToList()
                };
                voucher.RecalculateTotals();

                string phName = _settingsRepository?.GetSettings()?.PharmacyName ?? "صيدلية المستقبل";
                string path = await _excelExportService.ExportOpeningStockVoucherAsync(voucher, phName);

                StatusMessage = $"تم تصدير ملف Excel بنجاح: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "OpeningStockViewModel.ExportExcelAsync");
                StatusMessage = $"فشل تصدير Excel: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ExportTemplate()
        {
            if (_excelExportService == null) return;
            try
            {
                string path = _excelExportService.ExportOpeningStockTemplate();
                StatusMessage = $"تم إنشاء نموذج Excel بنجاح: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "OpeningStockViewModel.ExportTemplate");
                StatusMessage = $"فشل إنشاء النموذج: {ex.Message}";
            }
        }

        public void ImportFromExcelPath(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

            IsBusy = true;
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var workbook = new XLWorkbook(stream);
                var sheet = workbook.Worksheets.FirstOrDefault();
                if (sheet == null)
                {
                    StatusMessage = "ملف Excel فارغ.";
                    return;
                }

                var used = sheet.RangeUsed();
                if (used == null)
                {
                    StatusMessage = "لا توجد بيانات في ملف Excel.";
                    return;
                }

                int firstRow = used.FirstRow().RowNumber();
                int lastRow = used.LastRow().RowNumber();
                int importedCount = 0;

                // تخطي صف العناوين (الصف الأول)
                for (int r = firstRow + 1; r <= lastRow; r++)
                {
                    string barcode = sheet.Cell(r, 1).GetString().Trim();
                    string nameAr = sheet.Cell(r, 2).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(barcode)) continue;

                    string nameEn = sheet.Cell(r, 3).GetString().Trim();
                    string active = sheet.Cell(r, 4).GetString().Trim();

                    int strips = 1;
                    if (int.TryParse(sheet.Cell(r, 5).GetString(), out int sVal) && sVal > 0) strips = sVal;

                    int pieces = 10;
                    if (int.TryParse(sheet.Cell(r, 6).GetString(), out int pVal) && pVal > 0) pieces = pVal;

                    string batch = sheet.Cell(r, 7).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(batch)) batch = $"OP-{DateTime.Now:yyyyMMdd}";

                    DateTime expiry = DateTime.Today.AddYears(2);
                    string expStr = sheet.Cell(r, 8).GetString().Trim();
                    if (DateTime.TryParse(expStr, out var expDt)) expiry = expDt;

                    int qBoxes = 0;
                    if (int.TryParse(sheet.Cell(r, 9).GetString(), out int qbVal)) qBoxes = qbVal;

                    int qStrips = 0;
                    if (int.TryParse(sheet.Cell(r, 10).GetString(), out int qsVal)) qStrips = qsVal;

                    int qUnits = 0;
                    if (int.TryParse(sheet.Cell(r, 11).GetString(), out int quVal)) qUnits = quVal;

                    decimal cost = 0m;
                    if (decimal.TryParse(sheet.Cell(r, 12).GetString(), out decimal cVal)) cost = cVal;

                    decimal retail = 0m;
                    if (decimal.TryParse(sheet.Cell(r, 13).GetString(), out decimal retVal)) retail = retVal;

                    // فحص قاعدة البيانات لمعرفة ما إذا كان الصنف موجوداً بالفعل
                    Item? dbItem = null;
                    if (!string.IsNullOrWhiteSpace(barcode))
                        dbItem = _itemRepository.GetByBarcodeOrCode(barcode);

                    var line = new OpeningStockVoucherLine
                    {
                        ItemId = dbItem?.Id ?? 0,
                        ItemCode = dbItem?.Code ?? string.Empty,
                        ItemBarcode = !string.IsNullOrWhiteSpace(barcode) ? barcode : (dbItem?.Barcode ?? string.Empty),
                        ItemNameAr = !string.IsNullOrWhiteSpace(nameAr) ? nameAr : (dbItem?.NameAr ?? "صنف غير مسمى"),
                        ItemNameEn = !string.IsNullOrWhiteSpace(nameEn) ? nameEn : (dbItem?.NameEn ?? string.Empty),
                        ActiveIngredient = !string.IsNullOrWhiteSpace(active) ? active : (dbItem?.ActiveIngredient ?? string.Empty),
                        StripsPerBox = dbItem != null ? dbItem.StripsPerBox : strips,
                        PiecesPerStrip = dbItem != null ? dbItem.PiecesPerStrip : pieces,
                        BatchNumber = batch,
                        ExpiryDate = expiry,
                        QuantityBoxes = qBoxes,
                        QuantityStrips = qStrips,
                        QuantityUnits = qUnits,
                        CostPriceBoxPounds = cost > 0 ? cost : (dbItem?.CostPriceBoxPiastres / 100m ?? 0m),
                        RetailPriceBoxPounds = retail > 0 ? retail : (dbItem?.RetailPriceBoxPiastres / 100m ?? 0m)
                    };

                    line.RecalculateLine();
                    if (line.TotalSmallestUnits > 0)
                    {
                        Lines.Add(line);
                        importedCount++;
                    }
                }

                RecalculateTotals();
                StatusMessage = $"تم استيراد {importedCount} صنف من ملف Excel بنجاح.";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "OpeningStockViewModel.ImportFromExcelPath");
                StatusMessage = $"فشل استيراد Excel: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ===== البحث السريع (F4) =====
        public void OpenItemSearch()
        {
            SearchResults.Clear();
            var items = _itemRepository.GetTopSellingItems(30);
            if (items.Count == 0)
                items = _itemRepository.Search(string.Empty, 30);

            foreach (var item in items)
                SearchResults.Add(item);

            IsSearchPopupOpen = true;
        }

        private void PerformSearch(string query)
        {
            SearchResults.Clear();
            var items = _itemRepository.Search(query, 30);
            foreach (var item in items)
                SearchResults.Add(item);
        }

        public void SelectSearchResult()
        {
            if (SelectedSearchResult == null) return;
            PopulateFromItem(SelectedSearchResult, null);
            IsSearchPopupOpen = false;
            StatusMessage = $"تم اختيار الصنف: {SelectedSearchResult.NameAr}";
        }

        // ===== سجل السندات السابقة =====
        public void ToggleHistory()
        {
            IsHistoryOpen = !IsHistoryOpen;
            if (IsHistoryOpen)
            {
                LoadHistory();
            }
        }

        public void LoadHistory()
        {
            try
            {
                PastVouchers.Clear();
                var vouchers = _openingStockRepository.GetVouchersList(50);
                foreach (var v in vouchers)
                    PastVouchers.Add(v);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "OpeningStockViewModel.LoadHistory");
            }
        }

        public void LoadSelectedPastVoucher()
        {
            if (SelectedPastVoucher == null) return;

            try
            {
                var full = _openingStockRepository.GetVoucherById(SelectedPastVoucher.Id);
                if (full != null)
                {
                    Lines.Clear();
                    foreach (var l in full.Lines)
                        Lines.Add(l);

                    VoucherNumber = full.VoucherNumber;
                    VoucherDate = full.VoucherDate;
                    Notes = full.Notes;
                    RecalculateTotals();

                    IsHistoryOpen = false;
                    StatusMessage = $"تم استعراض سند رصيد أول المدة رقم {full.VoucherNumber}.";
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "OpeningStockViewModel.LoadSelectedPastVoucher");
                StatusMessage = $"تعذر تحميل السند: {ex.Message}";
            }
        }
    }
}
