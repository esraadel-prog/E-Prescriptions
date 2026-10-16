using System;
using System.Collections.Generic;
using System.Data.SQLite;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.Data.Repositories
{
    public class FinanceRepository
    {
        private readonly DatabaseContext _dbContext;

        public FinanceRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public string GenerateNextExpenseNumber()
        {
            string year = DateTime.Now.Year.ToString();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Expenses;";
            long count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0) + 1;
            return $"EXP-{year}-{count:D5}";
        }

        public string GenerateNextJournalEntryNumber()
        {
            string year = DateTime.Now.Year.ToString();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM JournalEntries;";
            long count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0) + 1;
            return $"JV-{year}-{count:D5}";
        }

        public void AddExpense(Expense expense)
        {
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = @"
                    INSERT INTO Expenses (ExpenseNumber, Category, AmountPiastres, Date, PaidTo, Notes, CreatedAt)
                    VALUES (@num, @cat, @amt, @date, @paidTo, @notes, datetime('now'));";
                cmd.Parameters.AddWithValue("@num", expense.ExpenseNumber);
                cmd.Parameters.AddWithValue("@cat", expense.Category);
                cmd.Parameters.AddWithValue("@amt", expense.AmountPiastres);
                cmd.Parameters.AddWithValue("@date", expense.Date.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@paidTo", expense.PaidTo);
                cmd.Parameters.AddWithValue("@notes", (object?)expense.Notes ?? DBNull.Value);
                cmd.ExecuteNonQuery();

                // تسجيل قيد يومي تلقائي: من حـ/ المصروفات إلى حـ/ الصندوق
                using var cmdJv = conn.CreateCommand();
                cmdJv.Transaction = trans;
                cmdJv.CommandText = @"
                    INSERT INTO JournalEntries (EntryNumber, Date, Description, DebitAccount, CreditAccount, AmountPiastres, CreatedAt)
                    VALUES (@jNum, @date, @desc, @deb, @crd, @amt, datetime('now'));";
                cmdJv.Parameters.AddWithValue("@jNum", $"JV-{expense.ExpenseNumber}");
                cmdJv.Parameters.AddWithValue("@date", expense.Date.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                cmdJv.Parameters.AddWithValue("@desc", $"إثبات مصروف: {expense.Category} - {expense.PaidTo}");
                cmdJv.Parameters.AddWithValue("@deb", $"مصروفات: {expense.Category}");
                cmdJv.Parameters.AddWithValue("@crd", "الصندوق / الخزينة");
                cmdJv.Parameters.AddWithValue("@amt", expense.AmountPiastres);
                cmdJv.ExecuteNonQuery();

                trans.Commit();
            }
            catch (Exception ex)
            {
                trans.Rollback();
                Logger.LogException(ex, "AddExpense");
                throw;
            }
        }

        public List<Expense> GetExpenses(DateTime from, DateTime to)
        {
            var list = new List<Expense>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, ExpenseNumber, Category, AmountPiastres, Date, PaidTo, Notes, CreatedAt
                FROM Expenses
                WHERE date(Date) >= date(@from) AND date(Date) <= date(@to)
                ORDER BY Date DESC;";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Expense
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    ExpenseNumber = reader["ExpenseNumber"].ToString() ?? string.Empty,
                    Category = reader["Category"].ToString() ?? string.Empty,
                    AmountPiastres = Convert.ToInt64(reader["AmountPiastres"]),
                    Date = DbDate.Parse(reader["Date"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                    PaidTo = reader["PaidTo"].ToString() ?? string.Empty,
                    Notes = reader["Notes"] is DBNull ? null : reader["Notes"].ToString()
                });
            }
            return list;
        }

        public List<JournalEntry> GetJournalEntries(DateTime from, DateTime to)
        {
            var list = new List<JournalEntry>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, EntryNumber, Date, Description, DebitAccount, CreditAccount, AmountPiastres, CreatedAt
                FROM JournalEntries
                WHERE date(Date) >= date(@from) AND date(Date) <= date(@to)
                ORDER BY Date DESC;";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new JournalEntry
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    EntryNumber = reader["EntryNumber"].ToString() ?? string.Empty,
                    Date = DbDate.Parse(reader["Date"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                    Description = reader["Description"].ToString() ?? string.Empty,
                    DebitAccount = reader["DebitAccount"].ToString() ?? string.Empty,
                    CreditAccount = reader["CreditAccount"].ToString() ?? string.Empty,
                    AmountPiastres = Convert.ToInt64(reader["AmountPiastres"])
                });
            }
            return list;
        }

        public VatReturnStatement GetVatReturnReport(DateTime from, DateTime to)
        {
            var vatReport = new VatReturnStatement
            {
                PeriodStart = from,
                PeriodEnd = to
            };

            using var conn = _dbContext.CreateConnection();

            // 1) مبيعات الفترة ومخرجات الضريبة (صافي بعد مرتجعات المبيعات، خطوة 47)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(CASE WHEN Type = 1 THEN SubtotalPiastres ELSE -SubtotalPiastres END), 0) AS TotalSales,
                           COALESCE(SUM(CASE WHEN Type = 1 THEN VatPiastres ELSE -VatPiastres END), 0) AS TotalSalesVat
                    FROM Invoices
                    WHERE Type IN (1, 2) AND Status <> 3 AND date(Date) >= date(@from) AND date(Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    vatReport.OutputVatPiastres = Convert.ToInt64(reader["TotalSalesVat"]);
                }
            }

            // الوعاء الضريبي من البنود الفعلية: الخاضع = صافي البند (بعد الخصم وقبل الضريبة) للبنود بنسبة > 0، والمعفى = نسبتها 0
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(CASE WHEN ii.VatRatePercent > 0 THEN (ii.TotalPiastres - ii.VatPiastres) * (CASE WHEN i.Type = 1 THEN 1 ELSE -1 END) ELSE 0 END), 0) AS Taxable,
                           COALESCE(SUM(CASE WHEN ii.VatRatePercent > 0 THEN 0 ELSE (ii.TotalPiastres - ii.VatPiastres) * (CASE WHEN i.Type = 1 THEN 1 ELSE -1 END) END), 0) AS Exempt
                    FROM InvoiceItems ii
                    JOIN Invoices i ON i.Id = ii.InvoiceId
                    WHERE i.Type IN (1, 2) AND i.Status <> 3 AND date(i.Date) >= date(@from) AND date(i.Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    vatReport.TaxableSalesPiastres = Convert.ToInt64(reader["Taxable"]);
                    vatReport.ExemptSalesPiastres = Convert.ToInt64(reader["Exempt"]);
                }
            }

            // 2) مشتريات الفترة ومدخلات الضريبة
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(SubtotalPiastres), 0) AS TotalPurchases,
                           COALESCE(SUM(VatPiastres), 0) AS TotalPurchasesVat
                    FROM PurchaseInvoices
                    WHERE date(InvoiceDate) >= date(@from) AND date(InvoiceDate) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    vatReport.InputVatPiastres = Convert.ToInt64(reader["TotalPurchasesVat"]);
                }
            }

            // البند الخاضع = نسبته المخزنة > 0 (أو بند قديم بدون نسبة مخزنة وعليه ضريبة فعلية)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(CASE WHEN (pi.VatPercent > 0 OR (pi.VatPercent IS NULL AND pi.VatPiastres > 0)) THEN pi.TotalPiastres - pi.VatPiastres ELSE 0 END), 0) AS Taxable,
                           COALESCE(SUM(CASE WHEN (pi.VatPercent > 0 OR (pi.VatPercent IS NULL AND pi.VatPiastres > 0)) THEN 0 ELSE pi.TotalPiastres - pi.VatPiastres END), 0) AS Exempt
                    FROM PurchaseInvoiceItems pi
                    JOIN PurchaseInvoices h ON h.Id = pi.PurchaseInvoiceId
                    WHERE date(h.InvoiceDate) >= date(@from) AND date(h.InvoiceDate) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    vatReport.TaxablePurchasesPiastres = Convert.ToInt64(reader["Taxable"]);
                    vatReport.ExemptPurchasesPiastres = Convert.ToInt64(reader["Exempt"]);
                }
            }

            return vatReport;
        }

        public CashFlowSummary GetCashFlowSummary(DateTime from, DateTime to)
        {
            var summary = new CashFlowSummary
            {
                PeriodStart = from,
                PeriodEnd = to
            };

            using var conn = _dbContext.CreateConnection();

            // المقبوضات: مبيعات نقدية
            using (var cmd = conn.CreateCommand())
            {
                // مبيعات نقدية (Type=1) بما فيها الجزء النقدي من الدفع المقسّم؛ المرتجعات تُحسب في بند منفصل (خطوة 47)
                cmd.CommandText = @"
                    SELECT COALESCE((SELECT SUM(TotalPiastres) FROM Invoices
                                     WHERE Type = 1 AND PaymentMethod = 1 AND Status <> 3 AND date(Date) >= date(@from) AND date(Date) <= date(@to)), 0)
                         + COALESCE((SELECT SUM(p.AmountPiastres) FROM InvoicePayments p JOIN Invoices i ON i.Id = p.InvoiceId
                                     WHERE p.Method = 1 AND i.Type = 1 AND i.Status <> 3 AND date(i.Date) >= date(@from) AND date(i.Date) <= date(@to)), 0);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                summary.CashSalesPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // المدفوعات: مرتجعات مبيعات نقدية (بتخرج من الدرج)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(TotalPiastres), 0) FROM Invoices
                    WHERE Type = 2 AND PaymentMethod = 1 AND Status <> 3 AND date(Date) >= date(@from) AND date(Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                summary.CashReturnsPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // المقبوضات: سندات قبض عملاء
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(AmountPiastres), 0) FROM CustomerVouchers
                    WHERE date(Date) >= date(@from) AND date(Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                summary.CustomerReceiptsPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // المدفوعات: سندات صرف موردين
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(AmountPiastres), 0) FROM SupplierVouchers
                    WHERE date(Date) >= date(@from) AND date(Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                summary.SupplierPaymentsPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // المدفوعات: مصروفات تشغيلية
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(AmountPiastres), 0) FROM Expenses
                    WHERE date(Date) >= date(@from) AND date(Date) <= date(@to);";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                summary.OperatingExpensesPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            return summary;
        }

        /// <summary>السعر المحفوظ لجرام الذهب، أو null لو لسه ما اتدخلش</summary>
        public ZakatGoldPrice? GetZakatGoldPrice()
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT GoldGramPricePiastres, UpdatedAt FROM ZakatSettings WHERE Id = 1;";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return new ZakatGoldPrice
            {
                GramPricePiastres = Convert.ToInt64(reader["GoldGramPricePiastres"]),
                UpdatedAt = DbDate.TryParse(reader["UpdatedAt"]?.ToString(), out var at) ? at : DateTime.MinValue
            };
        }

        /// <summary>يحفظ سعر جرام الذهب (لازم أكبر من صفر)</summary>
        public void SaveZakatGoldPrice(long gramPricePiastres)
        {
            if (gramPricePiastres <= 0)
                throw new ArgumentOutOfRangeException(nameof(gramPricePiastres), "سعر جرام الذهب لازم يكون أكبر من صفر.");

            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ZakatSettings (Id, GoldGramPricePiastres, UpdatedAt) VALUES (1, @p, @at)
                ON CONFLICT(Id) DO UPDATE SET GoldGramPricePiastres = @p, UpdatedAt = @at;";
            cmd.Parameters.AddWithValue("@p", gramPricePiastres);
            cmd.Parameters.AddWithValue("@at", DbDate.NowFull());
            cmd.ExecuteNonQuery();
        }

        public ZakatCalculation CalculateZakat(bool evaluateAtCost, long goldGramPricePiastres, bool isHijri)
        {
            if (goldGramPricePiastres <= 0)
                throw new ArgumentOutOfRangeException(nameof(goldGramPricePiastres), "لازم إدخال سعر جرام الذهب (أكبر من صفر) لحساب النصاب.");

            var zakat = new ZakatCalculation
            {
                EvaluateAtCost = evaluateAtCost,
                GoldGramPricePiastres = goldGramPricePiastres,
                IsHijriYear = isHijri
            };

            using var conn = _dbContext.CreateConnection();

            // 1) تقييم عروض التجارة (المخزون)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT TotalStockSmallestUnits, StripsPerBox, PiecesPerStrip,
                           RetailPriceBoxPiastres, CostPriceBoxPiastres
                    FROM Items WHERE IsActive = 1;";

                using var reader = cmd.ExecuteReader();
                long totalStockValue = 0;
                while (reader.Read())
                {
                    int totalUnits = Convert.ToInt32(reader["TotalStockSmallestUnits"]);
                    int strips = Convert.ToInt32(reader["StripsPerBox"]);
                    int pieces = Convert.ToInt32(reader["PiecesPerStrip"]);
                    long boxPrice = evaluateAtCost 
                        ? Convert.ToInt64(reader["CostPriceBoxPiastres"]) 
                        : Convert.ToInt64(reader["RetailPriceBoxPiastres"]);

                    int unitsPerBox = Math.Max(1, strips * pieces);
                    if (totalUnits > 0 && boxPrice > 0)
                    {
                        totalStockValue += (totalUnits * boxPrice) / unitsPerBox;
                    }
                }
                zakat.StockValuePiastres = totalStockValue;
            }

            // 2) النقدية الحاضرة (من رصيد الصندوق بالورديات)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(ExpectedCashPiastres), 0) FROM Shifts WHERE Status = 1;";
                zakat.CashAndBankPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // 3) الديون المرجوة على العملاء
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(CurrentBalancePiastres), 0) FROM Customers WHERE IsActive = 1;";
                zakat.GoodReceivablesPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            // 4) الالتزامات الحالة للموردين
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(BalancePiastres), 0) FROM Suppliers WHERE IsActive = 1;";
                zakat.DuePayablesPiastres = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            }

            return zakat;
        }
    }
}
