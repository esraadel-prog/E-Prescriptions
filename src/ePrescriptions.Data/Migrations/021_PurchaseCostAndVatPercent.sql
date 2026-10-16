-- 021_PurchaseCostAndVatPercent.sql
-- 1) إصلاح تكلفة وحدة التشغيلة المنشأة من فواتير الشراء:
--    كانت UnitCost ÷ SmallestUnitsCount (سعر وحدة واحدة ÷ إجمالي وحدات كل الكمية) فتطلع أقل من الحقيقي بمقدار الكمية.
--    الصح: (UnitCost × Quantity) ÷ SmallestUnitsCount بتقريب Half-Up بالقروش.
UPDATE ItemBatches
SET CostPricePerSmallestUnitPiastres = (
    SELECT (p.UnitCostPiastres * p.QuantityPurchased + p.SmallestUnitsCount / 2) / p.SmallestUnitsCount
    FROM PurchaseInvoiceItems p
    WHERE p.BatchId = ItemBatches.Id AND p.SmallestUnitsCount > 0
    LIMIT 1
)
WHERE Id IN (SELECT BatchId FROM PurchaseInvoiceItems WHERE BatchId IS NOT NULL AND SmallestUnitsCount > 0);

-- 2) نسبة الضريبة المحددة لكل بند شراء (NULL = بند قديم قبل تخزين النسبة؛ القيمة بتفضل في VatPiastres)
ALTER TABLE PurchaseInvoiceItems ADD COLUMN VatPercent INTEGER NULL;
