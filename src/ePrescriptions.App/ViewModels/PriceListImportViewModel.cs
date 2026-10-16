using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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
    /// <summary>سطر معاينة من قائمة أسعار المورد (يلفّ <see cref="PriceListLine"/> ويضيف تعليم الاعتماد)</summary>
    public class PriceListLineRow : ObservableObject
    {
        private readonly Action _onIncludeChanged;
        private bool _include;

        public PriceListLineRow(PriceListLine line, Action onIncludeChanged)
        {
            Line = line;
            _onIncludeChanged = onIncludeChanged;
            // اللي محتاج مراجعة بيظهر غير معلّم، والمستخدم يعلّمه بنفسه
            _include = IsUpdatable && !line.RequiresReview;
        }

        public PriceListLine Line { get; }
        public int RowNumber => Line.Row.RowNumber;
        public string ItemName => Line.Item != null ? Line.Item.NameAr : Line.Row.Name;
        public bool IsUpdatable => Line.Status == PriceLineStatus.WillUpdate;
        public bool RequiresReview => IsUpdatable && Line.RequiresReview;

        /// <summary>تعليم اعتماد السطر (مسموح بس للسطور اللي هتتحدّث)</summary>
        public bool Include
        {
            get => _include;
            set
            {
                if (!IsUpdatable) value = false;
                if (!SetProperty(ref _include, value)) return;
                _onIncludeChanged();
            }
        }

        public string StatusText => Line.Status switch
        {
            PriceLineStatus.WillUpdate => RequiresReview ? "يحتاج مراجعة" : "سيتحدّث",
            PriceLineStatus.Unchanged => "بدون تغيير",
            PriceLineStatus.NotFound => "غير موجود",
            PriceLineStatus.Ambiguous => "أكثر من صنف مطابق",
            PriceLineStatus.Conflict => "تعارض في الملف",
            PriceLineStatus.InvalidPrice => "سعر غير صالح",
            _ => "بدون سعر"
        };

        public string StatusColor => Line.Status switch
        {
            PriceLineStatus.WillUpdate => RequiresReview ? "#D97706" : "#16A34A",
            PriceLineStatus.Unchanged => "#64748B",
            _ => "#DC2626"
        };

        public string ChangesText => Line.Changes.Count == 0
            ? string.Empty
            : string.Join(" | ", Line.Changes.Select(c =>
                $"{c.FieldLabelAr}: {PriceListImportViewModel.FormatMoney(c.OldPiastres)} ← {PriceListImportViewModel.FormatMoney(c.NewPiastres)} ({c.ChangePercent:+0.#;-0.#;0}%)"));

        public string Note => Line.Note;
    }

    /// <summary>
    /// خطوة 40 جزء ب: شاشة تحديث الأسعار من ملف Excel لمورد. معاينة كاملة قبل أي كتابة، تعليم الصفوف،
    /// اعتماد بتأكيد، وتسجيل كل تغيير سعر (قديم/جديد/المورد/الملف) في سجل التدقيق الصح:
    /// Owner → OwnerAuditLog، أي دور تاني → StaffAuditLog (ما بيتخلطوش).
    /// مسموحة لـ Admin/Owner فقط لأن التعديل الجماعي أخطر من تعديل صنف واحد.
    /// </summary>
    public class PriceListImportViewModel : ViewModelBase
    {
        private readonly ItemRepository _itemRepository;
        private readonly SupplierRepository? _supplierRepository;
        private readonly Services.IPriceListFileReader _reader;
        private readonly Services.IUserSessionService? _sessionService;
        private readonly OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly StaffAuditLogRepository? _staffAuditLogRepository;

        private List<PriceListRow> _rows = new List<PriceListRow>();
        private bool _refreshing;

        public ObservableCollection<PriceListLineRow> Lines { get; } = new ObservableCollection<PriceListLineRow>();
        public ObservableCollection<Supplier> Suppliers { get; } = new ObservableCollection<Supplier>();

        /// <summary>تأكيد الاعتماد (قابل للاستبدال في الاختبارات)</summary>
        public Func<string, string, bool> ConfirmAction { get; set; } = (message, title) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        private string _filePath = string.Empty;
        public string FilePath { get => _filePath; private set => SetProperty(ref _filePath, value); }

        private Supplier? _selectedSupplier;
        public Supplier? SelectedSupplier { get => _selectedSupplier; set => SetProperty(ref _selectedSupplier, value); }

        private bool _updateRetail = true;
        public bool UpdateRetail { get => _updateRetail; set { if (SetProperty(ref _updateRetail, value)) RebuildPlan(); } }

        private bool _updateWholesale = true;
        public bool UpdateWholesale { get => _updateWholesale; set { if (SetProperty(ref _updateWholesale, value)) RebuildPlan(); } }

        private bool _updateCost = true;
        public bool UpdateCost { get => _updateCost; set { if (SetProperty(ref _updateCost, value)) RebuildPlan(); } }

        private string _statusMessage = "اختار ملف Excel لقائمة أسعار المورد (.xlsx). هتشوف معاينة كاملة قبل ما أي سعر يتغيّر.";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private string _summaryText = string.Empty;
        public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

        /// <summary>الصلاحية: Admin/Owner فقط (بتتقرا من الجلسة الحالية كل مرة)</summary>
        public bool CanImport
        {
            get
            {
                var role = _sessionService?.CurrentUser?.Role;
                return role == UserRole.Admin || role == UserRole.Owner;
            }
        }

        public int SelectedLinesCount => Lines.Count(l => l.Include);
        public int SelectedChangesCount => Lines.Where(l => l.Include).Sum(l => l.Line.Changes.Count);
        public int ReviewLinesCount => Lines.Count(l => l.RequiresReview);

        public IRelayCommand ApplyCommand { get; }
        public IRelayCommand SelectSafeCommand { get; }
        public IRelayCommand ClearSelectionCommand { get; }
        public IRelayCommand ClearCommand { get; }

        public PriceListImportViewModel(
            ItemRepository itemRepository,
            Services.IPriceListFileReader reader,
            Services.IUserSessionService? sessionService = null,
            OwnerAuditLogRepository? ownerAuditLogRepository = null,
            StaffAuditLogRepository? staffAuditLogRepository = null,
            SupplierRepository? supplierRepository = null)
        {
            Title = "تحديث الأسعار من ملف Excel";
            _itemRepository = itemRepository;
            _reader = reader;
            _sessionService = sessionService;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _supplierRepository = supplierRepository;

            ApplyCommand = new RelayCommand(ConfirmAndApply);
            SelectSafeCommand = new RelayCommand(SelectSafe);
            ClearSelectionCommand = new RelayCommand(ClearSelection);
            ClearCommand = new RelayCommand(Clear);

            try
            {
                if (_supplierRepository != null)
                    foreach (var s in _supplierRepository.GetAll().Where(s => s.IsActive)) Suppliers.Add(s);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PriceListImportViewModel.LoadSuppliers");
            }
        }

        /// <summary>قراءة الملف وبناء المعاينة (من غير أي كتابة في القاعدة). يرجع true لو المعاينة اتبنت.</summary>
        public bool LoadFile(string? path)
        {
            if (!CanImport)
            {
                StatusMessage = "تحديث الأسعار من ملف متاح للمدير وصاحب الصيدلية فقط.";
                return false;
            }

            IReadOnlyList<string[]> grid;
            try
            {
                // فحص الملف (الصيغة/الحجم/عدد الصفوف) مسؤولية القارئ نفسه
                grid = _reader.ReadGrid(path ?? string.Empty);
            }
            catch (Services.PriceListFileException ex)
            {
                StatusMessage = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PriceListImportViewModel.LoadFile");
                StatusMessage = "تعذر قراءة الملف: " + ex.Message;
                return false;
            }

            var mapping = PriceListParser.DetectMapping(grid);
            if (mapping == null || !mapping.HasIdentifier || !mapping.HasPrice)
            {
                StatusMessage = "مقدرتش أفهم أعمدة الملف. لازم صف عناوين فيه عمود تعريف (باركود/كود/اسم الصنف) وعمود سعر واحد على الأقل (سعر الجمهور/الجملة/التكلفة).";
                return false;
            }

            _rows = PriceListParser.Parse(grid, mapping);
            FilePath = path!;
            RebuildPlan();
            return true;
        }

        private PriceListOptions BuildOptions() => new PriceListOptions
        {
            UpdateRetail = UpdateRetail,
            UpdateWholesale = UpdateWholesale,
            UpdateCost = UpdateCost
        };

        /// <summary>يعيد المطابقة على بيانات الأصناف الحالية (بعد تغيير الخيارات أو بعد الاعتماد)</summary>
        private void RebuildPlan()
        {
            if (_rows.Count == 0) return;
            _refreshing = true;
            try
            {
                var plan = PriceListPlanner.Build(_rows, _itemRepository.GetAllItems(), BuildOptions());
                Lines.Clear();
                foreach (var line in plan.Lines)
                    Lines.Add(new PriceListLineRow(line, RaiseSelection));

                SummaryText = $"الصفوف: {plan.TotalRows} | هتتحدّث: {plan.UpdateCount} ({plan.ChangeCount} سعر) | " +
                              $"بدون تغيير: {plan.UnchangedCount} | غير موجود: {plan.NotFoundCount} | مشاكل: {plan.ProblemCount} | تحتاج مراجعة: {plan.ReviewCount}";
                StatusMessage = plan.UpdateCount == 0
                    ? "مفيش أي سعر هيتغيّر من الملف ده."
                    : "راجع المعاينة. الصفوف البرتقالي (تغيير كبير أو سعر جمهور أقل من التكلفة) غير معلّمة، علّمها بنفسك لو متأكد.";
            }
            finally
            {
                _refreshing = false;
            }
            RaiseSelection();
        }

        private void RaiseSelection()
        {
            if (_refreshing) return;
            OnPropertyChanged(nameof(SelectedLinesCount));
            OnPropertyChanged(nameof(SelectedChangesCount));
            OnPropertyChanged(nameof(ReviewLinesCount));
        }

        private void SelectSafe()
        {
            _refreshing = true;
            foreach (var l in Lines) l.Include = l.IsUpdatable && !l.RequiresReview;
            _refreshing = false;
            RaiseSelection();
        }

        private void ClearSelection()
        {
            _refreshing = true;
            foreach (var l in Lines) l.Include = false;
            _refreshing = false;
            RaiseSelection();
        }

        public void Clear()
        {
            _rows = new List<PriceListRow>();
            Lines.Clear();
            FilePath = string.Empty;
            SummaryText = string.Empty;
            StatusMessage = "اختار ملف Excel لقائمة أسعار المورد (.xlsx). هتشوف معاينة كاملة قبل ما أي سعر يتغيّر.";
            RaiseSelection();
        }

        private void ConfirmAndApply()
        {
            if (!CanImport)
            {
                StatusMessage = "تحديث الأسعار من ملف متاح للمدير وصاحب الصيدلية فقط.";
                return;
            }

            int lines = SelectedLinesCount;
            int changes = SelectedChangesCount;
            if (lines == 0)
            {
                StatusMessage = "علّم صف واحد على الأقل لاعتماده.";
                return;
            }

            if (!ConfirmAction(
                $"سيتم تغيير {changes} سعر في {lines} صنف، وتسجيل كل تغيير في سجل التدقيق.\nهل تريد اعتماد التحديث؟",
                "اعتماد تحديث الأسعار")) return;

            ApplyConfirmed();
        }

        /// <summary>
        /// الاعتماد الفعلي (بعد التأكيد): بيتحقق من الصلاحية مرة تانية وقت الاعتماد (الدور ممكن يتغيّر بعد المعاينة)،
        /// وبيرجّع عدد الأسعار اللي اتغيّرت فعلاً.
        /// </summary>
        public int ApplyConfirmed()
        {
            if (!CanImport)
            {
                StatusMessage = "تحديث الأسعار من ملف متاح للمدير وصاحب الصيدلية فقط.";
                return 0;
            }

            try
            {
                var chosen = Lines.Where(l => l.Include).ToList();
                var changes = chosen.SelectMany(l => l.Line.Changes).ToList();
                if (changes.Count == 0)
                {
                    StatusMessage = "علّم صف واحد على الأقل لاعتماده.";
                    return 0;
                }

                var result = _itemRepository.ApplyPriceChanges(changes);

                int auditFailures = 0;
                foreach (var change in result.Applied)
                    if (!LogAudit(change)) auditFailures++;

                var message = $"تم تغيير {result.Applied.Count} سعر.";
                if (result.Skipped.Count > 0)
                    message += $" اتخطّى {result.Skipped.Count} (مثلاً: السعر اتغيّر بعد المعاينة).";
                if (auditFailures > 0)
                    message += $" ⚠ تحذير: {auditFailures} تغيير ما اتسجّلش في سجل التدقيق.";
                RebuildPlan();
                StatusMessage = message;
                return result.Applied.Count;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PriceListImportViewModel.ApplyConfirmed");
                StatusMessage = "تعذر اعتماد التحديث: " + ex.Message;
                return 0;
            }
        }

        /// <summary>تدقيق: Owner → OwnerAuditLog، أي دور آخر → StaffAuditLog. يرجع false لو التسجيل فشل.</summary>
        private bool LogAudit(ItemPriceChange change)
        {
            try
            {
                var user = _sessionService?.CurrentUser;
                if (user == null) return false;

                string supplier = SelectedSupplier?.Name ?? "غير محدد";
                string file = Path.GetFileName(FilePath);
                string source = $"قائمة أسعار المورد: {supplier} | الملف: {file}";
                string oldVal = FormatMoney(change.OldPiastres);
                string newVal = FormatMoney(change.NewPiastres);

                if (user.Role == UserRole.Owner)
                {
                    if (_ownerAuditLogRepository == null) return false;
                    _ownerAuditLogRepository.LogChange(user.Id, "Item", change.ItemId.ToString(), change.ItemNameAr,
                        change.AuditFieldName, oldVal, newVal, source);
                }
                else
                {
                    if (_staffAuditLogRepository == null) return false;
                    _staffAuditLogRepository.LogChange(user.Id, user.FullName, user.Role.ToString(), "Item", change.ItemId.ToString(), change.ItemNameAr,
                        $"{change.AuditFieldName} ({source})", oldVal, newVal);
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PriceListImportViewModel.LogAudit");
                return false;
            }
        }

        public static string FormatMoney(long piastres) =>
            (piastres / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
