using System;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 23: عميل نقدي بعلامة IsDefaultCash بدل افتراض إنه رقم 1</summary>
    public class DefaultCashCustomerTests
    {
        private static long Scalar(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        private static void Exec(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static Invoice OnAccountSale(int customerId, int itemId, long total, int shiftId) => new Invoice
        {
            InvoiceNumber = "DC-" + Guid.NewGuid().ToString("N").Substring(0, 6),
            Type = InvoiceType.Sales, CustomerId = customerId, CustomerName = "عميل",
            PaymentMethod = PaymentMethod.OnAccount, UserId = 1, UserName = "اختبار", ShiftId = shiftId,
            SubtotalPiastres = total, TotalPiastres = total, PaidPiastres = 0, RemainingPiastres = total,
            Items = { new InvoiceItem { ItemId = itemId, ItemCode = "MED-004", ItemNameAr = "نكسيوم", UnitLevel = UnitLevel.Piece, QuantitySold = 1, SmallestUnitsCount = 1, UnitPricePiastres = total, TotalPiastres = total } }
        };

        [Fact]
        public void Migration015_ShouldAddColumnAndFlagOnlyTheCashCustomer()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            Scalar(db, "SELECT COUNT(*) FROM pragma_table_info('Customers') WHERE name = 'IsDefaultCash'").Should().Be(1);
            Scalar(db, "SELECT IsDefaultCash FROM Customers WHERE Id = 1").Should().Be(1);
            Scalar(db, "SELECT IsDefaultCash FROM Customers WHERE Id = 2").Should().Be(0);

            var repo = new CustomerRepository(db);
            repo.GetDefaultCashCustomerId().Should().Be(1);
            repo.GetById(1)!.IsDefaultCash.Should().BeTrue();
            repo.GetById(2)!.IsDefaultCash.Should().BeFalse();
        }

        [Fact]
        public void OnlyOneDefaultCashCustomer_ShouldBeAllowed()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            Action second = () => Exec(db, "INSERT INTO Customers (Name, Phone, CreditLimitPiastres, CurrentBalancePiastres, PriceCategory, IsActive, IsDefaultCash) VALUES ('نقدي تاني', '', 0, 0, 1, 1, 1);");
            second.Should().Throw<Exception>();
        }

        [Fact]
        public void EnsureCashCustomer_ShouldBeIdempotent_AndCreateOneFlaggedCustomer()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            db.EnsureCashCustomer();
            db.EnsureCashCustomer();
            Scalar(db, "SELECT COUNT(*) FROM Customers WHERE IsDefaultCash = 1").Should().Be(1);
            Scalar(db, "SELECT COUNT(*) FROM Customers").Should().Be(1);
        }

        [Fact]
        public void WhenId1BelongsToARealCustomer_CashCustomerGetsAnotherId_AndCreditRulesStillApplyToCustomer1()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            // نحاكي قاعدة فيها عميل حقيقي برقم 1 (مش العميل النقدي) وعميل نقدي برقم تاني
            Exec(db, "UPDATE Customers SET IsDefaultCash = 0, Name = 'أحمد', CreditLimitPiastres = 1000 WHERE Id = 1;");
            db.EnsureCashCustomer();
            var repo = new CustomerRepository(db);
            int cashId = repo.GetDefaultCashCustomerId()!.Value;
            cashId.Should().NotBe(1);

            var items = new ItemRepository(db);
            var invoices = new InvoiceRepository(db, items);
            int shiftId = new ShiftRepository(db).OpenShift(1, "اختبار", 0);
            int itemId = items.GetByBarcodeOrCode("6281001000404")!.Id;

            // العميل رقم 1 بقى عميل عادي: حد الائتمان بيتطبّق عليه (قبل الخطوة كان بيتعامل كنقدي ويعدّي)
            var over = OnAccountSale(1, itemId, 5000, shiftId);
            invoices.SaveInvoice(over, out string err).Should().BeFalse();
            err.Should().Contain(InvoiceRepository.CreditLimitErrorPrefix);

            // العميل النقدي (الجديد): مفيش حد ائتمان ولا رصيد بيتحدّث
            var cashSale = OnAccountSale(cashId, itemId, 5000, shiftId);
            invoices.SaveInvoice(cashSale, out string err2).Should().BeTrue(err2);
            Scalar(db, $"SELECT CurrentBalancePiastres FROM Customers WHERE Id = {cashId}").Should().Be(0);
        }
    }
}
