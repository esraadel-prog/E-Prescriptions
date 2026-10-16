using System;
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
    /// <summary>خطوة 10: الأصناف التي تتطلب وصفة لا تُباع بدون رقم وصفة واسم طبيب</summary>
    public class PrescriptionTests
    {
#pragma warning disable CS0067
        private class Session : IUserSessionService
        {
            public User? CurrentUser { get; set; }
            public Shift? CurrentShift { get; set; }
            public bool IsAuthenticated => CurrentUser != null;
            public bool IsLocked => false;
            public event Action? SessionChanged;
            public event Action<bool>? LockStateChanged;
            public bool Login(string u, string p, out string e) { e = string.Empty; return false; }
            public void Logout() { }
            public void Lock() { }
            public bool Unlock(string p, out string e) { e = string.Empty; return false; }
            public void SetCurrentShift(Shift? s) { CurrentShift = s; }
        }
#pragma warning restore CS0067

        private static long Scalar(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        private static (DatabaseContext Db, ItemRepository Items, InvoiceRepository Invoices, int ShiftId, Item RxItem) Setup()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var items = new ItemRepository(db);
            var invoices = new InvoiceRepository(db, items);
            int shiftId = new ShiftRepository(db).OpenShift(1, "اختبار", 0);

            int id = items.SaveItem(new Item
            {
                Code = "RX-1", Barcode = "6229999000001", NameAr = "مضاد حيوي بوصفة", StripsPerBox = 1, PiecesPerStrip = 10,
                RetailPriceBoxPiastres = 5000, CostPriceBoxPiastres = 4000, IsActive = true, IsPrescriptionRequired = true
            });
            items.AddBatch(new ItemBatch { ItemId = id, BatchNumber = "RXB", ExpiryDate = DateTime.Today.AddDays(300), RemainingSmallestUnits = 50, CostPricePerSmallestUnitPiastres = 400 });
            return (db, items, invoices, shiftId, items.GetById(id)!);
        }

        private static Invoice Sale(Item item, int shiftId, string? rx, string? doctor) => new Invoice
        {
            InvoiceNumber = "RXI-" + Guid.NewGuid().ToString("N").Substring(0, 6),
            Type = InvoiceType.Sales, CustomerName = "عميل", PaymentMethod = PaymentMethod.Cash,
            UserId = 1, UserName = "اختبار", ShiftId = shiftId,
            PrescriptionNumber = rx, DoctorName = doctor,
            SubtotalPiastres = 500, TotalPiastres = 500, PaidPiastres = 500,
            Items = { new InvoiceItem { ItemId = item.Id, ItemCode = item.Code, ItemNameAr = item.NameAr, UnitLevel = UnitLevel.Piece, QuantitySold = 1, SmallestUnitsCount = 1, UnitPricePiastres = 500, TotalPiastres = 500 } }
        };

        [Fact]
        public void Repository_ShouldRejectPrescriptionItemWithoutRxNumberOrDoctor_AndAcceptWithBoth()
        {
            var (db, items, invoices, shiftId, rxItem) = Setup();

            invoices.SaveInvoice(Sale(rxItem, shiftId, null, null), out string e1).Should().BeFalse();
            e1.Should().Contain("وصفة");
            invoices.SaveInvoice(Sale(rxItem, shiftId, "RX-100", ""), out _).Should().BeFalse();
            invoices.SaveInvoice(Sale(rxItem, shiftId, "", "د. أحمد"), out _).Should().BeFalse();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);
            items.GetById(rxItem.Id)!.TotalStockSmallestUnits.Should().Be(50);

            var ok = Sale(rxItem, shiftId, "RX-100", "د. أحمد");
            invoices.SaveInvoice(ok, out string e2).Should().BeTrue();
            e2.Should().BeEmpty();
            Scalar(db, $"SELECT COUNT(*) FROM InvoiceItems WHERE Id={ok.Items[0].Id} AND PrescriptionNumber='RX-100' AND DoctorName='د. أحمد'").Should().Be(1);
        }

        [Fact]
        public void NormalItems_ShouldNotRequirePrescription()
        {
            var (db, items, invoices, shiftId, _) = Setup();
            var normal = items.GetByBarcodeOrCode("6281001000101")!;

            invoices.SaveInvoice(Sale(normal, shiftId, null, null), out _).Should().BeTrue();
        }

        [Fact]
        public void SalesViewModel_ShouldBlockPrescriptionItemUntilRxAndDoctorEntered()
        {
            var (db, items, invoices, shiftId, rxItem) = Setup();
            var shiftRepo = new ShiftRepository(db);
            var session = new Session
            {
                CurrentUser = new UserRepository(db).Authenticate(DatabaseContext.OwnerUsername, DatabaseContext.DefaultOwnerPassword),
                CurrentShift = shiftRepo.GetCurrentOpenShift(1)
            };
            var vm = new SalesViewModel(items, invoices, new CustomerRepository(db), new SettingsRepository(db), null, session, shiftRepo);

            vm.AddItem(items.GetById(rxItem.Id));
            vm.CartItems[0].RequiresPrescription.Should().BeTrue();
            vm.CartItems[0].WarningText.Should().Contain("وصفة");

            vm.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);
            vm.IsStatusSuccess.Should().BeFalse();
            vm.StatusMessage.Should().Contain("وصفة");

            vm.PrescriptionNumber = "RX-555";
            vm.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(0);

            vm.DoctorName = "د. منى";
            vm.SaveCurrentInvoice();
            Scalar(db, "SELECT COUNT(*) FROM Invoices").Should().Be(1);
            Scalar(db, "SELECT COUNT(*) FROM InvoiceItems WHERE PrescriptionNumber='RX-555' AND DoctorName='د. منى'").Should().Be(1);
        }
    }
}
