using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.App.Services;

namespace ePrescriptions.App.ViewModels
{
    public class NearExpiryAlertItem
    {
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string BatchNumber { get; set; } = string.Empty;
        public string ExpiryDate { get; set; } = string.Empty;
        public int RemainingUnits { get; set; }
        public int DaysRemaining { get; set; }
        public bool IsExpired => DaysRemaining < 0;
        public string StatusText => IsExpired ? "منتهي الصلاحية" : $"متبقي {DaysRemaining} يوم";
    }

    public class LowStockAlertItem
    {
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int CurrentStock { get; set; }
        public int MinStockAlert { get; set; }
        public int Deficit => Math.Max(0, MinStockAlert - CurrentStock);
    }

    public class CreditLimitAlertItem
    {
        public string CustomerName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public decimal CurrentBalancePounds { get; set; }
        public decimal CreditLimitPounds { get; set; }
        public decimal ExcessPounds => Math.Max(0, CurrentBalancePounds - CreditLimitPounds);
    }

    public class SmartAlertsViewModel : ObservableObject
    {
        private readonly ISmartAlertsService? _alertsService;

        public event Action? RequestClose;

        public ObservableCollection<NearExpiryAlertItem> NearExpiryItems { get; } = new();
        public ObservableCollection<LowStockAlertItem> LowStockItems { get; } = new();
        public ObservableCollection<CreditLimitAlertItem> OverCreditLimitItems { get; } = new();

        private int _nearExpiryCount;
        public int NearExpiryCount
        {
            get => _nearExpiryCount;
            set => SetProperty(ref _nearExpiryCount, value);
        }

        private int _lowStockCount;
        public int LowStockCount
        {
            get => _lowStockCount;
            set => SetProperty(ref _lowStockCount, value);
        }

        private int _overCreditLimitCount;
        public int OverCreditLimitCount
        {
            get => _overCreditLimitCount;
            set => SetProperty(ref _overCreditLimitCount, value);
        }

        private int _totalAlertsCount;
        public int TotalAlertsCount
        {
            get => _totalAlertsCount;
            set => SetProperty(ref _totalAlertsCount, value);
        }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public IRelayCommand ContinueCommand { get; }

        public SmartAlertsViewModel()
        {
            ContinueCommand = new RelayCommand(OnContinue);
        }

        public SmartAlertsViewModel(ISmartAlertsService alertsService) : this()
        {
            _alertsService = alertsService;
        }

        public void LoadAlerts(SmartAlertsSummary? summary = null)
        {
            summary ??= _alertsService?.GetAlerts() ?? new SmartAlertsSummary();

            NearExpiryItems.Clear();
            foreach (var (item, batch) in summary.NearExpiryBatches)
            {
                int days = (batch.ExpiryDate.Date - DateTime.Today).Days;
                NearExpiryItems.Add(new NearExpiryAlertItem
                {
                    ItemCode = item.Code,
                    ItemName = item.NameAr,
                    BatchNumber = batch.BatchNumber,
                    ExpiryDate = ePrescriptions.Core.Helpers.DbDate.Date(batch.ExpiryDate),
                    RemainingUnits = batch.RemainingSmallestUnits,
                    DaysRemaining = days
                });
            }

            LowStockItems.Clear();
            foreach (var item in summary.LowStockItems)
            {
                LowStockItems.Add(new LowStockAlertItem
                {
                    ItemCode = item.Code,
                    ItemName = item.NameAr,
                    CurrentStock = item.TotalStockSmallestUnits,
                    MinStockAlert = item.MinStockAlertSmallestUnits
                });
            }

            OverCreditLimitItems.Clear();
            foreach (var cust in summary.OverCreditLimitCustomers)
            {
                OverCreditLimitItems.Add(new CreditLimitAlertItem
                {
                    CustomerName = cust.Name,
                    Phone = cust.Phone,
                    CurrentBalancePounds = cust.CurrentBalancePiastres / 100.0m,
                    CreditLimitPounds = cust.CreditLimitPiastres / 100.0m
                });
            }

            NearExpiryCount = NearExpiryItems.Count;
            LowStockCount = LowStockItems.Count;
            OverCreditLimitCount = OverCreditLimitItems.Count;
            TotalAlertsCount = NearExpiryCount + LowStockCount + OverCreditLimitCount;

            // تحديد التبويب الأول الذي يحتوي على تنبيهات
            if (NearExpiryCount > 0) SelectedTabIndex = 0;
            else if (LowStockCount > 0) SelectedTabIndex = 1;
            else if (OverCreditLimitCount > 0) SelectedTabIndex = 2;
            else SelectedTabIndex = 0;
        }

        private void OnContinue()
        {
            RequestClose?.Invoke();
        }
    }
}
