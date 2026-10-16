using System;
using ePrescriptions.Core.Helpers;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace ePrescriptions.Data.Repositories
{
    public class StaffAuditLogEntry
    {
        public int Id { get; set; }
        public int ActorUserId { get; set; }
        public string ActorUserName { get; set; } = string.Empty;
        public string ActorRole { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string EntityLabel { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>
    /// سجل تدقيق عام لتعديلات المدير والصيدلي والكاشير. يظهر فقط في شاشة مقصورة على دوري
    /// Admin وOwner. هذا الجدول منفصل تماماً عن OwnerAuditLog؛ لا يُستدعى أبداً لتسجيل تعديلات
    /// صاحب الصيدلية نفسه (سجله الخاص يبقى مخفياً عن الجميع بما فيهم المدير).
    /// </summary>
    public class StaffAuditLogRepository
    {
        private readonly DatabaseContext _dbContext;

        public StaffAuditLogRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public void LogChange(int actorUserId, string actorUserName, string actorRole, string entityType, string entityId, string entityLabel, string fieldName, string? oldValue, string? newValue)
        {
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;

            string changedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

            using var conn = _dbContext.CreateConnection();
            // الإدخال + ختم الـ hash في Transaction واحدة (أول INSERT بياخد قفل الكتابة)
            using var trans = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = @"
                INSERT INTO StaffAuditLog (ActorUserId, ActorUserName, ActorRole, EntityType, EntityId, EntityLabel, FieldName, OldValue, NewValue, ChangedAt)
                VALUES (@uid, @uname, @role, @etype, @eid, @elabel, @field, @old, @new, @at);";
                cmd.Parameters.AddWithValue("@uid", actorUserId);
                cmd.Parameters.AddWithValue("@uname", actorUserName);
                cmd.Parameters.AddWithValue("@role", actorRole);
                cmd.Parameters.AddWithValue("@etype", entityType);
                cmd.Parameters.AddWithValue("@eid", entityId);
                cmd.Parameters.AddWithValue("@elabel", entityLabel);
                cmd.Parameters.AddWithValue("@field", fieldName);
                cmd.Parameters.AddWithValue("@old", (object?)oldValue ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@new", (object?)newValue ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@at", changedAt);
                cmd.ExecuteNonQuery();
            }

            long id;
            using (var idCmd = conn.CreateCommand())
            {
                idCmd.Transaction = trans;
                idCmd.CommandText = "SELECT last_insert_rowid();";
                id = Convert.ToInt64(idCmd.ExecuteScalar());
            }

            AuditHashChain.Seal(conn, trans, "StaffAuditLog", ChainTag, id, new string?[]
            {
                actorUserId.ToString(CultureInfo.InvariantCulture), actorUserName, actorRole, entityType, entityId, entityLabel, fieldName, oldValue, newValue, changedAt
            });
            trans.Commit();
        }

        /// <summary>اسم هذا السجل داخل الـ hash (يمنع نقل سطر من سلسلة لأخرى)</summary>
        private const string ChainTag = "StaffAuditLog";
        private static readonly string[] ChainColumns = { "ActorUserId", "ActorUserName", "ActorRole", "EntityType", "EntityId", "EntityLabel", "FieldName", "OldValue", "NewValue", "ChangedAt" };

        /// <summary>فحص سلامة سلسلة hash لهذا السجل (يكشف التعديل/الحذف اليدوي في القاعدة)</summary>
        public AuditChainVerification VerifyIntegrity()
        {
            using var conn = _dbContext.CreateConnection();
            return AuditHashChain.Verify(conn, "StaffAuditLog", ChainTag, ChainColumns);
        }

        /// <summary>
        /// أحدث التعديلات (الأحدث أولاً). يُستدعى فقط من شاشة مقصورة على Admin/Owner.
        /// </summary>
        public List<StaffAuditLogEntry> GetRecent(int limit = 300)
        {
            var list = new List<StaffAuditLogEntry>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM StaffAuditLog ORDER BY Id DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new StaffAuditLogEntry
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    ActorUserId = Convert.ToInt32(reader["ActorUserId"]),
                    ActorUserName = reader["ActorUserName"]?.ToString() ?? string.Empty,
                    ActorRole = reader["ActorRole"]?.ToString() ?? string.Empty,
                    EntityType = reader["EntityType"]?.ToString() ?? string.Empty,
                    EntityId = reader["EntityId"]?.ToString() ?? string.Empty,
                    EntityLabel = reader["EntityLabel"]?.ToString() ?? string.Empty,
                    FieldName = reader["FieldName"]?.ToString() ?? string.Empty,
                    OldValue = reader["OldValue"] is DBNull ? null : reader["OldValue"].ToString(),
                    NewValue = reader["NewValue"] is DBNull ? null : reader["NewValue"].ToString(),
                    ChangedAt = DbDate.Parse(reader["ChangedAt"]?.ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture))
                });
            }
            return list;
        }
    }
}
