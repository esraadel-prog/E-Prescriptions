-- DemoData.sql: بيانات تجريبية مصرية اختيارية (تُحمَّل بطلب المستخدم من معالج الإعداد الأول)
-- مكتوبة على الهيكل النهائي للقاعدة (أعمدة Piastres) ولا تحتوي أي حسابات مستخدمين.

INSERT OR REPLACE INTO PharmacySettings (
    Id, PharmacyNameAr, PharmacyNameEn, TaxNumber, CommercialRegister, LicenseNumber, Phone, Address, DefaultVatPercent,
    EnableQrCode, ResponsiblePharmacist, TaxNumberLength
) VALUES (
    1, 'صيدلية تجريبية', 'Elite Pharmacy Care', '123456789', '1010776655', 'LIC-2024-889', '0114567890',
    'جمهورية مصر العربية - القاهرة', 14, 0, 'صيدلي تجريبي', 9
);

-- العميل النقدي العام + عميل آجل تجريبي
INSERT OR IGNORE INTO Customers (Id, Name, Phone, CreditLimitPiastres, CurrentBalancePiastres, PriceCategory, Notes, IsActive, IsDefaultCash)
VALUES (1, 'عميل نقدي', '01000000000', 0, 0, 1, 'العميل النقدي العام', 1, 1);

INSERT OR IGNORE INTO Customers (Id, Name, Phone, CreditLimitPiastres, CurrentBalancePiastres, PriceCategory, Notes, IsActive)
VALUES (2, 'مجمع النخبة الطبي (آجل)', '01122334455', 500000, 0, 2, 'حساب آجل بحد ائتماني 5000 جنيه', 1);

-- مورد تجريبي
INSERT OR IGNORE INTO Suppliers (Id, Name, Phone, TaxNumber, Address, BalancePiastres, IsActive)
VALUES (1, 'شركة الدواء للخدمات الطبية', '0225000000', '100200300', 'جمهورية مصر العربية - القاهرة', 0, 1);

-- أدوية تجريبية بأسعار بالقرش (الأدوية معفاة من الضريبة: CustomVatPercent = 0)
INSERT OR IGNORE INTO Items (Id, Code, Barcode, NameAr, NameEn, ActiveIngredient, StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres, WholesalePriceBoxPiastres, CostPriceBoxPiastres, MinStockAlertSmallestUnits, TotalStockSmallestUnits, IsActive, CustomVatPercent)
VALUES (1, 'MED-001', '6281001000101', 'بنادول إكسترا أحمر 24 قرص', 'Panadol Extra 24 Tabs', 'Paracetamol 500mg + Caffeine 65mg', 2, 12, 1200, 1050, 900, 48, 240, 1, 0);
INSERT OR IGNORE INTO ItemBatches (Id, ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits, CostPricePerSmallestUnitPiastres, CreatedAt)
VALUES (1, 1, 'BN-2024-A1', date('now', '+18 months'), 240, 38, datetime('now'));

INSERT OR IGNORE INTO Items (Id, Code, Barcode, NameAr, NameEn, ActiveIngredient, StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres, WholesalePriceBoxPiastres, CostPriceBoxPiastres, MinStockAlertSmallestUnits, TotalStockSmallestUnits, IsActive, CustomVatPercent)
VALUES (2, 'MED-002', '6281001000202', 'أوجمنتين 1 جم 14 قرص', 'Augmentin 1g 14 Tabs', 'Amoxicillin + Clavulanic Acid', 2, 7, 5650, 5000, 4200, 28, 140, 1, 0);
INSERT OR IGNORE INTO ItemBatches (Id, ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits, CostPricePerSmallestUnitPiastres, CreatedAt)
VALUES (2, 2, 'BN-2024-AUG', date('now', '+14 months'), 140, 300, datetime('now'));

INSERT OR IGNORE INTO Items (Id, Code, Barcode, NameAr, NameEn, ActiveIngredient, StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres, WholesalePriceBoxPiastres, CostPriceBoxPiastres, MinStockAlertSmallestUnits, TotalStockSmallestUnits, IsActive, CustomVatPercent)
VALUES (3, 'MED-003', '6281001000303', 'كونكور 5 مجم 30 قرص', 'Concor 5mg 30 Tabs', 'Bisoprolol Fumarate 5mg', 3, 10, 2480, 2200, 1850, 30, 180, 1, 0);
INSERT OR IGNORE INTO ItemBatches (Id, ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits, CostPricePerSmallestUnitPiastres, CreatedAt)
VALUES (3, 3, 'BN-2024-CNC', date('now', '+24 months'), 180, 62, datetime('now'));

INSERT OR IGNORE INTO Items (Id, Code, Barcode, NameAr, NameEn, ActiveIngredient, StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres, WholesalePriceBoxPiastres, CostPriceBoxPiastres, MinStockAlertSmallestUnits, TotalStockSmallestUnits, IsActive, CustomVatPercent)
VALUES (4, 'MED-004', '6281001000404', 'نكسيوم 40 مجم 28 قرص', 'Nexium 40mg 28 Tabs', 'Esomeprazole 40mg', 2, 14, 8800, 8000, 6800, 28, 112, 1, 0);
INSERT OR IGNORE INTO ItemBatches (Id, ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits, CostPricePerSmallestUnitPiastres, CreatedAt)
VALUES (4, 4, 'BN-2024-NX4', date('now', '+10 months'), 112, 243, datetime('now'));

-- دفعة قريبة الصلاحية لاختبار تنبيهات FEFO
INSERT OR IGNORE INTO Items (Id, Code, Barcode, NameAr, NameEn, ActiveIngredient, StripsPerBox, PiecesPerStrip, RetailPriceBoxPiastres, WholesalePriceBoxPiastres, CostPriceBoxPiastres, MinStockAlertSmallestUnits, TotalStockSmallestUnits, IsActive, CustomVatPercent)
VALUES (5, 'MED-005', '6281001000505', 'كتافلام 50 مجم 20 قرص', 'Cataflam 50mg 20 Tabs', 'Diclofenac Potassium 50mg', 2, 10, 2150, 1900, 1600, 20, 80, 1, 0);
INSERT OR IGNORE INTO ItemBatches (Id, ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits, CostPricePerSmallestUnitPiastres, CreatedAt)
VALUES (5, 5, 'BN-2024-CTF-NEAR', date('now', '+45 days'), 40, 80, datetime('now'));
INSERT OR IGNORE INTO ItemBatches (Id, ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits, CostPricePerSmallestUnitPiastres, CreatedAt)
VALUES (6, 5, 'BN-2024-CTF-FAR', date('now', '+16 months'), 40, 80, datetime('now'));
