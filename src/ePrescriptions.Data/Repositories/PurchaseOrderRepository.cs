using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.Data.Repositories
{
    /// <summary>
    /// أوامر الشراء المحفوظة. الأمر مستند طلب فقط (لا مخزون ولا نقدية ولا قيود يومية).
    /// بيتولّد من مقترحات النواقص (PurchaseRepository.GetSuggestedPurchaseOrders)، والأصناف الموجودة في أمر مفتوح
    /// بتختفي من المقترحات وما تتقبلش في أمر تاني لحد ما الأمر يتقفل أو يتلغي.
    /// </summary>
    public class PurchaseOrderRepository
    {
        private readonly DatabaseContext _dbContext;

        public PurchaseOrderRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// حفظ أمر شراء من مقترح. supplierId لازم يتحدد (من المقترح أو يختاره المستخدم لمجموعة "بدون مورد سابق").
        /// الكمية المحفوظة = علب كاملة (OrderUnits) والتقدير من آخر شراء.
        /// </summary>
        public int Create(SuggestedPurchaseOrder suggestion, int? supplierId, int userId, string userName, string? notes, out string error)
        {
            error = string.Empty;
            int sId = supplierId ?? suggestion.SupplierId ?? 0;
            if (sId <= 0) { error = "اختار المورد قبل حفظ أمر الشراء."; return 0; }

            var lines = suggestion.Lines.Where(l => l.OrderUnits > 0).ToList();
            if (lines.Count == 0) { error = "أمر الشراء ما فيهوش أصناف."; return 0; }
            if (lines.Select(l => l.ItemId).Distinct().Count() != lines.Count) { error = "في صنف متكرر داخل نفس الأمر."; return 0; }

            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                string supplierName;
                using (var sCmd = conn.CreateCommand())
                {
                    sCmd.Transaction = trans;
                    sCmd.CommandText = "SELECT Name, IsActive FROM Suppliers WHERE Id = @id;";
                    sCmd.Parameters.AddWithValue("@id", sId);
                    using var r = sCmd.ExecuteReader();
                    if (!r.Read()) throw new InvalidOperationException("المورد غير موجود.");
                    if (Convert.ToInt32(r["IsActive"]) == 0) throw new InvalidOperationException("المورد متوقف؛ اختار مورد نشط.");
                    supplierName = r["Name"].ToString() ?? string.Empty;
                }

                // منع تكرار الطلب: أي صنف في أمر مفتوح تاني
                foreach (var l in lines)
                {
                    using var dCmd = conn.CreateCommand();
                    dCmd.Transaction = trans;
                    dCmd.CommandText = @"
                        SELECT po.OrderNumber FROM PurchaseOrderItems poi
                        JOIN PurchaseOrders po ON po.Id = poi.OrderId
                        WHERE poi.ItemId = @i AND po.Status = 0 LIMIT 1;";
                    dCmd.Parameters.AddWithValue("@i", l.ItemId);
                    object? dup = dCmd.ExecuteScalar();
                    if (dup != null && dup != DBNull.Value)
                        throw new InvalidOperationException($"الصنف {l.ItemNameAr} موجود بالفعل في أمر شراء مفتوح رقم {dup}.");
                }

                long total = lines.Sum(l => l.EstimatedCostPiastres);
                string number;
                using (var nCmd = conn.CreateCommand())
                {
                    nCmd.Transaction = trans;
                    nCmd.CommandText = "SELECT IFNULL(MAX(Id), 0) + 1 FROM PurchaseOrders;";
                    long next = Convert.ToInt64(nCmd.ExecuteScalar() ?? 1);
                    number = $"PO-{DateTime.Now.Year}-{next:D5}";
                }

                int orderId;
                using (var hCmd = conn.CreateCommand())
                {
                    hCmd.Transaction = trans;
                    hCmd.CommandText = @"
                        INSERT INTO PurchaseOrders (OrderNumber, SupplierId, SupplierName, CreatedAt, Status, Notes, UserId, UserName, EstimatedTotalPiastres)
                        VALUES (@num, @sId, @sName, @at, 0, @notes, @uId, @uName, @total);
                        SELECT last_insert_rowid();";
                    hCmd.Parameters.AddWithValue("@num", number);
                    hCmd.Parameters.AddWithValue("@sId", sId);
                    hCmd.Parameters.AddWithValue("@sName", supplierName);
                    hCmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));
                    hCmd.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(notes) ? (object)DBNull.Value : notes!);
                    hCmd.Parameters.AddWithValue("@uId", userId);
                    hCmd.Parameters.AddWithValue("@uName", userName ?? string.Empty);
                    hCmd.Parameters.AddWithValue("@total", total);
                    orderId = Convert.ToInt32(hCmd.ExecuteScalar());
                }

                foreach (var l in lines)
                {
                    using var iCmd = conn.CreateCommand();
                    iCmd.Transaction = trans;
                    iCmd.CommandText = @"
                        INSERT INTO PurchaseOrderItems (OrderId, ItemId, ItemCode, ItemNameAr, SmallestUnitsPerBox, OrderUnits, EstimatedCostPiastres)
                        VALUES (@o, @i, @code, @name, @perBox, @units, @est);";
                    iCmd.Parameters.AddWithValue("@o", orderId);
                    iCmd.Parameters.AddWithValue("@i", l.ItemId);
                    iCmd.Parameters.AddWithValue("@code", l.ItemCode);
                    iCmd.Parameters.AddWithValue("@name", l.ItemNameAr);
                    iCmd.Parameters.AddWithValue("@perBox", Math.Max(1, l.SmallestUnitsPerBox));
                    iCmd.Parameters.AddWithValue("@units", l.OrderUnits);
                    iCmd.Parameters.AddWithValue("@est", l.EstimatedCostPiastres);
                    iCmd.ExecuteNonQuery();
                }

                trans.Commit();
                return orderId;
            }
            catch (InvalidOperationException ex)
            {
                trans.Rollback();
                error = ex.Message;
                return 0;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                Logger.LogException(ex, "PurchaseOrderRepository.Create");
                error = "حصل خطأ أثناء حفظ أمر الشراء.";
                return 0;
            }
        }

        /// <summary>المفتوح فقط يتقفل (استلام) أو يتلغي</summary>
        public bool SetStatus(int orderId, PurchaseOrderStatus newStatus, out string error)
        {
            error = string.Empty;
            if (newStatus == PurchaseOrderStatus.Open) { error = "مينفعش إعادة فتح أمر شراء."; return false; }

            using var conn = _dbContext.CreateConnection();
            using (var chk = conn.CreateCommand())
            {
                chk.CommandText = "SELECT Status FROM PurchaseOrders WHERE Id = @id;";
                chk.Parameters.AddWithValue("@id", orderId);
                object? cur = chk.ExecuteScalar();
                if (cur == null || cur == DBNull.Value || Convert.ToInt32(cur) != (int)PurchaseOrderStatus.Open)
                {
                    error = "أمر الشراء غير موجود أو مش مفتوح.";
                    return false;
                }
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE PurchaseOrders SET Status = @s WHERE Id = @id AND Status = 0;";
            cmd.Parameters.AddWithValue("@s", (int)newStatus);
            cmd.Parameters.AddWithValue("@id", orderId);
            cmd.ExecuteNonQuery();
            return true;
        }

        public PurchaseOrder? Get(int orderId)
        {
            using var conn = _dbContext.CreateConnection();
            PurchaseOrder? order = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = HeaderSelect + " WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", orderId);
                using var r = cmd.ExecuteReader();
                if (r.Read()) order = ReadHeader(r);
            }
            if (order == null) return null;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, OrderId, ItemId, ItemCode, ItemNameAr, SmallestUnitsPerBox, OrderUnits, EstimatedCostPiastres
                    FROM PurchaseOrderItems WHERE OrderId = @id ORDER BY Id;";
                cmd.Parameters.AddWithValue("@id", orderId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    order.Items.Add(new PurchaseOrderItem
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        OrderId = orderId,
                        ItemId = Convert.ToInt32(r["ItemId"]),
                        ItemCode = r["ItemCode"].ToString() ?? string.Empty,
                        ItemNameAr = r["ItemNameAr"].ToString() ?? string.Empty,
                        SmallestUnitsPerBox = Convert.ToInt32(r["SmallestUnitsPerBox"]),
                        OrderUnits = Convert.ToInt32(r["OrderUnits"]),
                        EstimatedCostPiastres = Convert.ToInt64(r["EstimatedCostPiastres"])
                    });
                }
            }
            return order;
        }

        public List<PurchaseOrder> GetRecent(int limit = 50, PurchaseOrderStatus? status = null)
        {
            var list = new List<PurchaseOrder>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = HeaderSelect + (status.HasValue ? " WHERE Status = @s" : string.Empty) + " ORDER BY Id DESC LIMIT @limit;";
            if (status.HasValue) cmd.Parameters.AddWithValue("@s", (int)status.Value);
            cmd.Parameters.AddWithValue("@limit", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadHeader(r));
            return list;
        }

        private const string HeaderSelect = @"
            SELECT Id, OrderNumber, SupplierId, SupplierName, CreatedAt, Status, Notes, UserId, UserName, EstimatedTotalPiastres
            FROM PurchaseOrders";

        private static PurchaseOrder ReadHeader(SQLiteDataReader r) => new PurchaseOrder
        {
            Id = Convert.ToInt32(r["Id"]),
            OrderNumber = r["OrderNumber"].ToString() ?? string.Empty,
            SupplierId = Convert.ToInt32(r["SupplierId"]),
            SupplierName = r["SupplierName"].ToString() ?? string.Empty,
            CreatedAt = DbDate.Parse(r["CreatedAt"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            Status = (PurchaseOrderStatus)Convert.ToInt32(r["Status"]),
            Notes = r["Notes"] is DBNull ? null : r["Notes"].ToString(),
            UserId = Convert.ToInt32(r["UserId"]),
            UserName = r["UserName"].ToString() ?? string.Empty,
            EstimatedTotalPiastres = Convert.ToInt64(r["EstimatedTotalPiastres"])
        };
    }
}
