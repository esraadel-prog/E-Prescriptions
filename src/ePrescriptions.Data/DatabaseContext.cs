using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Reflection;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Security;
using ePrescriptions.Data.Repositories;

namespace ePrescriptions.Data
{
    /// <summary>
    /// مدير قاعدة بيانات SQLite: يتولى المسار في ProgramData، تفعيل WAL والمفاتيح الأجنبية،
    /// وتطبيق الـ Migrations الآمنة عبر PRAGMA user_version.
    /// </summary>
    public class DatabaseContext
    {
        /// <summary>
        /// آخر إصدار للمخطط (عدد ملفات الـ Migrations المطبقة)
        /// </summary>
        public const int LatestSchemaVersion = 24;

        private readonly string _connectionString;
        private readonly bool _isInMemory;

        /// <summary>
        /// المسار الفعلي لملف قاعدة البيانات (ملف مؤقت في وضع :memory:)
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>%ProgramData%\ePrescriptions (مشترك بين كل مستخدمي ويندوز على الجهاز)</summary>
        public static string DefaultDatabaseDirectory => AppPaths.DataDirectory;

        public static string DefaultDatabasePath => AppPaths.DatabasePath;

        public DatabaseContext(string? customDbPath = null)
        {
            if (customDbPath == ":memory:" || customDbPath == "Data Source=:memory:")
            {
                // SQLite تنشئ قاعدة :memory: مستقلة وفارغة لكل اتصال جديد، والمستودعات هنا تفتح اتصالاً لكل عملية،
                // لذلك نستخدم ملفاً مؤقتاً فريداً يعمل كقاعدة "مؤقتة" مشتركة بين كل الاتصالات ويُحذف عند إنهاء العملية.
                _isInMemory = true;
                string tempPath = Path.Combine(Path.GetTempPath(), "e_prescriptions_tmp_" + Guid.NewGuid().ToString("N") + ".db");
                DatabasePath = tempPath;
                _connectionString = $"Data Source={tempPath};Version=3;Pooling=False;BusyTimeout=5000;";
                AppDomain.CurrentDomain.ProcessExit += (_, __) =>
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (Exception ex) { Logger.LogException(ex, "DatabaseContext (حذف ملف القاعدة المؤقتة)"); }
                };
            }
            else
            {
                _isInMemory = false;
                string dbPath = customDbPath ?? DefaultDatabasePath;
                DatabasePath = dbPath;
                string directory = Path.GetDirectoryName(dbPath)!;
                AppPaths.EnsureDirectory(directory);

                _connectionString = $"Data Source={dbPath};Version=3;Pooling=True;Max Pool Size=10;BusyTimeout=5000;";
            }
        }

        /// <summary>
        /// فتح اتصال SQLite مع تفعيل WAL والمفاتيح الأجنبية
        /// </summary>
        public SQLiteConnection CreateConnection()
        {
            var conn = new SQLiteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            if (!_isInMemory)
            {
                cmd.CommandText += " PRAGMA journal_mode = WAL;";
            }
            cmd.ExecuteNonQuery();

            return conn;
        }

