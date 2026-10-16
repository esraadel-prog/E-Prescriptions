using System;
using System.Data.SQLite;

namespace ePrescriptions.Data.Repositories
{
    /// <summary>
    /// ترقيم المستندات بعدّاد داخل جدول Counters. التخصيص (Next) بيتم داخل نفس Transaction حفظ المستند وبياخد قفل الكتابة
    /// من أول سطر، فمفيش مستندين (حتى من جهازين/خيطين) يقدروا ياخدوا نفس الرقم. لو العدّاد مش موجود بيبدأ من أكبر رقم موجود فعلاً.
    /// </summary>
    public static class NumberSequence
    {
        /// <summary>المفتاح مثل INV-2026، والرقم النهائي INV-2026-00001</summary>
        public static string Format(string key, long value) => $"{key}-{value:D5}";

        private static long SeedFromTable(SQLiteConnection conn, SQLiteTransaction? trans, string key, string table, string column)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            // table/column ثوابت داخلية من الكود (مش مدخلات مستخدم)
            cmd.CommandText = $"SELECT MAX(CAST(substr({column}, @len) AS INTEGER)) FROM {table} WHERE {column} LIKE @pat;";
            cmd.Parameters.AddWithValue("@len", key.Length + 2);
            cmd.Parameters.AddWithValue("@pat", key + "-%");
            object? v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? 0 : Convert.ToInt64(v);
        }

        /// <summary>الرقم التالي المتوقع بدون حجزه (للعرض في الشاشة فقط)</summary>
        public static long Peek(SQLiteConnection conn, string key, string table, string column)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Value FROM Counters WHERE Name = @k;";
                cmd.Parameters.AddWithValue("@k", key);
                object? v = cmd.ExecuteScalar();
                if (v != null && v != DBNull.Value) return Convert.ToInt64(v) + 1;
            }
            return SeedFromTable(conn, null, key, table, column) + 1;
        }

        /// <summary>حجز الرقم التالي داخل Transaction المستند</summary>
        public static long Next(SQLiteConnection conn, SQLiteTransaction trans, string key, string table, string column)
        {
            // 1) أخذ قفل الكتابة فوراً (قبل أي قراءة) لتسلسل المستندات المتزامنة
            using (var lockCmd = conn.CreateCommand())
            {
                lockCmd.Transaction = trans;
                lockCmd.CommandText = "UPDATE Counters SET Value = Value WHERE Name = @k;";
                lockCmd.Parameters.AddWithValue("@k", key);
                lockCmd.ExecuteNonQuery();
            }

            // 2) أول مرة: ابدأ من أكبر رقم موجود فعلاً (ترقية من الترقيم القديم)
            long seed = SeedFromTable(conn, trans, key, table, column);
            using (var initCmd = conn.CreateCommand())
            {
                initCmd.Transaction = trans;
                initCmd.CommandText = "INSERT OR IGNORE INTO Counters (Name, Value) VALUES (@k, @seed);";
                initCmd.Parameters.AddWithValue("@k", key);
                initCmd.Parameters.AddWithValue("@seed", seed);
                initCmd.ExecuteNonQuery();
            }

            // 3) زيادة العدّاد وقراءته
            using (var incCmd = conn.CreateCommand())
            {
                incCmd.Transaction = trans;
                incCmd.CommandText = "UPDATE Counters SET Value = MAX(Value, @seed) + 1 WHERE Name = @k;";
                incCmd.Parameters.AddWithValue("@k", key);
                incCmd.Parameters.AddWithValue("@seed", seed);
                incCmd.ExecuteNonQuery();
            }
            using var readCmd = conn.CreateCommand();
            readCmd.Transaction = trans;
            readCmd.CommandText = "SELECT Value FROM Counters WHERE Name = @k;";
            readCmd.Parameters.AddWithValue("@k", key);
            return Convert.ToInt64(readCmd.ExecuteScalar());
        }
    }
}
