using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using ePrescriptions.Domain.Helpers;

namespace ePrescriptions.App.ViewModels
{
    /// <summary>سطر جرد معروض: يلفّ <see cref="StocktakeLine"/> ويعرض الأرقام بصيغة مقروءة</summary>
    public class StocktakeRow : ObservableObject
    {
        private readonly StocktakeLine _line;
        private readonly Action<StocktakeRow, int> _onCountedEdited;

        public StocktakeRow(StocktakeLine line, int stripsPerBox, int piecesPerStrip, Action<StocktakeRow, int> onCountedEdited)
        {
            _line = line;
            StripsPerBox = stripsPerBox;
            PiecesPerStrip = piecesPerStrip;
            _onCountedEdited = onCountedEdited;
        }

        public int ItemId => _line.ItemId;
        public string Code => _line.Code;
        public string NameAr => _line.NameAr;
        public int StripsPerBox { get; }
        public int PiecesPerStrip { get; }

        public int BookSmallestUnits => _line.BookSmallestUnits;
        public string BookText => UnitBreakdownHelper.FormatStockDetailed(_line.BookSmallestUnits, StripsPerBox, PiecesPerStrip);
        public string CountedText => UnitBreakdownHelper.FormatStockDetailed(_line.CountedSmallestUnits, StripsPerBox, PiecesPerStrip);

        /// <summary>الكمية المعدودة بأصغر وحدة (قابلة للتعديل من الجدول)</summary>
        public int CountedSmallestUnits
        {
            get => _line.CountedSmallestUnits;
            set
            {
                if (value < 0) value = 0;
                if (value == _line.CountedSmallestUnits) return;
                _onCountedEdited(this, value);
            }
        }

        public int DifferenceSmallestUnits => _line.DifferenceSmallestUnits;
        public string DifferenceText => _line.DifferenceSmallestUnits == 0
            ? "مطابق"
            : (_line.DifferenceSmallestUnits < 0 ? "عجز " : "زيادة ") + Math.Abs(_line.DifferenceSmallestUnits) + " وحدة";
        public string StatusColor => _line.Status switch
        {
            StocktakeStatus.Shortage => "#DC2626",
            StocktakeStatus.Surplus => "#D97706",
            _ => "#16A34A"
        };