        /// <summary>
        /// فحص وتطبيق التحديثات المرقّمة (Migrations) عبر PRAGMA user_version داخل Transaction
        /// </summary>
        public void InitializeAndMigrate(bool seedData = true)
        {
            using var conn = CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                int currentVersion = GetUserVersion(conn);
                Logger.LogInfo($"فحص إصدار قاعدة البيانات: {currentVersion}", "DatabaseContext");

                if (currentVersion < 1)
                {
                    // تطبيق Migration 001: الهيكل الأساسي
                    string script001 = ReadEmbeddedSql("001_InitialSchema.sql");
                    ExecuteSqlScript(conn, script001);
                    SetUserVersion(conn, 1);
                    Logger.LogInfo("تم تطبيق Migration 001 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 2)
                {
                    // تطبيق Migration 002 (لم يعد يحتوي بيانات: البيانات التجريبية تُحمَّل عبر LoadDemoData عند الطلب)
                    string script002 = ReadEmbeddedSql("002_SeedData.sql");
                    ExecuteSqlScript(conn, script002);
                    SetUserVersion(conn, 2);
                    Logger.LogInfo("تم تطبيق Migration 002 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 3)
                {
                    // تطبيق Migration 003: المشتريات والسندات والجرد
                    string script003 = ReadEmbeddedSql("003_PurchasesAndVouchers.sql");
                    ExecuteSqlScript(conn, script003);
                    SetUserVersion(conn, 3);
                    Logger.LogInfo("تم تطبيق Migration 003 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 4)
                {
                    // تطبيق Migration 004: المصروفات والقيود اليومية والمالية
                    string script004 = ReadEmbeddedSql("004_FinanceAndStaff.sql");
                    ExecuteSqlScript(conn, script004);
                    SetUserVersion(conn, 4);
                    Logger.LogInfo("تم تطبيق Migration 004 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 5)
                {
                    // تطبيق Migration 005: التوطين المصري وضبط نسب الضريبة وبيانات الصيدلية
                    string script005 = ReadEmbeddedSql("005_EgyptLocalization.sql");
                    ExecuteSqlScript(conn, script005);
                    SetUserVersion(conn, 5);
                    Logger.LogInfo("تم تطبيق Migration 005 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 6)
                {
                    // تطبيق Migration 006: جدول سجل نشاط صاحب الصيدلية
                    string script006 = ReadEmbeddedSql("006_OwnerAuditLog.sql");
                    ExecuteSqlScript(conn, script006);
                    SetUserVersion(conn, 6);
                    Logger.LogInfo("تم تطبيق Migration 006 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 7)
                {
                    // تطبيق Migration 007: جدول سجل تدقيق الموظفين (مرئي للمدير وصاحب الصيدلية)
                    string script007 = ReadEmbeddedSql("007_StaffAuditLog.sql");
                    ExecuteSqlScript(conn, script007);
                    SetUserVersion(conn, 7);
                    Logger.LogInfo("تم تطبيق Migration 007 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 8)
                {
                    // تطبيق Migration 008: الرصيد السالب، ربط المرتجع بالفاتورة الأصلية، تتبع التشغيلات، مرتجع/إتلاف المنتهي
                    string script008 = ReadEmbeddedSql("008_ExpiryReturnsAndBatchLabels.sql");
                    ExecuteSqlScript(conn, script008);
                    SetUserVersion(conn, 8);
                    Logger.LogInfo("تم تطبيق Migration 008 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 9)
                {
                    // تطبيق Migration 009: سبب مرتجع/شطب المورد
                    string script009 = ReadEmbeddedSql("009_SupplierReturnReason.sql");
                    ExecuteSqlScript(conn, script009);
                    SetUserVersion(conn, 9);
                    Logger.LogInfo("تم تطبيق Migration 009 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 10)
                {
                    // تطبيق Migration 010: رقم الوصفة واسم الطبيب على بند الفاتورة
                    string script010 = ReadEmbeddedSql("010_ItemPrescriptionDetails.sql");
                    ExecuteSqlScript(conn, script010);
                    SetUserVersion(conn, 10);
                    Logger.LogInfo("تم تطبيق Migration 010 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 11)
                {
                    // تطبيق Migration 011: حالة الفاتورة وبيانات الإلغاء
                    string script011 = ReadEmbeddedSql("011_InvoiceCancellation.sql");
                    ExecuteSqlScript(conn, script011);
                    SetUserVersion(conn, 11);
                    Logger.LogInfo("تم تطبيق Migration 011 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 12)
                {
                    // تطبيق Migration 012: عدادات ترقيم المستندات
                    string script012 = ReadEmbeddedSql("012_NumberCounters.sql");
                    ExecuteSqlScript(conn, script012);
                    SetUserVersion(conn, 12);
                    Logger.LogInfo("تم تطبيق Migration 012 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 13)
                {
                    // تطبيق Migration 013: قفل الحساب بعد محاولات دخول فاشلة
                    string script013 = ReadEmbeddedSql("013_LoginLockout.sql");
                    ExecuteSqlScript(conn, script013);
                    SetUserVersion(conn, 13);
                    Logger.LogInfo("تم تطبيق Migration 013 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 14)
                {
                    // تطبيق Migration 014: سلسلة hash لسجلي التدقيق
                    string script014 = ReadEmbeddedSql("014_AuditHashChain.sql");
                    ExecuteSqlScript(conn, script014);
                    SetUserVersion(conn, 14);
                    Logger.LogInfo("تم تطبيق Migration 014 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 15)
                {
                    // تطبيق Migration 015: علامة العميل النقدي العام
                    string script015 = ReadEmbeddedSql("015_DefaultCashCustomer.sql");
                    ExecuteSqlScript(conn, script015);
                    SetUserVersion(conn, 15);
                    Logger.LogInfo("تم تطبيق Migration 015 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 16)
                {
                    // تطبيق Migration 016: عمود البحث الموحّد للأصناف (يُعبَّأ بعد الـ Commit)
                    string script016 = ReadEmbeddedSql("016_ItemSearchText.sql");
                    ExecuteSqlScript(conn, script016);
                    SetUserVersion(conn, 16);
                    Logger.LogInfo("تم تطبيق Migration 016 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 17)
                {
                    // تطبيق Migration 017: تواريخ الصلاحية كنص ISO + حماية الكتابة بمشغّلات
                    string script017 = ReadEmbeddedSql("017_ExpiryIsoText.sql");
                    ExecuteSqlScript(conn, script017);
                    SetUserVersion(conn, 17);
                    Logger.LogInfo("تم تطبيق Migration 017 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 18)
                {
                    // تطبيق Migration 018: سعر جرام الذهب المحفوظ لحاسبة الزكاة
                    string script018 = ReadEmbeddedSql("018_ZakatGoldPrice.sql");
                    ExecuteSqlScript(conn, script018);
                    SetUserVersion(conn, 18);
                    Logger.LogInfo("تم تطبيق Migration 018 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 19)
                {
                    // تطبيق Migration 019: أجزاء الدفع للفواتير المقسّمة الدفع
                    string script019 = ReadEmbeddedSql("019_InvoicePayments.sql");
                    ExecuteSqlScript(conn, script019);
                    SetUserVersion(conn, 19);
                    Logger.LogInfo("تم تطبيق Migration 019 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 20)
                {
                    // تطبيق Migration 020: ربط مرتجع المورد بفاتورة الشراء الأصلية (خطوة 39)
                    string script020 = ReadEmbeddedSql("020_PurchaseLinkedSupplierReturns.sql");
                    ExecuteSqlScript(conn, script020);
                    SetUserVersion(conn, 20);
                    Logger.LogInfo("تم تطبيق Migration 020 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 21)
                {
                    // تطبيق Migration 021: تصحيح تكلفة التشغيلة من الشراء + تخزين نسبة ضريبة بند الشراء
                    string script021 = ReadEmbeddedSql("021_PurchaseCostAndVatPercent.sql");
                    ExecuteSqlScript(conn, script021);
                    SetUserVersion(conn, 21);
                    Logger.LogInfo("تم تطبيق Migration 021 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 22)
                {
                    // تطبيق Migration 022: أوامر الشراء المحفوظة
                    string script022 = ReadEmbeddedSql("022_PurchaseOrders.sql");
                    ExecuteSqlScript(conn, script022);
                    SetUserVersion(conn, 22);
                    Logger.LogInfo("تم تطبيق Migration 022 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 23)
                {
                    // تطبيق Migration 023: الصلاحيات المخصصة للموظفين (مع التحقق تجنباً لتكرار العمود في نسخ الاختبار القديمة)
                    bool hasCol = false;
                    using (var checkCmd = conn.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = 'CustomPermissionsConfig';";
                        hasCol = Convert.ToInt64(checkCmd.ExecuteScalar() ?? 0) > 0;
                    }
                    if (!hasCol)
                    {
                        string script023 = ReadEmbeddedSql("023_UserCustomPermissions.sql");
                        ExecuteSqlScript(conn, script023);
                    }
                    SetUserVersion(conn, 23);
                    Logger.LogInfo("تم تطبيق Migration 023 بنجاح.", "DatabaseContext");
                }

                if (currentVersion < 24)
                {
                    // تطبيق Migration 024: جداول سندات رصيد أول المدة (Opening Stock Vouchers)
                    string script024 = ReadEmbeddedSql("024_OpeningStockVouchers.sql");
                    ExecuteSqlScript(conn, script024);
                    SetUserVersion(conn, 24);
                    Logger.LogInfo("تم تطبيق Migration 024 بنجاح.", "DatabaseContext");
                }

                trans.Commit();

            }
            catch (Exception ex)
            {
                trans.Rollback();
                Logger.LogException(ex, "InitializeAndMigrate");
                throw;
            }

            // فهرسة بحث الأصناف القديمة/المحمّلة بـ SQL مباشر (خطوة 24)
            ItemRepository.BackfillSearchText(this);
            NormalizeLegacyExpiryDates();

            if (seedData)
            {
                // مسار الاختبارات/التطوير فقط: بيانات تجريبية + حساب Owner بكلمة مرور افتراضية معروفة.
                // التطبيق الفعلي يستدعي InitializeAndMigrate(seedData: false) ثم يمرّ بمعالج الإعداد الأول.
                LoadDemoData();
                EnsureOwnerAccountExists();
            }
        }

        /// <summary>
        /// تصليح تواريخ صلاحية قديمة مخزّنة بصيغة غير ISO (مثل 31/01/2027) لتصير yyyy-MM-dd (خطوة 25).
        /// اللي ما يتقرأش بيتسجّل في الـ Logger وبيتساب زي ما هو (مش بيتخمّن).
        /// </summary>
        public void NormalizeLegacyExpiryDates()
        {
            using var conn = CreateConnection();
            var bad = new System.Collections.Generic.List<(long Id, string Text)>();
            using (var sel = conn.CreateCommand())
            {
                sel.CommandText = "SELECT Id, ExpiryDate FROM ItemBatches WHERE ExpiryDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]';";
                using var r = sel.ExecuteReader();
                while (r.Read()) bad.Add((Convert.ToInt64(r["Id"]), Convert.ToString(r["ExpiryDate"]) ?? string.Empty));
            }
            if (bad.Count == 0) return;

            var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy/MM/dd", "yyyy/M/d", "yyyy-M-d", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "MM/dd/yyyy" };
            foreach (var (id, text) in bad)
            {
                if (!DateTime.TryParseExact(text.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
                {
                    Logger.LogInfo($"تعذّر تحويل تاريخ صلاحية التشغيلة {id} ('{text}') لصيغة ISO.", "DatabaseContext.NormalizeLegacyExpiryDates");
                    continue;
                }
                using var upd = conn.CreateCommand();
                upd.CommandText = "UPDATE ItemBatches SET ExpiryDate = @d WHERE Id = @id;";
                upd.Parameters.AddWithValue("@d", d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                upd.Parameters.AddWithValue("@id", id);
                upd.ExecuteNonQuery();
            }
        }

        /// <summary>اسم مستخدم حساب صاحب الصيدلية</summary>
        public const string OwnerUsername = "pharmacy_owner";

        /// <summary>كلمة مرور Owner الافتراضية (مسار الاختبارات فقط، ولا تُنشأ في التثبيت الفعلي)</summary>
        public const string DefaultOwnerPassword = ePrescriptions.Core.Security.DefaultCredentials.OwnerPassword;

        /// <summary>هل يوجد حساب Owner؟ (غيابه = أول تشغيل)</summary>
        public bool OwnerExists()
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Role = 99;";
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0) > 0;
        }

        /// <summary>
        /// إنشاء حساب صاحب الصيدلية بكلمة مرور يختارها المستخدم. يُرفض لو كان موجوداً.
        /// الحساب لا يظهر في شاشات إدارة المستخدمين (UserRepository.GetAll يستثنيه دائماً).
        /// </summary>
        public void CreateOwnerAccount(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("كلمة المرور مطلوبة.", nameof(password));
            if (OwnerExists())
                throw new InvalidOperationException("حساب صاحب الصيدلية موجود بالفعل.");

            using var conn = CreateConnection();
            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO Users (Username, FullName, PasswordHash, Role, IsActive, CreatedAt)
                VALUES (@u, @f, @h, 99, 1, @c);";
            insertCmd.Parameters.AddWithValue("@u", OwnerUsername);
            insertCmd.Parameters.AddWithValue("@f", "صاحب الصيدلية");
            insertCmd.Parameters.AddWithValue("@h", PasswordHasher.HashPassword(password));
            insertCmd.Parameters.AddWithValue("@c", DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture));
            insertCmd.ExecuteNonQuery();
        }

        /// <summary>
        /// سجل أساسي لا غنى عنه: العميل النقدي العام (IsDefaultCash = 1). ليس بيانات تجريبية.
        /// لو موجود عميل نقدي بالفعل ما بيعملش حاجة؛ وإلا بيحاول رقم 1 (للتوافق) وبعدها أي رقم متاح.
        /// </summary>
        public void EnsureCashCustomer()
        {
            using var conn = CreateConnection();
            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(*) FROM Customers WHERE IsDefaultCash = 1;";
                if (Convert.ToInt64(check.ExecuteScalar() ?? 0) > 0) return;
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO Customers (Id, Name, Phone, CreditLimitPiastres, CurrentBalancePiastres, PriceCategory, Notes, IsActive, IsDefaultCash)
                    VALUES (1, 'عميل نقدي', '', 0, 0, 1, 'العميل النقدي العام', 1, 1);";
                cmd.ExecuteNonQuery();
            }

            using (var check2 = conn.CreateCommand())
            {
                check2.CommandText = "SELECT COUNT(*) FROM Customers WHERE IsDefaultCash = 1;";
                if (Convert.ToInt64(check2.ExecuteScalar() ?? 0) > 0) return;
            }

            // رقم 1 محجوز لعميل عادي: أنشئ العميل النقدي برقم تلقائي
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = @"
                INSERT INTO Customers (Name, Phone, CreditLimitPiastres, CurrentBalancePiastres, PriceCategory, Notes, IsActive, IsDefaultCash)
                VALUES ('عميل نقدي', '', 0, 0, 1, 'العميل النقدي العام', 1, 1);";
            cmd2.ExecuteNonQuery();
        }

        /// <summary>
        /// تحميل بيانات تجريبية (أصناف/تشغيلات/عملاء/مورد/إعدادات) على قاعدة فاضية من الأصناف فقط.
        /// ترجع false لو فيه أصناف بالفعل (لا تُكرَّر ولا تُدمج مع بيانات حقيقية).
        /// </summary>
        public bool LoadDemoData()
        {
            using var conn = CreateConnection();
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM Items;";
                if (Convert.ToInt64(checkCmd.ExecuteScalar() ?? 0) > 0) return false;
            }

            using var trans = conn.BeginTransaction();
            try
            {
                ExecuteSqlScript(conn, ReadEmbeddedSql("DemoData.sql"));
                trans.Commit();
                ItemRepository.BackfillSearchText(this);
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                Logger.LogException(ex, "DatabaseContext.LoadDemoData");
                throw;
            }
        }

        /// <summary>
        /// إنشاء حساب صاحب الصيدلية تلقائياً إن لم يكن موجوداً (مرة واحدة فقط).
        /// الحساب لا يظهر في أي شاشة لإدارة المستخدمين (UserRepository.GetAll يستثنيه دائماً)،
        /// ولا توجد قائمة مستخدمين في شاشة الدخول؛ يُدخل عبر اسم المستخدم وكلمة المرور مباشرة فقط.
        /// بيانات الدخول الافتراضية: pharmacy_owner / ChangeMe@Owner1 — يجب تغييرها فوراً بعد أول دخول.
        /// </summary>
        private void EnsureOwnerAccountExists()
        {
            if (OwnerExists()) return;

            CreateOwnerAccount(DefaultOwnerPassword);
            Logger.LogInfo("تم إنشاء حساب صاحب الصيدلية الافتراضي (مسار الاختبارات فقط).", "DatabaseContext.EnsureOwnerAccountExists");
        }

        public static int GetUserVersion(SQLiteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            object? result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : 0;
        }

        private static void SetUserVersion(SQLiteConnection conn, int version)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA user_version = {version};";
            cmd.ExecuteNonQuery();
        }

        private static void ExecuteSqlScript(SQLiteConnection conn, string script)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = script;
            cmd.ExecuteNonQuery();
        }

        private static string ReadEmbeddedSql(string filename)
        {
            var assembly = Assembly.GetExecutingAssembly();
            // البحث عن المورد المدمج بغض النظر عن الـ namespace prefix
            string? resourceName = null;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(filename, StringComparison.OrdinalIgnoreCase))
                {
                    resourceName = name;
                    break;
                }
            }

            if (resourceName == null)
            {
                // محاولة قراءة الملف مباشرة من مسار التطبيق إذا لم يكن مدمجاً
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Migrations", filename);
                if (File.Exists(localPath))
                {
                    return File.ReadAllText(localPath);
                }
                throw new FileNotFoundException($"تعذر العثور على ملف السكربت المدمج: {filename}");
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
