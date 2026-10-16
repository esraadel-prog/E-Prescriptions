-- 001_InitialSchema.sql: الهيكل الكامل لقاعدة بيانات صيدلية e_prescriptions
-- جميع القيم المالية مخزنة كأعداد صحيحة (INTEGER) تمثل الهللات.

CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL UNIQUE,
    FullName TEXT NOT NULL,
    PasswordHash TEXT NOT NULL,
    Role INTEGER NOT NULL DEFAULT 2, -- 1: Admin, 2: Pharmacist, 3: Cashier
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Shifts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    UserName TEXT NOT NULL,
    StartTime TEXT NOT NULL,
    EndTime TEXT NULL,
    OpeningBalanceHalalas INTEGER NOT NULL DEFAULT 0,
    TotalSalesCashHalalas INTEGER NOT NULL DEFAULT 0,
    TotalSalesNetworkHalalas INTEGER NOT NULL DEFAULT 0,
    TotalSalesOnAccountHalalas INTEGER NOT NULL DEFAULT 0,
    TotalReturnsCashHalalas INTEGER NOT NULL DEFAULT 0,
    ExpectedCashHalalas INTEGER NOT NULL DEFAULT 0,
    ActualCashHalalas INTEGER NULL,
    DifferenceHalalas INTEGER NULL,
    Status INTEGER NOT NULL DEFAULT 1, -- 1: Open, 2: Closed
    Notes TEXT NULL,
    FOREIGN KEY(UserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS Customers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Phone TEXT NOT NULL,
    NationalId TEXT NULL,
    CreditLimitHalalas INTEGER NOT NULL DEFAULT 0,
    CurrentBalanceHalalas INTEGER NOT NULL DEFAULT 0,
    PriceCategory INTEGER NOT NULL DEFAULT 1, -- 1: Retail, 2: Wholesale
    Notes TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Suppliers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Phone TEXT NOT NULL,
    TaxNumber TEXT NULL,
    Address TEXT NULL,
    BalanceHalalas INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Items (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL UNIQUE,
    Barcode TEXT NOT NULL UNIQUE,
    NameAr TEXT NOT NULL,
    NameEn TEXT NOT NULL,
    ActiveIngredient TEXT NOT NULL,
    StripsPerBox INTEGER NOT NULL DEFAULT 1,
    PiecesPerStrip INTEGER NOT NULL DEFAULT 10,
    RetailPriceBoxHalalas INTEGER NOT NULL DEFAULT 0,
    WholesalePriceBoxHalalas INTEGER NOT NULL DEFAULT 0,
    CostPriceBoxHalalas INTEGER NOT NULL DEFAULT 0,
    MinStockAlertSmallestUnits INTEGER NOT NULL DEFAULT 30,
    IsPrescriptionRequired INTEGER NOT NULL DEFAULT 0,
    IsVatExempt INTEGER NOT NULL DEFAULT 0,
    TotalStockSmallestUnits INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ItemBatches (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL,
    BatchNumber TEXT NOT NULL,
    ExpiryDate TEXT NOT NULL,
    RemainingSmallestUnits INTEGER NOT NULL DEFAULT 0,
    CostPricePerSmallestUnitHalalas INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(ItemId) REFERENCES Items(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Invoices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNumber TEXT NOT NULL UNIQUE,
    Type INTEGER NOT NULL DEFAULT 1, -- 1: Sales, 2: Return
    Date TEXT NOT NULL,
    CustomerId INTEGER NULL,
    CustomerName TEXT NOT NULL,
    CustomerPhone TEXT NOT NULL,
    PriceCategory INTEGER NOT NULL DEFAULT 1,
    PaymentMethod INTEGER NOT NULL DEFAULT 1,
    PrescriptionNumber TEXT NULL,
    DoctorName TEXT NULL,
    UserId INTEGER NOT NULL,
    UserName TEXT NOT NULL,
    ShiftId INTEGER NOT NULL,
    SubtotalHalalas INTEGER NOT NULL DEFAULT 0,
    DiscountHalalas INTEGER NOT NULL DEFAULT 0,
    VatHalalas INTEGER NOT NULL DEFAULT 0,
    TotalHalalas INTEGER NOT NULL DEFAULT 0,
    PaidHalalas INTEGER NOT NULL DEFAULT 0,
    RemainingHalalas INTEGER NOT NULL DEFAULT 0,
    Notes TEXT NULL,
    ZatcaQrBase64 TEXT NULL,
    FOREIGN KEY(CustomerId) REFERENCES Customers(Id),
    FOREIGN KEY(UserId) REFERENCES Users(Id),
    FOREIGN KEY(ShiftId) REFERENCES Shifts(Id)
);

CREATE TABLE IF NOT EXISTS InvoiceItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL,
    ItemId INTEGER NOT NULL,
    ItemCode TEXT NOT NULL,
    ItemNameAr TEXT NOT NULL,
    BatchId INTEGER NULL,
    BatchNumber TEXT NOT NULL,
    ExpiryDate TEXT NULL,
    UnitLevel INTEGER NOT NULL DEFAULT 1,
    QuantitySold INTEGER NOT NULL DEFAULT 1,
    SmallestUnitsCount INTEGER NOT NULL DEFAULT 1,
    UnitPriceHalalas INTEGER NOT NULL DEFAULT 0,
    DiscountHalalas INTEGER NOT NULL DEFAULT 0,
    VatRatePercent INTEGER NOT NULL DEFAULT 15,
    VatHalalas INTEGER NOT NULL DEFAULT 0,
    TotalHalalas INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(InvoiceId) REFERENCES Invoices(Id) ON DELETE CASCADE,
    FOREIGN KEY(ItemId) REFERENCES Items(Id),
    FOREIGN KEY(BatchId) REFERENCES ItemBatches(Id)
);

CREATE TABLE IF NOT EXISTS SuspendedInvoices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReferenceCode TEXT NOT NULL UNIQUE,
    SuspendedAt TEXT NOT NULL,
    CustomerName TEXT NOT NULL,
    JsonData TEXT NOT NULL,
    TotalHalalas INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS PharmacySettings (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    PharmacyNameAr TEXT NOT NULL,
    PharmacyNameEn TEXT NOT NULL,
    TaxNumber TEXT NOT NULL,
    CommercialRegister TEXT NOT NULL,
    LicenseNumber TEXT NOT NULL,
    Phone TEXT NOT NULL,
    Address TEXT NOT NULL,
    DefaultVatPercent INTEGER NOT NULL DEFAULT 15,
    ReceiptPrinterName TEXT NOT NULL DEFAULT '',
    A4PrinterName TEXT NOT NULL DEFAULT '',
    LabelPrinterName TEXT NOT NULL DEFAULT '',
    ReceiptPaperWidthMm INTEGER NOT NULL DEFAULT 80,
    LabelWidthMm INTEGER NOT NULL DEFAULT 50,
    LabelHeightMm INTEGER NOT NULL DEFAULT 25,
    EnableCashDrawerOnPrint INTEGER NOT NULL DEFAULT 1,
    CashDrawerEscPosBytes TEXT NOT NULL DEFAULT '27,112,0,25,250',
    BarcodeScanThresholdMs INTEGER NOT NULL DEFAULT 45
);

-- فهارس لتحسين سرعة البحث اللحظي
CREATE INDEX IF NOT EXISTS IX_Items_Code ON Items(Code);
CREATE INDEX IF NOT EXISTS IX_Items_Barcode ON Items(Barcode);
CREATE INDEX IF NOT EXISTS IX_Items_NameAr ON Items(NameAr);
CREATE INDEX IF NOT EXISTS IX_Items_ActiveIngredient ON Items(ActiveIngredient);
CREATE INDEX IF NOT EXISTS IX_ItemBatches_ItemId_Expiry ON ItemBatches(ItemId, ExpiryDate);
CREATE INDEX IF NOT EXISTS IX_Invoices_Date ON Invoices(Date);
CREATE INDEX IF NOT EXISTS IX_Invoices_Number ON Invoices(InvoiceNumber);
