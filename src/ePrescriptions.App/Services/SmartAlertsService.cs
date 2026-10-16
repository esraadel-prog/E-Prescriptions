using System.Collections.Generic;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.Services
{
    public class SmartAlertsSummary
    {
        public List<(Item Item, ItemBatch Batch)> NearExpiryBatches { get; set; } = new();
        public List<Item> LowStockItems { get; set; } = new();
        public List<Customer> OverCreditLimitCustomers { get; set; } = new();

        public int NearExpiryCount => NearExpiryBatches.Count;
        public int LowStockCount => LowStockItems.Count;
        public int OverCreditLimitCount => OverCreditLimitCustomers.Count;
        public int TotalAlertsCount => NearExpiryCount + LowStockCount + OverCreditLimitCount;
        public bool HasAnyAlerts => TotalAlertsCount > 0;
    }

    public interface ISmartAlertsService
    {
        SmartAlertsSummary GetAlerts(int withinDays = 90);
        bool HasAnyAlerts(int withinDays = 90);
    }

    public class SmartAlertsService : ISmartAlertsService
    {
        private readonly ItemRepository _itemRepository;
        private readonly CustomerRepository _customerRepository;

        public SmartAlertsService(ItemRepository itemRepository, CustomerRepository customerRepository)
        {
            _itemRepository = itemRepository;
            _customerRepository = customerRepository;
        }

        public SmartAlertsSummary GetAlerts(int withinDays = 90)
        {
            return new SmartAlertsSummary
            {
                NearExpiryBatches = _itemRepository.GetNearExpiryBatches(withinDays) ?? new(),
                LowStockItems = _itemRepository.GetLowStockItems() ?? new(),
                OverCreditLimitCustomers = _customerRepository.GetCreditLimitExceededCustomers() ?? new()
            };
        }

        public bool HasAnyAlerts(int withinDays = 90)
        {
            return GetAlerts(withinDays).HasAnyAlerts;
        }
    }
}
