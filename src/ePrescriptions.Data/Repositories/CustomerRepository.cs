using System;
using ePrescriptions.Core.Helpers;
using System.Collections.Generic;
using System.Data.SQLite;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Data.Repositories
{
    public class CustomerRepository
    {
        private readonly DatabaseContext _dbContext;

        public CustomerRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public List<Customer> GetAll()
        {
            var list = new List<Customer>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Phone, NationalId, CreditLimitPiastres, CurrentBalancePiastres, PriceCategory, Notes, IsActive, IsDefaultCash FROM Customers WHERE IsActive = 1 ORDER BY Name ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapCustomer(reader));
            }
            return list;
        }

        public Customer? GetById(int id)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Phone, NationalId, CreditLimitPiastres, CurrentBalancePiastres, PriceCategory, Notes, IsActive, IsDefaultCash FROM Customers WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapCustomer(reader) : null;
        }

        /// <summary>رقم العميل النقدي العام (null لو لسه مش موجود)</summary>
        public int? GetDefaultCashCustomerId()
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id FROM Customers WHERE IsDefaultCash = 1 LIMIT 1;";
            object? v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? (int?)null : Convert.ToInt32(v);
        }

        /// <summary>العملاء المتجاوزون لحد الائتمان (الرصيد أكبر من الحد المسموح، مع استبعاد العميل النقدي العام)</summary>
        public List<Customer> GetCreditLimitExceededCustomers()
        {
            var list = new List<Customer>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Name, Phone, NationalId, CreditLimitPiastres, CurrentBalancePiastres, PriceCategory, Notes, IsActive, IsDefaultCash
                FROM Customers
                WHERE IsActive = 1 AND IsDefaultCash = 0 AND CurrentBalancePiastres > CreditLimitPiastres
                ORDER BY (CurrentBalancePiastres - CreditLimitPiastres) DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapCustomer(reader));
            }
            return list;
        }

        public int AddCustomer(Customer customer)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Customers (Name, Phone, NationalId, CreditLimitPiastres, CurrentBalancePiastres, PriceCategory, Notes, IsActive)
                VALUES (@name, @phone, @nid, @limit, @bal, @cat, @notes, 1);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@name", customer.Name.Trim());
            cmd.Parameters.AddWithValue("@phone", customer.Phone.Trim());
            cmd.Parameters.AddWithValue("@nid", (object?)customer.NationalId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@limit", customer.CreditLimitPiastres);
            cmd.Parameters.AddWithValue("@bal", customer.CurrentBalancePiastres);
            cmd.Parameters.AddWithValue("@cat", (int)customer.PriceCategory);
            cmd.Parameters.AddWithValue("@notes", (object?)customer.Notes ?? DBNull.Value);

            int id = Convert.ToInt32(cmd.ExecuteScalar());
            customer.Id = id;
            return id;
        }

        public void UpdateCustomer(Customer customer)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Customers SET
                    Name = @name,
                    Phone = @phone,
                    NationalId = @nid,
                    CreditLimitPiastres = @limit,
                    PriceCategory = @cat,
                    Notes = @notes
                WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@name", customer.Name.Trim());
            cmd.Parameters.AddWithValue("@phone", customer.Phone.Trim());
            cmd.Parameters.AddWithValue("@nid", (object?)customer.NationalId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@limit", customer.CreditLimitPiastres);
            cmd.Parameters.AddWithValue("@cat", (int)customer.PriceCategory);
            cmd.Parameters.AddWithValue("@notes", (object?)customer.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", customer.Id);
            cmd.ExecuteNonQuery();
        }

        public string GenerateNextCustomerVoucherNumber()
        {
            string year = DateTime.Now.Year.ToString();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM CustomerVouchers;";
            long count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0) + 1;
            return $"CV-{year}-{count:D5}";
        }

        public bool AddCustomerVoucher(CustomerVoucher voucher)
        {
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = @"
                    INSERT INTO CustomerVouchers (VoucherNumber, CustomerId, CustomerName, Date, AmountPiastres, PaymentMethod, Notes, CreatedAt)
                    VALUES (@num, @cId, @cName, @date, @amt, @pMethod, @notes, datetime('now'));";
                cmd.Parameters.AddWithValue("@num", voucher.VoucherNumber);
                cmd.Parameters.AddWithValue("@cId", voucher.CustomerId);
                cmd.Parameters.AddWithValue("@cName", voucher.CustomerName);
                cmd.Parameters.AddWithValue("@date", voucher.Date.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@amt", voucher.AmountPiastres);
                cmd.Parameters.AddWithValue("@pMethod", (int)voucher.PaymentMethod);
                cmd.Parameters.AddWithValue("@notes", (object?)voucher.Notes ?? DBNull.Value);
                cmd.ExecuteNonQuery();

                // خصم المبلغ المقبوض من رصيد مديونية العميل
                using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = trans;
                updateCmd.CommandText = @"
                    UPDATE Customers
                    SET CurrentBalancePiastres = CurrentBalancePiastres - @amt
                    WHERE Id = @cId;";
                updateCmd.Parameters.AddWithValue("@amt", voucher.AmountPiastres);
                updateCmd.Parameters.AddWithValue("@cId", voucher.CustomerId);
                updateCmd.ExecuteNonQuery();

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                return false;
            }
        }

        public List<StatementRow> GetCustomerStatement(int customerId)
        {
            var rows = new List<StatementRow>();
            using var conn = _dbContext.CreateConnection();

            // 1) فواتير المبيعات الآجلة (مدين / Debit)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Date, InvoiceNumber, Type, TotalPiastres, PaidPiastres, RemainingPiastres
                    FROM Invoices
                    WHERE CustomerId = @cId AND Status <> 3
                    ORDER BY Date ASC;";
                cmd.Parameters.AddWithValue("@cId", customerId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long total = Convert.ToInt64(reader["TotalPiastres"]);
                    long paid = Convert.ToInt64(reader["PaidPiastres"]);
                    long remaining = Convert.ToInt64(reader["RemainingPiastres"]);
                    string invNum = reader["InvoiceNumber"].ToString() ?? string.Empty;
                    DateTime dt = DbDate.Parse(reader["Date"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));

                    bool isReturn = Convert.ToInt32(reader["Type"]) == (int)InvoiceType.Return;
                    if (total > 0)
                    {
                        if (isReturn)
                        {
                            // مرتجع مبيعات (خطوة 47): دائن بقيمته، ومدين بالجزء اللي اتردّ فوراً (نقدي/شبكة) فيبقى الأثر = -المتبقي
                            // زي ما SaveInvoice بيخصم المتبقي من رصيد العميل بالظبط.
                            rows.Add(new StatementRow
                            {
                                Date = dt,
                                ReferenceNumber = invNum,
                                Description = "مرتجع مبيعات",
                                DebitPiastres = total - remaining,
                                CreditPiastres = total
                            });
                        }
                        else
                        {
                            rows.Add(new StatementRow
                            {
                                Date = dt,
                                ReferenceNumber = invNum,
                                Description = "فاتورة مبيعات",
                                DebitPiastres = total,
                                CreditPiastres = paid
                            });
                        }
                    }
                }
            }

            // 2) سندات القبض (دائن / Credit)
            using (var cmdV = conn.CreateCommand())
            {
                cmdV.CommandText = @"
                    SELECT Date, VoucherNumber, AmountPiastres, Notes
                    FROM CustomerVouchers
                    WHERE CustomerId = @cId
                    ORDER BY Date ASC;";
                cmdV.Parameters.AddWithValue("@cId", customerId);
                using var readerV = cmdV.ExecuteReader();
                while (readerV.Read())
                {
                    rows.Add(new StatementRow
                    {
                        Date = DbDate.Parse(readerV["Date"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                        ReferenceNumber = readerV["VoucherNumber"].ToString() ?? string.Empty,
                        Description = "سند قبض نقدي / سداد",
                        DebitPiastres = 0,
                        CreditPiastres = Convert.ToInt64(readerV["AmountPiastres"])
                    });
                }
            }

            // ترتيب زمني واحتساب الرصيد التراكمي
            rows.Sort((a, b) => a.Date.CompareTo(b.Date));

            // رصيد سابق/افتتاحي (خطوة 47): الرصيد الحالي المخزّن - صافي حركات الكشف. لو غير صفر نعرضه كأول سطر
            // عشان آخر رصيد في الكشف يطابق رصيد العميل الفعلي.
            long currentBalance;
            using (var balCmd = conn.CreateCommand())
            {
                balCmd.CommandText = "SELECT CurrentBalancePiastres FROM Customers WHERE Id = @cId;";
                balCmd.Parameters.AddWithValue("@cId", customerId);
                currentBalance = Convert.ToInt64(balCmd.ExecuteScalar() ?? 0);
            }
            long movement = 0;
            foreach (var r in rows) movement += r.DebitPiastres - r.CreditPiastres;
            long opening = currentBalance - movement;
            if (opening != 0)
            {
                rows.Insert(0, new StatementRow
                {
                    Date = rows.Count > 0 ? rows[0].Date : DateTime.Today,
                    ReferenceNumber = "-",
                    Description = "رصيد سابق (افتتاحي)",
                    DebitPiastres = opening > 0 ? opening : 0,
                    CreditPiastres = opening < 0 ? -opening : 0
                });
            }
            long running = 0;
            foreach (var r in rows)
            {
                running += (r.DebitPiastres - r.CreditPiastres);
                r.RunningBalancePiastres = running;
            }

            return rows;
        }

        private static Customer MapCustomer(SQLiteDataReader reader)
        {
            return new Customer
            {
                Id = Convert.ToInt32(reader["Id"]),
                Name = reader["Name"].ToString() ?? string.Empty,
                Phone = reader["Phone"].ToString() ?? string.Empty,
                NationalId = reader["NationalId"] is DBNull ? null : reader["NationalId"].ToString(),
                CreditLimitPiastres = Convert.ToInt64(reader["CreditLimitPiastres"]),
                CurrentBalancePiastres = Convert.ToInt64(reader["CurrentBalancePiastres"]),
                PriceCategory = (PriceCategory)Convert.ToInt32(reader["PriceCategory"]),
                Notes = reader["Notes"] is DBNull ? null : reader["Notes"].ToString(),
                IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                IsDefaultCash = Convert.ToInt32(reader["IsDefaultCash"]) == 1
            };
        }
    }
}
