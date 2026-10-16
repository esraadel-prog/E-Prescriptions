-- 008_ExpiryReturnsAndBatchLabels.sql
-- 1) تتبع الكميات المباعة بالسالب (قبل تسجيل فاتورة الشراء) كي يتم تسويتها تلقائياً عند دخول الشراء
-- 2) ربط مرتجع المبيعات بفاتورة البيع الأصلية وبنودها
-- 3) تسجيل التشغيلات التي خُصم منها كل بند (تتبع التشغيلة + إرجاع لنفس التشغيلة)
-- 4) مرتجع المنتهي/القريب من الانتهاء للشركة أو إتلافه

ALTER TABLE Items ADD COLUMN OversoldSmallestUnits INTEGER NOT NULL DEFAULT 0;

-- الأرصدة السالبة الحالية تعتبر كميات مباعة بالسالب
UPDATE Items
SET OversoldSmallestUnits = CASE WHEN TotalStockSmallestUnits < 0 THEN -TotalStockSmallestUnits ELSE 0 END;

ALTER TABLE Invoices ADD COLUMN OriginalInvoiceId INTEGER NULL;
ALTER TABLE InvoiceItems ADD COLUMN OriginalInvoiceItemId INTEGER NULL;

CREATE INDEX IF NOT EXISTS IX_Invoices_OriginalInvoiceId ON Invoices(OriginalInvoiceId);
CREATE INDEX IF NOT EXISTS IX_InvoiceItems_OriginalItemId ON InvoiceItems(OriginalInvoiceItemId);
CREATE INDEX IF NOT EXISTS IX_ItemBatches_Expiry ON ItemBatches(ExpiryDate);

-- BatchId = NULL تعني كمية بيعت بالسالب (بدون تشغيلة)
CREATE TABLE IF NOT EXISTS InvoiceItemBatches (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceItemId INTEGER NOT NULL,
    BatchId INTEGER NULL,
    Units INTEGER NOT NULL,
    ReturnedUnits INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(InvoiceItemId) REFERENCES InvoiceItems(Id) ON DELETE CASCADE,
    FOREIGN KEY(BatchId) REFERENCES ItemBatches(Id)
);
CREATE INDEX IF NOT EXISTS IX_InvoiceItemBatches_Item ON InvoiceItemBatches(InvoiceItemId);

-- مرتجع للشركة (Kind = 1) أو إتلاف (Kind = 2)
CREATE TABLE IF NOT EXISTS SupplierReturns (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnNumber TEXT NOT NULL UNIQUE,
    Kind INTEGER NOT NULL DEFAULT 1,
    SupplierId INTEGER NULL,
    SupplierName TEXT NOT NULL DEFAULT '',
    Date TEXT NOT NULL,
    TotalCostPiastres INTEGER NOT NULL DEFAULT 0,
    CreditPiastres INTEGER NOT NULL DEFAULT 0,
    LossPiastres INTEGER NOT NULL DEFAULT 0,
    Notes TEXT NULL,
    UserId INTEGER NOT NULL DEFAULT 0,
    UserName TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id)
);

CREATE TABLE IF NOT EXISTS SupplierReturnItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnId INTEGER NOT NULL,
    ItemId INTEGER NOT NULL,
    ItemCode TEXT NOT NULL,
    ItemNameAr TEXT NOT NULL,
    BatchId INTEGER NOT NULL,
    BatchNumber TEXT NOT NULL,
    ExpiryDate TEXT NOT NULL,
    SmallestUnitsCount INTEGER NOT NULL,
    CostPerUnitPiastres INTEGER NOT NULL DEFAULT 0,
    DeductionPercent INTEGER NOT NULL DEFAULT 0,
    CostTotalPiastres INTEGER NOT NULL DEFAULT 0,
    CreditPiastres INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(ReturnId) REFERENCES SupplierReturns(Id) ON DELETE CASCADE,
    FOREIGN KEY(ItemId) REFERENCES Items(Id),
    FOREIGN KEY(BatchId) REFERENCES ItemBatches(Id)
);
CREATE INDEX IF NOT EXISTS IX_SupplierReturns_Date ON SupplierReturns(Date);
CREATE INDEX IF NOT EXISTS IX_SupplierReturnItems_Return ON SupplierReturnItems(ReturnId);
