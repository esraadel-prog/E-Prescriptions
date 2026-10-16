-- 020_PurchaseLinkedSupplierReturns.sql (خطوة 39-أ)
-- ربط مرتجع المورد بفاتورة الشراء الأصلية وببند الشراء نفسه:
-- 1) PurchaseInvoiceItems.BatchId = التشغيلة التي أنشأها البند عند الحفظ (كانت بتتحدد بالاسم فقط وبدون تخزين)
-- 2) SupplierReturns.PurchaseInvoiceId / SupplierReturnItems.PurchaseInvoiceItemId = مصدر المرتجع (NULL = شطب/مرتجع غير مرتبط بفاتورة)

ALTER TABLE PurchaseInvoiceItems ADD COLUMN BatchId INTEGER NULL;

-- تعبئة البنود القديمة: فقط لو في تشغيلة واحدة بالظبط بنفس الصنف + رقم التشغيلة + تاريخ الصلاحية
-- (لو في أكتر من تشغيلة مطابقة، الربط بيتساب NULL بدل ما يتخمّن غلط)
UPDATE PurchaseInvoiceItems
SET BatchId = (
    SELECT b.Id FROM ItemBatches b
    WHERE b.ItemId = PurchaseInvoiceItems.ItemId
      AND b.BatchNumber = PurchaseInvoiceItems.BatchNumber
      AND substr(b.ExpiryDate, 1, 10) = substr(PurchaseInvoiceItems.ExpiryDate, 1, 10)
)
WHERE (
    SELECT COUNT(*) FROM ItemBatches b
    WHERE b.ItemId = PurchaseInvoiceItems.ItemId
      AND b.BatchNumber = PurchaseInvoiceItems.BatchNumber
      AND substr(b.ExpiryDate, 1, 10) = substr(PurchaseInvoiceItems.ExpiryDate, 1, 10)
) = 1;

ALTER TABLE SupplierReturns ADD COLUMN PurchaseInvoiceId INTEGER NULL;
ALTER TABLE SupplierReturnItems ADD COLUMN PurchaseInvoiceItemId INTEGER NULL;

CREATE INDEX IF NOT EXISTS IX_SupplierReturns_PurchaseInvoice ON SupplierReturns(PurchaseInvoiceId);
CREATE INDEX IF NOT EXISTS IX_SupplierReturnItems_PurchaseItem ON SupplierReturnItems(PurchaseInvoiceItemId);
