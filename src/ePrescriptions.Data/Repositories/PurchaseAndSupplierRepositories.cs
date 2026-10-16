using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Data.Repositories
{
    public class SupplierRepository
    {
        private readonly DatabaseContext _dbContext;

        public SupplierRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public List<Supplier> GetAll()
        {
            var list = new List<Supplier>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Phone, TaxNumber, Address, BalancePiastres, IsActive FROM Suppliers WHERE IsActive = 1 ORDER BY Name ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Supplier
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = reader["Name"].ToString() ?? string.Empty,
                    Phone = reader["Phone"].ToString() ?? string.Empty,
                    TaxNumber = reader["TaxNumber"] is DBNull ? null : reader["TaxNumber"].ToString(),
                    Address = reader["Address"] is DBNull ? null : reader["Address"].ToString(),
                    BalancePiastres = Convert.ToInt64(reader["BalancePiastres"]),
                    IsActive = Convert.ToInt32(reader["IsActive"]) == 1
                });
            }
            return list;
        }

        public Supplier? GetById(int id)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Phone, TaxNumber, Address, BalancePiastres, IsActive FROM Suppliers WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new Supplier
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = reader["Name"].ToString() ?? string.Empty,
                    Phone = reader["Phone"].ToString() ?? string.Empty,
                    TaxNumber = reader["TaxNumber"] is DBNull ? null : reader["TaxNumber"].ToString(),
                    Address = reader["Address"] is DBNull ? null : reader["Address"].ToString(),
                    BalancePiastres = Convert.ToInt64(reader["BalancePiastres"]),
                    IsActive = Convert.ToInt32(reader["IsActive"]) == 1
                };
            }
            return null;
        }

        public int SaveSupplier(Supplier supplier)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            if (supplier.Id == 0)
            {
                cmd.CommandText = @"
                    INSERT INTO Suppliers (Name, Phone, TaxNumber, Address, BalancePiastres, IsActive)
                    VALUES (@name, @phone, @tax, @addr, @bal, 1);
                    SELECT last_insert_rowid();";
            }
            else
            {
                cmd.CommandText = @"
                    UPDATE Suppliers SET
                        Name = @name,
                        Phone = @phone,
                        TaxNumber = @tax,
                        Address = @addr
                    WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", supplier.Id);
            }

            cmd.Parameters.AddWithValue("@name", supplier.Name.Trim());
            cmd.Parameters.AddWithValue("@phone", supplier.Phone.Trim());
            cmd.Parameters.AddWithValue("@tax", (object?)supplier.TaxNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@addr", (object?)supplier.Address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bal", supplier.BalancePiastres);

            if (supplier.Id == 0)
            {
                supplier.Id = Convert.ToInt32(cmd.ExecuteScalar());
            }
            else
            {
                cmd.ExecuteNonQuery();
            }

            return supplier.Id;
        }
    }

    public class PurchaseRepository
    {
        private readonly DatabaseContext _dbContext;

        public PurchaseRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        private static string PurchaseSequenceKey() => $"PUR-{DateTime.Now.Year}";

        /// <summary>الرقم المتوقع (للعرض)؛ الرقم النهائي بيتحجز داخل Transaction الحفظ</summary>
        public string GenerateNextPurchaseInvoiceNumber()
        {
            string key = PurchaseSequenceKey();
            using var conn = _dbContext.CreateConnection();
            return NumberSequence.Format(key, NumberSequence.Peek(conn, key, "PurchaseInvoices", "InvoiceNumber"));
        }

        public string GenerateNextSupplierVoucherNumber()
        {
            string year = DateTime.Now.Year.ToString();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM SupplierVouchers;";
            long count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0) + 1;
            return $"SV-{year}-{count:D5}";
        }

        public bool SavePurchaseInvoice(PurchaseInvoice invoice, out string errorMessage)
        {
            errorMessage = string.Empty;
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();

            try
            {
                string purKey = PurchaseSequenceKey();
                invoice.InvoiceNumber = NumberSequence.Format(purKey, NumberSequence.Next(conn, trans, purKey, "PurchaseInvoices", "InvoiceNumber"));

                // 1) حفظ رأس فاتورة الشراء
                using var cmdInv = conn.CreateCommand();
                cmdInv.Transaction = trans;
                cmdInv.CommandText = @"
                    INSERT INTO PurchaseInvoices (
                        InvoiceNumber, SupplierId, SupplierName, InvoiceDate,
                        SubtotalPiastres, VatPiastres, TotalPiastres, PaidPiastres,
                        RemainingPiastres, Notes, CreatedAt
                    ) VALUES (
                        @num, @sId, @sName, @date,
                        @sub, @vat, @tot, @paid,
                        @rem, @notes, datetime('now')
                    );
                    SELECT last_insert_rowid();";

                cmdInv.Parameters.AddWithValue("@num", invoice.InvoiceNumber);
                cmdInv.Parameters.AddWithValue("@sId", invoice.SupplierId);
                cmdInv.Parameters.AddWithValue("@sName", invoice.SupplierName);
                cmdInv.Parameters.AddWithValue("@date", invoice.InvoiceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmdInv.Parameters.AddWithValue("@sub", invoice.SubtotalPiastres);
                cmdInv.Parameters.AddWithValue("@vat", invoice.VatPiastres);
                cmdInv.Parameters.AddWithValue("@tot", invoice.TotalPiastres);
                cmdInv.Parameters.AddWithValue("@paid", invoice.PaidPiastres);
                cmdInv.Parameters.AddWithValue("@rem", invoice.RemainingPiastres);
                cmdInv.Parameters.AddWithValue("@notes", (object?)invoice.Notes ?? DBNull.Value);

                int invId = Convert.ToInt32(cmdInv.ExecuteScalar());
                invoice.Id = invId;

                // 2) حفظ بنود الفاتورة وإضافة دفعات المخزون الجديدة
                foreach (var item in invoice.Items)
                {
                    item.PurchaseInvoiceId = invId;

                    using var cmdItem = conn.CreateCommand();
                    cmdItem.Transaction = trans;
                    cmdItem.CommandText = @"
                        INSERT INTO PurchaseInvoiceItems (
                            PurchaseInvoiceId, ItemId, ItemCode, ItemNameAr,
                            BatchNumber, ExpiryDate, UnitLevel, QuantityPurchased,
                            SmallestUnitsCount, UnitCostPiastres, VatPiastres, TotalPiastres, VatPercent
                        ) VALUES (
                            @pId, @itemId, @code, @name,
                            @batch, @exp, @uLevel, @qty,
                            @smUnits, @cost, @vat, @tot, @vatPct
                        );
                        SELECT last_insert_rowid();";

                    cmdItem.Parameters.AddWithValue("@pId", invId);
                    cmdItem.Parameters.AddWithValue("@itemId", item.ItemId);
                    cmdItem.Parameters.AddWithValue("@code", item.ItemCode);
                    cmdItem.Parameters.AddWithValue("@name", item.ItemNameAr);
                    cmdItem.Parameters.AddWithValue("@batch", item.BatchNumber);
                    cmdItem.Parameters.AddWithValue("@exp", ExpiryPolicy.ToIso(item.ExpiryDate));
                    cmdItem.Parameters.AddWithValue("@uLevel", (int)item.UnitLevel);
                    cmdItem.Parameters.AddWithValue("@qty", item.QuantityPurchased);
                    cmdItem.Parameters.AddWithValue("@smUnits", item.SmallestUnitsCount);
                    cmdItem.Parameters.AddWithValue("@cost", item.UnitCostPiastres);
                    cmdItem.Parameters.AddWithValue("@vat", item.VatPiastres);
                    cmdItem.Parameters.AddWithValue("@tot", item.TotalPiastres);
                    cmdItem.Parameters.AddWithValue("@vatPct", item.VatPercent.HasValue ? (object)item.VatPercent.Value : DBNull.Value);
                    item.Id = Convert.ToInt32(cmdItem.ExecuteScalar());

                    // تسوية ما بيع بالسالب قبل دخول هذه الفاتورة: الوارد يسدد السالب أولاً، والباقي يدخل رصيد التشغيلة
                    int unitsPlacedInBatch = ItemRepository.AbsorbOversoldStock(conn, trans, item.ItemId, item.SmallestUnitsCount);

                    // إضافة الدفعة إلى ItemBatches
                    using var cmdBatch = conn.CreateCommand();
                    cmdBatch.Transaction = trans;
                    cmdBatch.CommandText = @"
                        INSERT INTO ItemBatches (
                            ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits,
                            CostPricePerSmallestUnitPiastres, CreatedAt
                        ) VALUES (
                            @itemId, @batch, @exp, @rem,
                            @costPerUnit, datetime('now')
                        );
                        SELECT last_insert_rowid();";

                    // تكلفة القطعة = (سعر الوحدة المشتراة × الكمية) ÷ إجمالي الوحدات الصغرى، بتقريب Half-Up
                    // (كان بيتقسم سعر الوحدة الواحدة على الإجمالي فتطلع التكلفة أقل من الحقيقي بمقدار الكمية)
                    long costPerSmallestUnit = item.SmallestUnitsCount > 0
                        ? (item.UnitCostPiastres * item.QuantityPurchased + item.SmallestUnitsCount / 2) / item.SmallestUnitsCount
                        : item.UnitCostPiastres;

                    cmdBatch.Parameters.AddWithValue("@itemId", item.ItemId);
                    cmdBatch.Parameters.AddWithValue("@batch", item.BatchNumber);
                    cmdBatch.Parameters.AddWithValue("@exp", ExpiryPolicy.ToIso(item.ExpiryDate));
                    cmdBatch.Parameters.AddWithValue("@rem", unitsPlacedInBatch);
                    cmdBatch.Parameters.AddWithValue("@costPerUnit", costPerSmallestUnit);
                    item.BatchId = Convert.ToInt32(cmdBatch.ExecuteScalar());

                    // ربط بند الشراء بتشغيلته (يستخدمه مرتجع المورد المرتبط بالفاتورة - خطوة 39)
                    using var cmdLink = conn.CreateCommand();
                    cmdLink.Transaction = trans;
                    cmdLink.CommandText = "UPDATE PurchaseInvoiceItems SET BatchId = @b WHERE Id = @id;";
                    cmdLink.Parameters.AddWithValue("@b", item.BatchId);
                    cmdLink.Parameters.AddWithValue("@id", item.Id);
                    cmdLink.ExecuteNonQuery();

                    // زيادة رصيد مخزون الصنف الكلي
                    using var cmdStock = conn.CreateCommand();
                    cmdStock.Transaction = trans;
                    cmdStock.CommandText = @"
                        UPDATE Items
                        SET TotalStockSmallestUnits = TotalStockSmallestUnits + @added
                        WHERE Id = @itemId;";
                    cmdStock.Parameters.AddWithValue("@added", item.SmallestUnitsCount);
                    cmdStock.Parameters.AddWithValue("@itemId", item.ItemId);
                    cmdStock.ExecuteNonQuery();
                }

                // 3) تحديث رصيد المورد إذا كان هناك متبقي
                if (invoice.RemainingPiastres > 0)
                {
                    using var cmdSupp = conn.CreateCommand();
                    cmdSupp.Transaction = trans;
                    cmdSupp.CommandText = @"
                        UPDATE Suppliers
                        SET BalancePiastres = BalancePiastres + @rem
                        WHERE Id = @sId;";
                    cmdSupp.Parameters.AddWithValue("@rem", invoice.RemainingPiastres);
                    cmdSupp.Parameters.AddWithValue("@sId", invoice.SupplierId);
                    cmdSupp.ExecuteNonQuery();
                }

                JournalPosting.PostPurchaseInvoice(conn, trans, invoice);

                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                errorMessage = ex.Message;
                Logger.LogException(ex, "SavePurchaseInvoice");
                return false;
            }
        }

        /// <summary>
        /// إلغاء / التراجع عن حفظ فاتورة مشتريات: يعكس المخزون لنفس التشغيلات، يعكس رصيد المورد،
        /// ويعكس القيود اليومية المنشأة، ويعلّم الفاتورة ملغاة داخل Transaction واحدة.
        /// </summary>
        public bool CancelPurchaseInvoice(int purchaseInvoiceId, string reason, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(reason))
            {
                errorMessage = "يرجى كتابة سبب التراجع عن فاتورة المشتريات.";
                return false;
            }

            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                // التأكد من وجود أعمدة الإلغاء إذا لم تكن موجودة
                try
                {
                    using var alterCmd = conn.CreateCommand();
                    alterCmd.Transaction = trans;
                    alterCmd.CommandText = "ALTER TABLE PurchaseInvoices ADD COLUMN Status INTEGER NOT NULL DEFAULT 1;";
                    alterCmd.ExecuteNonQuery();
                }
                catch { /* العمود موجود بالفعل */ }

                int id = 0;
                string invoiceNumber = string.Empty;
                int supplierId = 0;
                long remainingPiastres = 0;
                int status = 1;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = trans;
                    cmd.CommandText = "SELECT Id, InvoiceNumber, SupplierId, RemainingHalalas, COALESCE(Status, 1) AS Status FROM PurchaseInvoices WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", purchaseInvoiceId);
                    using var r = cmd.ExecuteReader();
                    if (!r.Read())
                    {
                        errorMessage = "فاتورة المشتريات غير موجودة.";
                        return false;
                    }
                    id = Convert.ToInt32(r["Id"]);
                    invoiceNumber = r["InvoiceNumber"].ToString() ?? string.Empty;
                    supplierId = Convert.ToInt32(r["SupplierId"]);
                    remainingPiastres = Convert.ToInt64(r["RemainingHalalas"]);
                    status = Convert.ToInt32(r["Status"]);
                }

                if (status == 3)
                {
                    errorMessage = "فاتورة المشتريات ملغاة بالفعل.";
                    return false;
                }

                // 1) قراءة بنود الفاتورة لعكس المخزون والتشغيلات
                var items = new List<(int ItemId, int SmallestUnits, int BatchId, string BatchNumber)>();
                using (var itemCmd = conn.CreateCommand())
                {
                    itemCmd.Transaction = trans;
                    itemCmd.CommandText = "SELECT ItemId, SmallestUnitsCount, COALESCE(BatchId, 0) AS BatchId, BatchNumber FROM PurchaseInvoiceItems WHERE PurchaseInvoiceId = @id;";
                    itemCmd.Parameters.AddWithValue("@id", id);
                    using var ir = itemCmd.ExecuteReader();
                    while (ir.Read())
                    {
                        items.Add((
                            Convert.ToInt32(ir["ItemId"]),
                            Convert.ToInt32(ir["SmallestUnitsCount"]),
                            Convert.ToInt32(ir["BatchId"]),
                            ir["BatchNumber"]?.ToString() ?? string.Empty
                        ));
                    }
                }

                // 2) خصم الكميات من المخزون والتشغيلات
                foreach (var itm in items)
                {
                    using var stockCmd = conn.CreateCommand();
                    stockCmd.Transaction = trans;
                    stockCmd.CommandText = "UPDATE Items SET TotalStockSmallestUnits = TotalStockSmallestUnits - @deduct WHERE Id = @itemId;";
                    stockCmd.Parameters.AddWithValue("@deduct", itm.SmallestUnits);
                    stockCmd.Parameters.AddWithValue("@itemId", itm.ItemId);
                    stockCmd.ExecuteNonQuery();

                    if (itm.BatchId > 0)
                    {
                        using var batchCmd = conn.CreateCommand();
                        batchCmd.Transaction = trans;
                        batchCmd.CommandText = "UPDATE ItemBatches SET RemainingSmallestUnits = MAX(0, RemainingSmallestUnits - @deduct) WHERE Id = @bId;";
                        batchCmd.Parameters.AddWithValue("@deduct", itm.SmallestUnits);
                        batchCmd.Parameters.AddWithValue("@bId", itm.BatchId);
                        batchCmd.ExecuteNonQuery();
                    }
                    else if (!string.IsNullOrEmpty(itm.BatchNumber))
                    {
                        using var batchCmd = conn.CreateCommand();
                        batchCmd.Transaction = trans;
                        batchCmd.CommandText = "UPDATE ItemBatches SET RemainingSmallestUnits = MAX(0, RemainingSmallestUnits - @deduct) WHERE ItemId = @itemId AND BatchNumber = @batch;";
                        batchCmd.Parameters.AddWithValue("@deduct", itm.SmallestUnits);
                        batchCmd.Parameters.AddWithValue("@itemId", itm.ItemId);
                        batchCmd.Parameters.AddWithValue("@batch", itm.BatchNumber);
                        batchCmd.ExecuteNonQuery();
                    }
                }

                // 3) عكس أثر الآجل على رصيد المورد
                if (remainingPiastres > 0 && supplierId > 0)
                {
                    using var suppCmd = conn.CreateCommand();
                    suppCmd.Transaction = trans;
                    suppCmd.CommandText = "UPDATE Suppliers SET BalancePiastres = BalancePiastres - @rem WHERE Id = @sId;";
                    suppCmd.Parameters.AddWithValue("@rem", remainingPiastres);
                    suppCmd.Parameters.AddWithValue("@sId", supplierId);
                    suppCmd.ExecuteNonQuery();
                }

                // 4) عكس قيود اليومية المنشأة
                var originals = new List<(string Num, string Debit, string Credit, long Amount)>();
                using (var jCmd = conn.CreateCommand())
                {
                    jCmd.Transaction = trans;
                    jCmd.CommandText = "SELECT EntryNumber, DebitAccount, CreditAccount, AmountPiastres FROM JournalEntries WHERE EntryNumber LIKE @p ORDER BY Id;";
                    cmd.Parameters.Clear();
                    jCmd.Parameters.AddWithValue("@p", "JV-PUR-" + invoiceNumber + "-%");
                    using var jr = jCmd.ExecuteReader();
                    while (jr.Read())
                    {
                        originals.Add((
                            jr["EntryNumber"].ToString() ?? string.Empty,
                            jr["DebitAccount"].ToString() ?? string.Empty,
                            jr["CreditAccount"].ToString() ?? string.Empty,
                            Convert.ToInt64(jr["AmountPiastres"])
                        ));
                    }
                }

                foreach (var o in originals)
                {
                    using var rev = conn.CreateCommand();
                    rev.Transaction = trans;
                    rev.CommandText = @"
                        INSERT INTO JournalEntries (EntryNumber, Date, Description, DebitAccount, CreditAccount, AmountPiastres, CreatedAt)
                        VALUES (@num, @date, @desc, @deb, @crd, @amt, datetime('now'));";
                    rev.Parameters.AddWithValue("@num", "JV-CXL-" + o.Num.Substring(3));
                    rev.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                    rev.Parameters.AddWithValue("@desc", $"عكس قيد إلغاء فاتورة مشتريات رقم {invoiceNumber}");
                    rev.Parameters.AddWithValue("@deb", o.Credit);
                    rev.Parameters.AddWithValue("@crd", o.Debit);
                    rev.Parameters.AddWithValue("@amt", o.Amount);
                    rev.ExecuteNonQuery();
                }

                // 5) تعليم الفاتورة ملغاة وتحديث الملاحظات
                using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = trans;
                    upd.CommandText = @"UPDATE PurchaseInvoices SET Status = 3, Notes = COALESCE(Notes, '') || ' [ملغاة: ' || @r || ']' WHERE Id = @id;";
                    upd.Parameters.AddWithValue("@r", reason.Trim());
                    upd.Parameters.AddWithValue("@id", id);
                    upd.ExecuteNonQuery();
                }

                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                errorMessage = ex.Message;
                Logger.LogException(ex, "CancelPurchaseInvoice");
                return false;
            }
        }

        public PurchaseInvoice? GetLatestPurchaseInvoice()
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, InvoiceNumber, SupplierId, SupplierName, InvoiceDate, SubtotalHalalas, VatHalalas, TotalHalalas, PaidHalalas, RemainingHalalas, Notes FROM PurchaseInvoices WHERE Status != 3 ORDER BY Id DESC LIMIT 1;";
            try
            {
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                return new PurchaseInvoice
                {
                    Id = Convert.ToInt32(r["Id"]),
                    InvoiceNumber = r["InvoiceNumber"].ToString() ?? string.Empty,
                    SupplierId = Convert.ToInt32(r["SupplierId"]),
                    SupplierName = r["SupplierName"].ToString() ?? string.Empty,
                    InvoiceDate = DateTime.TryParse(r["InvoiceDate"].ToString(), out var dt) ? dt : DateTime.Now,
                    SubtotalPiastres = Convert.ToInt64(r["SubtotalHalalas"]),
                    VatPiastres = Convert.ToInt64(r["VatHalalas"]),
                    TotalPiastres = Convert.ToInt64(r["TotalHalalas"]),
                    PaidPiastres = Convert.ToInt64(r["PaidHalalas"]),
                    RemainingPiastres = Convert.ToInt64(r["RemainingHalalas"]),
                    Notes = r["Notes"]?.ToString() ?? string.Empty
                };
            }
            catch
            {
                // إذا لم يكن عمود Status موجوداً بعد
                cmd.CommandText = "SELECT Id, InvoiceNumber, SupplierId, SupplierName, InvoiceDate, SubtotalHalalas, VatHalalas, TotalHalalas, PaidHalalas, RemainingHalalas, Notes FROM PurchaseInvoices ORDER BY Id DESC LIMIT 1;";
                using var r2 = cmd.ExecuteReader();
                if (!r2.Read()) return null;
                return new PurchaseInvoice
                {
                    Id = Convert.ToInt32(r2["Id"]),
                    InvoiceNumber = r2["InvoiceNumber"].ToString() ?? string.Empty,
                    SupplierId = Convert.ToInt32(r2["SupplierId"]),
                    SupplierName = r2["SupplierName"].ToString() ?? string.Empty,
                    InvoiceDate = DateTime.TryParse(r2["InvoiceDate"].ToString(), out var dt) ? dt : DateTime.Now,
                    SubtotalPiastres = Convert.ToInt64(r2["SubtotalHalalas"]),
                    VatPiastres = Convert.ToInt64(r2["VatHalalas"]),
                    TotalPiastres = Convert.ToInt64(r2["TotalHalalas"]),
                    PaidPiastres = Convert.ToInt64(r2["PaidHalalas"]),
                    RemainingPiastres = Convert.ToInt64(r2["RemainingHalalas"]),
                    Notes = r2["Notes"]?.ToString() ?? string.Empty
                };
            }
        }

        /// <summary>
        /// أوامر شراء مقترحة من قائمة النواقص (أصناف نشطة رصيدها <= حدها الأدنى)، مجمّعة بمورد آخر شراء لكل صنف.
        /// للقراءة فقط: مفيش حفظ ولا حركة مخزون ولا رقم مستند بيتحجز. الأصناف الموجودة في أمر شراء مفتوح بتتستبعد.
        /// </summary>
        public List<SuggestedPurchaseOrder> GetSuggestedPurchaseOrders()
        {
            var groups = new Dictionary<int, SuggestedPurchaseOrder>();
            var noSupplier = new SuggestedPurchaseOrder { SupplierId = null, SupplierName = "بدون مورد سابق" };

            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT i.Id, i.Code, i.NameAr, i.TotalStockSmallestUnits, i.MinStockAlertSmallestUnits,
                       i.StripsPerBox, i.PiecesPerStrip,
                       s.Id AS SupplierId, s.Name AS SupplierName,
                       p.UnitCostPiastres AS LastUnitCost, p.QuantityPurchased AS LastQty, p.SmallestUnitsCount AS LastUnits
                FROM Items i
                LEFT JOIN PurchaseInvoiceItems p ON p.Id = (
                    SELECT p2.Id FROM PurchaseInvoiceItems p2
                    JOIN PurchaseInvoices h2 ON h2.Id = p2.PurchaseInvoiceId
                    WHERE p2.ItemId = i.Id
                    ORDER BY h2.InvoiceDate DESC, p2.Id DESC LIMIT 1)
                LEFT JOIN PurchaseInvoices h ON h.Id = p.PurchaseInvoiceId
                LEFT JOIN Suppliers s ON s.Id = h.SupplierId AND s.IsActive = 1
                WHERE i.IsActive = 1 AND i.TotalStockSmallestUnits <= i.MinStockAlertSmallestUnits
                  AND i.Id NOT IN (SELECT poi.ItemId FROM PurchaseOrderItems poi
                                   JOIN PurchaseOrders po ON po.Id = poi.OrderId WHERE po.Status = 0)
                ORDER BY i.NameAr;";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var line = new SuggestedOrderLine
                {
                    ItemId = Convert.ToInt32(r["Id"]),
                    ItemCode = r["Code"].ToString() ?? string.Empty,
                    ItemNameAr = r["NameAr"].ToString() ?? string.Empty,
                    CurrentStockSmallestUnits = Convert.ToInt32(r["TotalStockSmallestUnits"]),
                    MinStockAlertSmallestUnits = Convert.ToInt32(r["MinStockAlertSmallestUnits"]),
                    SmallestUnitsPerBox = Math.Max(1, Convert.ToInt32(r["StripsPerBox"]) * Convert.ToInt32(r["PiecesPerStrip"]))
                };
                if (line.SuggestedUnits <= 0) continue; // حد أدنى صفر ورصيد صفر: مفيش حاجة تتطلب

                if (!(r["LastUnits"] is DBNull))
                {
                    line.LastPurchaseUnits = Convert.ToInt32(r["LastUnits"]);
                    line.LastPurchaseTaxablePiastres = Convert.ToInt64(r["LastUnitCost"]) * Convert.ToInt32(r["LastQty"]);
                }

                if (r["SupplierId"] is DBNull)
                {
                    noSupplier.Lines.Add(line);
                    continue;
                }

                int supplierId = Convert.ToInt32(r["SupplierId"]);
                if (!groups.TryGetValue(supplierId, out var order))
                {
                    order = new SuggestedPurchaseOrder { SupplierId = supplierId, SupplierName = r["SupplierName"].ToString() ?? string.Empty };
                    groups[supplierId] = order;
                }
                order.Lines.Add(line);
            }

            var result = groups.Values.OrderBy(o => o.SupplierName, StringComparer.Ordinal).ToList();
            if (noSupplier.Lines.Count > 0) result.Add(noSupplier);
            return result;
        }

        public bool AddSupplierVoucher(SupplierVoucher voucher)
        {
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = @"
                    INSERT INTO SupplierVouchers (
                        VoucherNumber, SupplierId, SupplierName, Date,
                        AmountPiastres, PaymentMethod, Notes, CreatedAt
                    ) VALUES (
                        @num, @sId, @name, @date,
                        @amt, @pMethod, @notes, datetime('now')
                    );";

                cmd.Parameters.AddWithValue("@num", voucher.VoucherNumber);
                cmd.Parameters.AddWithValue("@sId", voucher.SupplierId);
                cmd.Parameters.AddWithValue("@name", voucher.SupplierName);
                cmd.Parameters.AddWithValue("@date", voucher.Date.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@amt", voucher.AmountPiastres);
                cmd.Parameters.AddWithValue("@pMethod", (int)voucher.PaymentMethod);
                cmd.Parameters.AddWithValue("@notes", (object?)voucher.Notes ?? DBNull.Value);
                cmd.ExecuteNonQuery();

                // خصم المبلغ المسدد من رصيد المورد
                using var cmdSupp = conn.CreateCommand();
                cmdSupp.Transaction = trans;
                cmdSupp.CommandText = @"
                    UPDATE Suppliers
                    SET BalancePiastres = BalancePiastres - @amt
                    WHERE Id = @sId;";
                cmdSupp.Parameters.AddWithValue("@amt", voucher.AmountPiastres);
                cmdSupp.Parameters.AddWithValue("@sId", voucher.SupplierId);
                cmdSupp.ExecuteNonQuery();

                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                Logger.LogException(ex, "AddSupplierVoucher");
                return false;
            }
        }

        public List<PurchaseInvoice> GetAllPurchases(int limit = 50)
        {
            var list = new List<PurchaseInvoice>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, InvoiceNumber, SupplierId, SupplierName, InvoiceDate,
                       SubtotalPiastres, VatPiastres, TotalPiastres, PaidPiastres,
                       RemainingPiastres, Notes, CreatedAt
                FROM PurchaseInvoices
                ORDER BY Id DESC
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new PurchaseInvoice
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    InvoiceNumber = reader["InvoiceNumber"].ToString() ?? string.Empty,
                    SupplierId = Convert.ToInt32(reader["SupplierId"]),
                    SupplierName = reader["SupplierName"].ToString() ?? string.Empty,
                    InvoiceDate = DbDate.Parse(reader["InvoiceDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                    SubtotalPiastres = Convert.ToInt64(reader["SubtotalPiastres"]),
                    VatPiastres = Convert.ToInt64(reader["VatPiastres"]),
                    TotalPiastres = Convert.ToInt64(reader["TotalPiastres"]),
                    PaidPiastres = Convert.ToInt64(reader["PaidPiastres"]),
                    RemainingPiastres = Convert.ToInt64(reader["RemainingPiastres"]),
                    Notes = reader["Notes"] is DBNull ? null : reader["Notes"].ToString(),
                    CreatedAt = DbDate.Parse(reader["CreatedAt"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture))
                });
            }
            return list;
        }

        /// <summary>
        /// تحميل فاتورة مشتريات كاملة ببنودها لعرض الفواتير السابقة
        /// </summary>
        public PurchaseInvoice? GetFullPurchaseInvoice(string invoiceNumber)
        {
            string number = (invoiceNumber ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(number)) return null;

            using var conn = _dbContext.CreateConnection();
            var inv = new PurchaseInvoice();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, InvoiceNumber, SupplierId, SupplierName, InvoiceDate,
                           SubtotalPiastres, VatPiastres, TotalPiastres, PaidPiastres,
                           RemainingPiastres, Notes, CreatedAt
                    FROM PurchaseInvoices 
                    WHERE InvoiceNumber = @n COLLATE NOCASE;";
                cmd.Parameters.AddWithValue("@n", number);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                inv.Id = Convert.ToInt32(r["Id"]);
                inv.InvoiceNumber = r["InvoiceNumber"].ToString() ?? string.Empty;
                inv.SupplierId = Convert.ToInt32(r["SupplierId"]);
                inv.SupplierName = r["SupplierName"].ToString() ?? string.Empty;
                inv.InvoiceDate = DbDate.Parse(r["InvoiceDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                inv.SubtotalPiastres = Convert.ToInt64(r["SubtotalPiastres"]);
                inv.VatPiastres = Convert.ToInt64(r["VatPiastres"]);
                inv.TotalPiastres = Convert.ToInt64(r["TotalPiastres"]);
                inv.PaidPiastres = Convert.ToInt64(r["PaidPiastres"]);
                inv.RemainingPiastres = Convert.ToInt64(r["RemainingPiastres"]);
                inv.Notes = r["Notes"] is DBNull ? null : r["Notes"].ToString();
                inv.CreatedAt = DbDate.Parse(r["CreatedAt"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));
            }

            // تحميل بنود فاتورة الشراء
            using (var itemCmd = conn.CreateCommand())
            {
                itemCmd.CommandText = @"
                    SELECT Id, ItemId, ItemCode, ItemNameAr, BatchNumber, ExpiryDate,
                           BatchId, UnitLevel, QuantityPurchased, SmallestUnitsCount,
                           UnitCostPiastres, VatPercent, VatPiastres, TotalPiastres
                    FROM PurchaseInvoiceItems
                    WHERE PurchaseInvoiceId = @id ORDER BY Id ASC;";
                itemCmd.Parameters.AddWithValue("@id", inv.Id);
                using var ir = itemCmd.ExecuteReader();
                while (ir.Read())
                {
                    inv.Items.Add(new PurchaseInvoiceItem
                    {
                        Id = Convert.ToInt32(ir["Id"]),
                        PurchaseInvoiceId = inv.Id,
                        ItemId = Convert.ToInt32(ir["ItemId"]),
                        ItemCode = ir["ItemCode"].ToString() ?? string.Empty,
                        ItemNameAr = ir["ItemNameAr"].ToString() ?? string.Empty,
                        BatchNumber = ir["BatchNumber"].ToString() ?? string.Empty,
                        ExpiryDate = DbDate.Parse(ir["ExpiryDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                        BatchId = ir["BatchId"] == DBNull.Value ? 0 : Convert.ToInt32(ir["BatchId"]),
                        UnitLevel = (UnitLevel)Convert.ToInt32(ir["UnitLevel"]),
                        QuantityPurchased = Convert.ToInt32(ir["QuantityPurchased"]),
                        SmallestUnitsCount = Convert.ToInt32(ir["SmallestUnitsCount"]),
                        UnitCostPiastres = Convert.ToInt64(ir["UnitCostPiastres"]),
                        VatPercent = ir["VatPercent"] == DBNull.Value ? null : Convert.ToInt32(ir["VatPercent"]),
                        VatPiastres = Convert.ToInt64(ir["VatPiastres"]),
                        TotalPiastres = Convert.ToInt64(ir["TotalPiastres"])
                    });
                }
            }

            return inv;
        }

        /// <summary>
        /// الحصول على رقم فاتورة المشتريات السابقة
        /// </summary>
        public string? GetPreviousPurchaseInvoiceNumber(string currentInvoiceNumber)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            string current = (currentInvoiceNumber ?? string.Empty).Trim();
            cmd.CommandText = @"
                SELECT InvoiceNumber FROM PurchaseInvoices 
                WHERE Id < COALESCE((SELECT Id FROM PurchaseInvoices WHERE InvoiceNumber = @n COLLATE NOCASE LIMIT 1), 9223372036854775807)
                ORDER BY Id DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("@n", current);

            var res = cmd.ExecuteScalar();
            return res == null || res == DBNull.Value ? null : res.ToString();
        }

        /// <summary>
        /// الحصول على رقم فاتورة المشتريات التالية
        /// </summary>
        public string? GetNextPurchaseInvoiceNumber(string currentInvoiceNumber)
        {
            string current = (currentInvoiceNumber ?? string.Empty).Trim();

            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT InvoiceNumber FROM PurchaseInvoices 
                WHERE Id > COALESCE((SELECT Id FROM PurchaseInvoices WHERE InvoiceNumber = @n COLLATE NOCASE LIMIT 1), 0)
                ORDER BY Id ASC LIMIT 1;";
            cmd.Parameters.AddWithValue("@n", current);

            var res = cmd.ExecuteScalar();
            return res == null || res == DBNull.Value ? null : res.ToString();
        }
    }
}
