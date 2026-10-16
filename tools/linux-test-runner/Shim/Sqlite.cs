// بديل خفيف لـ System.Data.SQLite فوق libsqlite3 (للمشغّل المؤقت على Linux فقط)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace System.Data.SQLite
{
    internal static class N
    {
        const string L = "libsqlite3.so.0";
        [DllImport(L)] public static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(L)] public static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(L)] public static extern int sqlite3_prepare_v2(IntPtr db, IntPtr sql, int n, out IntPtr stmt, out IntPtr tail);
        [DllImport(L)] public static extern int sqlite3_step(IntPtr s);
        [DllImport(L)] public static extern int sqlite3_finalize(IntPtr s);
        [DllImport(L)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(L)] public static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(L)] public static extern int sqlite3_bind_parameter_index(IntPtr s, byte[] name);
        [DllImport(L)] public static extern int sqlite3_bind_null(IntPtr s, int i);
        [DllImport(L)] public static extern int sqlite3_bind_int64(IntPtr s, int i, long v);
        [DllImport(L)] public static extern int sqlite3_bind_double(IntPtr s, int i, double v);
        [DllImport(L)] public static extern int sqlite3_bind_text(IntPtr s, int i, byte[] v, int n, IntPtr d);
        [DllImport(L)] public static extern int sqlite3_bind_blob(IntPtr s, int i, byte[] v, int n, IntPtr d);
        [DllImport(L)] public static extern int sqlite3_column_count(IntPtr s);
        [DllImport(L)] public static extern int sqlite3_column_type(IntPtr s, int i);
        [DllImport(L)] public static extern long sqlite3_column_int64(IntPtr s, int i);
        [DllImport(L)] public static extern double sqlite3_column_double(IntPtr s, int i);
        [DllImport(L)] public static extern IntPtr sqlite3_column_text(IntPtr s, int i);
        [DllImport(L)] public static extern IntPtr sqlite3_column_blob(IntPtr s, int i);
        [DllImport(L)] public static extern int sqlite3_column_bytes(IntPtr s, int i);
        [DllImport(L)] public static extern IntPtr sqlite3_column_name(IntPtr s, int i);
        [DllImport(L)] public static extern long sqlite3_last_insert_rowid(IntPtr db);
        public static byte[] Z(string s) { var b = Encoding.UTF8.GetBytes(s); var r = new byte[b.Length + 1]; Array.Copy(b, r, b.Length); return r; }
        public static string S(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
    }

    public class SQLiteException : Exception { public SQLiteException(string m) : base(m) { } }

    public class SQLiteParameter { public string Name = ""; public object? Value; }

    public class SQLiteParameterCollection
    {
        internal List<SQLiteParameter> Items = new();
        public SQLiteParameter AddWithValue(string name, object? value) { var p = new SQLiteParameter { Name = name, Value = value }; Items.Add(p); return p; }
        public void Add(SQLiteParameter p) => Items.Add(p);
        public void Clear() => Items.Clear();
    }

    public class SQLiteTransaction : IDisposable
    {
        internal SQLiteConnection Conn; bool done;
        internal SQLiteTransaction(SQLiteConnection c) { Conn = c; Conn.Exec("BEGIN"); }
        public void Commit() { if (!done) { Conn.Exec("COMMIT"); done = true; } }
        public void Rollback() { if (!done) { Conn.Exec("ROLLBACK"); done = true; } }
        public void Dispose() { if (!done) { try { Conn.Exec("ROLLBACK"); } catch { } done = true; } }
    }

    public class SQLiteConnection : IDisposable
    {
        internal IntPtr Db;
        readonly string _cs;
        public SQLiteConnection(string cs) { _cs = cs; }
        public static void ClearAllPools() { }
        public long LastInsertRowId => N.sqlite3_last_insert_rowid(Db);
        public void Open()
        {
            string? path = null; bool ro = false; int busyMs = 0;
            foreach (var part in _cs.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2); if (kv.Length != 2) continue;
                var k = kv[0].Trim().ToLowerInvariant(); var v = kv[1].Trim();
                if (k == "data source") path = v;
                if (k == "busytimeout") int.TryParse(v, out busyMs);
                if (k == "read only") ro = v.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            int flags = ro ? 1 : (2 | 4); // READONLY | READWRITE|CREATE
            int rc = N.sqlite3_open_v2(N.Z(path!), out Db, flags, IntPtr.Zero);
            if (rc != 0) throw new SQLiteException("open failed rc=" + rc);
            N.sqlite3_busy_timeout(Db, busyMs); // يحاكي System.Data.SQLite: الافتراضي 0 والمفتاح BusyTimeout هو اللي بيحدد
        }
        public SQLiteCommand CreateCommand() => new SQLiteCommand(this);
        public SQLiteTransaction BeginTransaction() => new SQLiteTransaction(this);
        internal void Exec(string sql) { using var c = CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
        public void Close() { if (Db != IntPtr.Zero) { N.sqlite3_close_v2(Db); Db = IntPtr.Zero; } }
        public void Dispose() => Close();
    }

    public class SQLiteDataReader : IDisposable
    {
        IntPtr _s; internal SQLiteConnection C; bool _first = true, _hasRow; readonly bool _preStepped;
        internal SQLiteDataReader(SQLiteConnection c, IntPtr s, bool hasRow) { C = c; _s = s; _hasRow = hasRow; _preStepped = true; }
        public bool Read()
        {
            if (_s == IntPtr.Zero) return false;
            if (_first) { _first = false; return _hasRow; }
            int rc = N.sqlite3_step(_s);
            if (rc == 100) return true;
            if (rc == 101) { return false; }
            throw new SQLiteException(N.S(N.sqlite3_errmsg(C.Db)));
        }
        public int FieldCount => N.sqlite3_column_count(_s);
        public string GetName(int i) => N.S(N.sqlite3_column_name(_s, i));
        public int GetOrdinal(string n) { for (int i = 0; i < FieldCount; i++) if (string.Equals(GetName(i), n, StringComparison.OrdinalIgnoreCase)) return i; throw new IndexOutOfRangeException(n); }
        public object this[int i]
        {
            get
            {
                switch (N.sqlite3_column_type(_s, i))
                {
                    case 1: return N.sqlite3_column_int64(_s, i);
                    case 2: return N.sqlite3_column_double(_s, i);
                    case 3: return N.S(N.sqlite3_column_text(_s, i));
                    case 4: { int n = N.sqlite3_column_bytes(_s, i); var b = new byte[n]; if (n > 0) Marshal.Copy(N.sqlite3_column_blob(_s, i), b, 0, n); return b; }
                    default: return DBNull.Value;
                }
            }
        }
        public object this[string n] => this[GetOrdinal(n)];
        public bool IsDBNull(int i) => N.sqlite3_column_type(_s, i) == 5;
        public void Dispose() { if (_s != IntPtr.Zero) { N.sqlite3_finalize(_s); _s = IntPtr.Zero; } }
    }

    public class SQLiteCommand : IDisposable
    {
        readonly SQLiteConnection _c;
        public string CommandText { get; set; } = "";
        public SQLiteParameterCollection Parameters { get; } = new();
        public SQLiteTransaction? Transaction { get; set; }
        internal SQLiteCommand(SQLiteConnection c) { _c = c; }

        void Bind(IntPtr s)
        {
            foreach (var p in Parameters.Items)
            {
                int idx = N.sqlite3_bind_parameter_index(s, N.Z(p.Name));
                if (idx == 0 && p.Name.Length > 0 && (p.Name[0] == '@' || p.Name[0] == ':' || p.Name[0] == '$')) continue;
                if (idx == 0) { idx = N.sqlite3_bind_parameter_index(s, N.Z("@" + p.Name)); if (idx == 0) continue; }
                var v = p.Value;
                if (v == null || v is DBNull) N.sqlite3_bind_null(s, idx);
                else if (v is bool b) N.sqlite3_bind_int64(s, idx, b ? 1 : 0);
                else if (v is Enum) N.sqlite3_bind_int64(s, idx, Convert.ToInt64(v));
                else if (v is int || v is long || v is short || v is byte || v is uint) N.sqlite3_bind_int64(s, idx, Convert.ToInt64(v));
                else if (v is double || v is float || v is decimal) N.sqlite3_bind_double(s, idx, Convert.ToDouble(v));
                else if (v is DateTime dt) { var t = N.Z(dt.ToString("yyyy-MM-dd HH:mm:ss")); N.sqlite3_bind_text(s, idx, t, t.Length - 1, new IntPtr(-1)); }
                else if (v is byte[] bl) N.sqlite3_bind_blob(s, idx, bl, bl.Length, new IntPtr(-1));
                else { var t = N.Z(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!); N.sqlite3_bind_text(s, idx, t, t.Length - 1, new IntPtr(-1)); }
            }
        }

        // ينفّذ كل العبارات؛ يرجع المؤشر على آخر عبارة (للقراءة) أو Zero
        (IntPtr stmt, bool hasRow) Run(bool keepLast)
        {
            var sqlBytes = Encoding.UTF8.GetBytes(CommandText + "\0");
            var h = GCHandle.Alloc(sqlBytes, GCHandleType.Pinned);
            try
            {
                IntPtr cur = h.AddrOfPinnedObject(); IntPtr end = cur + sqlBytes.Length - 1;
                IntPtr last = IntPtr.Zero; bool lastRow = false;
                while (cur != IntPtr.Zero && cur.ToInt64() < end.ToInt64())
                {
                    int rc = N.sqlite3_prepare_v2(_c.Db, cur, -1, out var s, out var tail);
                    if (rc != 0) throw new SQLiteException(N.S(N.sqlite3_errmsg(_c.Db)) + " | " + CommandText);
                    cur = tail;
                    if (s == IntPtr.Zero) continue;
                    Bind(s);
                    bool isLast = true;
                    // هل في عبارات تانية بعدها؟
                    for (var q = cur; q.ToInt64() < end.ToInt64(); q += 1) { byte ch = Marshal.ReadByte(q); if (ch != (byte)' ' && ch != '\n' && ch != '\r' && ch != '\t' && ch != (byte)';') { isLast = false; break; } }
                    int st = N.sqlite3_step(s);
                    if (st != 100 && st != 101) { var msg = N.S(N.sqlite3_errmsg(_c.Db)); N.sqlite3_finalize(s); throw new SQLiteException(msg + " | " + CommandText); }
                    if (keepLast && isLast) { return (s, st == 100); }
                    if (!keepLast) { while (st == 100) st = N.sqlite3_step(s); }
                    N.sqlite3_finalize(s);
                }
                return (last, lastRow);
            }
            finally { h.Free(); }
        }

        public int ExecuteNonQuery() { Run(false); return 0; }
        public object? ExecuteScalar()
        {
            var (s, hasRow) = Run(true);
            if (s == IntPtr.Zero) return null;
            using var r = new SQLiteDataReader(_c, s, hasRow);
            return r.Read() ? (r.FieldCount > 0 ? r[0] : null) is var v && v is DBNull ? null : (r.FieldCount > 0 ? r[0] : null) : null;
        }
        public SQLiteDataReader ExecuteReader()
        {
            var (s, hasRow) = Run(true);
            return new SQLiteDataReader(_c, s, hasRow);
        }
        public void Dispose() { }
    }
}
