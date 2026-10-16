-- 005_EgyptLocalization.sql: التوطين المصري وضبط نسب الضريبة وبيانات الصيدلية المصرية
-- يضمن التوافق التام مع قواعد البيانات السابقة (Upgraded) وقواعد البيانات الجديدة

-- 1) بيانات وإعدادات التوطين المصري لجدول PharmacySettings
ALTER TABLE PharmacySettings ADD COLUMN EnableQrCode INTEGER NOT NULL DEFAULT 0;
ALTER TABLE PharmacySettings ADD COLUMN ResponsiblePharmacist TEXT NOT NULL DEFAULT '';
ALTER TABLE PharmacySettings ADD COLUMN TaxNumberLength INTEGER NOT NULL DEFAULT 9;

UPDATE PharmacySettings 
SET DefaultVatPercent = 14 
WHERE DefaultVatPercent = 15;

UPDATE PharmacySettings 
SET TaxNumber = '123456789' 
WHERE LENGTH(TaxNumber) > 9;

UPDATE PharmacySettings 
SET PharmacyNameAr = 'الصيدلية' 
WHERE PharmacyNameAr = 'صيدلية النخبة الطبية';

UPDATE PharmacySettings 
SET Address = 'جمهورية مصر العربية - القاهرة';

-- 2) نسبة الضريبة المخصصة لكل صنف
ALTER TABLE Items ADD COLUMN CustomVatPercent INTEGER NOT NULL DEFAULT -1;

UPDATE Items 
SET CustomVatPercent = 0 
WHERE Code IN ('MED-001', 'MED-002', 'MED-003', 'MED-004', 'MED-005');

-- 3) تحديث الموردين والعملاء لتطابق البيانات المصرية
UPDATE Suppliers 
SET TaxNumber = '100200300', Address = 'جمهورية مصر العربية - القاهرة' 
WHERE Id = 1;

UPDATE Customers 
SET Phone = '01000000000' 
WHERE Id = 1;

UPDATE Customers 
SET Phone = '01122334455', Notes = 'حساب آجل بحد ائتماني 5000 جنيه' 
WHERE Id = 2;

-- 4) إضافة حقل رمز الاستجابة السريع البسيط للفواتير
ALTER TABLE Invoices ADD COLUMN QrCodeText TEXT NULL;

UPDATE Invoices 
SET QrCodeText = ZatcaQrBase64 
WHERE QrCodeText IS NULL AND ZatcaQrBase64 IS NOT NULL;

-- 5) إعادة تسمية الحقول في الجداول من Halalas إلى Piastres لمطابقة العملة الرسمية (القرش)
ALTER TABLE Invoices RENAME COLUMN SubtotalHalalas TO SubtotalPiastres;
ALTER TABLE Invoices RENAME COLUMN DiscountHalalas TO DiscountPiastres;
ALTER TABLE Invoices RENAME COLUMN VatHalalas TO VatPiastres;
ALTER TABLE Invoices RENAME COLUMN TotalHalalas TO TotalPiastres;
ALTER TABLE Invoices RENAME COLUMN PaidHalalas TO PaidPiastres;
ALTER TABLE Invoices RENAME COLUMN RemainingHalalas TO RemainingPiastres;

ALTER TABLE InvoiceItems RENAME COLUMN UnitPriceHalalas TO UnitPricePiastres;
ALTER TABLE InvoiceItems RENAME COLUMN DiscountHalalas TO DiscountPiastres;
ALTER TABLE InvoiceItems RENAME COLUMN VatHalalas TO VatPiastres;
ALTER TABLE InvoiceItems RENAME COLUMN TotalHalalas TO TotalPiastres;

ALTER TABLE SuspendedInvoices RENAME COLUMN TotalHalalas TO TotalPiastres;
ALTER TABLE Customers RENAME COLUMN CreditLimitHalalas TO CreditLimitPiastres;
ALTER TABLE Customers RENAME COLUMN CurrentBalanceHalalas TO CurrentBalancePiastres;
ALTER TABLE Suppliers RENAME COLUMN BalanceHalalas TO BalancePiastres;

ALTER TABLE Shifts RENAME COLUMN OpeningBalanceHalalas TO OpeningBalancePiastres;
ALTER TABLE Shifts RENAME COLUMN TotalSalesCashHalalas TO TotalSalesCashPiastres;
ALTER TABLE Shifts RENAME COLUMN TotalSalesNetworkHalalas TO TotalSalesNetworkPiastres;
ALTER TABLE Shifts RENAME COLUMN TotalSalesOnAccountHalalas TO TotalSalesOnAccountPiastres;
ALTER TABLE Shifts RENAME COLUMN TotalReturnsCashHalalas TO TotalReturnsCashPiastres;
ALTER TABLE Shifts RENAME COLUMN ExpectedCashHalalas TO ExpectedCashPiastres;
ALTER TABLE Shifts RENAME COLUMN ActualCashHalalas TO ActualCashPiastres;
ALTER TABLE Shifts RENAME COLUMN DifferenceHalalas TO DifferencePiastres;

ALTER TABLE Items RENAME COLUMN RetailPriceBoxHalalas TO RetailPriceBoxPiastres;
ALTER TABLE Items RENAME COLUMN WholesalePriceBoxHalalas TO WholesalePriceBoxPiastres;
ALTER TABLE Items RENAME COLUMN CostPriceBoxHalalas TO CostPriceBoxPiastres;

ALTER TABLE ItemBatches RENAME COLUMN CostPricePerSmallestUnitHalalas TO CostPricePerSmallestUnitPiastres;

ALTER TABLE PurchaseInvoices RENAME COLUMN SubtotalHalalas TO SubtotalPiastres;
ALTER TABLE PurchaseInvoices RENAME COLUMN VatHalalas TO VatPiastres;
ALTER TABLE PurchaseInvoices RENAME COLUMN TotalHalalas TO TotalPiastres;
ALTER TABLE PurchaseInvoices RENAME COLUMN PaidHalalas TO PaidPiastres;
ALTER TABLE PurchaseInvoices RENAME COLUMN RemainingHalalas TO RemainingPiastres;

ALTER TABLE PurchaseInvoiceItems RENAME COLUMN UnitCostHalalas TO UnitCostPiastres;
ALTER TABLE PurchaseInvoiceItems RENAME COLUMN VatHalalas TO VatPiastres;
ALTER TABLE PurchaseInvoiceItems RENAME COLUMN TotalHalalas TO TotalPiastres;

ALTER TABLE CustomerVouchers RENAME COLUMN AmountHalalas TO AmountPiastres;
ALTER TABLE SupplierVouchers RENAME COLUMN AmountHalalas TO AmountPiastres;
ALTER TABLE Expenses RENAME COLUMN AmountHalalas TO AmountPiastres;
ALTER TABLE Incomes RENAME COLUMN AmountHalalas TO AmountPiastres;
ALTER TABLE JournalEntries RENAME COLUMN AmountHalalas TO AmountPiastres;

