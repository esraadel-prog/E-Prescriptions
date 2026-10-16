-- 014_AuditHashChain.sql: سلسلة hash لسجلي التدقيق (خطوة 19)
-- RowHash = SHA256(وسم الجدول + Id + محتوى السطر + PrevHash)، وPrevHash = RowHash للسطر السابق.
-- السطور الموجودة قبل الترقية تبقى بدون hash (بادئة غير محمية)، والسلسلة تبدأ من أول سطر جديد.
-- كل جدول له سلسلته المستقلة تماماً (OwnerAuditLog منفصل عن StaffAuditLog).
ALTER TABLE OwnerAuditLog ADD COLUMN PrevHash TEXT NULL;
ALTER TABLE OwnerAuditLog ADD COLUMN RowHash TEXT NULL;
ALTER TABLE StaffAuditLog ADD COLUMN PrevHash TEXT NULL;
ALTER TABLE StaffAuditLog ADD COLUMN RowHash TEXT NULL;
