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
    public class InventoryItemDisplay : ObservableObject
    {
        public Item OriginalItem { get; }

        public int Id => OriginalItem.Id;
        public string Code => OriginalItem.Code;
        public string Barcode => OriginalItem.Barcode;
        public string NameAr => OriginalItem.NameAr;
        public string NameEn => OriginalItem.NameEn;
        public string ActiveIngredient => OriginalItem.ActiveIngredient;
        public int StripsPerBox => OriginalItem.StripsPerBox;
        public int PiecesPerStrip => OriginalItem.PiecesPerStrip;
        public int TotalStockSmallestUnits => OriginalItem.TotalStockSmallestUnits;
        public int MinStockAlertSmallestUnits => OriginalItem.MinStockAlertSmallestUnits;

        public bool IsLowStock => TotalStockSmallestUnits <= MinStockAlertSmallestUnits;

        public string FormattedStock => UnitBreakdownHelper.FormatStockDetailed(TotalStockSmallestUnits, StripsPerBox, PiecesPerStrip);
        public string FormattedRetailPrice => Money.FromPiastres(OriginalItem.RetailPriceBoxPiastres).ToFormattedString();
        public string FormattedCostPrice => Money.FromPiastres(OriginalItem.CostPriceBoxPiastres).ToFormattedString();

        public InventoryItemDisplay(Item item)
        {
            OriginalItem = item;
        }
    }

    public class ExpiryAlertDisplay
    {
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;
        public string BatchNumber { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public int RemainingSmallestUnits { get; set; }
        public int DaysRemaining => (int)(ExpiryDate.Date - DateTime.Today).TotalDays;
        public bool IsExpired => DaysRemaining <= 0;
        public string FormattedExpiry => ExpiryDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    public class InventoryViewModel : ViewModelBase
    {
        private readonly ItemRepository _itemRepository;
        private readonly Services.IBarcodeLabelService? _barcodeLabelService;
        private readonly Data.Repositories.OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly Data.Repositories.StaffAuditLogRepository? _staffAuditLogRepository;
        private readonly Services.IUserSessionService? _sessionService;

        // قائمة الأصناف والبحث
        private string _searchFilter = string.Empty;
        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                {
                    LoadItems();
                }
            }
        }

        public ObservableCollection<InventoryItemDisplay> ItemsList { get; } = new();
        public ObservableCollection<ExpiryAlertDisplay> NearExpiryList { get; } = new();
        public ObservableCollection<InventoryItemDisplay> LowStockList { get; } = new();

        // الصنف المحدد للتحرير أو الجرد
        private InventoryItemDisplay? _selectedItem;
        public InventoryItemDisplay? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value) && value != null)
                {
                    LoadItemToEdit(value.OriginalItem);
                }
            }
        }

        // حقول إضافة / تعديل صنف
        private int _editId;
        private string _editCode = string.Empty;
        public string EditCode { get => _editCode; set => SetProperty(ref _editCode, value); }

        private string _editBarcode = string.Empty;
        public string EditBarcode { get => _editBarcode; set => SetProperty(ref _editBarcode, value); }

        private string _editNameAr = string.Empty;
        public string EditNameAr { get => _editNameAr; set => SetProperty(ref _editNameAr, value); }

        private string _editNameEn = string.Empty;
        public string EditNameEn { get => _editNameEn; set => SetProperty(ref _editNameEn, value); }

        private string _editActiveIngredient = string.Empty;
        public string EditActiveIngredient { get => _editActiveIngredient; set => SetProperty(ref _editActiveIngredient, value); }

        private int _editStripsPerBox = 2;
        public int EditStripsPerBox { get => _editStripsPerBox; set => SetProperty(ref _editStripsPerBox, value); }

        private int _editPiecesPerStrip = 10;
        public int EditPiecesPerStrip { get => _editPiecesPerStrip; set => SetProperty(ref _editPiecesPerStrip, value); }

        private decimal _editRetailPriceRiyals = 10;
        public decimal EditRetailPriceRiyals { get => _editRetailPriceRiyals; set => SetProperty(ref _editRetailPriceRiyals, value); }

        private decimal _editCostPriceRiyals = 7;
        public decimal EditCostPriceRiyals { get => _editCostPriceRiyals; set => SetProperty(ref _editCostPriceRiyals, value); }

        private bool _editIsPrescriptionRequired;
        public bool EditIsPrescriptionRequired { get => _editIsPrescriptionRequired; set => SetProperty(ref _editIsPrescriptionRequired, value); }

        private int _editMinStockAlert = 30;
        public int EditMinStockAlert { get => _editMinStockAlert; set => SetProperty(ref _editMinStockAlert, value); }

        // حقول إضافة تشغيلة جديدة
        private string _newBatchNumber = string.Empty;
        public string NewBatchNumber { get => _newBatchNumber; set => SetProperty(ref _newBatchNumber, value); }

        private DateTime _newBatchExpiry = DateTime.Today.AddYears(2);
        public DateTime NewBatchExpiry { get => _newBatchExpiry; set => SetProperty(ref _newBatchExpiry, value); }

        private int _newBatchSmallestUnits = 50;
        public int NewBatchSmallestUnits { get => _newBatchSmallestUnits; set => SetProperty(ref _newBatchSmallestUnits, value); }

        // حقول تسوية الجرد الفعلي
        private int _actualStockUnits;
        public int ActualStockUnits { get => _actualStockUnits; set => SetProperty(ref _actualStockUnits, value); }

        private string _adjustmentReason = "تسوية جرد دوري";
        public string AdjustmentReason { get => _adjustmentReason; set => SetProperty(ref _adjustmentReason, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand NewItemCommand { get; }
        public IRelayCommand SaveItemCommand { get; }
        public IRelayCommand AddBatchCommand { get; }
        public IRelayCommand ApplyStockAdjustmentCommand { get; }
        public IRelayCommand PrintBarcodeLabelCommand { get; }

        public InventoryViewModel(
            ItemRepository itemRepository,
            Services.IBarcodeLabelService? barcodeLabelService = null,
            Data.Repositories.OwnerAuditLogRepository? ownerAuditLogRepository = null,
            Data.Repositories.StaffAuditLogRepository? staffAuditLogRepository = null,
            Services.IUserSessionService? sessionService = null)
        {
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _sessionService = sessionService;
            Title = "إدارة المخزون والأدوية";
            _itemRepository = itemRepository;
            _barcodeLabelService = barcodeLabelService;

            RefreshCommand = new RelayCommand(RefreshAll);
            NewItemCommand = new RelayCommand(ResetEditForm);
            SaveItemCommand = new RelayCommand(SaveItem);
            AddBatchCommand = new RelayCommand(AddBatch);
            ApplyStockAdjustmentCommand = new RelayCommand(ApplyAdjustment);
            PrintBarcodeLabelCommand = new RelayCommand(PrintSelectedLabel);

            RefreshAll();
        }

        private void PrintSelectedLabel()
        {
            if (SelectedItem == null || _barcodeLabelService == null)
            {
                StatusMessage = "يرجى اختيار صنف أولاً لطباعة ملصق الباركود.";
                return;
            }

            if (_barcodeLabelService.PrintBarcodeLabel(SelectedItem.OriginalItem, null, 1, out string error))
            {
                StatusMessage = $"تمت طباعة ملصق الباركود للصنف {SelectedItem.NameAr} بنجاح.";
            }
            else
            {
                StatusMessage = $"تعذر طباعة ملصق الباركود: {error}";
            }
        }

        public void RefreshAll()
        {
            LoadItems();
            LoadNearExpiry();
            LoadLowStock();
            StatusMessage = "تم تحديث بيانات المخزون بنجاح.";
        }

        private void LoadItems()
        {
            ItemsList.Clear();
            var items = string.IsNullOrWhiteSpace(SearchFilter)
                ? _itemRepository.GetAllItems()
                : _itemRepository.Search(SearchFilter, 100);

            foreach (var item in items)
            {
                ItemsList.Add(new InventoryItemDisplay(item));
            }
        }

        private void LoadNearExpiry()
        {
            NearExpiryList.Clear();
            var batches = _itemRepository.GetNearExpiryBatches(90);
            foreach (var (itm, b) in batches)
            {
                NearExpiryList.Add(new ExpiryAlertDisplay
                {
                    ItemCode = itm.Code,
                    ItemNameAr = itm.NameAr,
                    BatchNumber = b.BatchNumber,
                    ExpiryDate = b.ExpiryDate,
                    RemainingSmallestUnits = b.RemainingSmallestUnits
                });
            }
        }

        private void LoadLowStock()
        {
            LowStockList.Clear();
            var low = _itemRepository.GetLowStockItems();
            foreach (var itm in low)
            {
                LowStockList.Add(new InventoryItemDisplay(itm));
            }
        }

        private void LoadItemToEdit(Item item)
        {
            _editId = item.Id;
            EditCode = item.Code;
            EditBarcode = item.Barcode;
            EditNameAr = item.NameAr;
            EditNameEn = item.NameEn;
            EditActiveIngredient = item.ActiveIngredient;
            EditStripsPerBox = item.StripsPerBox;
            EditPiecesPerStrip = item.PiecesPerStrip;
            EditRetailPriceRiyals = Money.FromPiastres(item.RetailPriceBoxPiastres).ToPounds();
            EditCostPriceRiyals = Money.FromPiastres(item.CostPriceBoxPiastres).ToPounds();
            EditMinStockAlert = item.MinStockAlertSmallestUnits;
            EditIsPrescriptionRequired = item.IsPrescriptionRequired;
            ActualStockUnits = item.TotalStockSmallestUnits;
        }

        public void ResetEditForm()
        {
            _editId = 0;
            EditCode = $"MED-{DateTime.Now:fff}";
            EditBarcode = $"{DbDate.CompactFull(DateTime.Now)}";
            EditNameAr = string.Empty;
            EditNameEn = string.Empty;
            EditActiveIngredient = string.Empty;
            EditStripsPerBox = 2;
            EditPiecesPerStrip = 10;
            EditRetailPriceRiyals = 0;
            EditCostPriceRiyals = 0;
            EditMinStockAlert = 30;
            EditIsPrescriptionRequired = false;
            SelectedItem = null;
            StatusMessage = "جاهز لإدخال صنف جديد.";
        }

        public void SaveItem()
        {
            if (string.IsNullOrWhiteSpace(EditNameAr))
            {
                StatusMessage = "يرجى إدخال اسم الصنف باللغة العربية!";
                return;
            }

            // عند التعديل نبدأ من الصنف المحفوظ حتى لا تُفقد حقول غير معروضة (سعر الجملة، الضريبة، الوصفة...)
            var previous = _editId != 0 ? _itemRepository.GetById(_editId) : null;
            var item = previous ?? new Item();
            item.Id = _editId;
            item.Code = EditCode;
            item.Barcode = EditBarcode;
            item.NameAr = EditNameAr;
            item.NameEn = EditNameEn;
            item.ActiveIngredient = EditActiveIngredient;
            item.StripsPerBox = EditStripsPerBox;
            item.PiecesPerStrip = EditPiecesPerStrip;
            item.RetailPriceBoxPiastres = Money.FromPounds(EditRetailPriceRiyals).Piastres;
            item.CostPriceBoxPiastres = Money.FromPounds(EditCostPriceRiyals).Piastres;
            item.MinStockAlertSmallestUnits = EditMinStockAlert;
            item.IsPrescriptionRequired = EditIsPrescriptionRequired;
            item.IsActive = true;

            LogOwnerEditIfApplicable(previous, item);

            _itemRepository.SaveItem(item);
            StatusMessage = $"تم حفظ بيانات الصنف '{item.NameAr}' بنجاح.";
            RefreshAll();
        }

        public void AddBatch()
        {
            if (_selectedItem == null)
            {
                StatusMessage = "يرجى تحديد صنف أولاً لإضافة دفعة له!";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewBatchNumber))
            {
                StatusMessage = "يرجى إدخال رقم التشغيلة!";
                return;
            }

            long costPiastres = Money.FromPounds(EditCostPriceRiyals).Piastres;
            var itemForBatch = _selectedItem.OriginalItem;
            int perBox = Math.Max(1, itemForBatch.SmallestUnitsPerBox);
            var newBatch = new ItemBatch
            {
                ItemId = _selectedItem.Id,
                BatchNumber = NewBatchNumber.Trim(),
                ExpiryDate = NewBatchExpiry,
                RemainingSmallestUnits = NewBatchSmallestUnits,
                CostPricePerSmallestUnitPiastres = (costPiastres + (perBox / 2)) / perBox
            };
            int unitsAdded = NewBatchSmallestUnits;
            _itemRepository.AddBatch(newBatch);
            string added = $"تمت إضافة التشغيلة {newBatch.BatchNumber} بنجاح إلى رصيد الصنف.";
            NewBatchNumber = string.Empty;
            RefreshAll();
            StatusMessage = added;

            string? printMsg = Services.BatchLabelPrintHelper.OfferAndPrint(
                _barcodeLabelService,
                new System.Collections.Generic.List<(Item Item, ItemBatch Batch, int Units)> { (itemForBatch, newBatch, unitsAdded) });
            if (printMsg != null) StatusMessage = $"{added} {printMsg}";
        }

        public void ApplyAdjustment()
        {
            if (_selectedItem == null)
            {
                StatusMessage = "يرجى اختيار صنف لتسوية جرد رصيده!";
                return;
            }

            string adjNum = $"ADJ-{DbDate.DashStamp(DateTime.Now)}";

            var adj = new StockAdjustment
            {
                AdjustmentNumber = adjNum,
                ItemId = _selectedItem.Id,
                ItemCode = _selectedItem.Code,
                ItemNameAr = _selectedItem.NameAr,
                Date = DateTime.Now,
                BookSmallestUnits = _selectedItem.TotalStockSmallestUnits,
                ActualSmallestUnits = ActualStockUnits,
                Reason = AdjustmentReason
            };

            LogOwnerStockAdjustmentIfApplicable(_selectedItem, adj.BookSmallestUnits, adj.ActualSmallestUnits);

            _itemRepository.RecordStockAdjustment(adj);
            StatusMessage = $"تم تسجيل تسوية الجرد ({adjNum}) وتحديث الرصيد الفعلي إلى {ActualStockUnits} وحدة.";
            RefreshAll();
        }
        /// <summary>
        /// إذا كان منفّذ العملية هو صاحب الصيدلية، نسجّل أي حقل تغيّر في سجله الخاص فقط.
        /// لا يُستدعى أي تسجيل هنا لغير صاحب الصيدلية (المدير والموظفون لا يُسجَّل لهم شيء).
        /// </summary>
        /// <summary>
        /// يسجّل أي حقل تغيّر عند حفظ صنف: لصاحب الصيدلية في سجله الخاص المخفي عن الجميع،
        /// ولأي دور آخر (مدير/صيدلي/كاشير) في سجل الموظفين العام المرئي لـ Admin وOwner فقط.
        /// </summary>
        private void LogOwnerEditIfApplicable(Item? previous, Item updated)
        {
            if (_sessionService?.CurrentUser == null || previous == null) return; // صنف جديد، لا يوجد تعديل على قيمة سابقة

            string entityId = updated.Id.ToString();
            string label = updated.NameAr;

            void Log(string field, string? oldVal, string? newVal) =>
                LogAudit("Item", entityId, label, field, oldVal, newVal);

            Log("NameAr", previous.NameAr, updated.NameAr);
            Log("Code", previous.Code, updated.Code);
            Log("Barcode", previous.Barcode, updated.Barcode);
            Log("StripsPerBox", previous.StripsPerBox.ToString(), updated.StripsPerBox.ToString());
            Log("PiecesPerStrip", previous.PiecesPerStrip.ToString(), updated.PiecesPerStrip.ToString());
            Log("RetailPriceBoxPiastres", previous.RetailPriceBoxPiastres.ToString(), updated.RetailPriceBoxPiastres.ToString());
            Log("CostPriceBoxPiastres", previous.CostPriceBoxPiastres.ToString(), updated.CostPriceBoxPiastres.ToString());
            Log("MinStockAlertSmallestUnits", previous.MinStockAlertSmallestUnits.ToString(), updated.MinStockAlertSmallestUnits.ToString());
            Log("IsPrescriptionRequired", previous.IsPrescriptionRequired.ToString(), updated.IsPrescriptionRequired.ToString());
            Log("TotalStockSmallestUnits", previous.TotalStockSmallestUnits.ToString(), updated.TotalStockSmallestUnits.ToString());
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
        /// تسجيل تعديل كمية مباشر (تسوية جرد) إن كان المنفّذ صاحب الصيدلية.
        /// </summary>
        private void LogOwnerStockAdjustmentIfApplicable(InventoryItemDisplay item, int bookUnits, int actualUnits)
        {
            if (_sessionService?.CurrentUser == null || bookUnits == actualUnits) return;

            LogAudit("Item", item.Id.ToString(), item.NameAr, "TotalStockSmallestUnits (تسوية جرد)",
                bookUnits.ToString(), actualUnits.ToString());
        }
    }
}