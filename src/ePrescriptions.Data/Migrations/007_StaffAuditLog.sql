-- 007_StaffAuditLog.sql: سجل تدقيق عام لتعديلات المدير والصيدلي والكاشير
-- يظهر فقط لدوري Admin وOwner. منفصل تماماً عن OwnerAuditLog (سجل صاحب الصيدلية الخاص يبقى مخفياً عن الجميع بما فيهم المدير).

CREATE TABLE IF NOT EXISTS StaffAuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ActorUserId INTEGER NOT NULL,
    ActorUserName TEXT NOT NULL,
    ActorRole TEXT NOT NULL,
    EntityType TEXT NOT NULL,
    EntityId TEXT NOT NULL,
    EntityLabel TEXT NOT NULL,
    FieldName TEXT NOT NULL,
    OldValue TEXT NULL,
    NewValue TEXT NULL,
    ChangedAt TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_StaffAuditLog_ChangedAt ON StaffAuditLog(ChangedAt);
CREATE INDEX IF NOT EXISTS IX_StaffAuditLog_Entity ON StaffAuditLog(EntityType, EntityId);
