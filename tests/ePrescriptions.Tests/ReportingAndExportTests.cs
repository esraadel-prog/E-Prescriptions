using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using PdfSharp.Pdf.IO;
using QRCoder;
using ePrescriptions.Core.Helpers;
using ePrescriptions.App.Services;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    public class ReportingAndExportTests
    {
        [Fact]
        public void SalesPeriodSummary_Calculations_ShouldBeAccurateInEgyptianPounds()
        {
            // Arrange
            var summary = new SalesPeriodSummary
            {
                TotalInvoicesCount = 10,
                TotalSalesPiastres = 150000, // 1500.00 EGP
                TotalCostPiastres = 90000,   // 900.00 EGP
                TotalVatPiastres = 21000,    // 210.00 EGP
                CashSalesPiastres = 100000,  // 1000.00 EGP
                NetworkSalesPiastres = 50000 // 500.00 EGP
            };

            // Assert
            summary.TotalSalesPounds.Should().Be(1500.00m);
            summary.TotalCostPounds.Should().Be(900.00m);
            // خطوة 47: الضريبة (210.00) مش ربح، فالربح = 1500 - 210 - 900 = 390.00 (كان الاختبار بيثبّت 600.00 شاملة الضريبة)
            summary.NetProfitPiastres.Should().Be(39000);
            summary.NetProfitPounds.Should().Be(390.00m);
            // هامش الربح على الإيراد بدون الضريبة: 390 / 1290
            summary.ProfitMarginPercent.Should().BeApproximately(30.2326, 0.01);
            summary.CashSalesPounds.Should().Be(1000.00m);
            summary.NetworkSalesPounds.Should().Be(500.00m);
        }

        [Fact]
        public void LowStockReportRow_DeficitAndReorder_ShouldBeComputedCorrectly()
        {
            // Arrange
            var lowItem = new LowStockReportRow
            {
                ItemCode = "MED-001",
                NameAr = "بنادول إكسترا",
                CurrentStockSmallestUnits = 10,
                MinStockAlertSmallestUnits = 40,
                RetailPriceBoxPiastres = 1200
            };

            // Assert
            lowItem.DeficitUnits.Should().Be(30);
            lowItem.SuggestedOrderUnits.Should().Be(110); // 30 + 80
            lowItem.RetailPriceBoxPounds.Should().Be(12.00m);
        }

        [Fact]
        public void ExpiryReportRow_StatusAndDays_ShouldIdentifyExpiredBatches()
        {
            // Arrange
            var expiredBatch = new ExpiryReportRow
            {
                ItemCode = "MED-002",
                NameAr = "أوجمنتين",
                BatchNumber = "BATCH-EXP",
                ExpiryDate = DateTime.Today.AddDays(-5),
                RemainingSmallestUnits = 20,
                CostPricePerSmallestUnitPiastres = 50
            };

            var validBatch = new ExpiryReportRow
            {
                ItemCode = "MED-003",
                NameAr = "كونكور",
                BatchNumber = "BATCH-VAL",
                ExpiryDate = DateTime.Today.AddDays(45),
                RemainingSmallestUnits = 10,
                CostPricePerSmallestUnitPiastres = 100
            };

            // Assert
            expiredBatch.IsExpired.Should().BeTrue();
            expiredBatch.StatusDisplay.Should().Be("منتهي الصلاحية");
            expiredBatch.TotalCostPiastres.Should().Be(1000);
            expiredBatch.TotalCostPounds.Should().Be(10.00m);

            validBatch.IsExpired.Should().BeFalse();
            validBatch.DaysRemaining.Should().Be(45);
            validBatch.StatusDisplay.Should().Be("متبقي 45 يوم");
        }

        [Fact]
        public void ExcelExportService_ExportSalesReport_ShouldCreateValidExcelFile()
        {
            // Arrange
            var service = new ExcelExportService();
            var sales = new List<SalesReportRow>
            {
                new SalesReportRow
                {
                    InvoiceNumber = "INV-2026-00001",
                    Date = DateTime.Now,
                    CustomerName = "عميل نقدي",
                    PaymentMethod = PaymentMethod.Cash,
                    SubtotalPiastres = 10000,
                    VatPiastres = 1400,
                    TotalPiastres = 11400,
                    EstimatedCostPiastres = 7000
                }
            };

            var summary = new SalesPeriodSummary
            {
                TotalInvoicesCount = 1,
                TotalSalesPiastres = 11400,
                CashSalesPiastres = 11400,
                TotalCostPiastres = 7000
            };

            // Act
            string filePath = service.ExportSalesReport(sales, summary, DateTime.Today, DateTime.Today, "صيدلية الاختبار");

            // Assert
            File.Exists(filePath).Should().BeTrue();
            new FileInfo(filePath).Length.Should().BeGreaterThan(1000);

            // Clean up
            try { File.Delete(filePath); } catch { }
        }

        // ===== خطوة 28 (جزء ج): التصدير غير المتزامن (يحتاج ClosedXML/PdfSharp فمش بيشتغل في المشغّل البديل على Linux) =====

        [Fact]
        public void ExcelExportService_ExportSalesReportAsync_ShouldCreateSameKindOfFile()
        {
            var service = new ExcelExportService();
            var sales = new List<SalesReportRow>
            {
                new SalesReportRow
                {
                    InvoiceNumber = "INV-2026-00002",
                    Date = DateTime.Now,
                    CustomerName = "عميل نقدي",
                    PaymentMethod = PaymentMethod.Cash,
                    SubtotalPiastres = 10000,
                    VatPiastres = 1400,
                    TotalPiastres = 11400,
                    EstimatedCostPiastres = 7000
                }
            };
            var summary = new SalesPeriodSummary { TotalInvoicesCount = 1, TotalSalesPiastres = 11400, CashSalesPiastres = 11400, TotalCostPiastres = 7000 };

            string filePath = service.ExportSalesReportAsync(sales, summary, DateTime.Today, DateTime.Today, "صيدلية الاختبار")
                                     .GetAwaiter().GetResult();

            File.Exists(filePath).Should().BeTrue();
            new FileInfo(filePath).Length.Should().BeGreaterThan(1000);
            try { File.Delete(filePath); } catch (Exception) { }
        }

        [Fact]
        public void PdfReportService_GenerateVoucherPdfAsync_ShouldCreatePdfFile()
        {
            var service = new PdfReportService();

            string pdfPath = service.GenerateVoucherPdfAsync("PV-2026-0001", DateTime.Today, "مورد تجريبي", 50000, "سند صرف لمورد", "اختبار", new PharmacySettings())
                                    .GetAwaiter().GetResult();

            File.Exists(pdfPath).Should().BeTrue();
            new FileInfo(pdfPath).Length.Should().BeGreaterThan(500);
            try { File.Delete(pdfPath); } catch (Exception) { }
        }

        // ===== خطوة 43: تحقق فعلي من مخرجات ClosedXML وPdfSharp وQRCoder =====
        // (بتحتاج المكتبات الحقيقية فمش بتشتغل في المشغّل البديل على Linux؛ بتشتغل في Visual Studio / GitHub Actions على Windows)

        private static List<SalesReportRow> OneSale() => new List<SalesReportRow>
        {
            new SalesReportRow
            {
                InvoiceNumber = "INV-2026-00077",
                Date = new DateTime(2026, 10, 5, 14, 30, 0),
                CustomerName = "عميل نقدي",
                PaymentMethod = PaymentMethod.Cash,
                SubtotalPiastres = 10000,
                VatPiastres = 1400,
                TotalPiastres = 11400,
                EstimatedCostPiastres = 7000
            }
        };

        [Fact]
        public void ExcelSalesReport_ReopenedWithClosedXml_ShouldHaveTextHeadersRealNumbersAndSumFormulas()
        {
            var service = new ExcelExportService();
            var sales = OneSale();
            var summary = new SalesPeriodSummary { TotalInvoicesCount = 1, TotalSalesPiastres = 11400, CashSalesPiastres = 11400, TotalCostPiastres = 7000 };

            string path = service.ExportSalesReport(sales, summary, DateTime.Today, DateTime.Today, "صيدلية اختبار");
            try
            {
                using var wb = new XLWorkbook(path);
                var ws = wb.Worksheet(1);

                ws.Name.Should().Be("تقرير المبيعات");
                ws.Cell(4, 1).GetString().Should().Be("رقم الفاتورة");
                ws.Cell(5, 1).GetString().Should().Be("INV-2026-00077");
                ws.Cell(5, 3).GetString().Should().Be("عميل نقدي");

                // الأرقام لازم تتخزّن أرقام (مش نص) وبالجنيه مش بالقرش
                ws.Cell(5, 5).DataType.Should().Be(XLDataType.Number);
                ws.Cell(5, 5).GetDouble().Should().BeApproximately(100.00, 0.0001);
                ws.Cell(5, 6).GetDouble().Should().BeApproximately(14.00, 0.0001);
                ws.Cell(5, 7).GetDouble().Should().BeApproximately(114.00, 0.0001);
                ws.Cell(5, 8).GetDouble().Should().BeApproximately((double)sales[0].ProfitPounds, 0.0001);

                // سطر الإجمالي: معادلات SUM على صف البيانات الوحيد، وقيمتها بتتحسب صح
                ws.Cell(6, 5).HasFormula.Should().BeTrue();
                ws.Cell(6, 5).FormulaA1.Should().Be("SUM(E5:E5)");
                ws.Cell(6, 7).GetDouble().Should().BeApproximately(114.00, 0.0001);
            }
            finally
            {
                try { File.Delete(path); } catch (Exception) { }
            }
        }

        [Fact]
        public void ExcelSalesReport_ForPeriodWithNoSales_ShouldNotContainCircularSumFormulas()
        {
            var service = new ExcelExportService();

            string path = service.ExportSalesReport(new List<SalesReportRow>(), new SalesPeriodSummary(), DateTime.Today, DateTime.Today, "صيدلية اختبار");
            try
            {
                using var wb = new XLWorkbook(path);
                var ws = wb.Worksheet(1);

                // صف الإجمالي هو الصف 5 نفسه: SUM(E5:E4) كان بيشمل الخلية نفسها (مرجع دائري في Excel)
                for (int col = 5; col <= 8; col++)
                {
                    ws.Cell(5, col).HasFormula.Should().BeFalse($"العمود {col} فيه معادلة في فترة بدون مبيعات");
                    ws.Cell(5, col).GetDouble().Should().Be(0);
                }
            }
            finally
            {
                try { File.Delete(path); } catch (Exception) { }
            }
        }

        private static Invoice SampleInvoice() => new Invoice
        {
            InvoiceNumber = "INV-2026-00077",
            Date = new DateTime(2026, 10, 5, 14, 30, 0),
            CustomerName = "عميل نقدي",
            SubtotalPiastres = 10000,
            VatPiastres = 1400,
            TotalPiastres = 11400,
            PaidPiastres = 11400,
            Items = new List<InvoiceItem>
            {
                new InvoiceItem { ItemCode = "MED-001", ItemNameAr = "بنادول إكسترا", QuantitySold = 2, UnitPricePiastres = 5000, VatRatePercent = 14, VatPiastres = 1400, TotalPiastres = 11400 }
            }
        };

        private static long InvoicePdfSize(bool qr, out int pageCount)
        {
            var service = new PdfReportService();
            var settings = new PharmacySettings { PharmacyNameAr = "صيدلية اختبار", TaxNumber = "123456789", EnableQrCode = qr };
            string path = service.GenerateA4InvoicePdf(SampleInvoice(), settings);
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

                // فتح الملف بقارئ PdfSharp نفسه: لو الملف تالف هيرمي استثناء هنا
                using (var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import))
                {
                    pageCount = doc.PageCount;
                }
                return bytes.LongLength;
            }
            finally
            {
                try { File.Delete(path); } catch (Exception) { }
            }
        }

        [Fact]
        public void InvoicePdf_ShouldBeAValidSinglePagePdf_AndEmbedTheQrImageOnlyWhenEnabled()
        {
            long sizeWithoutQr = InvoicePdfSize(qr: false, out int pagesWithout);
            long sizeWithQr = InvoicePdfSize(qr: true, out int pagesWith);

            pagesWithout.Should().Be(1);
            pagesWith.Should().Be(1);
            sizeWithoutQr.Should().BeGreaterThan(1000);
            // صورة الـ QR المدمجة لازم تكبّر الملف فعلاً
            sizeWithQr.Should().BeGreaterThan(sizeWithoutQr + 100);
        }

        [Fact]
        public void QrImage_RenderedByQrCoder_ShouldMatchItsModuleMatrix_ModuleForModule()
        {
            string payload = EgyptQrHelper.BuildSimpleQrText("INV-2026-00077", new DateTime(2026, 10, 5, 14, 30, 0), 11400, "123456789", "صيدلية اختبار");
            const int ppm = 4;

            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            using var qr = new QRCode(data);
            using Bitmap bmp = qr.GetGraphic(ppm);

            List<BitArray> matrix = data.ModuleMatrix;
            int n = matrix.Count;
            (n >= 21).Should().BeTrue();
            bmp.Width.Should().Be(bmp.Height);

            // المنطقة الهادية (quiet zone) ممكن تكون جوا المصفوفة أو برّاها بحسب إصدار QRCoder؛ نحسب الإزاحة من الأبعاد
            int modulesInBitmap = bmp.Width / ppm;
            ((modulesInBitmap - n) % 2).Should().Be(0);
            int shift = (modulesInBitmap - n) / 2;
            (shift >= 0).Should().BeTrue();

            int mismatches = 0;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    Color px = bmp.GetPixel((x + shift) * ppm + ppm / 2, (y + shift) * ppm + ppm / 2);
                    bool dark = px.R < 128;
                    if (dark != matrix[y][x]) mismatches++;
                }
            }
            mismatches.Should().Be(0);
        }

        [Fact]
        public void QrPayload_WithArabicSellerName_ShouldFitInTheQrCapacity_AndBeDeterministic()
        {
            string payload = EgyptQrHelper.BuildSimpleQrText("INV-2026-00077", new DateTime(2026, 10, 5, 14, 30, 0), 11400, "123456789", "صيدلية الشفاء للأدوية والمستلزمات الطبية");

            using var gen = new QRCodeGenerator();
            using var a = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            using var b = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);

            a.ModuleMatrix.Count.Should().Be(b.ModuleMatrix.Count);
            for (int y = 0; y < a.ModuleMatrix.Count; y++)
            {
                for (int x = 0; x < a.ModuleMatrix.Count; x++)
                {
                    a.ModuleMatrix[y][x].Should().Be(b.ModuleMatrix[y][x]);
                }
            }
        }

        [Fact]
        public void BackupService_CreateAndValidateBackup_ShouldSucceedWithIntegrityCheck()
        {
            // Arrange
            string tempDbPath = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid():N}.db");
            var dbContext = new DatabaseContext(tempDbPath);
            dbContext.InitializeAndMigrate(seedData: true);

            var backupService = new BackupService(dbContext);
            string tempBackupFolder = Path.Combine(Path.GetTempPath(), $"backups_{Guid.NewGuid():N}");

            try
            {
                // Act: إنشاء نسخة احتياطية
                bool created = backupService.CreateBackup(tempBackupFolder, out string backupPath, out string error);

                // Assert
                created.Should().BeTrue();
                error.Should().BeEmpty();
                File.Exists(backupPath).Should().BeTrue();

                // Act: فحص السلامة
                bool valid = backupService.ValidateBackupFile(backupPath, out string valError);
                valid.Should().BeTrue();
                valError.Should().BeEmpty();
            }
            finally
            {
                // Clean up
                try { if (Directory.Exists(tempBackupFolder)) Directory.Delete(tempBackupFolder, true); } catch { }
                try { if (File.Exists(tempDbPath)) File.Delete(tempDbPath); } catch { }
            }
        }
    }
}
