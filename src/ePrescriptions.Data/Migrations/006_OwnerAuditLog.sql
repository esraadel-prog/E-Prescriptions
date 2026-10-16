-- 006_OwnerAuditLog.sql: سجل نشاط صاحب الصيدلية (مرئي لحسابه فقط عبر طبقة التطبيق)
-- هذا الجدول لا يُستعلم منه في أي شاشة غير شاشة صاحب الصيدلية؛ القيد الحقيقي في طبقة الـ Repository/ViewModel.

CREATE TABLE IF NOT EXISTS OwnerAuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId INTEGER NOT NULL,
    EntityType TEXT NOT NULL,      -- مثال: Item, Customer, PharmacySettings
    EntityId TEXT NOT NULL,        -- معرف السجل المعدَّل (كنص لمرونة الأنواع المختلفة)
    EntityLabel TEXT NOT NULL,     -- وصف مقروء للسجل (اسم الصنف مثلاً) وقت التعديل
    FieldName TEXT NOT NULL,       -- اسم الحقل المعدَّل
    OldValue TEXT NULL,
    NewValue TEXT NULL,
    ChangedAt TEXT NOT NULL,
    Note TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_OwnerAuditLog_ChangedAt ON OwnerAuditLog(ChangedAt);
CREATE INDEX IF NOT EXISTS IX_OwnerAuditLog_Entity ON OwnerAuditLog(EntityType, EntityId);
