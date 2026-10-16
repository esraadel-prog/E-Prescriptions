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
    /// <summary>خطوة 3: لا بيع ولا مرتجع بدون وردية مفتوحة</summary>
    public class SalesShiftTests
    {
#pragma warning disable CS0067
        private class FakeSession : IUserSessionService
        {
            public User? CurrentUser { get; set; }
            public Shift? CurrentShift { get; set; }
            public bool IsAuthenticated => CurrentUser != null;
            public bool IsLocked => false;
            public event Action? SessionChanged;
            public event Action<bool>? LockStateChanged;
            public bool Login(string username, string password, out string errorMessage) { errorMessage = string.Empty; return false; }
            public void Logout() { CurrentUser = null; CurrentShift = null; }
            public void Lock() { }
            public bool Unlock(string password, out string errorMessage) { errorMessage = string.Empty; return false; }
            public void SetCurrentShift(Shift? shift) { CurrentShift = shift; }
        }
#pragma warning restore CS0067

        private static long Count(DatabaseContext db, string table)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        private static Invoice BuildSale(Item item, int shiftId, int userId) => new Invoice
        {
            InvoiceNumber = "SH-" + Guid.NewGuid().ToString("N").Substring(0, 6),
            Type = InvoiceType.Sales,
            CustomerName = "عميل",
            PaymentMethod = PaymentMethod.Cash,
            UserId = userId,
            UserName = "اختبار",
            ShiftId = shiftId,
            SubtotalPiastres = 1000,
            TotalPiastres = 1000,
            PaidPiastres = 1000,
            Items =
            {
                new InvoiceItem
                {
                    ItemId = item.Id, ItemCode = item.Code, ItemNameAr = item.NameAr,
                    UnitLevel = UnitLevel.Piece, QuantitySold = 1, SmallestUnitsCount = 1,
                    UnitPricePiastres = 1000, TotalPiastres = 1000
                }
            }
        };

        [Fact]
        public void Repository_ShouldRejectSaleWithoutShiftOrWithClosedShift_AndRecordNothing()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var itemRepo = new ItemRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var shiftRepo = new ShiftRepository(db);
            var item = itemRepo.GetByBarcodeOrCode("6281001000101")!;
            int stockBefore = itemRepo.GetById(item.Id)!.TotalStockSmallestUnits;

            invRepo.SaveInvoice(BuildSale(item, 0, 1), out string noShiftErr).Should().BeFalse();
            noShiftErr.Should().Contain("وردية");

            int shiftId = shiftRepo.OpenShift(1, "اختبار", 0);
            shiftRepo.CloseShift(shiftId, 0, null);
            invRepo.SaveInvoice(BuildSale(item, shiftId, 1), out string closedErr).Should().BeFalse();
            closedErr.Should().Contain("وردية");

            Count(db, "Invoices").Should().Be(0);
            itemRepo.GetById(item.Id)!.TotalStockSmallestUnits.Should().Be(stockBefore);
        }

        [Fact]
        public void SalesViewModel_ShouldRefuseSaleUntilShiftIsOpened_ThenAllowIt()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var itemRepo = new ItemRepository(db);
            var invRepo = new InvoiceRepository(db, itemRepo);
            var userRepo = new UserRepository(db);
            var shiftRepo = new ShiftRepository(db);
            var session = new FakeSession { CurrentUser = userRepo.Authenticate(DatabaseContext.OwnerUsername, DatabaseContext.DefaultOwnerPassword) };
            session.CurrentUser.Should().NotBeNull();

            var vm = new SalesViewModel(itemRepo, invRepo, new CustomerRepository(db), new SettingsRepository(db), null, session, shiftRepo);
            vm.AddItem(itemRepo.GetByBarcodeOrCode("6281001000101"));

            vm.HasOpenShift.Should().BeFalse();
            vm.SaveCurrentInvoice();
            Count(db, "Invoices").Should().Be(0);
            vm.IsStatusSuccess.Should().BeFalse();
            vm.StatusMessage.Should().Contain("وردية");

            vm.OpeningBalancePounds = 100;
            vm.OpenShiftFromSales();
            vm.HasOpenShift.Should().BeTrue();
            session.CurrentShift!.OpeningBalancePiastres.Should().Be(10000);

            vm.SaveCurrentInvoice();
            Count(db, "Invoices").Should().Be(1);
        }
    }
}
