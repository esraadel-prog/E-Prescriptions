using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Helpers;

namespace ePrescriptions.Data.Repositories
{
    public class ItemRepository
    {
        private readonly DatabaseContext _dbContext;

        public ItemRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// بحث فوري بالاسم العربي، الإنجليزي، المادة الفعالة، الكود، أو الباركود
        /// </summary>
        public List<Item> Search(string query, int limit = 50)
        {
            var list = new List<Item>();
            using var conn = _dbContext.CreateConnection();

            using var cmd = conn.CreateCommand();

            // بحث موحّد: كل كلمة في البحث (بعد التطبيع) لازم تظهر في SearchText؛ الكود والباركود يُطابَقا كما هما
            BackfillSearchText(conn);
            string raw = (query ?? string.Empty).Trim();
            var tokens = ArabicTextNormalizer.Tokens(raw);
            var where = new System.Text.StringBuilder();
            for (int i = 0; i < tokens.Length; i++)
            {
                if (i > 0) where.Append(" AND ");
                where.Append($"SearchText LIKE @t{i} ESCAPE '\\'");
                cmd.Parameters.AddWithValue($"@t{i}", "%" + EscapeLike(tokens[i]) + "%");
            }
            string textCondition = tokens.Length > 0 ? "(" + where + ")" : "1 = 1";

            cmd.CommandText = $@"
                SELECT Id, Code, Barcode, NameAr, NameEn, ActiveIngredient,
                       StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres,
                       WholesalePriceBoxPiastres, CostPriceBoxPiastres,
                       MinStockAlertSmallestUnits, IsPrescriptionRequired,
                       IsVatExempt, CustomVatPercent, TotalStockSmallestUnits, IsActive
                FROM Items
                WHERE IsActive = 1 AND (
                    {textCondition} OR
                    Code LIKE @q ESCAPE '\' OR
                    Barcode LIKE @q ESCAPE '\'
                )
                ORDER BY NameAr ASC
                LIMIT @limit;";

            cmd.Parameters.AddWithValue("@q", "%" + EscapeLike(raw) + "%");
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapItem(reader));
            }

