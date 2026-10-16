using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.ViewModels
{
    public class ZakatViewModel : ViewModelBase
    {
        private readonly FinanceRepository _financeRepository;

        // خيارات الحساب
        private bool _evaluateAtCost = true;
        public bool EvaluateAtCost
        {
            get => _evaluateAtCost;
            set
            {
                if (SetProperty(ref _evaluateAtCost, value))
                {
                    RecalculateZakat();
                }
            }
        }

        private bool _isHijriYear = true;
        public bool IsHijriYear
        {
            get => _isHijriYear;
            set
            {
                if (SetProperty(ref _isHijriYear, value))
                {
                    RecalculateZakat();
                }
            }
        }

        // سعر جرام الذهب عيار 24 بالجنيه المصري: إدخال إلزامي بدون قيمة افتراضية (خطوة 29).
        // null = لسه ما اتدخلش. بيتحمّل من آخر سعر محفوظ، وبيتحفظ بزر "حفظ السعر".
        private decimal? _goldGramPricePounds;
        public decimal? GoldGramPricePounds
        {
            get => _goldGramPricePounds;
            set
            {
                if (SetProperty(ref _goldGramPricePounds, value))
                {
                    OnPropertyChanged(nameof(HasValidGoldPrice));
                    OnPropertyChanged(nameof(HasUnsavedGoldPrice));
                    SaveGoldPriceCommand.NotifyCanExecuteChanged();
                    RecalculateZakat();
                }
            }
        }

        /// <summary>true لو السعر المدخل أكبر من صفر (شرط لحساب النصاب)</summary>
        public bool HasValidGoldPrice => GoldGramPricePounds.HasValue && GoldGramPricePounds.Value > 0m;

        private long? _savedGoldPricePiastres;
        private DateTime? _savedGoldPriceAt;

        /// <summary>السعر المدخل مختلف عن المحفوظ (أو لسه ما اتحفظش)</summary>
        public bool HasUnsavedGoldPrice =>
            HasValidGoldPrice && _savedGoldPricePiastres != Money.FromPounds(GoldGramPricePounds!.Value).Piastres;

        public string SavedGoldPriceInfo => _savedGoldPricePiastres.HasValue
            ? $"آخر سعر محفوظ: {Money.FromPiastres(_savedGoldPricePiastres.Value).ToFormattedString()} للجرام بتاريخ {(_savedGoldPriceAt.HasValue ? DbDate.Slash(_savedGoldPriceAt.Value) : "-")}"
            : "لم يُحفظ سعر جرام الذهب بعد.";

        // نتائج الحساب الشرعي
        private ZakatCalculation _zakatResult = new();
        public ZakatCalculation ZakatResult
        {
            get => _zakatResult;
            set => SetProperty(ref _zakatResult, value);
        }

        public string FormattedStockValue => Money.FromPiastres(ZakatResult.StockValuePiastres).ToFormattedString();
        public string FormattedCashAndBank => Money.FromPiastres(ZakatResult.CashAndBankPiastres).ToFormattedString();
        public string FormattedReceivables => Money.FromPiastres(ZakatResult.GoodReceivablesPiastres).ToFormattedString();
        public string FormattedPayables => Money.FromPiastres(ZakatResult.DuePayablesPiastres).ToFormattedString();
        public string FormattedZakatBase => HasValidGoldPrice ? Money.FromPiastres(ZakatResult.ZakatBasePiastres).ToFormattedString() : "—";
        public string FormattedNisab => HasValidGoldPrice ? Money.FromPiastres(ZakatResult.NisabPiastres).ToFormattedString() : "—";
        public string FormattedZakatDue => HasValidGoldPrice ? Money.FromPiastres(ZakatResult.ZakatAmountPiastres).ToFormattedString() : "—";

        public bool IsZakatDue => HasValidGoldPrice && ZakatResult.IsZakatDue;
        public string ZakatStatusText => !HasValidGoldPrice
            ? "أدخل سعر جرام الذهب عيار 24 (أكبر من صفر) ثم احفظه عشان يتحسب النصاب والزكاة."
            : IsZakatDue
                ? "بلغ وعاء الزكاة النصاب الشرعي وتجب الزكاة."
                : "لم يبلغ وعاء الزكاة النصاب الشرعي (أقل من قيمة 85 جم ذهب).";

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public IRelayCommand RecalculateCommand { get; }
        public IRelayCommand SaveGoldPriceCommand { get; }

        public ZakatViewModel(FinanceRepository financeRepository)
        {
            Title = "حاسبة الزكاة الشرعية للصيدلية";
            _financeRepository = financeRepository;

            RecalculateCommand = new RelayCommand(RecalculateZakat);
            SaveGoldPriceCommand = new RelayCommand(() => SaveGoldPrice(), () => HasValidGoldPrice);

            // آخر سعر محفوظ (لو فيه). مفيش سعر افتراضي لو مفيش محفوظ.
            var saved = _financeRepository.GetZakatGoldPrice();
            if (saved != null)
            {
                _savedGoldPricePiastres = saved.GramPricePiastres;
                _savedGoldPriceAt = saved.UpdatedAt;
                _goldGramPricePounds = Money.FromPiastres(saved.GramPricePiastres).ToPounds();
            }

            RecalculateZakat();
        }

        /// <summary>يحفظ السعر المدخل. false (ومفيش حفظ) لو السعر فاضي أو صفر أو سالب.</summary>
        public bool SaveGoldPrice()
        {
            if (!HasValidGoldPrice)
            {
                StatusMessage = "أدخل سعر جرام الذهب (أكبر من صفر) قبل الحفظ.";
                return false;
            }

            try
            {
                long piastres = Money.FromPounds(GoldGramPricePounds!.Value).Piastres;
                if (piastres <= 0)
                {
                    StatusMessage = "سعر جرام الذهب صغير جداً ولا يمكن حفظه.";
                    return false;
                }

                _financeRepository.SaveZakatGoldPrice(piastres);
                var saved = _financeRepository.GetZakatGoldPrice();
                _savedGoldPricePiastres = saved?.GramPricePiastres ?? piastres;
                _savedGoldPriceAt = saved?.UpdatedAt ?? DateTime.Now;

                OnPropertyChanged(nameof(SavedGoldPriceInfo));
                OnPropertyChanged(nameof(HasUnsavedGoldPrice));
                StatusMessage = "تم حفظ سعر جرام الذهب.";
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "ZakatViewModel.SaveGoldPrice");
                StatusMessage = $"تعذر حفظ سعر الذهب: {ex.Message}";
                return false;
            }
        }

        public void RecalculateZakat()
        {
            if (HasValidGoldPrice)
            {
                long goldGramPiastres = Money.FromPounds(GoldGramPricePounds!.Value).Piastres;
                ZakatResult = _financeRepository.CalculateZakat(EvaluateAtCost, goldGramPiastres, IsHijriYear);
                StatusMessage = "تم احتساب وعاء ونصاب الزكاة الشرعية وفق أحدث أرصدة الصيدلية.";
            }
            else
            {
                // بدون سعر ذهب مفيش نصاب: ما نعرضش نتيجة (ولا نسبة "غير واجبة" مضللة)
                ZakatResult = new ZakatCalculation { EvaluateAtCost = EvaluateAtCost, IsHijriYear = IsHijriYear };
                StatusMessage = "أدخل سعر جرام الذهب لحساب الزكاة.";
            }

            OnPropertyChanged(nameof(FormattedStockValue));
            OnPropertyChanged(nameof(FormattedCashAndBank));
            OnPropertyChanged(nameof(FormattedReceivables));
            OnPropertyChanged(nameof(FormattedPayables));
            OnPropertyChanged(nameof(FormattedZakatBase));
            OnPropertyChanged(nameof(FormattedNisab));
            OnPropertyChanged(nameof(FormattedZakatDue));
            OnPropertyChanged(nameof(IsZakatDue));
            OnPropertyChanged(nameof(ZakatStatusText));
            OnPropertyChanged(nameof(HasValidGoldPrice));
            OnPropertyChanged(nameof(HasUnsavedGoldPrice));
            OnPropertyChanged(nameof(SavedGoldPriceInfo));
        }
    }
}
