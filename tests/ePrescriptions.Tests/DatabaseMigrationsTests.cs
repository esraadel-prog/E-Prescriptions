using System.Data.SQLite;
using ePrescriptions.Data;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class DatabaseMigrationsTests
    {
        [Fact]
        public void InitializeAndMigrate_OnInMemoryDatabase_ShouldCreateAllTablesAndApplySeed()
        {
            // Arrange
            var db = new DatabaseContext(":memory:");

            // Act
            db.InitializeAndMigrate(seedData: true);

            // Assert
            using var conn = db.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT COUNT(*) FROM Users;";
            long userCount = (long)(cmd.ExecuteScalar() ?? 0);
            // بعد خطوة 1: لا حسابات admin/pharmacist في البذرة، والموجود حساب Owner فقط (يُنشأ تلقائياً)
            userCount.Should().Be(1);

            cmd.CommandText = "SELECT COUNT(*) FROM Items;";
            long itemCount = (long)(cmd.ExecuteScalar() ?? 0);
            itemCount.Should().BeGreaterThanOrEqualTo(5);

            int version = DatabaseContext.GetUserVersion(conn);
            version.Should().Be(DatabaseContext.LatestSchemaVersion);
        }
    }
}