            return list;
        }

        private static string EscapeLike(string s) =>
            s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        /// <summary>النص المطبّع الموحّد للصنف: الاسم العربي + الإنجليزي + المادة الفعالة</summary>
        public static string BuildSearchText(string? nameAr, string? nameEn, string? activeIngredient) =>
            ArabicTextNormalizer.Normalize($"{nameAr} {nameEn} {activeIngredient}");

        /// <summary>يفهرس الأصناف اللي SearchText فيها NULL (قديمة أو اتحمّلت بـ SQL مباشر). سريعة لو مفيش حاجة ناقصة.</summary>
        public static void BackfillSearchText(DatabaseContext db)
        {
            using var conn = db.CreateConnection();
            BackfillSearchText(conn);
        }

        private static void BackfillSearchText(SQLiteConnection conn)
        {
            var pending = new List<(int Id, string Text)>();
            using (var sel = conn.CreateCommand())
            {
                sel.CommandText = "SELECT Id, NameAr, NameEn, ActiveIngredient FROM Items WHERE SearchText IS NULL;";
                using var r = sel.ExecuteReader();
                while (r.Read())
                {
                    pending.Add((Convert.ToInt32(r["Id"]),
                        BuildSearchText(Convert.ToString(r["NameAr"]),
                                        Convert.ToString(r["NameEn"]),
                                        Convert.ToString(r["ActiveIngredient"]))));
                }
            }
            if (pending.Count == 0) return;

            using var trans = conn.BeginTransaction();
            foreach (var p in pending)
            {
                using var upd = conn.CreateCommand();
                upd.CommandText = "UPDATE Items SET SearchText = @s WHERE Id = @id;";
                upd.Parameters.AddWithValue("@s", p.Text);
                upd.Parameters.AddWithValue("@id", p.Id);
                upd.ExecuteNonQuery();
            }
            trans.Commit();
        }

        public Item? GetByBarcodeOrCode(string code)
        {
            using var conn = _dbContext.CreateConnection();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Code, Barcode, NameAr, NameEn, ActiveIngredient,
                       StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres,
                       WholesalePriceBoxPiastres, CostPriceBoxPiastres,
                       MinStockAlertSmallestUnits, IsPrescriptionRequired,
                       IsVatExempt, CustomVatPercent, TotalStockSmallestUnits, IsActive
                FROM Items
                WHERE IsActive = 1 AND (Barcode = @c OR Code = @c)
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@c", code.Trim());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var item = MapItem(reader);
                item.Batches = GetActiveBatches(item.Id, conn);
                return item;
            }

            return null;
        }

        public Item? GetById(int id)
        {
            using var conn = _dbContext.CreateConnection();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Code, Barcode, NameAr, NameEn, ActiveIngredient,
                       StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres,
                       WholesalePriceBoxPiastres, CostPriceBoxPiastres,
                       MinStockAlertSmallestUnits, IsPrescriptionRequired,
                       IsVatExempt, CustomVatPercent, TotalStockSmallestUnits, IsActive
                FROM Items
                WHERE Id = @id
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var item = MapItem(reader);
                item.Batches = GetActiveBatches(item.Id, conn);
                return item;
            }

            return null;
        }

        public List<Item> GetTopSellingItems(int limit = 10)
        {
            var list = new List<Item>();
            using var conn = _dbContext.CreateConnection();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT i.Id, i.Code, i.Barcode, i.NameAr, i.NameEn, i.ActiveIngredient,
                       i.StripsPerBox, i.PiecesPerStrip, i.RetailPriceBoxPiastres,
                       i.WholesalePriceBoxPiastres, i.CostPriceBoxPiastres,
                       i.MinStockAlertSmallestUnits, i.IsPrescriptionRequired,
                       i.IsVatExempt, i.CustomVatPercent, i.TotalStockSmallestUnits, i.IsActive,
                       IFNULL(SUM(it.SmallestUnitsCount), 0) AS SoldUnits
                FROM Items i
                LEFT JOIN InvoiceItems it ON i.Id = it.ItemId
                WHERE i.IsActive = 1
                GROUP BY i.Id
                ORDER BY SoldUnits DESC, i.NameAr ASC
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var item = MapItem(reader);
                item.Batches = GetActiveBatches(item.Id, conn);
                list.Add(item);
            }

            return list;
        }

        public List<ItemBatch> GetActiveBatches(int itemId, SQLiteConnection? existingConn = null, SQLiteTransaction? existingTrans = null, bool includeExpired = false)
        {
            var batches = new List<ItemBatch>();
            bool closeConn = false;
            var conn = existingConn;
            if (conn == null)
            {
                conn = _dbContext.CreateConnection();
                closeConn = true;
            }

            try
            {
                using var cmd = conn.CreateCommand();
                if (existingTrans != null)
                {
                    cmd.Transaction = existingTrans;
                }
                cmd.CommandText = @"
                    SELECT Id, ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits,
                           CostPricePerSmallestUnitPiastres, CreatedAt
                    FROM ItemBatches
                    WHERE ItemId = @itemId AND RemainingSmallestUnits > 0
                      AND (@includeExpired = 1 OR ExpiryDate >= @today)
                    ORDER BY ExpiryDate ASC, Id ASC;";
                cmd.Parameters.AddWithValue("@itemId", itemId);
                cmd.Parameters.AddWithValue("@includeExpired", includeExpired ? 1 : 0);
                cmd.Parameters.AddWithValue("@today", ExpiryPolicy.TodayIso());

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    batches.Add(new ItemBatch
                    {
                        Id = Convert.ToInt32(reader["Id"]),
                        ItemId = Convert.ToInt32(reader["ItemId"]),
                        BatchNumber = reader["BatchNumber"]?.ToString() ?? string.Empty,
                        ExpiryDate = DbDate.Parse(reader["ExpiryDate"]?.ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                        RemainingSmallestUnits = Convert.ToInt32(reader["RemainingSmallestUnits"]),
                        CostPricePerSmallestUnitPiastres = Convert.ToInt64(reader["CostPricePerSmallestUnitPiastres"]),
                        CreatedAt = DbDate.Parse(reader["CreatedAt"]?.ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture))
                    });
                }
            }
            finally
            {
                if (closeConn)
                {
                    conn.Dispose();
                }
            }

            return batches;
        }

        /// <summary>
        /// خصم المخزون بنظام FEFO (الأقرب صلاحية أولاً) داخل Transaction موحدة.
        /// التشغيلات المنتهية لا تُباع أبداً. إذا لم تكفِ التشغيلات السارية تُسجَّل الكمية الباقية "بالسالب"
        /// (OversoldSmallestUnits) وتُسوّى تلقائياً عند دخول فاتورة شراء لنفس الصنف.
        /// إذا حُدد preferredBatchId (بيع بقراءة ملصق التشغيلة) تُخصم الكمية من هذه التشغيلة أولاً.
        /// </summary>
        public List<BatchAllocation> DeductStock(int itemId, int unitsToDeduct, SQLiteConnection conn, SQLiteTransaction trans, int? preferredBatchId = null)
        {
            var allocations = new List<BatchAllocation>();
            if (unitsToDeduct <= 0) return allocations;

            var batches = GetActiveBatches(itemId, conn, trans);
            if (preferredBatchId.HasValue)
            {
                var preferred = batches.FirstOrDefault(b => b.Id == preferredBatchId.Value);
                if (preferred != null)
                {
                    batches.Remove(preferred);
                    batches.Insert(0, preferred);
                }
            }

            int remainingToDeduct = unitsToDeduct;
            foreach (var batch in batches)
            {
                if (remainingToDeduct <= 0) break;

                int take = Math.Min(batch.RemainingSmallestUnits, remainingToDeduct);
                if (take <= 0) continue;

                using var updateBatchCmd = conn.CreateCommand();
                updateBatchCmd.Transaction = trans;
                updateBatchCmd.CommandText = @"
                    UPDATE ItemBatches
                    SET RemainingSmallestUnits = RemainingSmallestUnits - @take
                    WHERE Id = @bId;";
                updateBatchCmd.Parameters.AddWithValue("@take", take);
                updateBatchCmd.Parameters.AddWithValue("@bId", batch.Id);
                updateBatchCmd.ExecuteNonQuery();

                allocations.Add(new BatchAllocation
                {
                    BatchId = batch.Id,
                    BatchNumber = batch.BatchNumber,
                    ExpiryDate = batch.ExpiryDate,
                    Units = take
                });
                remainingToDeduct -= take;
            }

            // كمية لا تغطيها أي تشغيلة سارية: تُباع بالسالب وتُسجَّل لتسويتها عند الشراء
            if (remainingToDeduct > 0)
            {
                using var oversoldCmd = conn.CreateCommand();
                oversoldCmd.Transaction = trans;
                oversoldCmd.CommandText = @"
                    UPDATE Items
                    SET OversoldSmallestUnits = OversoldSmallestUnits + @over
                    WHERE Id = @itemId;";
                oversoldCmd.Parameters.AddWithValue("@over", remainingToDeduct);
                oversoldCmd.Parameters.AddWithValue("@itemId", itemId);
                oversoldCmd.ExecuteNonQuery();

                allocations.Add(new BatchAllocation
                {
                    BatchId = null,
                    BatchNumber = "بالسالب",
                    ExpiryDate = null,
                    Units = remainingToDeduct
                });
            }

            // تحديث إجمالي مخزون الصنف (قد يصبح سالباً)
            using var updateItemCmd = conn.CreateCommand();
            updateItemCmd.Transaction = trans;
            updateItemCmd.CommandText = @"
                UPDATE Items
                SET TotalStockSmallestUnits = TotalStockSmallestUnits - @deduct
                WHERE Id = @itemId;";
            updateItemCmd.Parameters.AddWithValue("@deduct", unitsToDeduct);
            updateItemCmd.Parameters.AddWithValue("@itemId", itemId);
            updateItemCmd.ExecuteNonQuery();

            return allocations;
        }

        /// <summary>
        /// واجهة قديمة للتوافق: تخصم FEFO وتُرجع رقم آخر تشغيلة مستخدمة
        /// </summary>
        public void DeductStockFefo(int itemId, int unitsToDeduct, SQLiteConnection conn, SQLiteTransaction trans, out string batchNumberUsed, out DateTime? expiryDateUsed)
        {
            var allocations = DeductStock(itemId, unitsToDeduct, conn, trans);
            var last = allocations.LastOrDefault(a => a.BatchId.HasValue);
            batchNumberUsed = last?.BatchNumber ?? "عام";
            expiryDateUsed = last?.ExpiryDate;
        }

        /// <summary>
        /// إرجاع كمية مرتجع مبيعات إلى نفس التشغيلات التي خُصمت منها في بند البيع الأصلي.
        /// الكمية التي بيعت بالسالب تُخفَّض من رصيد "المباع بالسالب" بدلاً من إضافتها لتشغيلة.
        /// للفواتير القديمة (قبل التحديث) التي ليس لها تفاصيل تشغيلات يُستخدم رقم التشغيلة المسجل في البند.
        /// </summary>
        public void RestoreForInvoiceItem(int originalInvoiceItemId, int itemId, int unitsToRestore, string? fallbackBatchNumber, SQLiteConnection conn, SQLiteTransaction trans)
        {
            if (unitsToRestore <= 0) return;

            var allocs = new List<(int Id, int? BatchId, int Units, int Returned)>();
            using (var readCmd = conn.CreateCommand())
            {
                readCmd.Transaction = trans;
                readCmd.CommandText = "SELECT Id, BatchId, Units, ReturnedUnits FROM InvoiceItemBatches WHERE InvoiceItemId = @id ORDER BY Id ASC;";
                readCmd.Parameters.AddWithValue("@id", originalInvoiceItemId);
                using var r = readCmd.ExecuteReader();
                while (r.Read())
                {
                    int? batchId = r["BatchId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["BatchId"]);
                    allocs.Add((Convert.ToInt32(r["Id"]), batchId, Convert.ToInt32(r["Units"]), Convert.ToInt32(r["ReturnedUnits"])));
                }
            }

            if (allocs.Count == 0)
            {
                RestoreStock(itemId, unitsToRestore, fallbackBatchNumber, conn, trans);
                return;
            }

            int remaining = unitsToRestore;
            foreach (var a in allocs)
            {
                if (remaining <= 0) break;
                int take = Math.Min(a.Units - a.Returned, remaining);
                if (take <= 0) continue;

                using var markCmd = conn.CreateCommand();
                markCmd.Transaction = trans;
                markCmd.CommandText = "UPDATE InvoiceItemBatches SET ReturnedUnits = ReturnedUnits + @t WHERE Id = @id;";
                markCmd.Parameters.AddWithValue("@t", take);
                markCmd.Parameters.AddWithValue("@id", a.Id);
                markCmd.ExecuteNonQuery();

                using var stockCmd = conn.CreateCommand();
                stockCmd.Transaction = trans;
                if (a.BatchId.HasValue)
                {
                    stockCmd.CommandText = "UPDATE ItemBatches SET RemainingSmallestUnits = RemainingSmallestUnits + @t WHERE Id = @b;";
                    stockCmd.Parameters.AddWithValue("@b", a.BatchId.Value);
                }
                else
                {
                    stockCmd.CommandText = "UPDATE Items SET OversoldSmallestUnits = MAX(0, OversoldSmallestUnits - @t) WHERE Id = @i;";
                    stockCmd.Parameters.AddWithValue("@i", itemId);
                }
                stockCmd.Parameters.AddWithValue("@t", take);
                stockCmd.ExecuteNonQuery();

                remaining -= take;
            }

            int restored = unitsToRestore - remaining;
            using var itemCmd = conn.CreateCommand();
            itemCmd.Transaction = trans;
            itemCmd.CommandText = "UPDATE Items SET TotalStockSmallestUnits = TotalStockSmallestUnits + @u WHERE Id = @itemId;";
            itemCmd.Parameters.AddWithValue("@u", restored);
            itemCmd.Parameters.AddWithValue("@itemId", itemId);
            itemCmd.ExecuteNonQuery();
        }

        /// <summary>
        /// عند دخول كمية جديدة (شراء أو إضافة تشغيلة) تُسدَّد أولاً الكمية المباعة بالسالب سابقاً.
        /// ترجع عدد الوحدات التي تدخل فعلياً في رصيد التشغيلة الجديدة (الوارد − ما سُدِّد من السالب).
        /// أما إجمالي رصيد الصنف فيزيد بالوارد كاملاً (فيعود الرصيد السالب إلى الصفر أو أعلى تلقائياً).
        /// </summary>
        public static int AbsorbOversoldStock(SQLiteConnection conn, SQLiteTransaction? trans, int itemId, int incomingUnits)
        {
            if (incomingUnits <= 0) return incomingUnits;

            int oversold;
            using (var readCmd = conn.CreateCommand())
            {
                readCmd.Transaction = trans;
                readCmd.CommandText = "SELECT OversoldSmallestUnits FROM Items WHERE Id = @id;";
                readCmd.Parameters.AddWithValue("@id", itemId);
                object? v = readCmd.ExecuteScalar();
                oversold = v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v);
            }

            int absorb = Math.Min(incomingUnits, Math.Max(0, oversold));
            if (absorb > 0)
            {
                using var updCmd = conn.CreateCommand();
                updCmd.Transaction = trans;
                updCmd.CommandText = "UPDATE Items SET OversoldSmallestUnits = OversoldSmallestUnits - @a WHERE Id = @id;";
                updCmd.Parameters.AddWithValue("@a", absorb);
                updCmd.Parameters.AddWithValue("@id", itemId);
                updCmd.ExecuteNonQuery();
            }

            return incomingUnits - absorb;
        }

        public ItemBatch? GetBatchById(int batchId)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits,
                       CostPricePerSmallestUnitPiastres, CreatedAt
                FROM ItemBatches WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", batchId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return new ItemBatch
            {
                Id = Convert.ToInt32(reader["Id"]),
                ItemId = Convert.ToInt32(reader["ItemId"]),
                BatchNumber = reader["BatchNumber"]?.ToString() ?? string.Empty,
                ExpiryDate = DbDate.Parse(reader["ExpiryDate"]?.ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                RemainingSmallestUnits = Convert.ToInt32(reader["RemainingSmallestUnits"]),
                CostPricePerSmallestUnitPiastres = Convert.ToInt64(reader["CostPricePerSmallestUnitPiastres"]),
                CreatedAt = DbDate.Parse(reader["CreatedAt"]?.ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture))
            };
        }

        /// <summary>
        /// قراءة ملصق تشغيلة: يرجع الصنف والتشغيلة المطابقة لرمز الباركود المطبوع على العلبة
        /// </summary>
        public (Item Item, ItemBatch Batch)? GetBatchWithItem(int batchId)
        {
            var batch = GetBatchById(batchId);
            if (batch == null) return null;
            var item = GetById(batch.ItemId);
            if (item == null) return null;
            return (item, batch);
        }

        /// <summary>
        /// تشغيلات سارية من نفس الصنف تنتهي قبل تاريخ معين وما زال لها رصيد (أي أقدم من التشغيلة المقروءة)
        /// </summary>
        public List<ItemBatch> GetOlderSellableBatches(int itemId, DateTime expiryDate, int excludeBatchId)
        {
            return GetActiveBatches(itemId)
                .Where(b => b.Id != excludeBatchId && b.ExpiryDate.Date < expiryDate.Date)
                .ToList();
        }

        /// <summary>
        /// تشغيلات منتهية الصلاحية ما زال لها رصيد على الرف (يجب سحبها من الرف)
        /// </summary>
        public List<ItemBatch> GetExpiredBatchesWithStock(int itemId)
        {
            return GetActiveBatches(itemId, null, null, includeExpired: true)
                .Where(b => ExpiryPolicy.IsExpired(b.ExpiryDate))
                .ToList();
        }

        /// <summary>
        /// إرجاع كمية إلى المخزون عند مرتجع المبيعات (إلى نفس التشغيلة إن وُجدت) داخل Transaction موحدة
        /// </summary>
        public void RestoreStock(int itemId, int unitsToRestore, string? batchNumber, SQLiteConnection conn, SQLiteTransaction trans)
        {
            if (unitsToRestore <= 0) return;

            if (!string.IsNullOrWhiteSpace(batchNumber))
            {
                using var batchCmd = conn.CreateCommand();
                batchCmd.Transaction = trans;
                batchCmd.CommandText = @"
                    UPDATE ItemBatches
                    SET RemainingSmallestUnits = RemainingSmallestUnits + @u
                    WHERE Id = (
                        SELECT Id FROM ItemBatches
                        WHERE ItemId = @itemId AND BatchNumber = @b
                        ORDER BY ExpiryDate DESC LIMIT 1
                    );";
                batchCmd.Parameters.AddWithValue("@u", unitsToRestore);
                batchCmd.Parameters.AddWithValue("@itemId", itemId);
                batchCmd.Parameters.AddWithValue("@b", batchNumber);
                batchCmd.ExecuteNonQuery();
            }

            using var itemCmd = conn.CreateCommand();
            itemCmd.Transaction = trans;
            itemCmd.CommandText = @"
                UPDATE Items
                SET TotalStockSmallestUnits = TotalStockSmallestUnits + @u
                WHERE Id = @itemId;";
            itemCmd.Parameters.AddWithValue("@u", unitsToRestore);
            itemCmd.Parameters.AddWithValue("@itemId", itemId);
            itemCmd.ExecuteNonQuery();
        }

        public List<Item> GetAllItems()
        {
            var list = new List<Item>();
            using var conn = _dbContext.CreateConnection();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Code, Barcode, NameAr, NameEn, ActiveIngredient,
                       StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres,
                       WholesalePriceBoxPiastres, CostPriceBoxPiastres,
                       MinStockAlertSmallestUnits, IsPrescriptionRequired,
                       IsVatExempt, CustomVatPercent, TotalStockSmallestUnits, IsActive
                FROM Items
                WHERE IsActive = 1
                ORDER BY NameAr ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapItem(reader));
            }
            return list;
        }

        public int SaveItem(Item item)
        {
            using var conn = _dbContext.CreateConnection();

            using var cmd = conn.CreateCommand();

            if (item.Id == 0)
            {
                cmd.CommandText = @"
                    INSERT INTO Items (
                        Code, Barcode, NameAr, NameEn, ActiveIngredient,
                        StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres,
                        WholesalePriceBoxPiastres, CostPriceBoxPiastres,
                        MinStockAlertSmallestUnits, IsPrescriptionRequired,
                        IsVatExempt, CustomVatPercent, TotalStockSmallestUnits, IsActive, SearchText
                    ) VALUES (
                        @code, @barcode, @nameAr, @nameEn, @ingr,
                        @strips, @pieces, @retPrice, @wsPrice, @costPrice,
                        @minAlert, @rxReq, @vatExempt, @custVat, @stock, @isActive, @searchText
                    );
                    SELECT last_insert_rowid();";
            }
            else
            {
                cmd.CommandText = @"
                    UPDATE Items SET
                        Code = @code,
                        Barcode = @barcode,
                        NameAr = @nameAr,
                        NameEn = @nameEn,
                        ActiveIngredient = @ingr,
                        StripsPerBox = @strips,
                        PiecesPerStrip = @pieces,
                        RetailPriceBoxPiastres = @retPrice,
                        WholesalePriceBoxPiastres = @wsPrice,
                        CostPriceBoxPiastres = @costPrice,
                        MinStockAlertSmallestUnits = @minAlert,
                        IsPrescriptionRequired = @rxReq,
                        IsVatExempt = @vatExempt,
                        CustomVatPercent = @custVat,
                        IsActive = @isActive,
                        SearchText = @searchText
                    WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", item.Id);
            }

            cmd.Parameters.AddWithValue("@code", item.Code.Trim());
            cmd.Parameters.AddWithValue("@barcode", item.Barcode.Trim());
            cmd.Parameters.AddWithValue("@nameAr", item.NameAr.Trim());
            cmd.Parameters.AddWithValue("@nameEn", item.NameEn.Trim());
            cmd.Parameters.AddWithValue("@ingr", item.ActiveIngredient.Trim());
            cmd.Parameters.AddWithValue("@strips", item.StripsPerBox);
            cmd.Parameters.AddWithValue("@pieces", item.PiecesPerStrip);
            cmd.Parameters.AddWithValue("@retPrice", item.RetailPriceBoxPiastres);
            cmd.Parameters.AddWithValue("@wsPrice", item.WholesalePriceBoxPiastres);
            cmd.Parameters.AddWithValue("@costPrice", item.CostPriceBoxPiastres);
            cmd.Parameters.AddWithValue("@minAlert", item.MinStockAlertSmallestUnits);
            cmd.Parameters.AddWithValue("@rxReq", item.IsPrescriptionRequired ? 1 : 0);
            cmd.Parameters.AddWithValue("@vatExempt", item.IsVatExempt ? 1 : 0);
            cmd.Parameters.AddWithValue("@custVat", item.CustomVatPercent);
            cmd.Parameters.AddWithValue("@stock", item.TotalStockSmallestUnits);
            cmd.Parameters.AddWithValue("@isActive", item.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@searchText", BuildSearchText(item.NameAr, item.NameEn, item.ActiveIngredient));

            if (item.Id == 0)
            {
                item.Id = Convert.ToInt32(cmd.ExecuteScalar());
            }
            else
            {
                cmd.ExecuteNonQuery();
            }

            return item.Id;
        }

        /// <summary>
        /// اعتماد تغييرات أسعار جاية من قائمة أسعار مورد (خطوة 40) في Transaction واحدة.
        /// كل تغيير بيتأكد إن السعر الحالي في القاعدة لسه = السعر القديم اللي اتعرض في المعاينة
        /// (لو حد عدّله في الأثناء بيتخطّى بدل ما يدوس على تعديله). التدقيق مسؤولية المستدعي (LogAudit).
        /// </summary>
        public PriceApplyResult ApplyPriceChanges(IEnumerable<ItemPriceChange> changes)
        {
            var result = new PriceApplyResult();
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();

            foreach (var change in changes)
            {
                string column = change.Field switch
                {
                    PriceField.Retail => "RetailPriceBoxPiastres",
                    PriceField.Wholesale => "WholesalePriceBoxPiastres",
                    PriceField.Cost => "CostPriceBoxPiastres",
                    _ => throw new InvalidOperationException("حقل سعر غير معروف")
                };

                if (change.NewPiastres <= 0)
                {
                    result.Skipped.Add(new PriceChangeSkip { Change = change, Reason = "السعر الجديد لازم يكون أكبر من صفر" });
                    continue;
                }

                long current;
                using (var read = conn.CreateCommand())
                {
                    read.Transaction = trans;
                    read.CommandText = $"SELECT {column} FROM Items WHERE Id = @id AND IsActive = 1;";
                    read.Parameters.AddWithValue("@id", change.ItemId);
                    object? value = read.ExecuteScalar();
                    if (value == null || value is DBNull)
                    {
                        result.Skipped.Add(new PriceChangeSkip { Change = change, Reason = "الصنف مش موجود أو متعطّل" });
                        continue;
                    }
                    current = Convert.ToInt64(value);
                }

                if (current != change.OldPiastres)
                {
                    result.Skipped.Add(new PriceChangeSkip { Change = change, Reason = "السعر اتغيّر بعد المعاينة" });
                    continue;
                }

                using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = trans;
                    upd.CommandText = $"UPDATE Items SET {column} = @p WHERE Id = @id;";
                    upd.Parameters.AddWithValue("@p", change.NewPiastres);
                    upd.Parameters.AddWithValue("@id", change.ItemId);
                    upd.ExecuteNonQuery();
                }
                result.Applied.Add(change);
            }

            trans.Commit();
            return result;
        }

        public void AddBatch(int itemId, string batchNumber, DateTime expiryDate, int smallestUnits, long costPricePerBoxPiastres)
        {
            var item = GetById(itemId);
            int perBox = item?.SmallestUnitsPerBox ?? 1;
            AddBatch(new ItemBatch
            {
                ItemId = itemId,
                BatchNumber = batchNumber.Trim(),
                ExpiryDate = expiryDate,
                RemainingSmallestUnits = smallestUnits,
                CostPricePerSmallestUnitPiastres = (costPricePerBoxPiastres + (perBox / 2)) / Math.Max(1, perBox)
            });
        }

        public void AddBatch(ItemBatch batch)
        {
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                int incomingUnits = batch.RemainingSmallestUnits;
                int unitsPlacedInBatch = AbsorbOversoldStock(conn, trans, batch.ItemId, incomingUnits);

                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = @"
                    INSERT INTO ItemBatches (
                        ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits,
                        CostPricePerSmallestUnitPiastres, CreatedAt
                    ) VALUES (
                        @itemId, @batch, @exp, @rem, @cost, @created
                    );
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@itemId", batch.ItemId);
                cmd.Parameters.AddWithValue("@batch", batch.BatchNumber);
                cmd.Parameters.AddWithValue("@exp", ExpiryPolicy.ToIso(batch.ExpiryDate));
                cmd.Parameters.AddWithValue("@rem", unitsPlacedInBatch);
                cmd.Parameters.AddWithValue("@cost", batch.CostPricePerSmallestUnitPiastres);
                cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));

                batch.Id = Convert.ToInt32(cmd.ExecuteScalar());

                // تحديث رصيد الصنف
                using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = trans;
                updateCmd.CommandText = @"
                    UPDATE Items
                    SET TotalStockSmallestUnits = TotalStockSmallestUnits + @added
                    WHERE Id = @itemId;";
                updateCmd.Parameters.AddWithValue("@added", incomingUnits);
                updateCmd.Parameters.AddWithValue("@itemId", batch.ItemId);
                updateCmd.ExecuteNonQuery();

                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        /// <summary>
        /// التشغيلات المنتهية أو القريبة من الانتهاء (خلال withinDays يوماً) مع بيانات الصنف
        /// </summary>
        public List<(Item Item, ItemBatch Batch)> GetNearExpiryBatches(int withinDays = 90)
        {
            var list = new List<(Item, ItemBatch)>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            string thresholdDate = ExpiryPolicy.ToIso(DateTime.Today.AddDays(withinDays));
            cmd.CommandText = @"
                SELECT b.Id AS BId, b.ItemId, b.BatchNumber, b.ExpiryDate, b.RemainingSmallestUnits,
                       b.CostPricePerSmallestUnitPiastres, b.CreatedAt,
                       i.Id, i.Code, i.Barcode, i.NameAr, i.NameEn, i.ActiveIngredient,
                       i.StripsPerBox, i.PiecesPerStrip, i.RetailPriceBoxPiastres,
                       i.WholesalePriceBoxPiastres, i.CostPriceBoxPiastres,
                       i.MinStockAlertSmallestUnits, i.IsPrescriptionRequired,
                       i.IsVatExempt, i.CustomVatPercent, i.TotalStockSmallestUnits, i.IsActive
                FROM ItemBatches b
                JOIN Items i ON b.ItemId = i.Id
                WHERE i.IsActive = 1
                  AND b.RemainingSmallestUnits > 0
                  AND b.ExpiryDate <= @thresh
                ORDER BY b.ExpiryDate ASC;";
            cmd.Parameters.AddWithValue("@thresh", thresholdDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var item = MapItem(reader);
                var batch = new ItemBatch
                {
                    Id = Convert.ToInt32(reader["BId"]),
                    ItemId = Convert.ToInt32(reader["ItemId"]),
                    BatchNumber = reader["BatchNumber"]?.ToString() ?? string.Empty,
                    ExpiryDate = DbDate.Parse(reader["ExpiryDate"]?.ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                    RemainingSmallestUnits = Convert.ToInt32(reader["RemainingSmallestUnits"]),
                    CostPricePerSmallestUnitPiastres = Convert.ToInt64(reader["CostPricePerSmallestUnitPiastres"]),
                    CreatedAt = DbDate.Parse(reader["CreatedAt"]?.ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture))
                };
                list.Add((item, batch));
            }

            return list;
        }

        public List<Item> GetLowStockItems()
        {
            var list = new List<Item>();
            using var conn = _dbContext.CreateConnection();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Code, Barcode, NameAr, NameEn, ActiveIngredient,
                       StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres,
                       WholesalePriceBoxPiastres, CostPriceBoxPiastres,
                       MinStockAlertSmallestUnits, IsPrescriptionRequired,
                       IsVatExempt, CustomVatPercent, TotalStockSmallestUnits, IsActive
                FROM Items
                WHERE IsActive = 1 AND TotalStockSmallestUnits <= MinStockAlertSmallestUnits
                ORDER BY TotalStockSmallestUnits ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapItem(reader));
            }

            return list;
        }

        /// <summary>
        /// خطوة 35: استرجاع بدائل الصنف بالمادة الفعالة. المطابقة باسم المادة فقط (بتتجاهل التركيز والمواد المضافة):
        /// أي صنف يشترك في اسم مادة واحدة على الأقل يظهر كبديل. الاستبعاد: الصنف نفسه والمعطّل (وغير المتاح لو inStockOnly).
        /// </summary>
        public List<Item> GetAlternativesByActiveIngredient(string activeIngredient, int excludeItemId = 0, bool inStockOnly = true)
        {
            var list = new List<Item>();
            var wanted = ActiveIngredientMatcher.ExtractNames(activeIngredient);
            if (wanted.Count == 0) return list;

            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            string stockFilter = inStockOnly ? "AND TotalStockSmallestUnits > 0" : "";
            cmd.CommandText = $@"
                SELECT Id, Code, Barcode, NameAr, NameEn, ActiveIngredient,
                       StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres,
                       WholesalePriceBoxPiastres, CostPriceBoxPiastres,
                       MinStockAlertSmallestUnits, IsPrescriptionRequired,
                       IsVatExempt, CustomVatPercent, TotalStockSmallestUnits, IsActive
                FROM Items
                WHERE IsActive = 1
                  AND Id <> @excludeId
                  AND TRIM(ActiveIngredient) <> ''
                  {stockFilter}
                ORDER BY TotalStockSmallestUnits DESC, RetailPriceBoxPiastres ASC;";
            cmd.Parameters.AddWithValue("@excludeId", excludeItemId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var item = MapItem(reader);
                if (ActiveIngredientMatcher.ExtractNames(item.ActiveIngredient).Overlaps(wanted))
                    list.Add(item);
            }
            return list;
        }

        /// <summary>
        /// خطوة 38: يحدّد الصنف من نص مقروء بالقارئ أثناء الجرد: ملصق تشغيلة (يحدد الصنف والتشغيلة)
        /// أو باركود/كود الصنف العادي. يرجع null لو الرمز غير معروف.
        /// </summary>
        public (Item Item, ItemBatch? Batch)? ResolveScannedCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            if (ePrescriptions.Core.Helpers.BatchLabelCode.TryDecode(code, out int batchId))
            {
                var found = GetBatchWithItem(batchId);
                if (found != null) return (found.Value.Item, found.Value.Batch);
            }

            var item = GetByBarcodeOrCode(code);
            return item == null ? null : (item, (ItemBatch?)null);
        }

        /// <summary>
        /// خطوة 38: يعتمد نتيجة جلسة جرد: لكل سطر فيه فرق يسجّل تسوية جرد (نفس <see cref="RecordStockAdjustment"/>
        /// المستخدمة في التسوية اليدوية)، كل سطر في Transaction مستقل داخل الدالة الموجودة.
        /// السطور المتطابقة لا تُسجَّل. يرجع التسويات المسجّلة.
        /// </summary>
        public List<Domain.Entities.StockAdjustment> ApplyStocktake(Domain.Helpers.StocktakeSession session, string reason = "جرد بالباركود")
        {
            var applied = new List<Domain.Entities.StockAdjustment>();
            string stamp = ePrescriptions.Core.Helpers.DbDate.DashStamp(DateTime.Now);
            int seq = 0;
            foreach (var line in session.LinesWithDifference())
            {
                seq++;
                var adj = new Domain.Entities.StockAdjustment
                {
                    AdjustmentNumber = $"CNT-{stamp}-{seq:D3}",
                    ItemId = line.ItemId,
                    ItemCode = line.Code,
                    ItemNameAr = line.NameAr,
                    Date = DateTime.Now,
                    BookSmallestUnits = line.BookSmallestUnits,
                    ActualSmallestUnits = line.CountedSmallestUnits,
                    Reason = reason
                };
                RecordStockAdjustment(adj);
                applied.Add(adj);
            }
            return applied;
        }

        public void RecordStockAdjustment(Domain.Entities.StockAdjustment adj)
        {
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = @"
                    INSERT INTO StockAdjustments (
                        AdjustmentNumber, ItemId, ItemCode, ItemNameAr, Date,
                        BookSmallestUnits, ActualSmallestUnits, DifferenceSmallestUnits,
                        Reason, CreatedAt
                    ) VALUES (
                        @num, @itemId, @code, @name, @date,
                        @book, @actual, @diff,
                        @reason, datetime('now')
                    );";
                cmd.Parameters.AddWithValue("@num", adj.AdjustmentNumber);
                cmd.Parameters.AddWithValue("@itemId", adj.ItemId);
                cmd.Parameters.AddWithValue("@code", adj.ItemCode);
                cmd.Parameters.AddWithValue("@name", adj.ItemNameAr);
                cmd.Parameters.AddWithValue("@date", adj.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@book", adj.BookSmallestUnits);
                cmd.Parameters.AddWithValue("@actual", adj.ActualSmallestUnits);
                cmd.Parameters.AddWithValue("@diff", adj.DifferenceSmallestUnits);
                cmd.Parameters.AddWithValue("@reason", adj.Reason);
                cmd.ExecuteNonQuery();

                // تحديث رصيد الصنف ليتطابق مع الفعلي
                using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = trans;
                updateCmd.CommandText = @"
                    UPDATE Items
                    SET TotalStockSmallestUnits = @actual,
                        OversoldSmallestUnits = CASE WHEN @actual < 0 THEN -@actual ELSE 0 END
                    WHERE Id = @itemId;";
                updateCmd.Parameters.AddWithValue("@actual", adj.ActualSmallestUnits);
                updateCmd.Parameters.AddWithValue("@itemId", adj.ItemId);
                updateCmd.ExecuteNonQuery();

                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        private static Item MapItem(SQLiteDataReader reader)
        {
            int customVat = -1;
            try
            {
                object val = reader["CustomVatPercent"];
                if (val != null && val != DBNull.Value)
                    customVat = Convert.ToInt32(val);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "ItemRepository.MapItem (CustomVatPercent)");
            }

            return new Item
            {
                Id = Convert.ToInt32(reader["Id"]),
                Code = reader["Code"]?.ToString() ?? string.Empty,
                Barcode = reader["Barcode"]?.ToString() ?? string.Empty,
                NameAr = reader["NameAr"]?.ToString() ?? string.Empty,
                NameEn = reader["NameEn"]?.ToString() ?? string.Empty,
                ActiveIngredient = reader["ActiveIngredient"]?.ToString() ?? string.Empty,
                StripsPerBox = Convert.ToInt32(reader["StripsPerBox"]),
                PiecesPerStrip = Convert.ToInt32(reader["PiecesPerStrip"]),
                RetailPriceBoxPiastres = Convert.ToInt64(reader["RetailPriceBoxPiastres"]),
                WholesalePriceBoxPiastres = Convert.ToInt64(reader["WholesalePriceBoxPiastres"]),
                CostPriceBoxPiastres = Convert.ToInt64(reader["CostPriceBoxPiastres"]),
                MinStockAlertSmallestUnits = Convert.ToInt32(reader["MinStockAlertSmallestUnits"]),
                IsPrescriptionRequired = Convert.ToInt32(reader["IsPrescriptionRequired"]) == 1,
                IsVatExempt = Convert.ToInt32(reader["IsVatExempt"]) == 1,
                CustomVatPercent = customVat,
                TotalStockSmallestUnits = Convert.ToInt32(reader["TotalStockSmallestUnits"]),
                IsActive = Convert.ToInt32(reader["IsActive"]) == 1
            };
        }
    }
}
