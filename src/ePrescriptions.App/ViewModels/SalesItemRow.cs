using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    public class SalesItemRow : ObservableObject
    {
        public int ItemId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string ActiveIngredient { get; set; } = string.Empty;

        public int StripsPerBox { get; set; } = 1;
        public int PiecesPerStrip { get; set; } = 10;
        public int MinStockAlertSmallestUnits { get; set; } = 30;

        public int TotalStockSmallestUnits { get; set; }

        public string BatchNumber { get; set; } = "بالسالب";
        public DateTime? ExpiryDate { get; set; }

        /// <summary>معرّف التشغيلة عند البيع بقراءة ملصق التشغيلة (null = اختيار FEFO تلقائي عند الحفظ)</summary>
        public int? BatchId { get; set; }

        /// <summary>الصنف يتطلب وصفة طبية (رقم الوصفة واسم الطبيب إلزاميان للبيع)</summary>
        public bool RequiresPrescription { get; private set; }

        private string _warningText = string.Empty;
        /// <summary>تنبيه يظهر بجانب البند (تشغيلة أقدم على الرف، قرب انتهاء الصلاحية، ...)</summary>
        public string WarningText
        {
            get => _warningText;
            set
            {
                if (SetProperty(ref _warningText, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasWarning));
            }
        }
        public bool HasWarning => !string.IsNullOrEmpty(_warningText);

        // ===== بيانات بند المرتجع (مرتبط ببند في فاتورة البيع الأصلية) =====
        public int? OriginalInvoiceItemId { get; private set; }
        public bool IsReturnLine => OriginalInvoiceItemId.HasValue;
        public int MaxReturnQuantity { get; private set; } = int.MaxValue;
        private int _origQuantity = 1;
        private long _origDiscountPiastres;

        private UnitLevel _selectedUnitLevel = UnitLevel.Box;
        public UnitLevel SelectedUnitLevel
        {
            get => _selectedUnitLevel;
            set
            {
                if (IsReturnLine) return; // وحدة المرتجع ثابتة = وحدة البيع الأصلية
                if (SetProperty(ref _selectedUnitLevel, value))
                {
                    UpdatePriceBasedOnUnit();
                    Recalculate();
                }
            }
        }

        private int _quantity = 1;
        public int Quantity
        {
            get => _quantity;
            set
            {
                if (value < 1) value = 1;
                if (IsReturnLine && value > MaxReturnQuantity) value = MaxReturnQuantity;
                if (SetProperty(ref _quantity, value))
                {
                    if (IsReturnLine)
                    {
                        // خصم المرتجع = حصة الكمية المرتجعة من خصم البيع الأصلي
                        _discountPiastres = _origQuantity > 0
                            ? (_origDiscountPiastres * _quantity + (_origQuantity / 2)) / _origQuantity
                            : 0;
                        OnPropertyChanged(nameof(DiscountPiastres));
                    }
                    Recalculate();
                }
            }
        }

        private long _unitPricePiastres;
        public long UnitPricePiastres
        {
            get => _unitPricePiastres;
            set
            {
                if (SetProperty(ref _unitPricePiastres, value))
                {
                    Recalculate();
                }
            }
        }

        private long _discountPiastres;
        public long DiscountPiastres
        {
            get => _discountPiastres;
            set
            {
                if (SetProperty(ref _discountPiastres, value))
                {
                    Recalculate();
                }
            }
        }

        /// <summary>
        /// نسبة الضريبة المطبقة على البند (0% أو 5% أو 14% أو ما حددته إعدادات الصيدلية)
        /// </summary>
        private int _vatRatePercent = 14;
        public int VatRatePercent
        {
            get => _vatRatePercent;
            set
            {
                if (SetProperty(ref _vatRatePercent, value))
                {
                    Recalculate();
                }
            }
        }

        public string VatRateDisplay => $"{VatRatePercent}%";

        private long _vatPiastres;
        public long VatPiastres
        {
            get => _vatPiastres;
            private set => SetProperty(ref _vatPiastres, value);
        }

        private long _totalPiastres;
        public long TotalPiastres
        {
            get => _totalPiastres;
            private set => SetProperty(ref _totalPiastres, value);
        }

        // إجمالي ما سيخصم من المخزون بأصغر وحدة
        public int SmallestUnitsToDeduct => UnitBreakdownHelper.CalculateSmallestUnits(SelectedUnitLevel, Quantity, StripsPerBox, PiecesPerStrip);

        // تنبيه قرب النفاد
        public bool IsLowStock => TotalStockSmallestUnits <= MinStockAlertSmallestUnits;

        public string FormattedStockDisplay => UnitBreakdownHelper.FormatStockDetailed(TotalStockSmallestUnits, StripsPerBox, PiecesPerStrip);

        public string FormattedUnitPrice => Money.FromPiastres(UnitPricePiastres).ToFormattedString();
        public string FormattedVat => Money.FromPiastres(VatPiastres).ToFormattedString();
        public string FormattedTotal => Money.FromPiastres(TotalPiastres).ToFormattedString();
        public string FormattedExpiry => ExpiryDate.HasValue ? ExpiryDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "-";

        private readonly Item _originalItem;
        private readonly PriceCategory _currentCategory;

        public SalesItemRow(Item item, PriceCategory category, int defaultVatPercent = 14)
        {
            _originalItem = item;
            _currentCategory = category;

            ItemId = item.Id;
            Code = item.Code;
            Barcode = item.Barcode;
            NameAr = item.NameAr;
            ActiveIngredient = item.ActiveIngredient;
            StripsPerBox = item.StripsPerBox;
            PiecesPerStrip = item.PiecesPerStrip;
            MinStockAlertSmallestUnits = item.MinStockAlertSmallestUnits;
            TotalStockSmallestUnits = item.TotalStockSmallestUnits;
            VatRatePercent = item.GetEffectiveVatRate(defaultVatPercent);

            RequiresPrescription = item.IsPrescriptionRequired;

            // item.Batches = التشغيلات السارية فقط (غير المنتهية) مرتبة FEFO، فأولها هو ما سيُخصم منه
            if (item.Batches != null && item.Batches.Count > 0)
            {
                BatchNumber = item.Batches[0].BatchNumber;
                ExpiryDate = item.Batches[0].ExpiryDate;
            }

            UpdatePriceBasedOnUnit();
            Recalculate();
        }

        public void UpdatePriceBasedOnUnit()
        {
            if (IsReturnLine) return; // سعر المرتجع ثابت = سعر البيع الأصلي
            _unitPricePiastres = _originalItem.GetPriceForUnit(SelectedUnitLevel, _currentCategory);
            OnPropertyChanged(nameof(UnitPricePiastres));
            OnPropertyChanged(nameof(FormattedUnitPrice));
        }

        /// <summary>
        /// تحويل الصف إلى بند مرتجع من بند فاتورة بيع أصلية (نفس الوحدة والسعر والضريبة، والكمية حتى المتبقي للإرجاع)
        /// </summary>
        public void ConfigureAsReturnLine(InvoiceItem original, int remainingQuantity)
        {
            OriginalInvoiceItemId = original.Id;
            MaxReturnQuantity = Math.Max(1, remainingQuantity);
            _origQuantity = Math.Max(1, original.QuantitySold);
            _origDiscountPiastres = original.DiscountPiastres;

            _selectedUnitLevel = original.UnitLevel;
            OnPropertyChanged(nameof(SelectedUnitLevel));
            _unitPricePiastres = original.UnitPricePiastres;
            _vatRatePercent = original.VatRatePercent;
            OnPropertyChanged(nameof(UnitPricePiastres));
            OnPropertyChanged(nameof(VatRatePercent));

            BatchNumber = string.IsNullOrWhiteSpace(original.BatchNumber) ? "-" : original.BatchNumber;
            ExpiryDate = original.ExpiryDate;
            OnPropertyChanged(nameof(BatchNumber));
            OnPropertyChanged(nameof(FormattedExpiry));

            Quantity = MaxReturnQuantity;
            _discountPiastres = (_origDiscountPiastres * _quantity + (_origQuantity / 2)) / _origQuantity;
            OnPropertyChanged(nameof(DiscountPiastres));
            Recalculate();
        }

        public void Recalculate()
        {
            long grossSubtotal = UnitPricePiastres * Quantity;
            long taxableAmount = Math.Max(0, grossSubtotal - DiscountPiastres);

            // حساب الضريبة بنسبة البند المحددة مع تقريب Half-Up بالقروش
            VatPiastres = Money.CalculateVat(taxableAmount, VatRatePercent).Piastres;
            TotalPiastres = taxableAmount + VatPiastres;

            OnPropertyChanged(nameof(SmallestUnitsToDeduct));
            OnPropertyChanged(nameof(FormattedUnitPrice));
            OnPropertyChanged(nameof(FormattedVat));
            OnPropertyChanged(nameof(FormattedTotal));
            OnPropertyChanged(nameof(VatRateDisplay));
        }

        /// <summary>
        /// إنشاء صف عرض لبند من فاتورة سابقة
        /// </summary>
        public static SalesItemRow FromHistoricalItem(InvoiceItem item, Item? baseItem = null)
        {
            var fallbackItem = baseItem ?? new Item
            {
                Id = item.ItemId,
                Code = item.ItemCode,
                NameAr = item.ItemNameAr,
                Barcode = item.ItemCode
            };
            var row = new SalesItemRow(fallbackItem, PriceCategory.Retail, item.VatPercent)
            {
                SelectedUnitLevel = item.UnitLevel,
                Quantity = item.QuantitySold,
                UnitPricePiastres = item.UnitPricePiastres,
                DiscountPiastres = item.DiscountPiastres,
                BatchNumber = string.IsNullOrWhiteSpace(item.BatchNumber) ? "-" : item.BatchNumber,
                ExpiryDate = item.ExpiryDate,
                BatchId = item.BatchId
            };
            row.Recalculate();
            return row;
        }
    }
}