        public void Refresh()
        {
            OnPropertyChanged(nameof(CountedSmallestUnits));
            OnPropertyChanged(nameof(CountedText));
            OnPropertyChanged(nameof(DifferenceSmallestUnits));
            OnPropertyChanged(nameof(DifferenceText));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    /// <summary>
    /// خطوة 38: شاشة الجرد بالباركود. مسح علبة = +علبة للصنف، المقارنة بالرصيد المسجّل لحظياً،
    /// والاعتماد يسجّل تسوية جرد للأصناف اللي فيها فرق فقط.
    /// </summary>
    public class StocktakeViewModel : ViewModelBase
    {
        private readonly ItemRepository _itemRepository;
        private readonly Services.IUserSessionService? _sessionService;
        private readonly OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly StaffAuditLogRepository? _staffAuditLogRepository;
        private readonly Services.IPdfReportService? _pdfReportService;
        private readonly SettingsRepository? _settingsRepository;
        private readonly StocktakeSession _session = new StocktakeSession();
        private readonly System.Collections.Generic.Dictionary<int, Item> _items = new System.Collections.Generic.Dictionary<int, Item>();

        public ObservableCollection<StocktakeRow> Rows { get; } = new ObservableCollection<StocktakeRow>();

        private string _scanInput = string.Empty;
        public string ScanInput { get => _scanInput; set => SetProperty(ref _scanInput, value); }

        private string _statusMessage = "امسح باركود أي علبة (أو ملصق التشغيلة) لبدء العدّ.";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public int ScanCount => _session.ScanCount;
        public int DifferenceLinesCount => _session.LinesWithDifference().Count;

        public IRelayCommand ScanCommand { get; }
        public IRelayCommand ApplyCommand { get; }
        public IRelayCommand ClearCommand { get; }
        public IRelayCommand PrintSheetCommand { get; }
        public IRelayCommand<StocktakeRow> RemoveRowCommand { get; }

        public StocktakeViewModel(
            ItemRepository itemRepository,
            Services.IUserSessionService? sessionService = null,
            OwnerAuditLogRepository? ownerAuditLogRepository = null,
            StaffAuditLogRepository? staffAuditLogRepository = null,
            Services.IPdfReportService? pdfReportService = null,
            SettingsRepository? settingsRepository = null)
        {
            Title = "الجرد بالباركود";
            _itemRepository = itemRepository;
            _sessionService = sessionService;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _pdfReportService = pdfReportService;
            _settingsRepository = settingsRepository;

            ScanCommand = new RelayCommand(() => Scan(ScanInput));
            ApplyCommand = new RelayCommand(ConfirmAndApply);
            ClearCommand = new RelayCommand(Clear);
            PrintSheetCommand = new AsyncRelayCommand(PrintSheetAsync);
            RemoveRowCommand = new RelayCommand<StocktakeRow>(RemoveRow);
        }

        /// <summary>مسح رمز واحد. يرجع true لو اتعرف على الصنف.</summary>
        public bool Scan(string? code)
        {
            ScanInput = string.Empty;
            if (string.IsNullOrWhiteSpace(code)) return false;

            var resolved = _itemRepository.ResolveScannedCode(code);
            if (resolved == null)
            {
                StatusMessage = $"لم يتم العثور على صنف بالباركود: {code.Trim()}";
                return false;
            }

            var item = resolved.Value.Item;
            _items[item.Id] = item;
            var line = _session.AddScannedBox(item);
            UpsertRow(line, item);

            string batchPart = resolved.Value.Batch != null ? $" (تشغيلة {resolved.Value.Batch.BatchNumber})" : string.Empty;
            StatusMessage = $"{item.NameAr}{batchPart}: المعدود {UnitBreakdownHelper.FormatStockDetailed(line.CountedSmallestUnits, item.StripsPerBox, item.PiecesPerStrip)}";
            RaiseTotals();
            return true;
        }

        private void UpsertRow(StocktakeLine line, Item item)
        {
            var row = Rows.FirstOrDefault(r => r.ItemId == line.ItemId);
            if (row == null)
            {
                row = new StocktakeRow(line, item.StripsPerBox, item.PiecesPerStrip, OnCountedEdited);
                Rows.Insert(0, row); // آخر صنف ممسوح فوق
            }
            else
            {
                row.Refresh();
            }
        }

        private void OnCountedEdited(StocktakeRow row, int newUnits)
        {
            if (!_items.TryGetValue(row.ItemId, out var item)) return;
            _session.SetCounted(item, newUnits);
            row.Refresh();
            RaiseTotals();
        }

        private void RemoveRow(StocktakeRow? row)
        {
            if (row == null) return;
            _session.RemoveLine(row.ItemId);
            _items.Remove(row.ItemId);
            Rows.Remove(row);
            RaiseTotals();
        }

        public void Clear()
        {
            _session.Clear();
            _items.Clear();
            Rows.Clear();
            StatusMessage = "تم مسح جلسة الجرد. امسح باركود علبة لبدء عدّ جديد.";
            RaiseTotals();
        }

        private void ConfirmAndApply()
        {
            int n = DifferenceLinesCount;
            if (n == 0)
            {
                StatusMessage = "لا توجد فروق للاعتماد (كل المعدود مطابق أو لم يُمسح شيء).";
                return;
            }

            var answer = MessageBox.Show(
                $"سيتم تسوية رصيد {n} صنف ليطابق الكمية المعدودة، وتسجيل ذلك في سجل التدقيق.\nهل تريد اعتماد الجرد؟",
                "اعتماد الجرد", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            ApplyConfirmed();
        }

        /// <summary>سطور قائمة الجرد المطبوعة: كل الأصناف النشطة مرتبة بالاسم (للعدّ على الرف)</summary>
        public System.Collections.Generic.List<StocktakeSheetRow> BuildSheetRows() =>
            StocktakeSheetBuilder.Build(_itemRepository.GetAllItems());

        /// <summary>طباعة/تصدير قائمة الجرد PDF (عملية غير متزامنة)</summary>
        public async System.Threading.Tasks.Task PrintSheetAsync()
        {
            if (_pdfReportService == null)
            {
                StatusMessage = "خدمة PDF غير متاحة.";
                return;
            }

            IsBusy = true;
            try
            {
                var rows = BuildSheetRows();
                if (rows.Count == 0)
                {
                    StatusMessage = "لا توجد أصناف نشطة لطباعة قائمة جرد.";
                    return;
                }
                var settings = _settingsRepository?.GetSettings() ?? new PharmacySettings();
                string path = await _pdfReportService.GenerateStocktakeSheetPdfAsync(rows, settings);
                StatusMessage = $"تم إنشاء قائمة الجرد ({rows.Count} صنف): {System.IO.Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StocktakeViewModel.PrintSheetAsync");
                StatusMessage = $"تعذر إنشاء قائمة الجرد: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>اعتماد الجرد فعلياً (بعد التأكيد): تسوية الأصناف اللي فيها فرق وتسجيل التدقيق ثم تفريغ الجلسة</summary>
        public int ApplyConfirmed()
        {
            try
            {
                var applied = _itemRepository.ApplyStocktake(_session);
                foreach (var adj in applied)
                    LogAudit(adj);

                Clear();
                StatusMessage = applied.Count == 0
                    ? "لا توجد فروق للاعتماد."
                    : $"تم اعتماد الجرد: اتسوّى رصيد {applied.Count} صنف.";
                return applied.Count;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StocktakeViewModel.ApplyConfirmed");
                StatusMessage = $"تعذر اعتماد الجرد: {ex.Message}";
                return 0;
            }
        }

        /// <summary>تدقيق: Owner → OwnerAuditLog، أي دور آخر → StaffAuditLog (ما بيتخلطوش)</summary>
        private void LogAudit(StockAdjustment adj)
        {
            try
            {
                var user = _sessionService?.CurrentUser;
                if (user == null) return;

                string field = "TotalStockSmallestUnits (جرد بالباركود)";
                string oldVal = adj.BookSmallestUnits.ToString();
                string newVal = adj.ActualSmallestUnits.ToString();

                if (user.Role == UserRole.Owner)
                    _ownerAuditLogRepository?.LogChange(user.Id, "Item", adj.ItemId.ToString(), adj.ItemNameAr, field, oldVal, newVal);
                else
                    _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "Item", adj.ItemId.ToString(), adj.ItemNameAr, field, oldVal, newVal);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StocktakeViewModel.LogAudit");
            }
        }

        private void RaiseTotals()
        {
            OnPropertyChanged(nameof(ScanCount));
            OnPropertyChanged(nameof(DifferenceLinesCount));
        }
    }
}
