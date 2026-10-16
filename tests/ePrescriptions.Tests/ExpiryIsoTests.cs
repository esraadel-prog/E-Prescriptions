using System;
using System.IO;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 25: ExpiryDate بيتخزّن ويتقارن كنص ISO (yyyy-MM-dd) مباشرة بدون date() في WHERE/ORDER BY</summary>
    public class ExpiryIsoTests
    {
        private static void Exec(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static long Scalar(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        private static string Text(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToString(cmd.ExecuteScalar()) ?? "";
        }

        private static (DatabaseContext db, ItemRepository repo, int itemId) NewDbWithItem()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            var repo = new ItemRepository(db);
            int id = repo.SaveItem(new Item { Code = "E1", Barcode = "622E1", NameAr = "صنف صلاحية", StripsPerBox = 1, PiecesPerStrip = 10, RetailPriceBoxPiastres = 1000, CostPriceBoxPiastres = 800, IsActive = true });
            return (db, repo, id);
        }

        [Fact]
        public void ToIso_ShouldBeInvariantCultureAndSortableAsText()
        {
            var old = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
                ExpiryPolicy.ToIso(new DateTime(2027, 3, 9)).Should().Be("2027-03-09");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = old; }

            string.CompareOrdinal(ExpiryPolicy.ToIso(new DateTime(2026, 12, 31)), ExpiryPolicy.ToIso(new DateTime(2027, 1, 1))).Should().BeLessThan(0);
        }

        [Fact]
        public void Repositories_ShouldNotWrapExpiryDateInDateFunctionInSql()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string? repos = null;
            while (dir != null)
            {
                string c = Path.Combine(dir.FullName, "src", "ePrescriptions.Data", "Repositories");
                if (Directory.Exists(c)) { repos = c; break; }
                dir = dir.Parent;
            }
            repos.Should().NotBeNull();
            foreach (var file in Directory.GetFiles(repos!, "*.cs"))
            {
                string text = File.ReadAllText(file);
                text.Contains("date(ExpiryDate)").Should().BeFalse(Path.GetFileName(file));
                text.Contains("date(b.ExpiryDate)").Should().BeFalse(Path.GetFileName(file));
            }
        }

        [Fact]
        public void Migration017_ShouldCreateTriggersThatRejectNonIsoExpiryOnInsertAndUpdate()
        {
            var (db, repo, itemId) = NewDbWithItem();
            Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'TR_ItemBatches_ExpiryIso%'").Should().Be(2);

            Action badInsert = () => Exec(db, $"INSERT INTO ItemBatches (ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits, CostPricePerSmallestUnitPiastres, CreatedAt) VALUES ({itemId}, 'B-BAD', '31/01/2027', 5, 10, datetime('now'));");
            badInsert.Should().Throw<Exception>();

            repo.AddBatch(itemId, "B-OK", new DateTime(2030, 1, 31), 10, 800);
            Text(db, "SELECT ExpiryDate FROM ItemBatches WHERE BatchNumber = 'B-OK'").Should().Be("2030-01-31");

            Action badUpdate = () => Exec(db, "UPDATE ItemBatches SET ExpiryDate = '2030-01-31T00:00:00' WHERE BatchNumber = 'B-OK';");
            badUpdate.Should().Throw<Exception>();

            // تعديل أي عمود تاني (الكمية) ما يتأثرش بالمشغّل
            Exec(db, "UPDATE ItemBatches SET RemainingSmallestUnits = 3 WHERE BatchNumber = 'B-OK';");
            Scalar(db, "SELECT RemainingSmallestUnits FROM ItemBatches WHERE BatchNumber = 'B-OK'").Should().Be(3);
        }

        [Fact]
        public void NormalizeLegacyExpiryDates_ShouldConvertOldFormatsToIso_AndLeaveUnparseableAlone()
        {
            var (db, repo, itemId) = NewDbWithItem();
            // محاكاة قاعدة قديمة: نشيل الحماية ونحط صيغ قديمة
            Exec(db, "DROP TRIGGER TR_ItemBatches_ExpiryIso_Insert; DROP TRIGGER TR_ItemBatches_ExpiryIso_Update;");
            string ins = "INSERT INTO ItemBatches (ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits, CostPricePerSmallestUnitPiastres, CreatedAt) VALUES (" + itemId + ", '{0}', '{1}', 5, 10, datetime('now'));";
            Exec(db, string.Format(ins, "L1", "31/01/2030"));
            Exec(db, string.Format(ins, "L2", "2030-02-15T00:00:00"));
            Exec(db, string.Format(ins, "L3", "2030-03-20 00:00:00"));
            Exec(db, string.Format(ins, "L4", "غير مفهوم"));
            Exec(db, string.Format(ins, "L5", "2030-04-01"));

            db.NormalizeLegacyExpiryDates();

            Text(db, "SELECT ExpiryDate FROM ItemBatches WHERE BatchNumber = 'L1'").Should().Be("2030-01-31");
            Text(db, "SELECT ExpiryDate FROM ItemBatches WHERE BatchNumber = 'L2'").Should().Be("2030-02-15");
            Text(db, "SELECT ExpiryDate FROM ItemBatches WHERE BatchNumber = 'L3'").Should().Be("2030-03-20");
            Text(db, "SELECT ExpiryDate FROM ItemBatches WHERE BatchNumber = 'L4'").Should().Be("غير مفهوم");
            Text(db, "SELECT ExpiryDate FROM ItemBatches WHERE BatchNumber = 'L5'").Should().Be("2030-04-01");
        }

        [Fact]
        public void GetActiveBatches_ShouldFilterAndOrderByIsoText_TodayIsStillValid()
        {
            var (db, repo, itemId) = NewDbWithItem();
            repo.AddBatch(itemId, "LATE", DateTime.Today.AddYears(2), 10, 800);
            repo.AddBatch(itemId, "TODAY", DateTime.Today, 10, 800);
            repo.AddBatch(itemId, "SOON", DateTime.Today.AddDays(30), 10, 800);
            Exec(db, "DROP TRIGGER TR_ItemBatches_ExpiryIso_Insert;");
            Exec(db, $"INSERT INTO ItemBatches (ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits, CostPricePerSmallestUnitPiastres, CreatedAt) VALUES ({itemId}, 'GONE', '{ExpiryPolicy.ToIso(DateTime.Today.AddDays(-1))}', 10, 10, datetime('now'));");

            var active = repo.GetActiveBatches(itemId).Select(b => b.BatchNumber).ToList();
            active.Should().Equal(new[] { "TODAY", "SOON", "LATE" });          // المنتهية غايبة، والترتيب بالأقدم أولاً
            repo.GetActiveBatches(itemId, null, null, true).Select(b => b.BatchNumber).First().Should().Be("GONE");
        }

        [Fact]
        public void GetNearExpiryBatches_ShouldUseIsoThreshold()
        {
            var (db, repo, itemId) = NewDbWithItem();
            repo.AddBatch(itemId, "N60", DateTime.Today.AddDays(60), 10, 800);
            repo.AddBatch(itemId, "N90", DateTime.Today.AddDays(90), 10, 800);
            repo.AddBatch(itemId, "N91", DateTime.Today.AddDays(91), 10, 800);

            var near = repo.GetNearExpiryBatches(90).Select(x => x.Batch.BatchNumber).ToList();
            near.Should().Equal(new[] { "N60", "N90" });
        }
    }
}
