-- 019_InvoicePayments.sql: الدفع المقسّم (خطوة 36)
-- الفاتورة المقسّمة بيبقى Invoices.PaymentMethod = 5 (Mixed) وأجزاء الدفع هنا. الفواتير بطريقة واحدة ما بيتكتبلهاش صفوف.
CREATE TABLE IF NOT EXISTS InvoicePayments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL,
    Method INTEGER NOT NULL CHECK (Method IN (1, 2, 3, 4)),
    AmountPiastres INTEGER NOT NULL CHECK (AmountPiastres > 0),
    FOREIGN KEY (InvoiceId) REFERENCES Invoices(Id),
    UNIQUE (InvoiceId, Method)
);
CREATE INDEX IF NOT EXISTS IX_InvoicePayments_InvoiceId ON InvoicePayments(InvoiceId);
