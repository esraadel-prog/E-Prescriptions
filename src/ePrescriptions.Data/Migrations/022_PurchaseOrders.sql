-- 022_PurchaseOrders.sql: أوامر الشراء المحفوظة (من مقترحات النواقص أو يدوية)
-- Status: 0 = مفتوح، 1 = تم الاستلام، 2 = ملغي. أمر الشراء مستند طلب فقط: لا يحرّك مخزون ولا نقدية ولا قيود.
CREATE TABLE IF NOT EXISTS PurchaseOrders (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderNumber TEXT NOT NULL UNIQUE,
    SupplierId INTEGER NOT NULL,
    SupplierName TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    Status INTEGER NOT NULL DEFAULT 0 CHECK (Status IN (0, 1, 2)),
    Notes TEXT NULL,
    UserId INTEGER NOT NULL DEFAULT 0,
    UserName TEXT NOT NULL DEFAULT '',
    EstimatedTotalPiastres INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id)
);

CREATE TABLE IF NOT EXISTS PurchaseOrderItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderId INTEGER NOT NULL,
    ItemId INTEGER NOT NULL,
    ItemCode TEXT NOT NULL,
    ItemNameAr TEXT NOT NULL,
    SmallestUnitsPerBox INTEGER NOT NULL DEFAULT 1,
    OrderUnits INTEGER NOT NULL CHECK (OrderUnits > 0),
    EstimatedCostPiastres INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (OrderId) REFERENCES PurchaseOrders(Id) ON DELETE CASCADE,
    FOREIGN KEY (ItemId) REFERENCES Items(Id)
);

CREATE INDEX IF NOT EXISTS IX_PurchaseOrderItems_Order ON PurchaseOrderItems(OrderId);
CREATE INDEX IF NOT EXISTS IX_PurchaseOrderItems_Item ON PurchaseOrderItems(ItemId);
CREATE INDEX IF NOT EXISTS IX_PurchaseOrders_Status ON PurchaseOrders(Status);
