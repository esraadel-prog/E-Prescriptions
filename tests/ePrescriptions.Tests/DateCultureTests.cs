using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 26: التواريخ ثابتة الثقافة في الكتابة والقراءة والعرض (مهما كانت لغة/تقويم ويندوز)</summary>
    public class DateCultureTests
    {
        /// <summary>ثقافة "عدائية": فاصل التاريخ والوقت نقطة، فأي تنسيق بدون ثقافة ثابتة هيطلع بنقط بدل - و/ و:</summary>
        private static CultureInfo HostileCulture()
        {
            var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            c.DateTimeFormat.DateSeparator = ".";
            c.DateTimeFormat.TimeSeparator = ".";
            return c;
        }

        private static void WithCulture(CultureInfo culture, Action action)
        {
            var old = Thread.CurrentThread.CurrentCulture;
            try { Thread.CurrentThread.CurrentCulture = culture; action(); }
            finally { Thread.CurrentThread.CurrentCulture = old; }
        }

        private static string Text(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToString(cmd.ExecuteScalar()) ?? "";
        }

        [Fact]
        public void DbDate_ShouldIgnoreCurrentCulture()
        {
            var d = new DateTime(2027, 3, 9, 14, 5, 7);
            WithCulture(HostileCulture(), () =>
            {
                DbDate.Date(d).Should().Be("2027-03-09");
                DbDate.Full(d).Should().Be("2027-03-09 14:05:07");
                DbDate.Iso(d).Should().Be("2027-03-09T14:05:07");
                DbDate.Minutes(d).Should().Be("2027-03-09 14:05");
                DbDate.Slash(d).Should().Be("2027/03/09");
                DbDate.SlashMinutes(d).Should().Be("2027/03/09 14:05");
                DbDate.Stamp(d).Should().Be("20270309_140507");
                DbDate.Time(d).Should().Be("14:05:07");
                DbDate.Parse("2027-03-09 14:05:07").Should().Be(d);
                DbDate.Parse("2027-03-09T14:05:07").Should().Be(d);
                DbDate.TryParse("2027-03-09", out var only).Should().BeTrue();
                only.Should().Be(new DateTime(2027, 3, 9));
            });
        }

        [Fact]
        public void RepositoryWritesAndReads_ShouldRoundTripUnderHostileCulture()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var items = new ItemRepository(db);
            var invoices = new InvoiceRepository(db, items);
            var shifts = new ShiftRepository(db);
            var staff = new StaffAuditLogRepository(db);

            WithCulture(HostileCulture(), () =>
            {
                int shiftId = shifts.OpenShift(1, "اختبار", 0);
                int itemId = items.GetByBarcodeOrCode("6281001000404")!.Id;
                items.AddBatch(itemId, "CULT-1", DateTime.Today.AddDays(200), 10, 800);

                var inv = new Invoice
                {
                    CustomerId = 1, ShiftId = shiftId, UserId = 1, Date = new DateTime(2027, 3, 9, 14, 5, 7),
                    PaymentMethod = PaymentMethod.Cash, TotalPiastres = 1000, SubtotalPiastres = 1000, PaidPiastres = 1000,
                    Items = { new InvoiceItem { ItemId = itemId, ItemCode = "MED-004", ItemNameAr = "نكسيوم", UnitLevel = UnitLevel.Piece, QuantitySold = 1, SmallestUnitsCount = 1, UnitPricePiastres = 1000, TotalPiastres = 1000 } }
                };
                invoices.SaveInvoice(inv, out string err).Should().BeTrue(err);
                staff.LogChange(1, "admin", "Admin", "Item", "1", "اختبار", "Name", "a", "b");

                // الاستعلام الفعلي بيقرأ نفس القيم بنفس الثقافة العدائية
                items.GetNearExpiryBatches(365).Should().NotBeEmpty();
                shifts.GetCurrentOpenShift(1)!.StartTime.Date.Should().Be(DateTime.Today);
                staff.GetRecent(10).Should().NotBeEmpty();
            });

            // وبعد كده بثقافة عادية: القيم المخزّنة لازم تكون بالصيغة الثابتة (مفيش نقط)
            var dateRegex = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$");
            dateRegex.IsMatch(Text(db, "SELECT Date FROM Invoices ORDER BY Id DESC LIMIT 1")).Should().BeTrue();
            dateRegex.IsMatch(Text(db, "SELECT StartTime FROM Shifts ORDER BY Id DESC LIMIT 1")).Should().BeTrue();
            dateRegex.IsMatch(Text(db, "SELECT ChangedAt FROM StaffAuditLog ORDER BY Id DESC LIMIT 1")).Should().BeTrue();
            Text(db, "SELECT Date FROM Invoices ORDER BY Id DESC LIMIT 1").Should().Be("2027-03-09 14:05:07");
            new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$").IsMatch(Text(db, "SELECT CreatedAt FROM ItemBatches WHERE BatchNumber = 'CULT-1'")).Should().BeTrue();
        }

        [Fact]
        public void SourceCode_ShouldNotUseCultureDependentDateFormattingOrParsing()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string? src = null;
            while (dir != null)
            {
                string c = Path.Combine(dir.FullName, "src");
                if (Directory.Exists(Path.Combine(c, "ePrescriptions.Data"))) { src = c; break; }
                dir = dir.Parent;
            }
            src.Should().NotBeNull();

            var toStringNoCulture = new Regex(@"\.ToString\(""(yyyy[^""]*|s|HH[^""]*)""\)");
            var interpolated = new Regex(@"\{[A-Za-z_][\w\.\[\]\(\)]*:(yyyy|HH)[^{}""]*\}");
            foreach (var file in Directory.GetFiles(src!, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
                string name = Path.GetFileName(file);
                if (name == "DbDate.cs") continue; // التنفيذ الوحيد المسموح له يستدعي Parse/ToString (بثقافة ثابتة)
                string text = File.ReadAllText(file);
                toStringNoCulture.IsMatch(text).Should().BeFalse("ToString بتنسيق تاريخ بدون ثقافة في " + name);
                interpolated.IsMatch(text).Should().BeFalse("تنسيق تاريخ داخل نص مدمج في " + name);
                text.Contains("DateTime.Parse(").Should().BeFalse("DateTime.Parse في " + name);
                text.Contains("DateTime.TryParse(").Should().BeFalse("DateTime.TryParse في " + name);
            }
        }
    }
}
