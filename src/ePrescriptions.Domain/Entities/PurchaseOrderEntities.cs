using System;
using System.Collections.Generic;
using System.Linq;

namespace ePrescriptions.Domain.Entities
{
    public enum PurchaseOrderStatus
    {
        Open = 0,
        Received = 1,
        Cancelled = 2
    }

    public class PurchaseOrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;
        public int SmallestUnitsPerBox { get; set; } = 1;

        /// <summary>الكمية المطلوبة بأصغر وحدة (علب كاملة)</summary>
        public int OrderUnits { get; set; }
        public int OrderBoxes => SmallestUnitsPerBox <= 0 ? OrderUnits : OrderUnits / SmallestUnitsPerBox;
        public long EstimatedCostPiastres { get; set; }
    }

    /// <summary>أمر شراء: مستند طلب لمورد (لا يحرّك مخزون ولا نقدية ولا قيود)</summary>
    public class PurchaseOrder
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Open;
        public string? Notes { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public long EstimatedTotalPiastres { get; set; }
        public List<PurchaseOrderItem> Items { get; set; } = new();
    }
}
