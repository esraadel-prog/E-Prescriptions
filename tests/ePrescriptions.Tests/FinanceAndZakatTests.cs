using System;
using ePrescriptions.Core.Financial;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class FinanceAndZakatTests
    {
        [Fact]
        public void Expense_ShouldRecordExpenseAndCreateJournalEntry()
        {
            // Arrange
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);

            var finRepo = new FinanceRepository(db);

            // Act: تسجيل مصروف كهرباء بقيمة 750.00 ج.م (75000 قرش/هللة)
            string expNum = finRepo.GenerateNextExpenseNumber();
            var exp = new Expense
            {
                ExpenseNumber = expNum,
                Category = "كهرباء ومياه",
                AmountPiastres = 75000,
                Date = DateTime.Today,
                PaidTo = "شركة الكهرباء",
                Notes = "فاتورة شهرية"
            };

            finRepo.AddExpense(exp);

            // Assert
            var expenses = finRepo.GetExpenses(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            expenses.Should().ContainSingle(e => e.ExpenseNumber == expNum && e.AmountPiastres == 75000);

            var jvs = finRepo.GetJournalEntries(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
            jvs.Should().Contain(j => j.CreditAccount == "الصندوق / الخزينة" && j.AmountPiastres == 75000);
        }

        [Fact]
        public void ZakatCalculation_ShouldAccuratelyDetermineNisabAndZakatDue()
        {
            // Arrange:
            // مخزون = 400,000 ج.م (40,000,000 قرش)
            // نقدية = 100,000 ج.م (10,000,000 قرش)
            // ديون مرجوة = 50,000 ج.م (5,000,000 قرش)
            // التزامات حالة = 150,000 ج.م (15,000,000 قرش)
            // وعاء الزكاة = (400,000 + 100,000 + 50,000) - 150,000 = 400,000 ج.م (40,000,000 قرش)
            var zakat = new ZakatCalculation
            {
                StockValuePiastres = 40000000,
                CashAndBankPiastres = 10000000,
                GoodReceivablesPiastres = 5000000,
                DuePayablesPiastres = 15000000,
                GoldGramPricePiastres = 380000, // 3,800.00 ج.م للجرام
                IsHijriYear = true
            };

            // Act & Assert
            // النصاب = 85 * 3800 = 323,000 ج.م (32,300,000 قرش)
            zakat.NisabPiastres.Should().Be(32300000);
            zakat.ZakatBasePiastres.Should().Be(40000000);
            zakat.IsZakatDue.Should().BeTrue();

            // الزكاة الهجرية 2.5% = 400,000 * 2.5% = 10,000.00 ج.م (1,000,000 قرش)
            zakat.ZakatAmountPiastres.Should().Be(1000000);

            // عند اختيار الحول الميلادي 2.577%:
            zakat.IsHijriYear = false;
            // 400,000 * 2.577% = 10,308.00 ج.م (1,030,800 قرش)
            zakat.ZakatAmountPiastres.Should().Be(1030800);
        }

        [Fact]
        public void ZakatCalculation_WhenBaseBelowNisab_ShouldNotBeDue()
        {
            var zakat = new ZakatCalculation
            {
                StockValuePiastres = 10000000, // 100,000 ج.م
                CashAndBankPiastres = 1000000,  // 10,000 ج.م
                GoodReceivablesPiastres = 0,
                DuePayablesPiastres = 0,
                GoldGramPricePiastres = 380000, // النصاب = 323,000 ج.م
                IsHijriYear = true
            };

            zakat.ZakatBasePiastres.Should().Be(11000000); // 110,000 ج.م
            zakat.IsZakatDue.Should().BeFalse();
            zakat.ZakatAmountPiastres.Should().Be(0);
        }

        [Fact]
        public void ShiftClosing_ShouldComputeCashDifferenceCorrectly()
        {
            // Arrange
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);

            var shiftRepo = new ShiftRepository(db);

            // Act: فتح وردية برصيد 200 ج.م (20000 قرش)
            int shiftId = shiftRepo.OpenShift(1, "كاشير الاختبار", 20000);
            var openShift = shiftRepo.GetCurrentOpenShift(1)!;
            openShift.ExpectedCashPiastres.Should().Be(20000);

            // إغلاق الوردية برصيد فعلي 180 ج.م (عجز 20 ج.م = -2000 قرش)
            shiftRepo.CloseShift(shiftId, 18000, "عجز بسيط في الصرف");

            // Assert
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ActualCashPiastres, DifferencePiastres, Status FROM Shifts WHERE Id = @sId;";
            cmd.Parameters.AddWithValue("@sId", shiftId);
            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue();

            Convert.ToInt64(reader["ActualCashPiastres"]).Should().Be(18000);
            Convert.ToInt64(reader["DifferencePiastres"]).Should().Be(-2000);
            Convert.ToInt32(reader["Status"]).Should().Be(2); // Closed
        }
    }
}
