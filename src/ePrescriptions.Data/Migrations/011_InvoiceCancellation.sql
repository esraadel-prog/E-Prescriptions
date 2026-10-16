-- 011_InvoiceCancellation.sql: إلغاء الفاتورة (Status: 1 = مكتملة، 3 = ملغاة) مع بيانات من ألغاها ولماذا
ALTER TABLE Invoices ADD COLUMN Status INTEGER NOT NULL DEFAULT 1;
ALTER TABLE Invoices ADD COLUMN CancelledAt TEXT NULL;
ALTER TABLE Invoices ADD COLUMN CancelledByUserId INTEGER NULL;
ALTER TABLE Invoices ADD COLUMN CancelledByName TEXT NULL;
ALTER TABLE Invoices ADD COLUMN CancelReason TEXT NULL;
