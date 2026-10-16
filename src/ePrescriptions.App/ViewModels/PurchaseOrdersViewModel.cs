using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    /// <summary>سطر في عرض أمر شراء مقترح (صنف ناقص)</summary>
    public class SuggestedLineRow
    {
        public SuggestedLineRow(SuggestedOrderLine line)
        {
            Line = line;
        }

        public SuggestedOrderLine Line { get; }
        public string ItemName => Line.ItemNameAr;
        public string ItemCode => Line.ItemCode;
        public string StockText => UnitBreakdownHelper.FormatStockDetailed(Line.CurrentStockSmallestUnits, 1, Math.Max(1, Line.SmallestUnitsPerBox));
        public int MinStock => Line.MinStockAlertSmallestUnits;
        public int OrderBoxes => Line.SuggestedBoxes;
        public string EstimatedText => Line.HasPurchaseHistory ? Money.FromPiastres(Line.EstimatedCostPiastres).ToFormattedString() : "غير معروف";
    }

    /// <summary>أمر شراء مقترح لمورد (مجموعة أصناف ناقصة)</summary>
    public class SuggestedOrderRow
    {
        public SuggestedOrderRow(SuggestedPurchaseOrder order)
        {
            Order = order;
        }

        public SuggestedPurchaseOrder Order { get; }
        public string SupplierName => Order.HasSupplier ? Order.SupplierName : "بدون مورد سابق (اختار مورد)";
        public bool HasSupplier => Order.HasSupplier;
        public int ItemsCount => Order.Lines.Count;
        public string EstimatedTotalText => Money.FromPiastres(Order.EstimatedTotalPiastres).ToFormattedString() + (Order.HasUnpricedLines ? " +" : string.Empty);
    }

    /// <summary>أمر شراء محفوظ في السجل</summary>
    public class PurchaseOrderRow
    {
        public PurchaseOrderRow(PurchaseOrder order)
        {
            Order = order;
        }

        public PurchaseOrder Order { get; }
        public string OrderNumber => Order.OrderNumber;
        public string DateText => Order.CreatedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        public string SupplierName => Order.SupplierName;
        public string StatusText => Order.Status == PurchaseOrderStatus.Open ? "مفتوح" : Order.Status == PurchaseOrderStatus.Received ? "تم الاستلام" : "ملغي";
        public bool IsOpen => Order.Status == PurchaseOrderStatus.Open;
        public string EstimatedTotalText => Money.FromPiastres(Order.EstimatedTotalPiastres).ToFormattedString();
        public string UserName => Order.UserName;
    }

    public class PurchaseOrderLineRow
    {
        public PurchaseOrderLineRow(PurchaseOrderItem item) { Item = item; }
        public PurchaseOrderItem Item { get; }
        public string ItemName => Item.ItemNameAr;
        public string ItemCode => Item.ItemCode;
        public int Boxes => Item.OrderBoxes;
        public string EstimatedText => Item.EstimatedCostPiastres > 0 ? Money.FromPiastres(Item.EstimatedCostPiastres).ToFormattedString() : "غير معروف";
    }

    /// <summary>
    /// أوامر الشراء: مقترحات تلقائية من النواقص مجمّعة بمورد آخر شراء، وحفظها كأمر شراء، ومتابعة الأوامر المفتوحة.
    /// الأمر مستند طلب فقط (لا يحرّك مخزون ولا نقدية)؛ الأصناف في أمر مفتوح بتختفي من المقترحات.
    /// </summary>
    public class PurchaseOrdersViewModel : ViewModelBase
    {
        private readonly PurchaseRepository _purchaseRepository;
        private readonly PurchaseOrderRepository _orderRepository;
        private readonly SupplierRepository _supplierRepository;
        private readonly Services.IUserSessionService? _sessionService;
        private readonly OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly StaffAuditLogRepository? _staffAuditLogRepository;

        public ObservableCollection<SuggestedOrderRow> Suggestions { get; } = new();
        public ObservableCollection<SuggestedLineRow> SuggestionLines { get; } = new();
        public ObservableCollection<Supplier> Suppliers { get; } = new();
        public ObservableCollection<PurchaseOrderRow> Orders { get; } = new();
        public ObservableCollection<PurchaseOrderLineRow> OrderLines { get; } = new();

        private SuggestedOrderRow? _selectedSuggestion;
        public SuggestedOrderRow? SelectedSuggestion
        {
            get => _selectedSuggestion;
            set
            {
                if (!SetProperty(ref _selectedSuggestion, value)) return;
                SuggestionLines.Clear();
                if (value != null)
                    foreach (var l in value.Order.Lines) SuggestionLines.Add(new SuggestedLineRow(l));
                OnPropertyChanged(nameof(NeedsSupplierChoice));
            }
        }

        /// <summary>مجموعة "بدون مورد سابق": لازم المستخدم يختار موردها قبل الحفظ</summary>
        public bool NeedsSupplierChoice => SelectedSuggestion != null && !SelectedSuggestion.HasSupplier;

        private Supplier? _selectedSupplier;
        public Supplier? SelectedSupplier
        {
            get => _selectedSupplier;
            set => SetProperty(ref _selectedSupplier, value);
        }

        private string _notes = string.Empty;
        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        private PurchaseOrderRow? _selectedOrder;
        public PurchaseOrderRow? SelectedOrder
        {
            get => _selectedOrder;
            set
            {
                if (!SetProperty(ref _selectedOrder, value)) return;
                OrderLines.Clear();
                if (value != null)
                {
                    var full = _orderRepository.Get(value.Order.Id);
                    if (full != null)
                        foreach (var i in full.Items) OrderLines.Add(new PurchaseOrderLineRow(i));
                }
            }
        }

        private string _statusMessage = "الأصناف الناقصة متجمّعة بمورد آخر شراء لكل صنف. اختار مجموعة واحفظها كأمر شراء.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand CreateOrderCommand { get; }
        public IRelayCommand ReceiveOrderCommand { get; }
        public IRelayCommand CancelOrderCommand { get; }

        public PurchaseOrdersViewModel(
            PurchaseRepository purchaseRepository,
            PurchaseOrderRepository orderRepository,
            SupplierRepository supplierRepository,
            Services.IUserSessionService? sessionService = null,
            OwnerAuditLogRepository? ownerAuditLogRepository = null,
            StaffAuditLogRepository? staffAuditLogRepository = null)
        {
            Title = "أوامر الشراء";
            _purchaseRepository = purchaseRepository;
            _orderRepository = orderRepository;
            _supplierRepository = supplierRepository;
            _sessionService = sessionService;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;

            RefreshCommand = new RelayCommand(LoadData);
            CreateOrderCommand = new RelayCommand(CreateOrder);
            ReceiveOrderCommand = new RelayCommand(() => ChangeStatus(PurchaseOrderStatus.Received));
            CancelOrderCommand = new RelayCommand(() => ChangeStatus(PurchaseOrderStatus.Cancelled));

            LoadData();
        }

        public override void OnNavigatedTo() => LoadData();

        public void LoadData()
        {
            try
            {
                int? keepSuggestionSupplier = SelectedSuggestion?.Order.SupplierId;
                int? keepOrderId = SelectedOrder?.Order.Id;

                Suppliers.Clear();
                foreach (var s in _supplierRepository.GetAll()) Suppliers.Add(s);

                Suggestions.Clear();
                foreach (var o in _purchaseRepository.GetSuggestedPurchaseOrders()) Suggestions.Add(new SuggestedOrderRow(o));
                SelectedSuggestion = Suggestions.FirstOrDefault(s => s.Order.SupplierId == keepSuggestionSupplier) ?? Suggestions.FirstOrDefault();

                Orders.Clear();
                foreach (var o in _orderRepository.GetRecent(50)) Orders.Add(new PurchaseOrderRow(o));
                SelectedOrder = Orders.FirstOrDefault(o => o.Order.Id == keepOrderId);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PurchaseOrdersViewModel.LoadData");
                StatusMessage = $"تعذر تحميل البيانات: {ex.Message}";
            }
        }

        /// <summary>نافذة التأكيد (قابلة للاستبدال في الاختبارات)</summary>
        public Func<string, string, bool> ConfirmAction { get; set; } = (message, title) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == MessageBoxResult.Yes;

        public void CreateOrder()
        {
            if (SelectedSuggestion == null)
            {
                StatusMessage = "اختار أمر شراء مقترح الأول.";
                return;
            }

            int? supplierId = SelectedSuggestion.HasSupplier ? SelectedSuggestion.Order.SupplierId : SelectedSupplier?.Id;
            string supplierName = SelectedSuggestion.HasSupplier ? SelectedSuggestion.Order.SupplierName : SelectedSupplier?.Name ?? string.Empty;
            if (!supplierId.HasValue)
            {
                StatusMessage = "اختار المورد لأمر الشراء ده (الأصناف دي مالهاش مورد سابق).";
                return;
            }

            if (!ConfirmAction(
                $"حفظ أمر شراء للمورد {supplierName}\nعدد الأصناف: {SelectedSuggestion.ItemsCount}\nالتكلفة التقديرية: {SelectedSuggestion.EstimatedTotalText}\n\nتكمّل؟",
                "تأكيد أمر الشراء")) return;

            var user = _sessionService?.CurrentUser;
            int id = _orderRepository.Create(SelectedSuggestion.Order, supplierId, user?.Id ?? 0, user?.FullName ?? string.Empty,
                string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(), out string err);
            if (id <= 0)
            {
                StatusMessage = $"فشل حفظ أمر الشراء: {err}";
                return;
            }

            var saved = _orderRepository.Get(id);
            LogAudit(saved, "إنشاء أمر شراء", $"{saved?.Items.Count ?? 0} صنف / تقديري {saved?.EstimatedTotalPiastres ?? 0}");
            StatusMessage = $"تم حفظ أمر الشراء {saved?.OrderNumber} للمورد {supplierName}.";
            Notes = string.Empty;
            SelectedSupplier = null;
            LoadData();
        }

        public void ChangeStatus(PurchaseOrderStatus newStatus)
        {
            if (SelectedOrder == null)
            {
                StatusMessage = "اختار أمر شراء من السجل.";
                return;
            }

            string what = newStatus == PurchaseOrderStatus.Received ? "تسجيل استلام" : "إلغاء";
            if (!ConfirmAction($"{what} أمر الشراء {SelectedOrder.OrderNumber}؟", "تأكيد")) return;

            var order = SelectedOrder.Order;
            if (!_orderRepository.SetStatus(order.Id, newStatus, out string err))
            {
                StatusMessage = err;
                return;
            }

            LogAudit(order, newStatus == PurchaseOrderStatus.Received ? "استلام أمر شراء" : "إلغاء أمر شراء", order.OrderNumber);
            StatusMessage = $"تم {what} أمر الشراء {order.OrderNumber}.";
            LoadData();
        }

        private void LogAudit(PurchaseOrder? order, string field, string newValue)
        {
            try
            {
                var user = _sessionService?.CurrentUser;
                if (user == null || order == null) return;

                if (user.Role == UserRole.Owner)
                    _ownerAuditLogRepository?.LogChange(user.Id, "PurchaseOrder", order.Id.ToString(), order.OrderNumber, field, "-", newValue);
                else
                    _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "PurchaseOrder", order.Id.ToString(), order.OrderNumber, field, "-", newValue);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PurchaseOrdersViewModel.LogAudit");
            }
        }
    }
}
