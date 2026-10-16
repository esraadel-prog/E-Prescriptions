using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.Data.Repositories
{
    /// <summary>
    /// مرتجع الأدوية المنتهية أو القريبة من الانتهاء للشركة (بخصم نسبة) أو إتلافها.
    /// الحفظ ذري: خصم الكمية من التشغيلة ومن رصيد الصنف + تسجيل المستند + تخفيض رصيد المورد بقيمة الاسترداد.
    /// </summary>
    public class SupplierReturnRepository
    {
        private readonly DatabaseContext _dbContext;

        public SupplierReturnRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>قيمة الاسترداد بعد خصم النسبة (تقريب Half-Up بالقروش)</summary>
        public static long CalculateCredit(long costTotalPiastres, int deductionPercent)
        {
            if (costTotalPiastres <= 0) return 0;
            int pct = Math.Max(0, Math.Min(100, deductionPercent));
            return (costTotalPiastres * (100 - pct) + 50) / 100;
        }

        public bool Save(SupplierReturn ret, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (ret.Items.Count == 0)
            {
                errorMessage = "أضف تشغيلة واحدة على الأقل.";
                return false;
            }
            bool linked = ret.PurchaseInvoiceId.HasValue && ret.PurchaseInvoiceId.Value > 0;
            if (!linked && ret.Kind == SupplierReturnKind.ReturnToSupplier && (!ret.SupplierId.HasValue || ret.SupplierId.Value <= 0))
            {
                errorMessage = "اختار الشركة (المورد) اللي هترجعلها الأدوية.";
                return false;
            }

            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                long totalCost = 0;
                long totalCredit = 0;

                // الكميات المحجوزة داخل نفس المستند (بند مكرر أو نفس التشغيلة في بندين) عشان ما نتخطاش المتاح
                var usedPerBatch = new Dictionary<int, int>();
                var usedPerPurchaseItem = new Dictionary<int, int>();

                if (linked)
                {
                    if (ret.Kind != SupplierReturnKind.ReturnToSupplier)
                        throw new InvalidOperationException("المرتجع المرتبط بفاتورة شراء لازم يكون مرتجع للشركة (مش إتلاف).");
                    if (string.IsNullOrWhiteSpace(ret.Reason))
                        throw new InvalidOperationException("اكتب سبب المرتجع (عيب أو خطأ في التوريد).");

                    using var pCmd = conn.CreateCommand();
                    pCmd.Transaction = trans;
                    pCmd.CommandText = "SELECT InvoiceNumber, SupplierId, SupplierName FROM PurchaseInvoices WHERE Id = @id;";
                    pCmd.Parameters.AddWithValue("@id", ret.PurchaseInvoiceId!.Value);
                    using var pr = pCmd.ExecuteReader();
                    if (!pr.Read())
                        throw new InvalidOperationException("فاتورة الشراء الأصلية غير موجودة.");
                    int invoiceSupplierId = Convert.ToInt32(pr["SupplierId"]);
                    if (ret.SupplierId.HasValue && ret.SupplierId.Value > 0 && ret.SupplierId.Value != invoiceSupplierId)
                        throw new InvalidOperationException("المورد المختار غير مطابق لمورد فاتورة الشراء الأصلية.");
                    ret.SupplierId = invoiceSupplierId;
                    ret.SupplierName = pr["SupplierName"].ToString() ?? string.Empty;
                    ret.PurchaseInvoiceNumber = pr["InvoiceNumber"].ToString() ?? string.Empty;
                }

                // فحص وحساب كل بند من بيانات التشغيلة الفعلية (لا نثق في أرقام الواجهة)
                foreach (var line in ret.Items)
                {
                    if (line.SmallestUnitsCount <= 0)
                        throw new InvalidOperationException($"كمية غير صحيحة للصنف {line.ItemNameAr}.");

                    // المرتجع المرتبط: البند والتشغيلة والسعر كلها بتتاخد من فاتورة الشراء الأصلية
                    long linkedLineTotal = 0;
                    int linkedPurchasedUnits = 0;
                    int linkedAlreadyUnits = 0;
                    if (linked)
                    {
                        if (!line.PurchaseInvoiceItemId.HasValue)
                            throw new InvalidOperationException("كل بند في المرتجع لازم يكون مسحوب من بنود فاتورة الشراء الأصلية.");

                        int pItemId = line.PurchaseInvoiceItemId.Value;
                        using (var piCmd = conn.CreateCommand())
                        {
                            piCmd.Transaction = trans;
                            piCmd.CommandText = @"
                                SELECT PurchaseInvoiceId, BatchId, SmallestUnitsCount, TotalPiastres, ItemNameAr,
                                       (SELECT IFNULL(SUM(r.SmallestUnitsCount), 0) FROM SupplierReturnItems r WHERE r.PurchaseInvoiceItemId = PurchaseInvoiceItems.Id) AS ReturnedUnits
                                FROM PurchaseInvoiceItems WHERE Id = @id;";
                            piCmd.Parameters.AddWithValue("@id", pItemId);
                            using var pir = piCmd.ExecuteReader();
                            if (!pir.Read() || Convert.ToInt32(pir["PurchaseInvoiceId"]) != ret.PurchaseInvoiceId!.Value)
                                throw new InvalidOperationException("البند المختار مش من فاتورة الشراء الأصلية.");
                            if (pir["BatchId"] == DBNull.Value)
                                throw new InvalidOperationException(
                                    $"بند {pir["ItemNameAr"]} في الفاتورة دي مش مربوط بتشغيلة محددة (فاتورة قديمة)؛ استخدم شاشة الشطب واختار التشغيلة يدوياً.");

                            line.BatchId = Convert.ToInt32(pir["BatchId"]);
                            linkedPurchasedUnits = Convert.ToInt32(pir["SmallestUnitsCount"]);
                            linkedLineTotal = Convert.ToInt64(pir["TotalPiastres"]);
                            linkedAlreadyUnits = Convert.ToInt32(pir["ReturnedUnits"]);
                        }

                        usedPerPurchaseItem.TryGetValue(pItemId, out int usedHere);
                        linkedAlreadyUnits += usedHere;
                        if (linkedAlreadyUnits + line.SmallestUnitsCount > linkedPurchasedUnits)
                            throw new InvalidOperationException(
                                $"الكمية المطلوب إرجاعها ({line.SmallestUnitsCount}) أكبر من المتاح للإرجاع من هذا البند: " +
                                $"المشترى {linkedPurchasedUnits} وسبق إرجاع {linkedAlreadyUnits}.");
                        usedPerPurchaseItem[pItemId] = usedHere + line.SmallestUnitsCount;
                    }

                    using var bCmd = conn.CreateCommand();
                    bCmd.Transaction = trans;
                    bCmd.CommandText = @"
                        SELECT b.ItemId, b.BatchNumber, b.ExpiryDate, b.RemainingSmallestUnits, b.CostPricePerSmallestUnitPiastres,
                               i.Code, i.NameAr
                        FROM ItemBatches b JOIN Items i ON i.Id = b.ItemId
                        WHERE b.Id = @id;";
                    bCmd.Parameters.AddWithValue("@id", line.BatchId);
                    using var r = bCmd.ExecuteReader();
                    if (!r.Read())
                        throw new InvalidOperationException($"التشغيلة المختارة للصنف {line.ItemNameAr} غير موجودة.");

                    int remaining = Convert.ToInt32(r["RemainingSmallestUnits"]);
                    usedPerBatch.TryGetValue(line.BatchId, out int usedFromBatch);
                    remaining -= usedFromBatch;
                    if (line.SmallestUnitsCount > remaining)
                        throw new InvalidOperationException(
                            $"الكمية المطلوب إرجاعها من {r["NameAr"]} (تشغيلة {r["BatchNumber"]}) أكبر من المتاح بالتشغيلة ({remaining}).");

                    line.ItemId = Convert.ToInt32(r["ItemId"]);
                    line.ItemCode = r["Code"].ToString() ?? string.Empty;
                    line.ItemNameAr = r["NameAr"].ToString() ?? string.Empty;
                    line.BatchNumber = r["BatchNumber"].ToString() ?? string.Empty;
                    line.ExpiryDate = DbDate.Parse(r["ExpiryDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                    line.CostPerUnitPiastres = Convert.ToInt64(r["CostPricePerSmallestUnitPiastres"]);

                    usedPerBatch[line.BatchId] = usedFromBatch + line.SmallestUnitsCount;

                    line.DeductionPercent = ret.Kind == SupplierReturnKind.Destroy ? 100 : Math.Max(0, Math.Min(100, line.DeductionPercent));
                    line.CostTotalPiastres = line.CostPerUnitPiastres * line.SmallestUnitsCount;
                    if (linked)
                    {
                        // السعر من فاتورة الشراء (إجمالي البند شامل الضريبة موزّع على وحداته) مش من تكلفة التشغيلة ولا مدخل يدوي.
                        // الفرق التراكمي بيخلّي مجموع المرتجعات الجزئية = إجمالي البند بالظبط.
                        line.CostTotalPiastres = ProportionalAmount(linkedLineTotal, linkedAlreadyUnits + line.SmallestUnitsCount, linkedPurchasedUnits)
                                                 - ProportionalAmount(linkedLineTotal, linkedAlreadyUnits, linkedPurchasedUnits);
                        line.CostPerUnitPiastres = linkedPurchasedUnits > 0 ? (linkedLineTotal + linkedPurchasedUnits / 2) / linkedPurchasedUnits : 0;
                    }
                    line.CreditPiastres = CalculateCredit(line.CostTotalPiastres, line.DeductionPercent);

                    totalCost += line.CostTotalPiastres;
                    totalCredit += line.CreditPiastres;
                }

                ret.TotalCostPiastres = totalCost;
                ret.CreditPiastres = totalCredit;
                ret.LossPiastres = totalCost - totalCredit;

                // رقم المستند داخل نفس الـ Transaction لتفادي التكرار
                using (var numCmd = conn.CreateCommand())
                {
                    numCmd.Transaction = trans;
                    numCmd.CommandText = "SELECT IFNULL(MAX(Id), 0) + 1 FROM SupplierReturns;";
                    long next = Convert.ToInt64(numCmd.ExecuteScalar() ?? 1);
                    string prefix = ret.Kind == SupplierReturnKind.Destroy ? "DST" : "SRT";
                    ret.ReturnNumber = $"{prefix}-{DateTime.Now.Year}-{next:D5}";
                }

                string supplierName = ret.SupplierName ?? string.Empty;
                if (ret.SupplierId.HasValue && ret.SupplierId.Value > 0 && string.IsNullOrWhiteSpace(supplierName))
                {
                    using var sCmd = conn.CreateCommand();
                    sCmd.Transaction = trans;
                    sCmd.CommandText = "SELECT Name FROM Suppliers WHERE Id = @id;";
                    sCmd.Parameters.AddWithValue("@id", ret.SupplierId.Value);
                    supplierName = sCmd.ExecuteScalar()?.ToString() ?? string.Empty;
                }
                ret.SupplierName = supplierName;

                using (var hCmd = conn.CreateCommand())
                {
                    hCmd.Transaction = trans;
                    hCmd.CommandText = @"
                        INSERT INTO SupplierReturns (
                            ReturnNumber, Kind, SupplierId, SupplierName, Date,
                            TotalCostPiastres, CreditPiastres, LossPiastres, Reason, Notes, UserId, UserName, CreatedAt,
                            PurchaseInvoiceId
                        ) VALUES (
                            @num, @kind, @sId, @sName, @date,
                            @cost, @credit, @loss, @reason, @notes, @uId, @uName, @created,
                            @pInv
                        );
                        SELECT last_insert_rowid();";
                    hCmd.Parameters.AddWithValue("@num", ret.ReturnNumber);
                    hCmd.Parameters.AddWithValue("@kind", (int)ret.Kind);
                    hCmd.Parameters.AddWithValue("@sId", ret.Kind == SupplierReturnKind.ReturnToSupplier && ret.SupplierId.HasValue ? (object)ret.SupplierId.Value : DBNull.Value);
                    hCmd.Parameters.AddWithValue("@sName", ret.SupplierName);
                    hCmd.Parameters.AddWithValue("@date", ret.Date.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                    hCmd.Parameters.AddWithValue("@cost", ret.TotalCostPiastres);
                    hCmd.Parameters.AddWithValue("@credit", ret.CreditPiastres);
                    hCmd.Parameters.AddWithValue("@loss", ret.LossPiastres);
                    hCmd.Parameters.AddWithValue("@reason", ret.Reason ?? string.Empty);
                    hCmd.Parameters.AddWithValue("@notes", (object?)ret.Notes ?? DBNull.Value);
                    hCmd.Parameters.AddWithValue("@uId", ret.UserId);
                    hCmd.Parameters.AddWithValue("@uName", ret.UserName ?? string.Empty);
                    hCmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));
                    hCmd.Parameters.AddWithValue("@pInv", linked ? (object)ret.PurchaseInvoiceId!.Value : DBNull.Value);
                    ret.Id = Convert.ToInt32(hCmd.ExecuteScalar());
                }

                foreach (var line in ret.Items)
                {
                    line.ReturnId = ret.Id;

                    using var iCmd = conn.CreateCommand();
                    iCmd.Transaction = trans;
                    iCmd.CommandText = @"
                        INSERT INTO SupplierReturnItems (
                            ReturnId, ItemId, ItemCode, ItemNameAr, BatchId, BatchNumber, ExpiryDate,
                            SmallestUnitsCount, CostPerUnitPiastres, DeductionPercent, CostTotalPiastres, CreditPiastres,
                            PurchaseInvoiceItemId
                        ) VALUES (
                            @rId, @itemId, @code, @name, @bId, @batch, @exp,
                            @units, @cpu, @pct, @cost, @credit,
                            @pItem
                        );";
                    iCmd.Parameters.AddWithValue("@rId", ret.Id);
                    iCmd.Parameters.AddWithValue("@itemId", line.ItemId);
                    iCmd.Parameters.AddWithValue("@code", line.ItemCode);
                    iCmd.Parameters.AddWithValue("@name", line.ItemNameAr);
                    iCmd.Parameters.AddWithValue("@bId", line.BatchId);
                    iCmd.Parameters.AddWithValue("@batch", line.BatchNumber);
                    iCmd.Parameters.AddWithValue("@exp", ExpiryPolicy.ToIso(line.ExpiryDate));
                    iCmd.Parameters.AddWithValue("@units", line.SmallestUnitsCount);
                    iCmd.Parameters.AddWithValue("@cpu", line.CostPerUnitPiastres);
                    iCmd.Parameters.AddWithValue("@pct", line.DeductionPercent);
                    iCmd.Parameters.AddWithValue("@cost", line.CostTotalPiastres);
                    iCmd.Parameters.AddWithValue("@credit", line.CreditPiastres);
                    iCmd.Parameters.AddWithValue("@pItem", linked && line.PurchaseInvoiceItemId.HasValue ? (object)line.PurchaseInvoiceItemId.Value : DBNull.Value);
                    iCmd.ExecuteNonQuery();

                    // خصم الكمية من التشغيلة ومن إجمالي رصيد الصنف
                    using var bUpd = conn.CreateCommand();
                    bUpd.Transaction = trans;
                    bUpd.CommandText = "UPDATE ItemBatches SET RemainingSmallestUnits = RemainingSmallestUnits - @u WHERE Id = @id;";
                    bUpd.Parameters.AddWithValue("@u", line.SmallestUnitsCount);
                    bUpd.Parameters.AddWithValue("@id", line.BatchId);
                    bUpd.ExecuteNonQuery();

                    using var tUpd = conn.CreateCommand();
                    tUpd.Transaction = trans;
                    tUpd.CommandText = "UPDATE Items SET TotalStockSmallestUnits = TotalStockSmallestUnits - @u WHERE Id = @id;";
                    tUpd.Parameters.AddWithValue("@u", line.SmallestUnitsCount);
                    tUpd.Parameters.AddWithValue("@id", line.ItemId);
                    tUpd.ExecuteNonQuery();
                }

                // قيمة الاسترداد تخفض ما نحن مدينون به للمورد (قد يصبح رصيده بالسالب = له عندنا مستحقات تُسوّى لاحقاً)
                if (ret.Kind == SupplierReturnKind.ReturnToSupplier && ret.SupplierId.HasValue && ret.CreditPiastres > 0)
                {
                    using var supCmd = conn.CreateCommand();
                    supCmd.Transaction = trans;
                    supCmd.CommandText = "UPDATE Suppliers SET BalancePiastres = BalancePiastres - @c WHERE Id = @id;";
                    supCmd.Parameters.AddWithValue("@c", ret.CreditPiastres);
                    supCmd.Parameters.AddWithValue("@id", ret.SupplierId.Value);
                    supCmd.ExecuteNonQuery();
                }

                // قيد اليومية داخل نفس الـ Transaction (مخزون / موردون / خسائر تالف)
                JournalPosting.PostSupplierReturn(conn, trans, ret);

                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                errorMessage = ex.Message;
                if (ex is not InvalidOperationException)
                    Logger.LogException(ex, "SupplierReturnRepository.Save");
                return false;
            }
        }

        /// <summary>(المبلغ × الجزء ÷ الكل) بتقريب Half-Up بالقروش، بدون كسور عشرية</summary>
        public static long ProportionalAmount(long totalPiastres, int part, int whole)
        {
            if (whole <= 0 || part <= 0 || totalPiastres <= 0) return 0;
            if (part >= whole) return totalPiastres;
            return (totalPiastres * part + whole / 2) / whole;
        }

        /// <summary>بحث فواتير الشراء برقم الفاتورة أو اسم المورد (لاختيار الفاتورة الأصلية في شاشة المرتجع)</summary>
        public List<PurchaseInvoice> SearchPurchases(string term, int limit = 20)
        {
            var list = new List<PurchaseInvoice>();
            string like = "%" + (term ?? string.Empty).Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, InvoiceNumber, SupplierId, SupplierName, InvoiceDate, TotalPiastres
                FROM PurchaseInvoices
                WHERE InvoiceNumber LIKE @t ESCAPE '\' OR SupplierName LIKE @t ESCAPE '\'
                ORDER BY Id DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@t", like);
            cmd.Parameters.AddWithValue("@limit", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new PurchaseInvoice
                {
                    Id = Convert.ToInt32(r["Id"]),
                    InvoiceNumber = r["InvoiceNumber"].ToString() ?? string.Empty,
                    SupplierId = Convert.ToInt32(r["SupplierId"]),
                    SupplierName = r["SupplierName"].ToString() ?? string.Empty,
                    InvoiceDate = DbDate.Parse(r["InvoiceDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                    TotalPiastres = Convert.ToInt64(r["TotalPiastres"])
                });
            }
            return list;
        }

        /// <summary>فاتورة شراء برقمها مع بنودها والكمية القابلة للإرجاع من كل بند (null لو الرقم غير موجود)</summary>
        public ReturnablePurchase? GetReturnablePurchaseByNumber(string invoiceNumber)
        {
            string number = (invoiceNumber ?? string.Empty).Trim();
            if (number.Length == 0) return null;

            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id FROM PurchaseInvoices WHERE UPPER(InvoiceNumber) = UPPER(@n);";
            cmd.Parameters.AddWithValue("@n", number);
            object? id = cmd.ExecuteScalar();
            return id == null || id == DBNull.Value ? null : GetReturnablePurchase(Convert.ToInt32(id));
        }

        public ReturnablePurchase? GetReturnablePurchase(int purchaseInvoiceId)
        {
            using var conn = _dbContext.CreateConnection();

            var result = new ReturnablePurchase();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, InvoiceNumber, SupplierId, SupplierName, InvoiceDate,
                           SubtotalPiastres, VatPiastres, TotalPiastres, PaidPiastres, RemainingPiastres, Notes
                    FROM PurchaseInvoices WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", purchaseInvoiceId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                result.Invoice = new PurchaseInvoice
                {
                    Id = Convert.ToInt32(r["Id"]),
                    InvoiceNumber = r["InvoiceNumber"].ToString() ?? string.Empty,
                    SupplierId = Convert.ToInt32(r["SupplierId"]),
                    SupplierName = r["SupplierName"].ToString() ?? string.Empty,
                    InvoiceDate = DbDate.Parse(r["InvoiceDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                    SubtotalPiastres = Convert.ToInt64(r["SubtotalPiastres"]),
                    VatPiastres = Convert.ToInt64(r["VatPiastres"]),
                    TotalPiastres = Convert.ToInt64(r["TotalPiastres"]),
                    PaidPiastres = Convert.ToInt64(r["PaidPiastres"]),
                    RemainingPiastres = Convert.ToInt64(r["RemainingPiastres"]),
                    Notes = r["Notes"] is DBNull ? null : r["Notes"].ToString()
                };
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT p.Id, p.ItemId, p.ItemCode, p.ItemNameAr, p.BatchNumber, p.ExpiryDate, p.BatchId,
                           p.UnitLevel, p.QuantityPurchased, p.SmallestUnitsCount, p.UnitCostPiastres, p.VatPiastres, p.TotalPiastres, p.VatPercent,
                           (SELECT IFNULL(SUM(sri.SmallestUnitsCount), 0) FROM SupplierReturnItems sri WHERE sri.PurchaseInvoiceItemId = p.Id) AS ReturnedUnits,
                           (SELECT b.RemainingSmallestUnits FROM ItemBatches b WHERE b.Id = p.BatchId) AS BatchRemaining
                    FROM PurchaseInvoiceItems p
                    WHERE p.PurchaseInvoiceId = @id
                    ORDER BY p.Id;";
                cmd.Parameters.AddWithValue("@id", purchaseInvoiceId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var line = new PurchaseInvoiceItem
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        PurchaseInvoiceId = purchaseInvoiceId,
                        ItemId = Convert.ToInt32(r["ItemId"]),
                        ItemCode = r["ItemCode"].ToString() ?? string.Empty,
                        ItemNameAr = r["ItemNameAr"].ToString() ?? string.Empty,
                        BatchNumber = r["BatchNumber"].ToString() ?? string.Empty,
                        ExpiryDate = DbDate.Parse(r["ExpiryDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                        BatchId = r["BatchId"] is DBNull ? 0 : Convert.ToInt32(r["BatchId"]),
                        UnitLevel = (ePrescriptions.Domain.Enums.UnitLevel)Convert.ToInt32(r["UnitLevel"]),
                        QuantityPurchased = Convert.ToInt32(r["QuantityPurchased"]),
                        SmallestUnitsCount = Convert.ToInt32(r["SmallestUnitsCount"]),
                        UnitCostPiastres = Convert.ToInt64(r["UnitCostPiastres"]),
                        VatPiastres = Convert.ToInt64(r["VatPiastres"]),
                        TotalPiastres = Convert.ToInt64(r["TotalPiastres"]),
                        VatPercent = r["VatPercent"] is DBNull ? (int?)null : Convert.ToInt32(r["VatPercent"])
                    };
                    result.Lines.Add(new ReturnablePurchaseLine
                    {
                        Line = line,
                        ReturnedUnits = Convert.ToInt32(r["ReturnedUnits"]),
                        BatchRemainingUnits = r["BatchRemaining"] is DBNull ? 0 : Convert.ToInt32(r["BatchRemaining"])
                    });
                }
            }
            return result;
        }

        /// <summary>آخر المرتجعات/الإتلافات (للعرض في الشاشة)</summary>
        public List<SupplierReturn> GetRecent(int limit = 50)
        {
            var list = new List<SupplierReturn>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT sr.Id, sr.ReturnNumber, sr.Kind, sr.SupplierId, sr.SupplierName, sr.Date,
                       sr.TotalCostPiastres, sr.CreditPiastres, sr.LossPiastres, sr.Reason, sr.Notes, sr.UserId, sr.UserName,
                       sr.PurchaseInvoiceId, pi.InvoiceNumber AS PurchaseInvoiceNumber
                FROM SupplierReturns sr
                LEFT JOIN PurchaseInvoices pi ON pi.Id = sr.PurchaseInvoiceId
                ORDER BY sr.Id DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SupplierReturn
                {
                    Id = Convert.ToInt32(r["Id"]),
                    ReturnNumber = r["ReturnNumber"].ToString() ?? string.Empty,
                    Kind = (SupplierReturnKind)Convert.ToInt32(r["Kind"]),
                    SupplierId = r["SupplierId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["SupplierId"]),
                    SupplierName = r["SupplierName"].ToString() ?? string.Empty,
                    Date = DbDate.Parse(r["Date"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture)),
                    TotalCostPiastres = Convert.ToInt64(r["TotalCostPiastres"]),
                    CreditPiastres = Convert.ToInt64(r["CreditPiastres"]),
                    LossPiastres = Convert.ToInt64(r["LossPiastres"]),
                    Reason = r["Reason"].ToString() ?? string.Empty,
                    Notes = r["Notes"] == DBNull.Value ? null : r["Notes"].ToString(),
                    UserId = Convert.ToInt32(r["UserId"]),
                    UserName = r["UserName"].ToString() ?? string.Empty,
                    PurchaseInvoiceId = r["PurchaseInvoiceId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["PurchaseInvoiceId"]),
                    PurchaseInvoiceNumber = r["PurchaseInvoiceNumber"] == DBNull.Value ? string.Empty : (r["PurchaseInvoiceNumber"].ToString() ?? string.Empty)
                });
            }
            return list;
        }
    }
}
