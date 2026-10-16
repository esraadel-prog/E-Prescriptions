using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.App.Services;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.ViewModels
{
    public class ReportsViewModel : ViewModelBase
    {
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private readonly ReportRepository _reportRepository;
        private readonly SettingsRepository _settingsRepository;
        private readonly IExcelExportService _excelExportService;

        private DateTime _fromDate = DateTime.Today;
        public DateTime FromDate
        {
            get => _fromDate;
            set => SetProperty(ref _fromDate, value);
        }

        private DateTime _toDate = DateTime.Today;
        public DateTime ToDate
        {
            get => _toDate;
            set => SetProperty(ref _toDate, value);
        }

        private int _selectedPaymentMethodIndex = 0; // 0: الكل, 1: نقدي, 2: شبكة, 3: آجل, 4: تأمين
        public int SelectedPaymentMethodIndex
        {
            get => _selectedPaymentMethodIndex;
            set => SetProperty(ref _selectedPaymentMethodIndex, value);
        }

        private int _selectedReportTabIndex = 0;
        public int SelectedReportTabIndex
        {
            get => _selectedReportTabIndex;
            set
            {
                if (SetProperty(ref _selectedReportTabIndex, value))
                {
                    LoadActiveReport();
                }
            }
        }

        private SalesPeriodSummary _salesSummary = new();
        public SalesPeriodSummary SalesSummary
        {
            get => _salesSummary;
            set => SetProperty(ref _salesSummary, value);
        }

        private InventoryValuationSummary _valuationSummary = new();
        public InventoryValuationSummary ValuationSummary
        {
            get => _valuationSummary;
            set => SetProperty(ref _valuationSummary, value);
        }

        public ObservableCollection<SalesReportRow> SalesReportRows { get; } = new();
        public ObservableCollection<ItemMovementReportRow> ItemMovementRows { get; } = new();
        public ObservableCollection<LowStockReportRow> LowStockRows { get; } = new();
        public ObservableCollection<ExpiryReportRow> ExpiryRows { get; } = new();
        public ObservableCollection<CashierShiftSummaryRow> CashierShiftRows { get; } = new();
        public ObservableCollection<CashierDiscrepancySummary> CashierDiscrepancyRows { get; } = new();

        public IRelayCommand RefreshCommand { get; }
        public IRelayCommand FilterTodayCommand { get; }
        public IRelayCommand FilterYesterdayCommand { get; }
        public IRelayCommand FilterThisWeekCommand { get; }
        public IRelayCommand FilterThisMonthCommand { get; }
        public IRelayCommand ExportExcelCommand { get; }

        public ReportsViewModel(
            ReportRepository reportRepository,
            SettingsRepository settingsRepository,
            IExcelExportService excelExportService)
        {
            _reportRepository = reportRepository;
            _settingsRepository = settingsRepository;
            _excelExportService = excelExportService;

            Title = "التقارير والإحصاءات المتقدمة";

            RefreshCommand = new RelayCommand(LoadActiveReport);
            FilterTodayCommand = new RelayCommand(() => SetDateRange(DateTime.Today, DateTime.Today));
            FilterYesterdayCommand = new RelayCommand(() => SetDateRange(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(-1)));
            FilterThisWeekCommand = new RelayCommand(() => SetDateRange(DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek), DateTime.Today));
            FilterThisMonthCommand = new RelayCommand(() => SetDateRange(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today));
            ExportExcelCommand = new AsyncRelayCommand(ExportCurrentReportToExcelAsync);
        }

        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();
            LoadActiveReport();
        }

        private void SetDateRange(DateTime from, DateTime to)
        {
            FromDate = from;
            ToDate = to;
            LoadActiveReport();
        }

        public void LoadActiveReport()
        {
            IsBusy = true;
            try
            {
                PaymentMethod? method = SelectedPaymentMethodIndex switch
                {
                    1 => PaymentMethod.Cash,
                    2 => PaymentMethod.Network,
                    3 => PaymentMethod.OnAccount,
                    4 => PaymentMethod.Insurance,
                    _ => null
                };

                // 1) تحميل ملخص المبيعات العام
                SalesSummary = _reportRepository.GetSalesPeriodSummary(FromDate, ToDate);

                // 2) تحميل التقرير الخاص بالتبويب المفتوح
                switch (SelectedReportTabIndex)
                {
                    case 0: // مبيعات الفواتير
                        SalesReportRows.Clear();
                        var sales = _reportRepository.GetSalesReport(FromDate, ToDate, method);
                        foreach (var s in sales) SalesReportRows.Add(s);
                        StatusMessage = $"تم تحميل {SalesReportRows.Count} فاتورة مبيعات.";
                        break;

                    case 1: // حركة الأصناف والأكثر مبيعاً
                        ItemMovementRows.Clear();
                        var movements = _reportRepository.GetItemMovementReport(FromDate, ToDate);
                        foreach (var m in movements) ItemMovementRows.Add(m);
                        StatusMessage = $"تم تحميل حركة ومبيعات {ItemMovementRows.Count} صنفاً.";
                        break;

                    case 2: // النواقص وإعادة الطلب
                        LowStockRows.Clear();
                        var lows = _reportRepository.GetLowStockReport();
                        foreach (var l in lows) LowStockRows.Add(l);
                        StatusMessage = $"يوجد {LowStockRows.Count} أصناف وصلت أو دون حد الطلب الأدنى.";
                        break;

                    case 3: // صلاحيات الأدوية (FEFO)
                        ExpiryRows.Clear();
                        var exp = _reportRepository.GetNearExpiryReport(90);
                        foreach (var e in exp) ExpiryRows.Add(e);
                        StatusMessage = $"يوجد {ExpiryRows.Count} تشغيلات تنتهي خلال 90 يوماً أو منتهية.";
                        break;

                    case 4: // التقييم المالي للمخزون
                        ValuationSummary = _reportRepository.GetInventoryValuation();
                        StatusMessage = "تم احتساب القيمة المالية الحالية للمخزون بالكامل.";
                        break;

                    case 5: // ورديات الكاشيرات وعجز/زيادة الصندوق
                        CashierShiftRows.Clear();
                        var shifts = _reportRepository.GetCashierShiftsReport(FromDate, ToDate);
                        foreach (var sh in shifts) CashierShiftRows.Add(sh);

                        CashierDiscrepancyRows.Clear();
                        var disc = _reportRepository.GetCashierDiscrepancySummary(FromDate, ToDate);
                        foreach (var d in disc) CashierDiscrepancyRows.Add(d);

                        StatusMessage = $"تم تحميل {CashierShiftRows.Count} وردية لـ {CashierDiscrepancyRows.Count} كاشيراً للفترة المحددة.";
                        break;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"خطأ في استخراج التقرير: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>خطوة 28: توليد ملف Excel على خيط خلفي (الواجهة ما تتجمدش)</summary>
        private async Task ExportCurrentReportToExcelAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var settings = _settingsRepository.GetSettings();
                string pharmacyName = string.IsNullOrWhiteSpace(settings.PharmacyNameAr) ? PharmacySettings.DefaultPharmacyName : settings.PharmacyNameAr;
                string exportedFilePath = string.Empty;

                switch (SelectedReportTabIndex)
                {
                    case 0:
                        var salesList = new System.Collections.Generic.List<SalesReportRow>(SalesReportRows);
                        exportedFilePath = await _excelExportService.ExportSalesReportAsync(salesList, SalesSummary, FromDate, ToDate, pharmacyName);
                        break;

                    case 1:
                        var moveList = new System.Collections.Generic.List<ItemMovementReportRow>(ItemMovementRows);
                        exportedFilePath = await _excelExportService.ExportItemMovementReportAsync(moveList, FromDate, ToDate, pharmacyName);
                        break;

                    case 2:
                        var lowList = new System.Collections.Generic.List<LowStockReportRow>(LowStockRows);
                        exportedFilePath = await _excelExportService.ExportLowStockReportAsync(lowList, pharmacyName);
                        break;

                    case 3:
                        var expList = new System.Collections.Generic.List<ExpiryReportRow>(ExpiryRows);
                        exportedFilePath = await _excelExportService.ExportExpiryReportAsync(expList, pharmacyName);
                        break;

                    case 4:
                        exportedFilePath = await _excelExportService.ExportInventoryValuationReportAsync(ValuationSummary, pharmacyName);
                        break;

                    case 5:
                        var shiftsList = new System.Collections.Generic.List<CashierShiftSummaryRow>(CashierShiftRows);
                        var discList = new System.Collections.Generic.List<CashierDiscrepancySummary>(CashierDiscrepancyRows);
                        exportedFilePath = await _excelExportService.ExportCashierShiftsReportAsync(shiftsList, discList, FromDate, ToDate, pharmacyName);
                        break;
                }

                if (!string.IsNullOrEmpty(exportedFilePath) && File.Exists(exportedFilePath))
                {
                    StatusMessage = $"تم تصدير ملف Excel بنجاح إلى: {Path.GetFileName(exportedFilePath)}";
                    MessageBox.Show(
                        $"تم إنشاء ملف Excel بنجاح وتنسيقه بالكامل:\n{exportedFilePath}",
                        "تم التصدير بنجاح",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"فشل تصدير Excel: {ex.Message}";
                MessageBox.Show(
                    $"حدث خطأ أثناء تصدير ملف Excel:\n{ex.Message}",
                    "خطأ التصدير",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
