-- 003_PurchasesAndVouchers.sql: جداول المشتريات وسندات القبض والصرف وتسويات الجرد
-- جميع القيم المالية مخزنة بالهللات كأعداد صحيحة INTEGER

CREATE TABLE IF NOT EXISTS PurchaseInvoices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNumber TEXT NOT NULL UNIQUE,
    SupplierId INTEGER NOT NULL,
    SupplierName TEXT NOT NULL,
    InvoiceDate TEXT NOT NULL,
    SubtotalHalalas INTEGER NOT NULL DEFAULT 0,
    VatHalalas INTEGER NOT NULL DEFAULT 0,
    TotalHalalas INTEGER NOT NULL DEFAULT 0,
    PaidHalalas INTEGER NOT NULL DEFAULT 0,
    RemainingHalalas INTEGER NOT NULL DEFAULT 0,
    Notes TEXT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id)
);

CREATE TABLE IF NOT EXISTS PurchaseInvoiceItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PurchaseInvoiceId INTEGER NOT NULL,
    ItemId INTEGER NOT NULL,
    ItemCode TEXT NOT NULL,
    ItemNameAr TEXT NOT NULL,
    BatchNumber TEXT NOT NULL,
    ExpiryDate TEXT NOT NULL,
    UnitLevel INTEGER NOT NULL DEFAULT 1,
    QuantityPurchased INTEGER NOT NULL DEFAULT 1,
    SmallestUnitsCount INTEGER NOT NULL DEFAULT 1,
    UnitCostHalalas INTEGER NOT NULL DEFAULT 0,
    VatHalalas INTEGER NOT NULL DEFAULT 0,
    TotalHalalas INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(PurchaseInvoiceId) REFERENCES PurchaseInvoices(Id) ON DELETE CASCADE,
    FOREIGN KEY(ItemId) REFERENCES Items(Id)
);

CREATE TABLE IF NOT EXISTS CustomerVouchers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    VoucherNumber TEXT NOT NULL UNIQUE,
    CustomerId INTEGER NOT NULL,
    CustomerName TEXT NOT NULL,
    Date TEXT NOT NULL,
    AmountHalalas INTEGER NOT NULL DEFAULT 0,
    PaymentMethod INTEGER NOT NULL DEFAULT 1,
    Notes TEXT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(CustomerId) REFERENCES Customers(Id)
);

CREATE TABLE IF NOT EXISTS SupplierVouchers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    VoucherNumber TEXT NOT NULL UNIQUE,
    SupplierId INTEGER NOT NULL,
    SupplierName TEXT NOT NULL,
    Date TEXT NOT NULL,
    AmountHalalas INTEGER NOT NULL DEFAULT 0,
    PaymentMethod INTEGER NOT NULL DEFAULT 1,
    Notes TEXT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(SupplierId) REFERENCES Suppliers(Id)
);

CREATE TABLE IF NOT EXISTS StockAdjustments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AdjustmentNumber TEXT NOT NULL UNIQUE,
    ItemId INTEGER NOT NULL,
    ItemCode TEXT NOT NULL,
    ItemNameAr TEXT NOT NULL,
    Date TEXT NOT NULL,
    BookSmallestUnits INTEGER NOT NULL,
    ActualSmallestUnits INTEGER NOT NULL,
    DifferenceSmallestUnits INTEGER NOT NULL,
    Reason TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(ItemId) REFERENCES Items(Id)
);

CREATE INDEX IF NOT EXISTS IX_Purchases_SupplierId ON PurchaseInvoices(SupplierId);
CREATE INDEX IF NOT EXISTS IX_CustomerVouchers_CustomerId ON CustomerVouchers(CustomerId);
CREATE INDEX IF NOT EXISTS IX_SupplierVouchers_SupplierId ON SupplierVouchers(SupplierId);
