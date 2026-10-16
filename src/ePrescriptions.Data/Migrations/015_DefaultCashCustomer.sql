-- 015_DefaultCashCustomer.sql: علامة صريحة للعميل النقدي العام بدل افتراض إنه رقم 1 (خطوة 23)
ALTER TABLE Customers ADD COLUMN IsDefaultCash INTEGER NOT NULL DEFAULT 0;
-- القواعد القائمة: العميل النقدي كان بالاتفاق رقم 1
UPDATE Customers SET IsDefaultCash = 1 WHERE Id = 1;
-- عميل نقدي افتراضي واحد فقط
CREATE UNIQUE INDEX IF NOT EXISTS IX_Customers_DefaultCash ON Customers(IsDefaultCash) WHERE IsDefaultCash = 1;
