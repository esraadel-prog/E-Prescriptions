using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ePrescriptions.Data.Repositories
{
    /// <summary>نتيجة فحص سلسلة hash لسجل تدقيق</summary>
    public class AuditChainVerification
    {
        /// <summary>true = السلسلة سليمة (ما فيش تعديل/حذف/إدخال يدوي مكتشف)</summary>
        public bool IsValid { get; set; } = true;
        /// <summary>عدد السطور المحمية بـ hash اللي اتفحصت</summary>
        public int CheckedCount { get; set; }
        /// <summary>سطور قديمة (قبل ترقية 014) بدون hash: مش محمية، لكنها مش اعتبرت كسر</summary>
        public int LegacyUnprotectedCount { get; set; }
        /// <summary>رقم أول سطر اتكشف عنده الكسر (لو في كسر)</summary>
        public int? FirstBrokenId { get; set; }
        public string Message { get; set; } = "السلسلة سليمة.";
    }

    /// <summary>
    /// سلسلة hash لسجلات التدقيق: كل سطر فيه hash لمحتواه + hash السطر السابق، فأي تعديل يدوي لسطر أو حذف سطر
    /// من النص أو إدخال سطر مزوّر أو فك hash يكسر السلسلة ويكتشفه <see cref="Verify"/>.
    /// حدود معروفة (موثّقة في FIXES.md): لا تكشف حذف آخر السطور (ذيل السلسلة) ولا إعادة بناء السلسلة كلها
    /// بمن يعرف الخوارزمية (القاعدة غير مشفّرة)؛ الحل الأقوى (SQLCipher / مرساة خارجية) خارج نطاق هذه الخطوة.
    /// </summary>
    public static class AuditHashChain
    {
        /// <summary>PrevHash لأول سطر في السلسلة</summary>
        public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

        /// <summary>
        /// hash السطر. الحقول بتتكتب بطول+قيمة (فمفيش التباس بين "ab","c" و"a","bc")، والـ null له علامة مختلفة عن النص الفاضي.
        /// </summary>
        public static string ComputeHash(string tableTag, long id, string prevHash, IList<string?> fields)
        {
            var sb = new StringBuilder();
            sb.Append(tableTag).Append('\n').Append(id.ToString(CultureInfo.InvariantCulture)).Append('\n').Append(prevHash).Append('\n');
            foreach (var f in fields)
            {
                if (f == null) sb.Append("~|");
                else sb.Append(f.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(f).Append('|');
            }
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }

        /// <summary>
        /// يختم السطر اللي لسه اتضاف (نفس الـ Transaction بتاعة الإدخال، وبالتالي قفل الكتابة معانا):
        /// بيجيب hash السطر اللي قبله مباشرة (أو Genesis لو أول سطر/اللي قبله قديم بدون hash) ويكتب PrevHash وRowHash.
        /// </summary>
        public static void Seal(SQLiteConnection conn, SQLiteTransaction trans, string table, string tableTag, long id, IList<string?> fields)
        {
            string prev = GenesisHash;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = trans;
                // table اسم ثابت من الكود (مش مدخل مستخدم)
                cmd.CommandText = $"SELECT RowHash FROM {table} WHERE Id = (SELECT MAX(Id) FROM {table} WHERE Id < @id);";
                cmd.Parameters.AddWithValue("@id", id);
                object? v = cmd.ExecuteScalar();
                if (v != null && v != DBNull.Value && !string.IsNullOrEmpty(v.ToString())) prev = v.ToString()!;
            }

            string rowHash = ComputeHash(tableTag, id, prev, fields);
            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = trans;
                upd.CommandText = $"UPDATE {table} SET PrevHash = @p, RowHash = @h WHERE Id = @id;";
                upd.Parameters.AddWithValue("@p", prev);
                upd.Parameters.AddWithValue("@h", rowHash);
                upd.Parameters.AddWithValue("@id", id);
                upd.ExecuteNonQuery();
            }
        }

        /// <summary>فحص السلسلة كاملة بالترتيب. columns = أعمدة المحتوى بنفس ترتيب الكتابة في Seal.</summary>
        public static AuditChainVerification Verify(SQLiteConnection conn, string table, string tableTag, string[] columns)
        {
            var result = new AuditChainVerification();
            string expectedPrev = GenesisHash;
            bool seenChained = false;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT Id, PrevHash, RowHash, {string.Join(", ", columns)} FROM {table} ORDER BY Id ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture);
                string? prevStored = reader["PrevHash"] is DBNull ? null : reader["PrevHash"].ToString();
                string? rowHash = reader["RowHash"] is DBNull ? null : reader["RowHash"].ToString();

                if (string.IsNullOrEmpty(rowHash))
                {
                    if (seenChained) return Broken(result, id, "سطر بدون hash بعد بداية السلسلة (اتشال hash أو اتدخل سطر يدوياً).");
                    result.LegacyUnprotectedCount++;
                    continue;
                }

                seenChained = true;
                if (prevStored != expectedPrev)
                    return Broken(result, id, "السلسلة مقطوعة قبل هذا السطر (حذف سطر أو إدخال سطر أو تغيير ترتيب).");

                var fields = new List<string?>(columns.Length);
                foreach (string col in columns)
                {
                    object val = reader[col];
                    fields.Add(val is DBNull ? null : Convert.ToString(val, CultureInfo.InvariantCulture));
                }

                if (ComputeHash(tableTag, id, prevStored!, fields) != rowHash)
                    return Broken(result, id, "محتوى هذا السطر اتغيّر يدوياً بعد تسجيله.");

                expectedPrev = rowHash!;
                result.CheckedCount++;
            }
            return result;
        }

        private static AuditChainVerification Broken(AuditChainVerification r, int id, string message)
        {
            r.IsValid = false;
            r.FirstBrokenId = id;
            r.Message = $"تم اكتشاف كسر في السجل عند السطر رقم {id}: {message}";
            return r;
        }
    }
}
