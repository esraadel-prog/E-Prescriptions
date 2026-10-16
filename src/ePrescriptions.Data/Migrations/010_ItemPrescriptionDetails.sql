-- 010_ItemPrescriptionDetails.sql: رقم الوصفة واسم الطبيب على مستوى بند الفاتورة (للأصناف التي تتطلب وصفة)
ALTER TABLE InvoiceItems ADD COLUMN PrescriptionNumber TEXT NULL;
ALTER TABLE InvoiceItems ADD COLUMN DoctorName TEXT NULL;
