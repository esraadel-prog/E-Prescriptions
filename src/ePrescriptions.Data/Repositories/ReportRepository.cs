using ePrescriptions.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Data.Repositories
{
    public class ReportRepository
    {
        private readonly DatabaseContext _dbContext;

        public ReportRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// تقرير فواتير المبيعات التفصيلي مع فلترة التاريخ وطريقة السداد
        /// </summary>
        public List<SalesReportRow> GetSalesReport(DateTime fromDate, DateTime toDate, PaymentMethod? paymentMethod = null)
        {
            var list = new List<SalesReportRow>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            string methodFilter = paymentMethod.HasValue
                ? "AND (inv.PaymentMethod = @pMethod OR EXISTS (SELECT 1 FROM InvoicePayments pp WHERE pp.InvoiceId = inv.Id AND pp.Method = @pMethod))"
                : "";

            cmd.CommandText = $@"
                SELECT inv.Id, inv.InvoiceNumber, inv.Date, inv.CustomerName, inv.PaymentMethod,
                       inv.SubtotalPiastres, inv.DiscountPiastres, inv.VatPiastres, inv.TotalPiastres,
                       IFNULL(SUM(((it.SmallestUnitsCount * i.CostPriceBoxPiastres + MAX(1, i.StripsPerBox * i.PiecesPerStrip) / 2) / MAX(1, i.StripsPerBox * i.PiecesPerStrip))), 0) AS EstimatedCostPiastres
                FROM Invoices inv
                LEFT JOIN InvoiceItems it ON inv.Id = it.InvoiceId
                LEFT JOIN Items i ON it.ItemId = i.Id
                WHERE inv.Type = 1 AND inv.Status <> 3
                  AND date(inv.Date) >= date(@from)
                  AND date(inv.Date) <= date(@to)
                  {methodFilter}
                GROUP BY inv.Id
                ORDER BY datetime(inv.Date) DESC;";

            cmd.Parameters.AddWithValue("@from", fromDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", toDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            if (paymentMethod.HasValue)
            {
                cmd.Parameters.AddWithValue("@pMethod", (int)paymentMethod.Value);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new SalesReportRow
                {
                    InvoiceId = Convert.ToInt32(reader["Id"]),
                    InvoiceNumber = reader["InvoiceNumber"].ToString() ?? string.Empty,
                    Date = DbDate.Parse(reader["Date"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                    CustomerName = reader["CustomerName"].ToString() ?? string.Empty,
                    PaymentMethod = (PaymentMethod)Convert.ToInt32(reader["PaymentMethod"]),
                    SubtotalPiastres = Convert.ToInt64(reader["SubtotalPiastres"]),
                    DiscountPiastres = Convert.ToInt64(reader["DiscountPiastres"]),
                    VatPiastres = Convert.ToInt64(reader["VatPiastres"]),
                    TotalPiastres = Convert.ToInt64(reader["TotalPiastres"]),
                    EstimatedCostPiastres = Convert.ToInt64(reader["EstimatedCostPiastres"])
                });
            }

            return list;
        }

        /// <summary>
        /// تقرير ملخص المبيعات المالي للفترة
        /// </summary>
        public SalesPeriodSummary GetSalesPeriodSummary(DateTime fromDate, DateTime toDate)
        {
            var summary = new SalesPeriodSummary();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT 
                    COUNT(inv.Id) AS InvoicesCount,
                    IFNULL(SUM(inv.TotalPiastres), 0) AS TotalSales,
                    IFNULL(SUM(inv.VatPiastres), 0) AS TotalVat,
                    IFNULL(SUM(CASE WHEN inv.PaymentMethod = 1 THEN inv.TotalPiastres ELSE 0 END), 0)
                      + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON pi.Id = p.InvoiceId
                                WHERE p.Method = 1 AND pi.Type = 1 AND pi.Status <> 3
                                  AND date(pi.Date) >= date(@from) AND date(pi.Date) <= date(@to)), 0) AS CashSales,
                    IFNULL(SUM(CASE WHEN inv.PaymentMethod = 2 THEN inv.TotalPiastres ELSE 0 END), 0)
                      + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON pi.Id = p.InvoiceId
                                WHERE p.Method = 2 AND pi.Type = 1 AND pi.Status <> 3
                                  AND date(pi.Date) >= date(@from) AND date(pi.Date) <= date(@to)), 0) AS NetworkSales,
                    IFNULL(SUM(CASE WHEN inv.PaymentMethod = 3 THEN inv.TotalPiastres ELSE 0 END), 0)
                      + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON pi.Id = p.InvoiceId
                                WHERE p.Method = 3 AND pi.Type = 1 AND pi.Status <> 3
                                  AND date(pi.Date) >= date(@from) AND date(pi.Date) <= date(@to)), 0) AS OnAccountSales,
                    IFNULL(SUM(CASE WHEN inv.PaymentMethod = 4 THEN inv.TotalPiastres ELSE 0 END), 0)
                      + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON pi.Id = p.InvoiceId
                                WHERE p.Method = 4 AND pi.Type = 1 AND pi.Status <> 3
                                  AND date(pi.Date) >= date(@from) AND date(pi.Date) <= date(@to)), 0) AS InsuranceSales,
                    (
                        SELECT IFNULL(SUM(((it.SmallestUnitsCount * i.CostPriceBoxPiastres + MAX(1, i.StripsPerBox * i.PiecesPerStrip) / 2) / MAX(1, i.StripsPerBox * i.PiecesPerStrip))), 0)
                        FROM InvoiceItems it
                        JOIN Invoices inv2 ON it.InvoiceId = inv2.Id
                        JOIN Items i ON it.ItemId = i.Id
                        WHERE inv2.Type = 1 AND inv2.Status <> 3
                          AND date(inv2.Date) >= date(@from)
                          AND date(inv2.Date) <= date(@to)
                    ) AS TotalCost
                FROM Invoices inv
                WHERE inv.Type = 1 AND inv.Status <> 3
                  AND date(inv.Date) >= date(@from)
                  AND date(inv.Date) <= date(@to);";

            cmd.Parameters.AddWithValue("@from", fromDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", toDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                summary.TotalInvoicesCount = Convert.ToInt32(reader["InvoicesCount"]);
                summary.TotalSalesPiastres = Convert.ToInt64(reader["TotalSales"]);
                summary.TotalVatPiastres = Convert.ToInt64(reader["TotalVat"]);
                summary.CashSalesPiastres = Convert.ToInt64(reader["CashSales"]);
                summary.NetworkSalesPiastres = Convert.ToInt64(reader["NetworkSales"]);
                summary.OnAccountSalesPiastres = Convert.ToInt64(reader["OnAccountSales"]);
                summary.InsuranceSalesPiastres = Convert.ToInt64(reader["InsuranceSales"]);
                summary.TotalCostPiastres = Convert.ToInt64(reader["TotalCost"]);
            }

            // مرتجعات المبيعات في نفس الفترة: بتتخصم من المبيعات والضريبة والتكلفة (خطوة 47)
            using (var retCmd = conn.CreateCommand())
            {
                retCmd.CommandText = @"
                    SELECT IFNULL(SUM(inv.TotalPiastres), 0) AS RetTotal,
                           IFNULL(SUM(inv.VatPiastres), 0) AS RetVat,
                           (
                               SELECT IFNULL(SUM(((it.SmallestUnitsCount * i.CostPriceBoxPiastres + MAX(1, i.StripsPerBox * i.PiecesPerStrip) / 2) / MAX(1, i.StripsPerBox * i.PiecesPerStrip))), 0)
                               FROM InvoiceItems it
                               JOIN Invoices inv2 ON it.InvoiceId = inv2.Id
                               JOIN Items i ON it.ItemId = i.Id
                               WHERE inv2.Type = 2 AND inv2.Status <> 3
                                 AND date(inv2.Date) >= date(@from)
                                 AND date(inv2.Date) <= date(@to)
                           ) AS RetCost
                    FROM Invoices inv
                    WHERE inv.Type = 2 AND inv.Status <> 3
                      AND date(inv.Date) >= date(@from)
                      AND date(inv.Date) <= date(@to);";
                retCmd.Parameters.AddWithValue("@from", fromDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                retCmd.Parameters.AddWithValue("@to", toDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                using var retReader = retCmd.ExecuteReader();
                if (retReader.Read())
                {
                    summary.ReturnsTotalPiastres = Convert.ToInt64(retReader["RetTotal"]);
                    summary.ReturnsVatPiastres = Convert.ToInt64(retReader["RetVat"]);
                    summary.ReturnsCostPiastres = Convert.ToInt64(retReader["RetCost"]);
                }
            }

            return summary;
        }

        /// <summary>
        /// تقرير حركة الأصناف والأكثر مبيعاً
        /// </summary>
        public List<ItemMovementReportRow> GetItemMovementReport(DateTime fromDate, DateTime toDate)
        {
            var list = new List<ItemMovementReportRow>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT i.Id, i.Code, i.Barcode, i.NameAr, i.TotalStockSmallestUnits,
                       IFNULL(SUM(it.SmallestUnitsCount), 0) AS SoldUnits,
                       IFNULL(SUM(it.TotalPiastres - it.VatPiastres), 0) AS TotalRevenue,
                       IFNULL(SUM(((it.SmallestUnitsCount * i.CostPriceBoxPiastres + MAX(1, i.StripsPerBox * i.PiecesPerStrip) / 2) / MAX(1, i.StripsPerBox * i.PiecesPerStrip))), 0) AS TotalCost
                FROM Items i
                -- فلتر الفترة والنوع داخل subquery (قبل كان في ON للـ LEFT JOIN فبنود الفواتير والمرتجعات خارج الفترة كانت بتتعدّ، خطوة 47)
                LEFT JOIN (
                    SELECT x.ItemId, x.SmallestUnitsCount, x.TotalPiastres, x.VatPiastres
                    FROM InvoiceItems x
                    JOIN Invoices inv ON x.InvoiceId = inv.Id
                    WHERE inv.Type = 1 AND inv.Status <> 3
                      AND date(inv.Date) >= date(@from)
                      AND date(inv.Date) <= date(@to)
                ) it ON i.Id = it.ItemId
                WHERE i.IsActive = 1
                GROUP BY i.Id
                ORDER BY SoldUnits DESC, TotalRevenue DESC;";

            cmd.Parameters.AddWithValue("@from", fromDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", toDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ItemMovementReportRow
                {
                    ItemId = Convert.ToInt32(reader["Id"]),
                    ItemCode = reader["Code"].ToString() ?? string.Empty,
                    Barcode = reader["Barcode"].ToString() ?? string.Empty,
                    NameAr = reader["NameAr"].ToString() ?? string.Empty,
                    CurrentStockSmallestUnits = Convert.ToInt32(reader["TotalStockSmallestUnits"]),
                    TotalSoldSmallestUnits = Convert.ToInt32(reader["SoldUnits"]),
                    TotalRevenuePiastres = Convert.ToInt64(reader["TotalRevenue"]),
                    TotalCostPiastres = Convert.ToInt64(reader["TotalCost"])
                });
            }

            return list;
        }

        /// <summary>
        /// تقرير النواقص وحد إعادة الطلب
        /// </summary>
        public List<LowStockReportRow> GetLowStockReport()
        {
            var list = new List<LowStockReportRow>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT Id, Code, Barcode, NameAr, TotalStockSmallestUnits,
                       MinStockAlertSmallestUnits, RetailPriceBoxPiastres
                FROM Items
                WHERE IsActive = 1 AND TotalStockSmallestUnits <= MinStockAlertSmallestUnits
                ORDER BY TotalStockSmallestUnits ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new LowStockReportRow
                {
                    ItemId = Convert.ToInt32(reader["Id"]),
                    ItemCode = reader["Code"].ToString() ?? string.Empty,
                    Barcode = reader["Barcode"].ToString() ?? string.Empty,
                    NameAr = reader["NameAr"].ToString() ?? string.Empty,
                    CurrentStockSmallestUnits = Convert.ToInt32(reader["TotalStockSmallestUnits"]),
                    MinStockAlertSmallestUnits = Convert.ToInt32(reader["MinStockAlertSmallestUnits"]),
                    RetailPriceBoxPiastres = Convert.ToInt64(reader["RetailPriceBoxPiastres"])
                });
            }

            return list;
        }

        /// <summary>
        /// تقرير الأدوية قريبة الصلاحية والمنتهية
        /// </summary>
        public List<ExpiryReportRow> GetNearExpiryReport(int days = 90)
        {
            var list = new List<ExpiryReportRow>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            string thresholdDate = ExpiryPolicy.ToIso(DateTime.Today.AddDays(days));

            cmd.CommandText = @"
                SELECT b.Id, b.ItemId, i.Code, i.NameAr, b.BatchNumber, b.ExpiryDate,
                       b.RemainingSmallestUnits, b.CostPricePerSmallestUnitPiastres
                FROM ItemBatches b
                JOIN Items i ON b.ItemId = i.Id
                WHERE b.RemainingSmallestUnits > 0
                  AND b.ExpiryDate <= @thresh
                ORDER BY b.ExpiryDate ASC;";

            cmd.Parameters.AddWithValue("@thresh", thresholdDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ExpiryReportRow
                {
                    ItemId = Convert.ToInt32(reader["ItemId"]),
                    ItemCode = reader["Code"].ToString() ?? string.Empty,
                    NameAr = reader["NameAr"].ToString() ?? string.Empty,
                    BatchNumber = reader["BatchNumber"].ToString() ?? string.Empty,
                    ExpiryDate = DbDate.Parse(reader["ExpiryDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                    RemainingSmallestUnits = Convert.ToInt32(reader["RemainingSmallestUnits"]),
                    CostPricePerSmallestUnitPiastres = Convert.ToInt64(reader["CostPricePerSmallestUnitPiastres"])
                });
            }

            return list;
        }

        /// <summary>
        /// تقرير تقييم المخزون المالي
        /// </summary>
        public InventoryValuationSummary GetInventoryValuation()
        {
            var summary = new InventoryValuationSummary();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT 
                    (SELECT COUNT(*) FROM Items WHERE IsActive = 1) AS ItemsCount,
                    (SELECT COUNT(*) FROM ItemBatches WHERE RemainingSmallestUnits > 0) AS BatchesCount,
                    (SELECT IFNULL(SUM(TotalStockSmallestUnits), 0) FROM Items WHERE IsActive = 1) AS TotalUnits,
                    (
                        SELECT IFNULL(SUM(RemainingSmallestUnits * CostPricePerSmallestUnitPiastres), 0)
                        FROM ItemBatches
                        WHERE RemainingSmallestUnits > 0
                    ) AS TotalCostValuation,
                    (
                        SELECT IFNULL(SUM((TotalStockSmallestUnits * RetailPriceBoxPiastres) / MAX(1, StripsPerBox * PiecesPerStrip)), 0)
                        FROM Items
                        WHERE IsActive = 1 AND TotalStockSmallestUnits > 0
                    ) AS TotalRetailValuation;";

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                summary.TotalItemsCount = Convert.ToInt32(reader["ItemsCount"]);
                summary.TotalBatchesCount = Convert.ToInt32(reader["BatchesCount"]);
                summary.TotalSmallestUnitsCount = Convert.ToInt32(reader["TotalUnits"]);
                summary.TotalValuationCostPiastres = Convert.ToInt64(reader["TotalCostValuation"]);
                summary.TotalValuationRetailPiastres = Convert.ToInt64(reader["TotalRetailValuation"]);
            }

            return summary;
        }

        /// <summary>
        /// تقرير تفصيلي لوردية واحدة: تفصيل طرق الدفع ومقارنة النقدية المتوقعة بالفعلية
        /// </summary>
        public ShiftClosureReport? GetShiftClosureReport(int shiftId)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, UserId, UserName, StartTime, EndTime, OpeningBalancePiastres,
                       TotalSalesCashPiastres, TotalSalesNetworkPiastres, TotalSalesOnAccountPiastres,
                       TotalReturnsCashPiastres, ExpectedCashPiastres, ActualCashPiastres, DifferencePiastres,
                       Status, Notes
                FROM Shifts
                WHERE Id = @sId
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@sId", shiftId);

            ShiftClosureReport? report = null;
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read()) return null;
                report = new ShiftClosureReport
                {
                    ShiftId = Convert.ToInt32(reader["Id"]),
                    UserId = Convert.ToInt32(reader["UserId"]),
                    CashierName = reader["UserName"].ToString() ?? string.Empty,
                    StartTime = DbDate.Parse(reader["StartTime"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                    EndTime = reader["EndTime"] is DBNull ? null : DbDate.Parse(reader["EndTime"].ToString()!),
                    OpeningBalancePiastres = Convert.ToInt64(reader["OpeningBalancePiastres"]),
                    Status = (ShiftStatus)Convert.ToInt32(reader["Status"]),
                    ActualCashPiastres = reader["ActualCashPiastres"] is DBNull ? null : Convert.ToInt64(reader["ActualCashPiastres"]),
                    DifferencePiastres = reader["DifferencePiastres"] is DBNull ? null : Convert.ToInt64(reader["DifferencePiastres"]),
                    Notes = reader["Notes"] is DBNull ? null : reader["Notes"].ToString()
                };
            }

            // استعلام إحصائيات الفواتير وطرق الدفع الدقيقة لهذه الوردية
            using var invCmd = conn.CreateCommand();
            invCmd.CommandText = @"
                SELECT 
                    COUNT(CASE WHEN inv.Type = 1 AND inv.Status <> 3 THEN 1 END) AS SalesInvoicesCount,
                    COUNT(CASE WHEN inv.Type = 2 AND inv.Status <> 3 THEN 1 END) AS ReturnsInvoicesCount,
                    IFNULL(SUM(CASE WHEN inv.Type = 1 AND inv.Status <> 3 AND inv.PaymentMethod = 1 THEN inv.TotalPiastres ELSE 0 END), 0)
                      + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON p.InvoiceId = pi.Id
                                WHERE pi.ShiftId = @sId AND pi.Type = 1 AND pi.Status <> 3 AND p.Method = 1), 0) AS CashSales,
                    IFNULL(SUM(CASE WHEN inv.Type = 1 AND inv.Status <> 3 AND inv.PaymentMethod = 2 THEN inv.TotalPiastres ELSE 0 END), 0)
                      + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON p.InvoiceId = pi.Id
                                WHERE pi.ShiftId = @sId AND pi.Type = 1 AND pi.Status <> 3 AND p.Method = 2), 0) AS NetworkSales,
                    IFNULL(SUM(CASE WHEN inv.Type = 1 AND inv.Status <> 3 AND inv.PaymentMethod = 3 THEN inv.TotalPiastres ELSE 0 END), 0)
                      + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON p.InvoiceId = pi.Id
                                WHERE pi.ShiftId = @sId AND pi.Type = 1 AND pi.Status <> 3 AND p.Method = 3), 0) AS OnAccountSales,
                    IFNULL(SUM(CASE WHEN inv.Type = 1 AND inv.Status <> 3 AND inv.PaymentMethod = 4 THEN inv.TotalPiastres ELSE 0 END), 0)
                      + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON p.InvoiceId = pi.Id
                                WHERE pi.ShiftId = @sId AND pi.Type = 1 AND pi.Status <> 3 AND p.Method = 4), 0) AS InsuranceSales,
                    IFNULL(SUM(CASE WHEN inv.Type = 2 AND inv.Status <> 3 AND inv.PaymentMethod = 1 THEN inv.TotalPiastres ELSE 0 END), 0) AS CashReturns,
                    IFNULL(SUM(CASE WHEN inv.Type = 2 AND inv.Status <> 3 AND inv.PaymentMethod <> 1 THEN inv.TotalPiastres ELSE 0 END), 0) AS NonCashReturns
                FROM Invoices inv
                WHERE inv.ShiftId = @sId;";
            invCmd.Parameters.AddWithValue("@sId", shiftId);

            using (var reader = invCmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    report.InvoicesCount = Convert.ToInt32(reader["SalesInvoicesCount"]);
                    report.ReturnsCount = Convert.ToInt32(reader["ReturnsInvoicesCount"]);
                    report.CashSalesPiastres = Convert.ToInt64(reader["CashSales"]);
                    report.NetworkSalesPiastres = Convert.ToInt64(reader["NetworkSales"]);
                    report.OnAccountSalesPiastres = Convert.ToInt64(reader["OnAccountSales"]);
                    report.InsuranceSalesPiastres = Convert.ToInt64(reader["InsuranceSales"]);
                    report.CashReturnsPiastres = Convert.ToInt64(reader["CashReturns"]);
                    report.NonCashReturnsPiastres = Convert.ToInt64(reader["NonCashReturns"]);
                }
            }

            // المتوقع = الافتتاحي + المقبوض نقداً - المرتجع نقداً
            report.ExpectedCashPiastres = report.OpeningBalancePiastres + report.CashSalesPiastres - report.CashReturnsPiastres;
            if (report.ActualCashPiastres.HasValue)
            {
                report.DifferencePiastres = report.ActualCashPiastres.Value - report.ExpectedCashPiastres;
            }

            return report;
        }

        /// <summary>
        /// تقرير ورديات الكاشيرات للفترة المحددة: عجز وزيادة ومقارنة النقدية وتفصيل طرق الدفع
        /// </summary>
        public List<CashierShiftSummaryRow> GetCashierShiftsReport(DateTime fromDate, DateTime toDate, int? userId = null)
        {
            var list = new List<CashierShiftSummaryRow>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            string userFilter = (userId.HasValue && userId.Value > 0) ? "AND s.UserId = @uId" : "";

            cmd.CommandText = $@"
                SELECT s.Id, s.UserId, s.UserName, s.StartTime, s.EndTime, s.Status,
                       s.OpeningBalancePiastres, s.ActualCashPiastres, s.DifferencePiastres,
                       (SELECT COUNT(*) FROM Invoices i WHERE i.ShiftId = s.Id AND i.Type = 1 AND i.Status <> 3) AS InvoicesCount,
                       (SELECT COUNT(*) FROM Invoices i WHERE i.ShiftId = s.Id AND i.Type = 2 AND i.Status <> 3) AS ReturnsCount,
                       IFNULL((SELECT SUM(CASE WHEN i.PaymentMethod = 1 THEN i.TotalPiastres ELSE 0 END) FROM Invoices i WHERE i.ShiftId = s.Id AND i.Type = 1 AND i.Status <> 3), 0)
                         + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON p.InvoiceId = pi.Id WHERE pi.ShiftId = s.Id AND pi.Type = 1 AND pi.Status <> 3 AND p.Method = 1), 0) AS CashSales,
                       IFNULL((SELECT SUM(CASE WHEN i.PaymentMethod = 2 THEN i.TotalPiastres ELSE 0 END) FROM Invoices i WHERE i.ShiftId = s.Id AND i.Type = 1 AND i.Status <> 3), 0)
                         + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON p.InvoiceId = pi.Id WHERE pi.ShiftId = s.Id AND pi.Type = 1 AND pi.Status <> 3 AND p.Method = 2), 0) AS NetworkSales,
                       IFNULL((SELECT SUM(CASE WHEN i.PaymentMethod = 3 THEN i.TotalPiastres ELSE 0 END) FROM Invoices i WHERE i.ShiftId = s.Id AND i.Type = 1 AND i.Status <> 3), 0)
                         + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON p.InvoiceId = pi.Id WHERE pi.ShiftId = s.Id AND pi.Type = 1 AND pi.Status <> 3 AND p.Method = 3), 0) AS OnAccountSales,
                       IFNULL((SELECT SUM(CASE WHEN i.PaymentMethod = 4 THEN i.TotalPiastres ELSE 0 END) FROM Invoices i WHERE i.ShiftId = s.Id AND i.Type = 1 AND i.Status <> 3), 0)
                         + IFNULL((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices pi ON p.InvoiceId = pi.Id WHERE pi.ShiftId = s.Id AND pi.Type = 1 AND pi.Status <> 3 AND p.Method = 4), 0) AS InsuranceSales,
                       IFNULL((SELECT SUM(CASE WHEN i.PaymentMethod = 1 THEN i.TotalPiastres ELSE 0 END) FROM Invoices i WHERE i.ShiftId = s.Id AND i.Type = 2 AND i.Status <> 3), 0) AS CashReturns
                FROM Shifts s
                WHERE date(s.StartTime) >= date(@from) AND date(s.StartTime) <= date(@to)
                  {userFilter}
                ORDER BY s.Id DESC;";

            cmd.Parameters.AddWithValue("@from", fromDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", toDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            if (userId.HasValue && userId.Value > 0)
                cmd.Parameters.AddWithValue("@uId", userId.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                long openBal = Convert.ToInt64(reader["OpeningBalancePiastres"]);
                long cashSales = Convert.ToInt64(reader["CashSales"]);
                long cashRet = Convert.ToInt64(reader["CashReturns"]);
                long expected = openBal + cashSales - cashRet;

                long? actual = reader["ActualCashPiastres"] is DBNull ? null : Convert.ToInt64(reader["ActualCashPiastres"]);
                long? diff = actual.HasValue ? actual.Value - expected : (reader["DifferencePiastres"] is DBNull ? null : Convert.ToInt64(reader["DifferencePiastres"]));

                list.Add(new CashierShiftSummaryRow
                {
                    ShiftId = Convert.ToInt32(reader["Id"]),
                    CashierId = Convert.ToInt32(reader["UserId"]),
                    CashierName = reader["UserName"].ToString() ?? string.Empty,
                    StartTime = DbDate.Parse(reader["StartTime"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                    EndTime = reader["EndTime"] is DBNull ? null : DbDate.Parse(reader["EndTime"].ToString()!),
                    Status = (ShiftStatus)Convert.ToInt32(reader["Status"]),
                    InvoicesCount = Convert.ToInt32(reader["InvoicesCount"]),
                    ReturnsCount = Convert.ToInt32(reader["ReturnsCount"]),
                    OpeningBalancePiastres = openBal,
                    CashSalesPiastres = cashSales,
                    NetworkSalesPiastres = Convert.ToInt64(reader["NetworkSales"]),
                    OnAccountSalesPiastres = Convert.ToInt64(reader["OnAccountSales"]),
                    InsuranceSalesPiastres = Convert.ToInt64(reader["InsuranceSales"]),
                    CashReturnsPiastres = cashRet,
                    ExpectedCashPiastres = expected,
                    ActualCashPiastres = actual,
                    DifferencePiastres = diff
                });
            }

            return list;
        }

        /// <summary>
        /// تقرير ملخص العجز والزيادة مجمّعاً لكل كاشير
        /// </summary>
        public List<CashierDiscrepancySummary> GetCashierDiscrepancySummary(DateTime fromDate, DateTime toDate)
        {
            var shifts = GetCashierShiftsReport(fromDate, toDate);
            var grouped = new Dictionary<int, CashierDiscrepancySummary>();

            foreach (var s in shifts)
            {
                if (!grouped.TryGetValue(s.CashierId, out var sum))
                {
                    sum = new CashierDiscrepancySummary
                    {
                        CashierId = s.CashierId,
                        CashierName = s.CashierName
                    };
                    grouped[s.CashierId] = sum;
                }

                sum.ShiftsCount++;
                if (s.Status == ShiftStatus.Closed) sum.ClosedShiftsCount++;
                sum.TotalSalesPiastres += s.TotalSalesPiastres;
                sum.TotalExpectedCashPiastres += s.ExpectedCashPiastres;

                if (s.ActualCashPiastres.HasValue)
                {
                    sum.TotalActualCashPiastres += s.ActualCashPiastres.Value;
                }

                if (s.DifferencePiastres.HasValue)
                {
                    long d = s.DifferencePiastres.Value;
                    if (d < 0)
                        sum.TotalShortagePiastres += Math.Abs(d);
                    else if (d > 0)
                        sum.TotalSurplusPiastres += d;
                }
            }

            var result = new List<CashierDiscrepancySummary>(grouped.Values);
            result.Sort((a, b) => string.Compare(a.CashierName, b.CashierName, StringComparison.Ordinal));
            return result;
        }
    }
}
