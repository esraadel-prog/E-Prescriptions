using System;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 21: تنظيف بدون تغيير سلوك — حفظ/قراءة الفواتير المعلّقة بعد حذف فحوص الأعمدة الزائدة</summary>
    public class SuspendedInvoiceTests
    {
        [Fact]
        public void SaveAndGetSuspendedInvoice_ShouldRoundTripTotalInPiastres_AndReplaceSameReference()
        {
            var db = new DatabaseContext(":memory:");
            db.InitializeAndMigrate(seedData: true);
            var repo = new InvoiceRepository(db, new ItemRepository(db));

            repo.SaveSuspendedInvoice(new SuspendedInvoice { ReferenceCode = "S-1", SuspendedAt = new DateTime(2026, 10, 12, 9, 30, 0), CustomerName = "عميل", JsonData = "{}", TotalPiastres = 12345 });
            repo.SaveSuspendedInvoice(new SuspendedInvoice { ReferenceCode = "S-1", SuspendedAt = new DateTime(2026, 10, 12, 9, 45, 0), CustomerName = "عميل", JsonData = "{}", TotalPiastres = 20000 });

            var list = repo.GetSuspendedInvoices();
            list.Should().ContainSingle();
            list[0].TotalPiastres.Should().Be(20000);
            list[0].ReferenceCode.Should().Be("S-1");
        }
    }
}
