using System;
using System.Collections.Generic;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 34: تنبيهات ذكية عند فتح البرنامج (صلاحية، نواقص، تجاوز حد الائتمان)</summary>
    public class SmartAlertsTests
    {
        private static DatabaseContext CreateDb()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            return db;
        }

        [Fact]
        public void GetCreditLimitExceededCustomers_ShouldReturnOnlyCustomersExceedingLimit_AndExcludeCashCustomer()
        {
            var db = CreateDb();
            var custRepo = new CustomerRepository(db);

            // 1. عميل متجاوز لحد الائتمان: الحد 1000 جنيه (100000 قرش) والرصيد 1500 جنيه (150000 قرش)
            int c1 = custRepo.AddCustomer(new Customer
            {
                Name = "عميل متجاوز 1",
                Phone = "01000000001",
                CreditLimitPiastres = 100000,
                CurrentBalancePiastres = 150000,
                IsActive = true
            });

            // 2. عميل غير متجاوز: الحد 2000 جنيه والرصيد 500 جنيه
            custRepo.AddCustomer(new Customer
            {
                Name = "عميل منتظم",
                Phone = "01000000002",
                CreditLimitPiastres = 200000,
                CurrentBalancePiastres = 50000,
                IsActive = true
            });

            // 3. عميل بحد ائتمان 0 ورصيد مدين 300 جنيه (متجاوز لأن حده صفر)
            int c3 = custRepo.AddCustomer(new Customer
            {
                Name = "عميل بدون حد ائتمان وعليه دين",
                Phone = "01000000003",
                CreditLimitPiastres = 0,
                CurrentBalancePiastres = 30000,
                IsActive = true
            });

            // 4. العميل النقدي العام الافتراضي (له رصيد مدين ولكن يجب استبعاده لأنه نقدي)
            int? defaultCashId = custRepo.GetDefaultCashCustomerId();
            if (defaultCashId.HasValue)
            {
                using var conn = db.CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Customers SET CurrentBalancePiastres = 99999 WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", defaultCashId.Value);
                cmd.ExecuteNonQuery();
            }

            var exceeded = custRepo.GetCreditLimitExceededCustomers();

            exceeded.Should().HaveCount(2);
            exceeded.Should().Contain(c => c.Id == c1);
            exceeded.Should().Contain(c => c.Id == c3);
            exceeded.Should().NotContain(c => c.IsDefaultCash);
        }

        [Fact]
        public void SmartAlertsService_ShouldDetectNearExpiryBatches_LowStock_AndExceededCreditLimits()
        {
            var db = CreateDb();
            var itemRepo = new ItemRepository(db);
            var custRepo = new CustomerRepository(db);

            // صنف 1: تشغيلة قاربت على الانتهاء (بعد 30 يوماً)
            int item1Id = itemRepo.SaveItem(new Item
            {
                Code = "MED-EXP-1",
                Barcode = "6220000000001",
                NameAr = "دواء قريب الانتهاء",
                MinStockAlertSmallestUnits = 5,
                TotalStockSmallestUnits = 20,
                IsActive = true
            });
            itemRepo.AddBatch(item1Id, "BATCH-01", DateTime.Today.AddDays(30), 10, 1000);

            // صنف 2: تشغيلة صلاحيتها بعيدة (بعد 300 يوماً)
            int item2Id = itemRepo.SaveItem(new Item
            {
                Code = "MED-SAFE-1",
                Barcode = "6220000000002",
                NameAr = "دواء آمن الصلاحية",
                MinStockAlertSmallestUnits = 5,
                TotalStockSmallestUnits = 20,
                IsActive = true
            });
            itemRepo.AddBatch(item2Id, "BATCH-02", DateTime.Today.AddDays(300), 20, 1000);

            // صنف 3: تحت الحد الأدنى للمخزون (الرصيد 2 والحد 10)
            int item3Id = itemRepo.SaveItem(new Item
            {
                Code = "MED-LOW-1",
                Barcode = "6220000000003",
                NameAr = "دواء ناقص المخزون",
                MinStockAlertSmallestUnits = 10,
                TotalStockSmallestUnits = 2,
                IsActive = true
            });
            itemRepo.AddBatch(item3Id, "BATCH-03", DateTime.Today.AddDays(200), 2, 1000);

            // عميل متجاوز لحد الائتمان
            custRepo.AddCustomer(new Customer
            {
                Name = "مستشفى الأمل",
                Phone = "01234567890",
                CreditLimitPiastres = 500000,
                CurrentBalancePiastres = 750000,
                IsActive = true
            });

            var service = new SmartAlertsService(itemRepo, custRepo);
            var summary = service.GetAlerts(withinDays: 90);

            summary.HasAnyAlerts.Should().BeTrue();
            summary.NearExpiryCount.Should().Be(1);
            summary.NearExpiryBatches[0].Item.Code.Should().Be("MED-EXP-1");

            summary.LowStockCount.Should().Be(1);
            summary.LowStockItems[0].Code.Should().Be("MED-LOW-1");

            summary.OverCreditLimitCount.Should().Be(1);
            summary.OverCreditLimitCustomers[0].Name.Should().Be("مستشفى الأمل");

            summary.TotalAlertsCount.Should().Be(3);
        }

        [Fact]
        public void SmartAlertsService_WhenNoAlertsExist_ShouldReturnEmptyAndHasAnyAlertsFalse()
        {
            var db = CreateDb();
            var itemRepo = new ItemRepository(db);
            var custRepo = new CustomerRepository(db);

            // صنف برصيد وفير وصلاحية بعيدة
            int itemId = itemRepo.SaveItem(new Item
            {
                Code = "MED-OK",
                Barcode = "6220000000099",
                NameAr = "صنف سليم",
                MinStockAlertSmallestUnits = 5,
                TotalStockSmallestUnits = 100,
                IsActive = true
            });
            itemRepo.AddBatch(itemId, "B-OK", DateTime.Today.AddDays(250), 100, 500);

            // عميل برصيد آمن
            custRepo.AddCustomer(new Customer
            {
                Name = "عميل آمن",
                Phone = "01111111111",
                CreditLimitPiastres = 200000,
                CurrentBalancePiastres = 50000,
                IsActive = true
            });

            var service = new SmartAlertsService(itemRepo, custRepo);
            var summary = service.GetAlerts();

            summary.HasAnyAlerts.Should().BeFalse();
            summary.TotalAlertsCount.Should().Be(0);
            summary.NearExpiryCount.Should().Be(0);
            summary.LowStockCount.Should().Be(0);
            summary.OverCreditLimitCount.Should().Be(0);
        }

        [Fact]
        public void SmartAlertsViewModel_ShouldPopulateItemsAndCalculateTotals_AndSelectFirstRelevantTab()
        {
            var summary = new SmartAlertsSummary
            {
                LowStockItems = new List<Item>
                {
                    new Item { Code = "IT-1", NameAr = "صنف ناقص", TotalStockSmallestUnits = 1, MinStockAlertSmallestUnits = 5 }
                },
                OverCreditLimitCustomers = new List<Customer>
                {
                    new Customer { Name = "عميل متجاوز", Phone = "01000", CurrentBalancePiastres = 20000, CreditLimitPiastres = 10000 }
                }
            };

            var vm = new SmartAlertsViewModel();
            vm.LoadAlerts(summary);

            vm.TotalAlertsCount.Should().Be(2);
            vm.NearExpiryCount.Should().Be(0);
            vm.LowStockCount.Should().Be(1);
            vm.OverCreditLimitCount.Should().Be(1);

            // يجب أن يختار تلقائياً التبويب رقم 1 (النواقص) لعدم وجود تنبيهات صلاحية
            vm.SelectedTabIndex.Should().Be(1);

            // التحقق من إطلاق حدث الإغلاق عند المتابعة
            bool closed = false;
            vm.RequestClose += () => closed = true;
            vm.ContinueCommand.Execute(null);
            closed.Should().BeTrue();
        }
    }
}
