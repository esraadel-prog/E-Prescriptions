-- Migration 024: جداول سندات رصيد أول المدة (Opening Stock Vouchers)
-- تُسجّل سندات وبنود إدخال رصيد المخزون الافتتاحي للبرنامج قبل بدء العمل الفعلي

CREATE TABLE IF NOT EXISTS OpeningStockVouchers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    VoucherNumber TEXT NOT NULL UNIQUE,
    VoucherDate TEXT NOT NULL,
    Notes TEXT,
    CreatedByUserId INTEGER NOT NULL DEFAULT 1,
    CreatedByUserName TEXT NOT NULL DEFAULT '',
    TotalItemsCount INTEGER NOT NULL DEFAULT 0,
    TotalSmallestUnits INTEGER NOT NULL DEFAULT 0,
    TotalCostPiastres INTEGER NOT NULL DEFAULT 0,
    TotalRetailPiastres INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS OpeningStockVoucherLines (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    VoucherId INTEGER NOT NULL,
    ItemId INTEGER NOT NULL,
    ItemCode TEXT,
    ItemBarcode TEXT,
    ItemNameAr TEXT NOT NULL,
    ItemNameEn TEXT,
    ActiveIngredient TEXT,
    StripsPerBox INTEGER NOT NULL DEFAULT 1,
    PiecesPerStrip INTEGER NOT NULL DEFAULT 10,
    BatchNumber TEXT NOT NULL,
    ExpiryDate TEXT NOT NULL,
    QuantityBoxes INTEGER NOT NULL DEFAULT 0,
    QuantityStrips INTEGER NOT NULL DEFAULT 0,
    QuantityUnits INTEGER NOT NULL DEFAULT 0,
    TotalSmallestUnits INTEGER NOT NULL DEFAULT 0,
    CostPriceBoxPiastres INTEGER NOT NULL DEFAULT 0,
    RetailPriceBoxPiastres INTEGER NOT NULL DEFAULT 0,
    TotalCostPiastres INTEGER NOT NULL DEFAULT 0,
    TotalRetailPiastres INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (VoucherId) REFERENCES OpeningStockVouchers(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_OpeningStockVouchers_Date ON OpeningStockVouchers(VoucherDate);
CREATE INDEX IF NOT EXISTS IX_OpeningStockVoucherLines_VoucherId ON OpeningStockVoucherLines(VoucherId);
CREATE INDEX IF NOT EXISTS IX_OpeningStockVoucherLines_ItemId ON OpeningStockVoucherLines(ItemId);
