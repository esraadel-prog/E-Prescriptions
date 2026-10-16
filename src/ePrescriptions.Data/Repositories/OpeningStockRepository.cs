using System;
using System.Collections.Generic;
using System.Data.SQLite;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Data.Repositories
{
    /// <summary>
    /// مستودع بيانات سندات رصيد أول المدة (بضاعة أول المدة):
    /// يتولى الترقيم الآلي وحفظ السند وبنوده داخل Transaction ذرية واحدة،
    /// مع إضافة التشغيلات وتحديث أرصدة الأصناف وتسجيل قيود اليومية المحاسبية وسجلات التدقيق.
    /// </summary>
    public class OpeningStockRepository
    {
        private readonly DatabaseContext _dbContext;
        private readonly ItemRepository _itemRepository;
        private readonly OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly StaffAuditLogRepository? _staffAuditLogRepository;

        public OpeningStockRepository(
            DatabaseContext dbContext,
            ItemRepository itemRepository,
            OwnerAuditLogRepository? ownerAuditLogRepository = null,
            StaffAuditLogRepository? staffAuditLogRepository = null)
        {
            _dbContext = dbContext;
            _itemRepository = itemRepository;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
        }

        /// <summary>
        /// استخراج رقم السند التالي للعرض في الشاشة بدون حجزه
        /// </summary>
        public string PeekNextVoucherNumber()
        {
            using var conn = _dbContext.CreateConnection();
            string yearKey = $"OP-{DateTime.Now:yyyy}";
            long nextSeq = NumberSequence.Peek(conn, yearKey, "OpeningStockVouchers", "VoucherNumber");
            return NumberSequence.Format(yearKey, nextSeq);
        }

        /// <summary>
        /// حفظ واعتماد سند رصيد أول المدة بالكامل داخل Transaction واحدة
        /// </summary>
        public void SaveVoucher(OpeningStockVoucher voucher, User? currentUser = null)
        {
            if (voucher == null) throw new ArgumentNullException(nameof(voucher));
            if (voucher.Lines == null || voucher.Lines.Count == 0)
                throw new InvalidOperationException("لا يمكن حفظ سند رصيد أول المدة بدون بنود.");

            voucher.RecalculateTotals();

            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                // 1) حجز رقم السند الرسمي بعدّاد تسلسلي آمن
                string yearKey = $"OP-{voucher.VoucherDate:yyyy}";
                long seq = NumberSequence.Next(conn, trans, yearKey, "OpeningStockVouchers", "VoucherNumber");
                voucher.VoucherNumber = NumberSequence.Format(yearKey, seq);

                if (currentUser != null)
                {
                    voucher.CreatedByUserId = currentUser.Id;
                    voucher.CreatedByUserName = currentUser.FullName;
                }

                // 2) إدراج رأس السند
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                        INSERT INTO OpeningStockVouchers (
                            VoucherNumber, VoucherDate, Notes, CreatedByUserId,
                            CreatedByUserName, TotalItemsCount, TotalSmallestUnits,
                            TotalCostPiastres, TotalRetailPiastres, CreatedAt
                        ) VALUES (
                            @num, @date, @notes, @userId,
                            @userName, @count, @units,
                            @cost, @retail, @createdAt
                        );
                        SELECT last_insert_rowid();";

                    cmd.Parameters.AddWithValue("@num", voucher.VoucherNumber);
                    cmd.Parameters.AddWithValue("@date", voucher.VoucherDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                    cmd.Parameters.AddWithValue("@notes", voucher.Notes ?? string.Empty);
                    cmd.Parameters.AddWithValue("@userId", voucher.CreatedByUserId);
                    cmd.Parameters.AddWithValue("@userName", voucher.CreatedByUserName ?? string.Empty);
                    cmd.Parameters.AddWithValue("@count", voucher.TotalItemsCount);
                    cmd.Parameters.AddWithValue("@units", voucher.TotalSmallestUnits);
                    cmd.Parameters.AddWithValue("@cost", voucher.TotalCostPiastres);
                    cmd.Parameters.AddWithValue("@retail", voucher.TotalRetailPiastres);
                    cmd.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));

                    voucher.Id = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // 3) معالجة كل بند: إنشاء الصنف إذا كان جديداً + إدراج التشغيلة + زيادة المخزون + إدراج سطر السند
                int lineIndex = 0;
                foreach (var line in voucher.Lines)
                {
                    lineIndex++;
                    line.VoucherId = voucher.Id;
                    line.RecalculateLine();

                    int perBox = line.SmallestUnitsPerBox;

                    // أ) إذا كان الصنف جديداً (غير موجود في جدول Items)
                    if (line.ItemId <= 0)
                    {
                        var existing = !string.IsNullOrWhiteSpace(line.ItemBarcode)
                            ? _itemRepository.GetByBarcodeOrCode(line.ItemBarcode.Trim())
                            : null;

                        if (existing != null)
                        {
                            line.ItemId = existing.Id;
                            line.ItemCode = existing.Code;
                            line.StripsPerBox = existing.StripsPerBox;
                            line.PiecesPerStrip = existing.PiecesPerStrip;
                        }
                        else
                        {
                            // إنشاء كود تلقائي إن لم يُحدد
                            string itemCode = string.IsNullOrWhiteSpace(line.ItemCode)
                                ? $"MED-{DateTime.Now:yyyyMMdd}-{lineIndex:D3}"
                                : line.ItemCode.Trim();

                            using var insertItemCmd = conn.CreateCommand();
                            insertItemCmd.Transaction = trans;
                            insertItemCmd.CommandText = @"
                                INSERT INTO Items (
                                    Code, Barcode, NameAr, NameEn, ActiveIngredient,
                                    StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres,
                                    WholesalePriceBoxPiastres, CostPriceBoxPiastres,
                                    MinStockAlertSmallestUnits, IsPrescriptionRequired,
                                    IsVatExempt, CustomVatPercent, TotalStockSmallestUnits,
                                    OversoldSmallestUnits, IsActive, CreatedAt
                                ) VALUES (
                                    @code, @barcode, @nameAr, @nameEn, @active,
                                    @strips, @pieces, @retail,
                                    @wholesale, @cost,
                                    30, 0,
                                    0, -1, 0,
                                    0, 1, datetime('now')
                                );
                                SELECT last_insert_rowid();";

                            insertItemCmd.Parameters.AddWithValue("@code", itemCode);
                            insertItemCmd.Parameters.AddWithValue("@barcode", line.ItemBarcode ?? string.Empty);
                            insertItemCmd.Parameters.AddWithValue("@nameAr", line.ItemNameAr?.Trim() ?? "صنف جديد");
                            insertItemCmd.Parameters.AddWithValue("@nameEn", line.ItemNameEn?.Trim() ?? string.Empty);
                            insertItemCmd.Parameters.AddWithValue("@active", line.ActiveIngredient?.Trim() ?? string.Empty);
                            insertItemCmd.Parameters.AddWithValue("@strips", Math.Max(1, line.StripsPerBox));
                            insertItemCmd.Parameters.AddWithValue("@pieces", Math.Max(1, line.PiecesPerStrip));
                            insertItemCmd.Parameters.AddWithValue("@retail", line.RetailPriceBoxPiastres);
                            insertItemCmd.Parameters.AddWithValue("@wholesale", line.RetailPriceBoxPiastres);
                            insertItemCmd.Parameters.AddWithValue("@cost", line.CostPriceBoxPiastres);

                            line.ItemId = Convert.ToInt32(insertItemCmd.ExecuteScalar());
                            line.ItemCode = itemCode;
                        }
                    }

                    // ب) إنشاء التشغيلة واستيعاب الرصيد السالب إن وُجد
                    string batchNumber = string.IsNullOrWhiteSpace(line.BatchNumber)
                        ? $"OP-{voucher.VoucherDate:yyyyMMdd}"
                        : line.BatchNumber.Trim();

                    long unitCostPiastres = (line.CostPriceBoxPiastres + (perBox / 2)) / Math.Max(1, perBox);
                    int unitsPlacedInBatch = ItemRepository.AbsorbOversoldStock(conn, trans, line.ItemId, line.TotalSmallestUnits);

                    using (var batchCmd = conn.CreateCommand())
                    {
                        batchCmd.Transaction = trans;
                        batchCmd.CommandText = @"
                            INSERT INTO ItemBatches (
                                ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits,
                                CostPricePerSmallestUnitPiastres, CreatedAt
                            ) VALUES (
                                @itemId, @batch, @exp, @rem, @cost, @created
                            );";

                        batchCmd.Parameters.AddWithValue("@itemId", line.ItemId);
                        batchCmd.Parameters.AddWithValue("@batch", batchNumber);
                        batchCmd.Parameters.AddWithValue("@exp", ExpiryPolicy.ToIso(line.ExpiryDate));
                        batchCmd.Parameters.AddWithValue("@rem", unitsPlacedInBatch);
                        batchCmd.Parameters.AddWithValue("@cost", unitCostPiastres);
                        batchCmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));
                        batchCmd.ExecuteNonQuery();
                    }

                    // ج) زيادة رصيد الصنف في جدول Items
                    using (var stockCmd = conn.CreateCommand())
                    {
                        stockCmd.Transaction = trans;
                        stockCmd.CommandText = @"
                            UPDATE Items
                            SET TotalStockSmallestUnits = TotalStockSmallestUnits + @added,
                                CostPriceBoxPiastres = CASE WHEN CostPriceBoxPiastres <= 0 THEN @costBox ELSE CostPriceBoxPiastres END,
                                RetailPriceBoxPiastres = CASE WHEN RetailPriceBoxPiastres <= 0 THEN @retailBox ELSE RetailPriceBoxPiastres END
                            WHERE Id = @itemId;";

                        stockCmd.Parameters.AddWithValue("@added", line.TotalSmallestUnits);
                        stockCmd.Parameters.AddWithValue("@costBox", line.CostPriceBoxPiastres);
                        stockCmd.Parameters.AddWithValue("@retailBox", line.RetailPriceBoxPiastres);
                        stockCmd.Parameters.AddWithValue("@itemId", line.ItemId);
                        stockCmd.ExecuteNonQuery();
                    }

                    // د) تسجيل سطر السند في جدول OpeningStockVoucherLines
                    using (var lineCmd = conn.CreateCommand())
                    {
                        lineCmd.Transaction = trans;
                        lineCmd.CommandText = @"
                            INSERT INTO OpeningStockVoucherLines (
                                VoucherId, ItemId, ItemCode, ItemBarcode, ItemNameAr, ItemNameEn, ActiveIngredient,
                                StripsPerBox, PiecesPerStrip, BatchNumber, ExpiryDate,
                                QuantityBoxes, QuantityStrips, QuantityUnits, TotalSmallestUnits,
                                CostPriceBoxPiastres, RetailPriceBoxPiastres, TotalCostPiastres, TotalRetailPiastres
                            ) VALUES (
                                @vId, @itemId, @code, @barcode, @nameAr, @nameEn, @active,
                                @strips, @pieces, @batch, @exp,
                                @qBoxes, @qStrips, @qUnits, @totalUnits,
                                @costBox, @retailBox, @totalCost, @totalRetail
                            );
                            SELECT last_insert_rowid();";

                        lineCmd.Parameters.AddWithValue("@vId", line.VoucherId);
                        lineCmd.Parameters.AddWithValue("@itemId", line.ItemId);
                        lineCmd.Parameters.AddWithValue("@code", line.ItemCode ?? string.Empty);
                        lineCmd.Parameters.AddWithValue("@barcode", line.ItemBarcode ?? string.Empty);
                        lineCmd.Parameters.AddWithValue("@nameAr", line.ItemNameAr ?? string.Empty);
                        lineCmd.Parameters.AddWithValue("@nameEn", line.ItemNameEn ?? string.Empty);
                        lineCmd.Parameters.AddWithValue("@active", line.ActiveIngredient ?? string.Empty);
                        lineCmd.Parameters.AddWithValue("@strips", line.StripsPerBox);
                        lineCmd.Parameters.AddWithValue("@pieces", line.PiecesPerStrip);
                        lineCmd.Parameters.AddWithValue("@batch", batchNumber);
                        lineCmd.Parameters.AddWithValue("@exp", ExpiryPolicy.ToIso(line.ExpiryDate));
                        lineCmd.Parameters.AddWithValue("@qBoxes", line.QuantityBoxes);
                        lineCmd.Parameters.AddWithValue("@qStrips", line.QuantityStrips);
                        lineCmd.Parameters.AddWithValue("@qUnits", line.QuantityUnits);
                        lineCmd.Parameters.AddWithValue("@totalUnits", line.TotalSmallestUnits);
                        lineCmd.Parameters.AddWithValue("@costBox", line.CostPriceBoxPiastres);
                        lineCmd.Parameters.AddWithValue("@retailBox", line.RetailPriceBoxPiastres);
                        lineCmd.Parameters.AddWithValue("@totalCost", line.TotalCostPiastres);
                        lineCmd.Parameters.AddWithValue("@totalRetail", line.TotalRetailPiastres);

                        line.Id = Convert.ToInt32(lineCmd.ExecuteScalar());
                    }

                    // هـ) تسجيل حركة تسوية في جدول StockAdjustments لتدقيق المخزون
                    using (var adjCmd = conn.CreateCommand())
                    {
                        adjCmd.Transaction = trans;
                        adjCmd.CommandText = @"
                            INSERT INTO StockAdjustments (
                                AdjustmentNumber, ItemId, ItemCode, ItemNameAr, Date,
                                BookSmallestUnits, ActualSmallestUnits, DifferenceSmallestUnits,
                                Reason, CreatedAt
                            ) VALUES (
                                @adjNum, @itemId, @code, @nameAr, @date,
                                0, @totalUnits, @totalUnits,
                                @reason, datetime('now')
                            );";

                        adjCmd.Parameters.AddWithValue("@adjNum", $"ADJ-{voucher.VoucherNumber}-{lineIndex:D3}");
                        adjCmd.Parameters.AddWithValue("@itemId", line.ItemId);
                        adjCmd.Parameters.AddWithValue("@code", line.ItemCode ?? string.Empty);
                        adjCmd.Parameters.AddWithValue("@nameAr", line.ItemNameAr ?? string.Empty);
                        adjCmd.Parameters.AddWithValue("@date", voucher.VoucherDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                        adjCmd.Parameters.AddWithValue("@totalUnits", line.TotalSmallestUnits);
                        adjCmd.Parameters.AddWithValue("@reason", $"رصيد أول المدة - سند رقم {voucher.VoucherNumber}");
                        adjCmd.ExecuteNonQuery();
                    }
                }

                // 4) توليد قيد اليومية المحاسبي (مدين: المخزون / دائن: رأس المال ورصيد أول المدة)
                if (voucher.TotalCostPiastres > 0)
                {
                    JournalPosting.Post(
                        conn,
                        trans,
                        voucher.VoucherNumber,
                        voucher.VoucherDate,
                        $"قيد بضاعة أول المدة - سند رقم {voucher.VoucherNumber}",
                        new[] { new JournalPosting.Leg(Accounts.Inventory, voucher.TotalCostPiastres) },
                        new[] { new JournalPosting.Leg(Accounts.OpeningBalanceEquity, voucher.TotalCostPiastres) }
                    );
                }

                // 5) تسجيل في سجل التدقيق
                LogAudit(currentUser, voucher);

                trans.Commit();
            }
            catch (Exception ex)
            {
                trans.Rollback();
                Logger.LogException(ex, "OpeningStockRepository.SaveVoucher");
                throw;
            }

            // تحديث فهرس البحث السريع للأصناف الجديدة
            try
            {
                ItemRepository.BackfillSearchText(_dbContext);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "OpeningStockRepository.BackfillSearchText");
            }
        }

        /// <summary>
        /// جلب قائمة بسندات رصيد أول المدة السابقة
        /// </summary>
        public List<OpeningStockVoucher> GetVouchersList(int limit = 100)
        {
            var list = new List<OpeningStockVoucher>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, VoucherNumber, VoucherDate, Notes, CreatedByUserId,
                       CreatedByUserName, TotalItemsCount, TotalSmallestUnits,
                       TotalCostPiastres, TotalRetailPiastres, CreatedAt
                FROM OpeningStockVouchers
                ORDER BY Id DESC
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(MapVoucherHeader(reader));
            }
            return list;
        }

        /// <summary>
        /// جلب سند رصيد أول المدة بالكامل مع بنوده بواسطة المعرّف
        /// </summary>
        public OpeningStockVoucher? GetVoucherById(int id)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, VoucherNumber, VoucherDate, Notes, CreatedByUserId,
                       CreatedByUserName, TotalItemsCount, TotalSmallestUnits,
                       TotalCostPiastres, TotalRetailPiastres, CreatedAt
                FROM OpeningStockVouchers
                WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var voucher = MapVoucherHeader(reader);
            voucher.Lines = GetVoucherLines(voucher.Id, conn);
            return voucher;
        }

        /// <summary>
        /// جلب بنود سند رصيد أول المدة
        /// </summary>
        public List<OpeningStockVoucherLine> GetVoucherLines(int voucherId, SQLiteConnection? existingConn = null)
        {
            var lines = new List<OpeningStockVoucherLine>();
            var conn = existingConn ?? _dbContext.CreateConnection();
            bool disposeConn = existingConn == null;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT Id, VoucherId, ItemId, ItemCode, ItemBarcode, ItemNameAr, ItemNameEn, ActiveIngredient,
                           StripsPerBox, PiecesPerStrip, BatchNumber, ExpiryDate,
                           QuantityBoxes, QuantityStrips, QuantityUnits, TotalSmallestUnits,
                           CostPriceBoxPiastres, RetailPriceBoxPiastres, TotalCostPiastres, TotalRetailPiastres
                    FROM OpeningStockVoucherLines
                    WHERE VoucherId = @vId
                    ORDER BY Id ASC;";
                cmd.Parameters.AddWithValue("@vId", voucherId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    lines.Add(MapVoucherLine(reader));
                }
            }
            finally
            {
                if (disposeConn) conn.Dispose();
            }

            return lines;
        }

        private static OpeningStockVoucher MapVoucherHeader(SQLiteDataReader reader)
        {
            return new OpeningStockVoucher
            {
                Id = Convert.ToInt32(reader["Id"]),
                VoucherNumber = reader["VoucherNumber"]?.ToString() ?? string.Empty,
                VoucherDate = DateTime.TryParse(reader["VoucherDate"]?.ToString(), out var dt) ? dt : DateTime.Today,
                Notes = reader["Notes"]?.ToString() ?? string.Empty,
                CreatedByUserId = Convert.ToInt32(reader["CreatedByUserId"]),
                CreatedByUserName = reader["CreatedByUserName"]?.ToString() ?? string.Empty,
                TotalItemsCount = Convert.ToInt32(reader["TotalItemsCount"]),
                TotalSmallestUnits = Convert.ToInt32(reader["TotalSmallestUnits"]),
                TotalCostPiastres = Convert.ToInt64(reader["TotalCostPiastres"]),
                TotalRetailPiastres = Convert.ToInt64(reader["TotalRetailPiastres"]),
                CreatedAt = DateTime.TryParse(reader["CreatedAt"]?.ToString(), out var cat) ? cat : DateTime.Now
            };
        }

        private static OpeningStockVoucherLine MapVoucherLine(SQLiteDataReader reader)
        {
            return new OpeningStockVoucherLine
            {
                Id = Convert.ToInt32(reader["Id"]),
                VoucherId = Convert.ToInt32(reader["VoucherId"]),
                ItemId = Convert.ToInt32(reader["ItemId"]),
                ItemCode = reader["ItemCode"]?.ToString() ?? string.Empty,
                ItemBarcode = reader["ItemBarcode"]?.ToString() ?? string.Empty,
                ItemNameAr = reader["ItemNameAr"]?.ToString() ?? string.Empty,
                ItemNameEn = reader["ItemNameEn"]?.ToString() ?? string.Empty,
                ActiveIngredient = reader["ActiveIngredient"]?.ToString() ?? string.Empty,
                StripsPerBox = Convert.ToInt32(reader["StripsPerBox"]),
                PiecesPerStrip = Convert.ToInt32(reader["PiecesPerStrip"]),
                BatchNumber = reader["BatchNumber"]?.ToString() ?? string.Empty,
                ExpiryDate = DateTime.TryParse(reader["ExpiryDate"]?.ToString(), out var exp) ? exp : DateTime.Today.AddYears(2),
                QuantityBoxes = Convert.ToInt32(reader["QuantityBoxes"]),
                QuantityStrips = Convert.ToInt32(reader["QuantityStrips"]),
                QuantityUnits = Convert.ToInt32(reader["QuantityUnits"]),
                TotalSmallestUnits = Convert.ToInt32(reader["TotalSmallestUnits"]),
                CostPriceBoxPiastres = Convert.ToInt64(reader["CostPriceBoxPiastres"]),
                RetailPriceBoxPiastres = Convert.ToInt64(reader["RetailPriceBoxPiastres"]),
                TotalCostPiastres = Convert.ToInt64(reader["TotalCostPiastres"]),
                TotalRetailPiastres = Convert.ToInt64(reader["TotalRetailPiastres"])
            };
        }

        private void LogAudit(User? user, OpeningStockVoucher voucher)
        {
            if (user == null) return;
            string desc = $"سند رصيد أول المدة رقم {voucher.VoucherNumber}";
            string val = $"{voucher.TotalItemsCount} صنف - تكلفة {voucher.FormattedTotalCost}";

            if (user.Role == UserRole.Owner)
            {
                _ownerAuditLogRepository?.LogChange(user.Id, "OpeningStock", voucher.Id.ToString(), voucher.VoucherNumber, desc, string.Empty, val);
            }
            else
            {
                _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), "OpeningStock", voucher.Id.ToString(), voucher.VoucherNumber, desc, string.Empty, val);
            }
        }
    }
}
