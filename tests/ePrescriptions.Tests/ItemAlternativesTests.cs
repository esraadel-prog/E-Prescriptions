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
    /// <summary>
    /// خطوة 35: بدائل الصنف بالمادة الفعالة في شاشة المبيعات
    /// </summary>
    public class ItemAlternativesTests
    {
        private static DatabaseContext CreateDb()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            return db;
        }

        [Fact]
        public void GetAlternatives_ShouldFilterByActiveIngredient_ExcludeSelf_AndIncludeOnlyInStock()
        {
            var db = CreateDb();
            var itemRepo = new ItemRepository(db);

            // 1. صنف أصلي (نافد من المخزون)
            int itemAId = itemRepo.SaveItem(new Item
            {
                Code = "MED001",
                Barcode = "628000000001",
                NameAr = "بانادول أقراص",
                NameEn = "Panadol Tabs",
                ActiveIngredient = "Paracetamol",
                RetailPriceBoxPiastres = 2500,
                TotalStockSmallestUnits = 0,
                IsActive = true
            });

            // 2. بديل 1 في المخزون (100 وحدة بسعر 1500 قرش)
            int itemBId = itemRepo.SaveItem(new Item
            {
                Code = "MED002",
                Barcode = "628000000002",
                NameAr = "بارامول أقراص",
                NameEn = "Paramol Tabs",
                ActiveIngredient = "Paracetamol",
                RetailPriceBoxPiastres = 1500,
                TotalStockSmallestUnits = 100,
                IsActive = true
            });

            // 3. بديل 2 في المخزون (50 وحدة بسعر 2000 قرش)
            int itemCId = itemRepo.SaveItem(new Item
            {
                Code = "MED003",
                Barcode = "628000000003",
                NameAr = "سيتال أقراص",
                NameEn = "Cetal Tabs",
                ActiveIngredient = "Paracetamol",
                RetailPriceBoxPiastres = 2000,
                TotalStockSmallestUnits = 50,
                IsActive = true
            });

            // 4. صنف بنفس المادة لكن نافد من المخزون
            int itemDId = itemRepo.SaveItem(new Item
            {
                Code = "MED004",
                Barcode = "628000000004",
                NameAr = "أدول أقراص",
                NameEn = "Adol Tabs",
                ActiveIngredient = "Paracetamol",
                RetailPriceBoxPiastres = 1800,
                TotalStockSmallestUnits = 0,
                IsActive = true
            });

            // 5. صنف بنفس المادة ولديه رصيد لكنه معطّل (IsActive = 0)
            int itemEId = itemRepo.SaveItem(new Item
            {
                Code = "MED005",
                Barcode = "628000000005",
                NameAr = "بيرال معطل",
                NameEn = "Pyral Inactive",
                ActiveIngredient = "Paracetamol",
                RetailPriceBoxPiastres = 1200,
                TotalStockSmallestUnits = 30,
                IsActive = false
            });

            // 6. صنف بمادة فعالة مختلفة
            itemRepo.SaveItem(new Item
            {
                Code = "MED006",
                Barcode = "628000000006",
                NameAr = "بروفين 400",
                NameEn = "Brufen 400",
                ActiveIngredient = "Ibuprofen",
                RetailPriceBoxPiastres = 3500,
                TotalStockSmallestUnits = 80,
                IsActive = true
            });

            // استرجاع البدائل للصنف A
            var alts = itemRepo.GetAlternativesByActiveIngredient("Paracetamol", excludeItemId: itemAId);

            alts.Should().HaveCount(2);
            // مرتب بالأعلى رصيداً أولاً
            alts[0].Id.Should().Be(itemBId);
            alts[0].NameAr.Should().Be("بارامول أقراص");
            alts[1].Id.Should().Be(itemCId);
            alts[1].NameAr.Should().Be("سيتال أقراص");

            // الصنف المستبعد ونافد المخزون والمعطل لا يظهرون
            alts.Should().NotContain(x => x.Id == itemAId);
            alts.Should().NotContain(x => x.Id == itemDId);
            alts.Should().NotContain(x => x.Id == itemEId);
        }

        [Fact]
        public void GetAlternatives_ShouldBeCaseInsensitiveAndTrimmed()
        {
            var db = CreateDb();
            var itemRepo = new ItemRepository(db);

            int bId = itemRepo.SaveItem(new Item
            {
                Code = "AMX001",
                Barcode = "628000000010",
                NameAr = "أموكسيل 500",
                NameEn = "Amoxil 500",
                ActiveIngredient = "  Amoxicillin  ",
                RetailPriceBoxPiastres = 3000,
                TotalStockSmallestUnits = 40,
                IsActive = true
            });

            var alts = itemRepo.GetAlternativesByActiveIngredient("amoxicillin", excludeItemId: 999);
            alts.Should().HaveCount(1);
            alts[0].Id.Should().Be(bId);
        }

        [Fact]
        public void SalesViewModel_OutOfStockItem_ShouldTriggerAlternativesPanel()
        {
            var db = CreateDb();
            var itemRepo = new ItemRepository(db);
            var userRepo = new UserRepository(db);
            var shiftRepo = new ShiftRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var custRepo = new CustomerRepository(db);
            var setRepo = new SettingsRepository(db);
            var session = new UserSessionService(userRepo, shiftRepo);

            int savedOutOfStockId = itemRepo.SaveItem(new Item
            {
                Code = "MED010",
                Barcode = "628000000020",
                NameAr = "كونجستال",
                ActiveIngredient = "Paracetamol Compound",
                TotalStockSmallestUnits = 0,
                IsActive = true
            });

            int altId = itemRepo.SaveItem(new Item
            {
                Code = "MED011",
                Barcode = "628000000021",
                NameAr = "123 أقراص",
                ActiveIngredient = "Paracetamol Compound",
                TotalStockSmallestUnits = 25,
                RetailPriceBoxPiastres = 2200,
                IsActive = true
            });

            var outOfStockItem = itemRepo.GetById(savedOutOfStockId)!;

            var vm = new SalesViewModel(itemRepo, invRepo, custRepo, setRepo, null!, session);

            // إضافة الصنف النافد
            vm.AddItem(outOfStockItem);

            vm.IsAlternativesVisible.Should().BeTrue();
            vm.OutOfStockItemName.Should().Be("كونجستال");
            vm.OutOfStockActiveIngredient.Should().Be("Paracetamol Compound");
            vm.AlternativeItems.Should().HaveCount(1);
            vm.AlternativeItems[0].NameAr.Should().Be("123 أقراص");
            vm.AlternativeItems[0].RetailPricePounds.Should().Be(22.0m);
        }

        [Fact]
        public void SalesViewModel_AddAlternativeToCart_ShouldAddAlternativeAndHidePanel()
        {
            var db = CreateDb();
            var itemRepo = new ItemRepository(db);
            var userRepo = new UserRepository(db);
            var shiftRepo = new ShiftRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var custRepo = new CustomerRepository(db);
            var setRepo = new SettingsRepository(db);
            var session = new UserSessionService(userRepo, shiftRepo);

            int savedOutOfStockId = itemRepo.SaveItem(new Item
            {
                Code = "MED010",
                Barcode = "628000000020",
                NameAr = "كونجستال",
                ActiveIngredient = "Paracetamol Compound",
                TotalStockSmallestUnits = 0,
                IsActive = true
            });

            int altId = itemRepo.SaveItem(new Item
            {
                Code = "MED011",
                Barcode = "628000000021",
                NameAr = "123 أقراص",
                ActiveIngredient = "Paracetamol Compound",
                TotalStockSmallestUnits = 25,
                RetailPriceBoxPiastres = 2200,
                IsActive = true
            });

            var outOfStockItem = itemRepo.GetById(savedOutOfStockId)!;

            var vm = new SalesViewModel(itemRepo, invRepo, custRepo, setRepo, null!, session);
            vm.AddItem(outOfStockItem);

            vm.IsAlternativesVisible.Should().BeTrue();
            vm.AlternativeItems.Should().HaveCount(1);

            // الضغط على زر إضافة البديل
            vm.AddAlternativeToCartCommand.Execute(vm.AlternativeItems[0]);

            // التحقق من إخفاء اللوحة وإضافة البديل إلى السلة
            vm.IsAlternativesVisible.Should().BeFalse();
            vm.CartItems.Should().HaveCount(1);
            vm.CartItems[0].ItemId.Should().Be(altId);
            vm.CartItems[0].NameAr.Should().Be("123 أقراص");
        }

        [Fact]
        public void SalesViewModel_InStockItem_ShouldNotTriggerAlternativesPanel()
        {
            var db = CreateDb();
            var itemRepo = new ItemRepository(db);
            var userRepo = new UserRepository(db);
            var shiftRepo = new ShiftRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var custRepo = new CustomerRepository(db);
            var setRepo = new SettingsRepository(db);
            var session = new UserSessionService(userRepo, shiftRepo);

            var inStockItem = new Item
            {
                Id = 20,
                Code = "MED020",
                Barcode = "628000000030",
                NameAr = "أوجمنتين 1 جم",
                ActiveIngredient = "Amoxicillin Clavulanate",
                TotalStockSmallestUnits = 50,
                RetailPriceBoxPiastres = 9000,
                IsActive = true
            };

            var vm = new SalesViewModel(itemRepo, invRepo, custRepo, setRepo, null!, session);
            vm.AddItem(inStockItem);

            vm.IsAlternativesVisible.Should().BeFalse();
            vm.CartItems.Should().HaveCount(1);
            vm.CartItems[0].ItemId.Should().Be(20);
        }
    }
}
