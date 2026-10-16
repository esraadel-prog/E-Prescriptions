using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 19: سلسلة hash في OwnerAuditLog وStaffAuditLog (أي تعديل/حذف يدوي بيكسرها)</summary>
    public class AuditHashChainTests
    {
        private static DatabaseContext NewDb()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: false);
            return db;
        }

        private static void Exec(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static string Text(DatabaseContext db, string sql)
        {
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar()?.ToString() ?? string.Empty;
        }

        private static void FillStaff(StaffAuditLogRepository repo, int n)
        {
            for (int i = 1; i <= n; i++)
                repo.LogChange(2, "cashier1", "Cashier", "Item", i.ToString(), "صنف " + i, "SalePrice", "100", (100 + i).ToString());
        }

        private static void FillOwner(OwnerAuditLogRepository repo, int n)
        {
            for (int i = 1; i <= n; i++)
                repo.LogChange(1, "Item", i.ToString(), "صنف " + i, "SalePrice", "100", (100 + i).ToString(), i % 2 == 0 ? "ملاحظة" : null);
        }

        [Fact]
        public void Migration014_ShouldAddHashColumnsToBothLogs_AndSchemaVersionIsLatest()
        {
            var db = NewDb();
            (DatabaseContext.LatestSchemaVersion >= 14).Should().BeTrue();
            foreach (string table in new[] { "OwnerAuditLog", "StaffAuditLog" })
                foreach (string col in new[] { "PrevHash", "RowHash" })
                    Text(db, $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{col}'").Should().Be("1");
        }

        [Fact]
        public void StaffLog_ShouldBeChained_AndPassVerification()
        {
            var db = NewDb();
            var repo = new StaffAuditLogRepository(db);
            FillStaff(repo, 5);

            var r = repo.VerifyIntegrity();
            r.IsValid.Should().BeTrue(r.Message);
            r.CheckedCount.Should().Be(5);
            r.LegacyUnprotectedCount.Should().Be(0);

            // أول سطر بيشاور على Genesis، والباقي كل سطر بيشاور على hash اللي قبله
            Text(db, "SELECT PrevHash FROM StaffAuditLog WHERE Id = 1").Should().Be(AuditHashChain.GenesisHash);
            Text(db, "SELECT RowHash FROM StaffAuditLog WHERE Id = 1").Should().Be(Text(db, "SELECT PrevHash FROM StaffAuditLog WHERE Id = 2"));
            Text(db, "SELECT RowHash FROM StaffAuditLog WHERE Id = 2").Should().Be(Text(db, "SELECT PrevHash FROM StaffAuditLog WHERE Id = 3"));
        }

        [Fact]
        public void OwnerLog_ShouldBeChained_AndPassVerification()
        {
            var db = NewDb();
            var repo = new OwnerAuditLogRepository(db);
            FillOwner(repo, 4);

            var r = repo.VerifyIntegrity();
            r.IsValid.Should().BeTrue(r.Message);
            r.CheckedCount.Should().Be(4);
            repo.GetRecent().Count.Should().Be(4); // القراءة العادية ما اتأثرتش
        }

        [Fact]
        public void EmptyLogs_ShouldVerifyAsValid()
        {
            var db = NewDb();
            new StaffAuditLogRepository(db).VerifyIntegrity().IsValid.Should().BeTrue();
            new OwnerAuditLogRepository(db).VerifyIntegrity().IsValid.Should().BeTrue();
        }

        [Fact]
        public void ManualEditOfAStaffRow_ShouldBreakTheChain_AtThatRow()
        {
            var db = NewDb();
            var repo = new StaffAuditLogRepository(db);
            FillStaff(repo, 5);
            repo.VerifyIntegrity().IsValid.Should().BeTrue();

            Exec(db, "UPDATE StaffAuditLog SET NewValue = '1' WHERE Id = 3;"); // تزوير القيمة المسجّلة

            var r = repo.VerifyIntegrity();
            r.IsValid.Should().BeFalse();
            r.FirstBrokenId.Should().Be(3);
            r.CheckedCount.Should().Be(2);
        }

        [Fact]
        public void ManualEditOfOwnerRow_ShouldBreakTheChain_IncludingNoteAndNullChanges()
        {
            var db = NewDb();
            var repo = new OwnerAuditLogRepository(db);
            FillOwner(repo, 4);

            Exec(db, "UPDATE OwnerAuditLog SET Note = NULL WHERE Id = 2;"); // الملاحظة كانت 'ملاحظة' → NULL

            var r = repo.VerifyIntegrity();
            r.IsValid.Should().BeFalse();
            r.FirstBrokenId.Should().Be(2);
        }

        [Fact]
        public void ManualEditOfTimestampOrActor_ShouldBeDetected()
        {
            var db = NewDb();
            var repo = new StaffAuditLogRepository(db);
            FillStaff(repo, 3);
            Exec(db, "UPDATE StaffAuditLog SET ChangedAt = '2020-01-01 00:00:00' WHERE Id = 2;");
            repo.VerifyIntegrity().FirstBrokenId.Should().Be(2);

            var db2 = NewDb();
            var repo2 = new StaffAuditLogRepository(db2);
            FillStaff(repo2, 3);
            Exec(db2, "UPDATE StaffAuditLog SET ActorUserName = 'admin', ActorRole = 'Admin' WHERE Id = 3;");
            repo2.VerifyIntegrity().FirstBrokenId.Should().Be(3);
        }

        [Fact]
        public void DeletingARowFromTheMiddle_ShouldBreakTheChain_AtTheNextRow()
        {
            var db = NewDb();
            var repo = new StaffAuditLogRepository(db);
            FillStaff(repo, 5);

            Exec(db, "DELETE FROM StaffAuditLog WHERE Id = 3;");

            var r = repo.VerifyIntegrity();
            r.IsValid.Should().BeFalse();
            r.FirstBrokenId.Should().Be(4);
        }

        [Fact]
        public void DeletingTheFirstChainedRow_ShouldBeDetected()
        {
            var db = NewDb();
            var repo = new OwnerAuditLogRepository(db);
            FillOwner(repo, 3);

            Exec(db, "DELETE FROM OwnerAuditLog WHERE Id = 1;");

            var r = repo.VerifyIntegrity();
            r.IsValid.Should().BeFalse();
            r.FirstBrokenId.Should().Be(2);
        }

        [Fact]
        public void StrippingTheHashFromARow_OrInsertingARawRow_ShouldBeDetected()
        {
            var db = NewDb();
            var repo = new StaffAuditLogRepository(db);
            FillStaff(repo, 3);
            Exec(db, "UPDATE StaffAuditLog SET RowHash = NULL, PrevHash = NULL WHERE Id = 2;");
            var stripped = repo.VerifyIntegrity();
            stripped.IsValid.Should().BeFalse();
            stripped.FirstBrokenId.Should().Be(2);

            var db2 = NewDb();
            var repo2 = new StaffAuditLogRepository(db2);
            FillStaff(repo2, 2);
            Exec(db2, "INSERT INTO StaffAuditLog (ActorUserId, ActorUserName, ActorRole, EntityType, EntityId, EntityLabel, FieldName, OldValue, NewValue, ChangedAt) VALUES (9, 'x', 'Admin', 'Item', '1', 'x', 'SalePrice', '1', '2', '2026-01-01 00:00:00');");
            var inserted = repo2.VerifyIntegrity();
            inserted.IsValid.Should().BeFalse();
            inserted.FirstBrokenId.Should().Be(3);
        }

        [Fact]
        public void LegacyRowsBeforeTheUpgrade_ShouldBeTolerated_AndTheChainStartsAfterThem()
        {
            var db = NewDb();
            // سطرين قديمين (قبل ترقية 014): بدون hash
            Exec(db, "INSERT INTO StaffAuditLog (ActorUserId, ActorUserName, ActorRole, EntityType, EntityId, EntityLabel, FieldName, OldValue, NewValue, ChangedAt) VALUES (2, 'old', 'Cashier', 'Item', '1', 'قديم', 'SalePrice', '1', '2', '2026-01-01 00:00:00');");
            Exec(db, "INSERT INTO StaffAuditLog (ActorUserId, ActorUserName, ActorRole, EntityType, EntityId, EntityLabel, FieldName, OldValue, NewValue, ChangedAt) VALUES (2, 'old', 'Cashier', 'Item', '2', 'قديم', 'SalePrice', '1', '2', '2026-01-02 00:00:00');");
            var repo = new StaffAuditLogRepository(db);
            FillStaff(repo, 2);

            var r = repo.VerifyIntegrity();
            r.IsValid.Should().BeTrue(r.Message);
            r.LegacyUnprotectedCount.Should().Be(2);
            r.CheckedCount.Should().Be(2);
            Text(db, "SELECT PrevHash FROM StaffAuditLog WHERE Id = 3").Should().Be(AuditHashChain.GenesisHash);
        }

        [Fact]
        public void TheTwoLogsAreIndependentChains_TamperingOneDoesNotAffectTheOther()
        {
            var db = NewDb();
            var staff = new StaffAuditLogRepository(db);
            var owner = new OwnerAuditLogRepository(db);
            FillStaff(staff, 3);
            FillOwner(owner, 3);

            Exec(db, "UPDATE StaffAuditLog SET NewValue = 'x' WHERE Id = 2;");

            staff.VerifyIntegrity().IsValid.Should().BeFalse();
            owner.VerifyIntegrity().IsValid.Should().BeTrue();
            // سجلات Owner ما ظهرتش في سجل الموظفين والعكس
            staff.GetRecent().Count.Should().Be(3);
            owner.GetRecent().Count.Should().Be(3);
        }

        [Fact]
        public void SameContentInDifferentLogs_ShouldProduceDifferentHashes()
        {
            string[] fields = { "1", "Item", "1", "x", "SalePrice", "1", "2", "2026-01-01 00:00:00", null! };
            AuditHashChain.ComputeHash("OwnerAuditLog", 1, AuditHashChain.GenesisHash, fields)
                .Should().NotBe(AuditHashChain.ComputeHash("StaffAuditLog", 1, AuditHashChain.GenesisHash, fields));
        }

        [Fact]
        public void NullAndEmptyStringValues_ShouldHashDifferently()
        {
            AuditHashChain.ComputeHash("T", 1, "p", new string?[] { null })
                .Should().NotBe(AuditHashChain.ComputeHash("T", 1, "p", new string?[] { "" }));
            AuditHashChain.ComputeHash("T", 1, "p", new string?[] { "ab", "c" })
                .Should().NotBe(AuditHashChain.ComputeHash("T", 1, "p", new string?[] { "a", "bc" }));
        }

        [Fact]
        public void UnchangedValue_ShouldNotBeLogged_AndNotAffectTheChain()
        {
            var db = NewDb();
            var repo = new StaffAuditLogRepository(db);
            FillStaff(repo, 2);
            repo.LogChange(2, "cashier1", "Cashier", "Item", "1", "صنف", "SalePrice", "5", "5");
            repo.GetRecent().Count.Should().Be(2);
            repo.VerifyIntegrity().IsValid.Should().BeTrue();
        }

        [Fact]
        public void ConcurrentWritersOnTheSameFile_ShouldKeepASingleValidChain()
        {
            string file = Path.Combine(Path.GetTempPath(), "audit_chain_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var db = new DatabaseContext(file);
                db.InitializeAndMigrate(seedData: false);
                var errors = new List<Exception>();

                void Worker(int actor)
                {
                    try
                    {
                        var repo = new StaffAuditLogRepository(new DatabaseContext(file)); // اتصال مستقل لكل خيط
                        for (int i = 0; i < 10; i++)
                            repo.LogChange(actor, "u" + actor, "Cashier", "Item", i.ToString(), "صنف", "SalePrice", "1", (2 + i).ToString());
                    }
                    catch (Exception ex) { lock (errors) errors.Add(ex); }
                }

                var t1 = new Thread(() => Worker(2));
                var t2 = new Thread(() => Worker(3));
                t1.Start(); t2.Start(); t1.Join(); t2.Join();

                errors.Should().BeEmpty();
                var r = new StaffAuditLogRepository(db).VerifyIntegrity();
                r.IsValid.Should().BeTrue(r.Message);
                r.CheckedCount.Should().Be(20);
            }
            finally
            {
                foreach (string f in new[] { file, file + "-wal", file + "-shm" })
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }
    }
}
