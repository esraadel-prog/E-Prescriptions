using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;

namespace ePrescriptions.App.Services
{
    /// <summary>نتيجة نسخة احتياطية مضبوطة (أساسي + خارجي اختياري)</summary>
    public class BackupRunResult
    {
        public bool Success { get; set; }
        public string PrimaryPath { get; set; } = string.Empty;
        public string ExternalPath { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        /// <summary>true لو النسخة الأساسية نجحت لكن النسخة على القرص الخارجي فشلت</summary>
        public bool ExternalFailed { get; set; }
    }

    /// <summary>نتيجة استعادة نسخة احتياطية (للنسخة غير المتزامنة اللي ما تقدرش تستخدم out)</summary>
    public class BackupRestoreResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>خطوة 41: نتيجة اختبار استعادة نسخة في قاعدة مؤقتة (القاعدة الحية لا تُلمس أبداً)</summary>
    public class BackupVerifyResult
    {
        public bool Success { get; set; }
        public string BackupPath { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        /// <summary>إصدار مخطط النسخة قبل تطبيق أي migrations ناقصة</summary>
        public int BackupSchemaVersion { get; set; }
        public long UsersCount { get; set; }
        public long ItemsCount { get; set; }
        public long InvoicesCount { get; set; }
    }

    public interface IBackupService
    {
        string GetDefaultBackupFolder();
        bool CreateBackup(string? customFolder, out string backupFilePath, out string errorMessage);
        bool RestoreBackup(string backupFilePath, out string errorMessage);
        bool ValidateBackupFile(string backupFilePath, out string errorMessage);

        /// <summary>نسخة بحسب الإعدادات: المجلد الأساسي + المجلد الخارجي (لو محدد) + حذف الأقدم من كل منهما</summary>
        BackupRunResult RunConfiguredBackup(BackupSettings settings);

        /// <summary>تُستدعى بعد إغلاق الوردية: لا تفعل شيئاً لو النسخ التلقائي معطّل. لا ترمي استثناءات أبداً.</summary>
        BackupRunResult? RunAfterShiftBackup(BackupSettings settings);

        /// <summary>خطوة 28: نفس <see cref="RunConfiguredBackup"/> لكن على خيط خلفي (الواجهة ما تتجمدش أثناء النسخ)</summary>
        Task<BackupRunResult> RunConfiguredBackupAsync(BackupSettings settings);

        /// <summary>خطوة 28: نفس <see cref="RunAfterShiftBackup"/> لكن على خيط خلفي. لا ترمي استثناءات.</summary>
        Task<BackupRunResult?> RunAfterShiftBackupAsync(BackupSettings settings);

        /// <summary>خطوة 28: نفس <see cref="RestoreBackup"/> لكن على خيط خلفي. المستدعي مسؤول عن منع أي استخدام للقاعدة أثناء الاستعادة (IsBusy).</summary>
        Task<BackupRestoreResult> RestoreBackupAsync(string backupFilePath);

        /// <summary>حذف أقدم النسخ (بنمط e_prescriptions_backup_*.db فقط) مع إبقاء عدد محدد من الأحدث. يرجع عدد المحذوف.</summary>
        int PruneOldBackups(string folder, int keepCount);

        /// <summary>
        /// خطوة 41: اختبار استعادة: بينسخ الملف لمجلد مؤقت، يفحص السلامة، يطبّق الـ migrations الناقصة، ويتأكد إن الجداول الأساسية
        /// بتتقرأ. القاعدة الحية والنسخة الأصلية ما بيتعدلوش. لا يرمي استثناءات.
        /// </summary>
        BackupVerifyResult VerifyBackupRestorable(string backupFilePath);

        /// <summary>خطوة 41: أحدث ملف نسخة (بالاسم) في المجلد الأساسي بحسب الإعدادات، أو null لو مفيش</summary>
        string? FindLatestBackup(BackupSettings settings);
    }

    public class BackupService : IBackupService
    {
        public const string BackupFilePrefix = "e_prescriptions_backup_";
        public const string BackupFilePattern = BackupFilePrefix + "*.db";

        private readonly DatabaseContext _dbContext;
        private readonly BackupStatusStore? _statusStore;
        private readonly Func<DateTime> _clock;

        // نسختين في نفس اللحظة (بعد وردية + جدولة مثلاً) ما يتخانقوش على نفس اسم الملف
        private static readonly object BackupGate = new object();

        /// <param name="statusStore">لو موجود: كل نسخة (يدوية/بعد وردية/مجدولة) بتسجّل نجاحها أو فشلها فيه (خطوة 41)</param>
        /// <param name="clock">ساعة قابلة للاستبدال في الاختبارات (الافتراضي: توقيت الجهاز)</param>
        public BackupService(DatabaseContext dbContext, BackupStatusStore? statusStore = null, Func<DateTime>? clock = null)
        {
            _dbContext = dbContext;
            _statusStore = statusStore;
            _clock = clock ?? (() => DateTime.Now);
        }

        public string GetDefaultBackupFolder()
        {
            return ePrescriptions.Core.Helpers.AppPaths.EnsureDirectory(ePrescriptions.Core.Helpers.AppPaths.BackupsDirectory);
        }

        public bool CreateBackup(string? customFolder, out string backupFilePath, out string errorMessage)
        {
            errorMessage = string.Empty;
            backupFilePath = string.Empty;

            try
            {
                string folder = string.IsNullOrWhiteSpace(customFolder) ? GetDefaultBackupFolder() : customFolder!;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                lock (BackupGate)
                {
                    backupFilePath = NextBackupPath(folder);
                    VacuumInto(_dbContext, backupFilePath);
                }

                Logger.LogInfo($"تم إنشاء نسخة احتياطية بنجاح في: {backupFilePath}", "BackupService.CreateBackup");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Logger.LogException(ex, "BackupService.CreateBackup");
                return false;
            }
        }

        /// <summary>اسم ملف نسخة جديد غير مستخدم (ثواني + رقم تسلسلي لو اتأخدت أكتر من نسخة في نفس الثانية)</summary>
        private static string NextBackupPath(string folder)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(folder, $"{BackupFilePrefix}{stamp}.db");
            int n = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, $"{BackupFilePrefix}{stamp}_{n:D2}.db");
                n++;
            }
            return path;
        }

        /// <summary>
        /// نسخة متّسقة من القاعدة وهي مفتوحة عبر VACUUM INTO (بتشمل محتوى ملف WAL، وبتكتب ملف واحد مستقل).
        /// بيتكتب لملف مؤقت وبعدين يتسمّى، عشان نسخة ناقصة ما تظهرش أبداً باسم نسخة صالحة.
        /// </summary>
        private static void VacuumInto(DatabaseContext db, string destination)
        {
            string tmp = destination + ".tmp";
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);

                using (var conn = db.CreateConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "VACUUM INTO '" + tmp.Replace("'", "''") + "';";
                    cmd.ExecuteNonQuery();
                }

                // فحص سلامة النسخة الناتجة قبل اعتمادها
                if (!IntegrityOk(tmp, out string why))
                {
                    throw new InvalidOperationException("فشل فحص سلامة النسخة الناتجة: " + why);
                }

                File.Move(tmp, destination);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception ex) { Logger.LogException(ex, "BackupService (حذف الملف المؤقت)"); }
            }
        }

        private static bool IntegrityOk(string path, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                var connStr = $"Data Source={path};Version=3;Read Only=True;Pooling=False;";
                using var conn = new SQLiteConnection(connStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check;";
                var result = cmd.ExecuteScalar()?.ToString();
                if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)) return true;
                errorMessage = result ?? "غير معروف";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public BackupRunResult RunConfiguredBackup(BackupSettings settings)
        {
            BackupRunResult result;
            try
            {
                result = RunConfiguredBackupCore(settings);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupService.RunConfiguredBackup");
                result = new BackupRunResult { Success = false, Message = "فشل إنشاء النسخة الاحتياطية: " + ex.Message };
            }
            RecordOutcome(result);
            return result;
        }

        /// <summary>تسجيل نتيجة النسخة في ملف الحالة (لو متاح). مفيش حاجة هنا بترمي استثناء.</summary>
        private void RecordOutcome(BackupRunResult result)
        {
            if (_statusStore == null) return;
            try
            {
                DateTime now = _clock();
                if (result.Success)
                {
                    _statusStore.RecordBackupSuccess(now, result.PrimaryPath, result.ExternalFailed ? result.Message : string.Empty);
                }
                else
                {
                    _statusStore.RecordBackupFailure(now, result.Message);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupService.RecordOutcome");
            }
        }

        private BackupRunResult RunConfiguredBackupCore(BackupSettings settings)
        {
            var result = new BackupRunResult();
            int keep = settings.EffectiveRetention;

            string primaryFolder = string.IsNullOrWhiteSpace(settings.PrimaryFolder) ? GetDefaultBackupFolder() : settings.PrimaryFolder;
            if (!CreateBackup(primaryFolder, out string primaryPath, out string error))
            {
                result.Success = false;
                result.Message = "فشل إنشاء النسخة الاحتياطية: " + error;
                return result;
            }
            result.PrimaryPath = primaryPath;
            result.Success = true;
            PruneSafe(primaryFolder, keep);

            if (!string.IsNullOrWhiteSpace(settings.ExternalFolder))
            {
                string extFolder = settings.ExternalFolder;
                // لو الخارجي هو نفس مجلد البرنامج الأساسي ما يبقاش نسخة "منفصلة": نرفض بوضوح
                if (SameFolder(extFolder, primaryFolder))
                {
                    result.ExternalFailed = true;
                    result.Message = "تم أخذ النسخة الأساسية، لكن المجلد الخارجي هو نفس مجلد النسخ الأساسي. اختر قرصاً أو USB منفصلاً.";
                    return result;
                }
                // نسخ ملف النسخة الجاهزة (مش VACUUM تاني) لتكون النسختان متطابقتين
                try
                {
                    Directory.CreateDirectory(extFolder);
                    string extPath = Path.Combine(extFolder, Path.GetFileName(primaryPath));
                    string extTmp = extPath + ".tmp";
                    File.Copy(primaryPath, extTmp, overwrite: true);
                    if (File.Exists(extPath)) File.Delete(extPath);
                    File.Move(extTmp, extPath);
                    result.ExternalPath = extPath;
                    PruneSafe(extFolder, keep);
                }
                catch (Exception ex)
                {
                    result.ExternalFailed = true;
                    result.Message = "تم أخذ النسخة الأساسية، لكن فشل النسخ على القرص الخارجي: " + ex.Message;
                    Logger.LogException(ex, "BackupService.RunConfiguredBackup.External");
                    return result;
                }
            }

            result.Message = string.IsNullOrEmpty(result.ExternalPath)
                ? "تم أخذ نسخة احتياطية: " + Path.GetFileName(primaryPath)
                : "تم أخذ نسخة احتياطية في المجلد الأساسي وعلى القرص الخارجي: " + Path.GetFileName(primaryPath);
            return result;
        }

        public Task<BackupRunResult> RunConfiguredBackupAsync(BackupSettings settings)
        {
            return Task.Run(() => RunConfiguredBackup(settings));
        }

        public Task<BackupRunResult?> RunAfterShiftBackupAsync(BackupSettings settings)
        {
            return Task.Run(() => RunAfterShiftBackup(settings));
        }

        public Task<BackupRestoreResult> RestoreBackupAsync(string backupFilePath)
        {
            return Task.Run(() =>
            {
                bool ok = RestoreBackup(backupFilePath, out string error);
                return new BackupRestoreResult { Success = ok, ErrorMessage = error };
            });
        }

        public BackupRunResult? RunAfterShiftBackup(BackupSettings settings)
        {
            if (!settings.AutoBackupAfterShift) return null;
            try
            {
                return RunConfiguredBackup(settings);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupService.RunAfterShiftBackup");
                return new BackupRunResult { Success = false, Message = "فشل النسخ الاحتياطي التلقائي: " + ex.Message };
            }
        }

        private void PruneSafe(string folder, int keep)
        {
            try { PruneOldBackups(folder, keep); }
            catch (Exception ex) { Logger.LogException(ex, "BackupService.PruneOldBackups"); }
        }

        private static bool SameFolder(string a, string b)
        {
            try
            {
                string na = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string nb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public int PruneOldBackups(string folder, int keepCount)
        {
            if (keepCount < BackupSettings.MinRetentionCount) keepCount = BackupSettings.MinRetentionCount;
            if (!Directory.Exists(folder)) return 0;

            // الاسم يحتوي التاريخ والوقت، فالترتيب بالاسم = الترتيب الزمني (وأأمن من تواريخ الملفات بعد النسخ على USB)
            var files = Directory.GetFiles(folder, BackupFilePattern)
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            int deleted = 0;
            foreach (var old in files.Skip(keepCount))
            {
                try { File.Delete(old); deleted++; }
                catch (Exception ex) { Logger.LogException(ex, "BackupService.PruneOldBackups"); }
            }
            return deleted;
        }

        public string? FindLatestBackup(BackupSettings settings)
        {
            try
            {
                string folder = string.IsNullOrWhiteSpace(settings.PrimaryFolder) ? GetDefaultBackupFolder() : settings.PrimaryFolder;
                if (!Directory.Exists(folder)) return null;
                // الاسم فيه التاريخ والوقت، فأكبر اسم = الأحدث (نفس منطق الحذف)
                return Directory.GetFiles(folder, BackupFilePattern)
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupService.FindLatestBackup");
                return null;
            }
        }

        public BackupVerifyResult VerifyBackupRestorable(string backupFilePath)
        {
            var r = new BackupVerifyResult { BackupPath = backupFilePath ?? string.Empty };
            string tmpDir = Path.Combine(Path.GetTempPath(), "ep_restoretest_" + Guid.NewGuid().ToString("N"));
            try
            {
                if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
                {
                    r.Message = "ملف النسخة الاحتياطية غير موجود.";
                    return r;
                }

                if (!ValidateBackupFile(backupFilePath, out string invalid))
                {
                    r.Message = invalid;
                    return r;
                }

                r.BackupSchemaVersion = ReadSchemaVersion(backupFilePath);
                if (r.BackupSchemaVersion > DatabaseContext.LatestSchemaVersion)
                {
                    r.Message = $"النسخة من إصدار أحدث للبرنامج (إصدار القاعدة {r.BackupSchemaVersion} وإصدار هذا البرنامج {DatabaseContext.LatestSchemaVersion}).";
                    return r;
                }

                // نشتغل على نسخة من النسخة، في قاعدة مؤقتة منفصلة تماماً عن القاعدة الحية
                Directory.CreateDirectory(tmpDir);
                string tmpDb = Path.Combine(tmpDir, "restore_test.db");
                File.Copy(backupFilePath, tmpDb, overwrite: true);

                var tmpContext = new DatabaseContext(tmpDb);
                tmpContext.InitializeAndMigrate(seedData: false);

                using (var conn = tmpContext.CreateConnection())
                {
                    int finalVersion = DatabaseContext.GetUserVersion(conn);
                    if (finalVersion != DatabaseContext.LatestSchemaVersion)
                    {
                        r.Message = $"إصدار القاعدة بعد تطبيق الـ migrations {finalVersion} بدل {DatabaseContext.LatestSchemaVersion}.";
                        return r;
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA integrity_check;";
                        string? integrity = cmd.ExecuteScalar()?.ToString();
                        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                        {
                            r.Message = "فشل فحص السلامة بعد الفتح: " + (integrity ?? "غير معروف");
                            return r;
                        }
                    }

                    // الجداول الأساسية لازم تتقرأ فعلاً (مش مجرد ملف سليم شكلياً)
                    r.UsersCount = CountRows(conn, "Users");
                    r.ItemsCount = CountRows(conn, "Items");
                    r.InvoicesCount = CountRows(conn, "Invoices");
                }

                r.Success = true;
                r.Message = $"النسخة صالحة للاستعادة ({r.UsersCount} مستخدم، {r.ItemsCount} صنف، {r.InvoicesCount} فاتورة).";
                return r;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupService.VerifyBackupRestorable");
                r.Success = false;
                r.Message = "تعذر فتح النسخة في قاعدة مؤقتة: " + ex.Message;
                return r;
            }
            finally
            {
                CleanupTempDir(tmpDir);
            }
        }

        private static long CountRows(SQLiteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM " + table + ";";
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        private static void CleanupTempDir(string dir)
        {
            try
            {
                SQLiteConnection.ClearAllPools();
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupService (حذف مجلد اختبار الاستعادة المؤقت)");
            }
        }

        public bool ValidateBackupFile(string backupFilePath, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!File.Exists(backupFilePath))
            {
                errorMessage = "ملف النسخة الاحتياطية غير موجود.";
                return false;
            }

            if (IntegrityOk(backupFilePath, out string why)) return true;
            errorMessage = $"فشل فحص سلامة قاعدة البيانات أو الملف غير صالح: {why}";
            return false;
        }

        private static int ReadSchemaVersion(string path)
        {
            var connStr = $"Data Source={path};Version=3;Read Only=True;Pooling=False;";
            using var conn = new SQLiteConnection(connStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }

        public bool RestoreBackup(string backupFilePath, out string errorMessage)
        {
            // نفس قفل النسخ: نسخة مجدولة ما تشتغلش وقت استبدال ملف القاعدة، ولا العكس
            lock (BackupGate)
            {
                return RestoreBackupCore(backupFilePath, out errorMessage);
            }
        }

        private bool RestoreBackupCore(string backupFilePath, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!ValidateBackupFile(backupFilePath, out errorMessage))
            {
                return false;
            }

            // نسخة أحدث من البرنامج الحالي: ما نقدرش نفهمها، فلا نستبدل أي شيء
            int backupVersion;
            try { backupVersion = ReadSchemaVersion(backupFilePath); }
            catch (Exception ex) { errorMessage = "تعذر قراءة إصدار النسخة: " + ex.Message; return false; }

            if (backupVersion > DatabaseContext.LatestSchemaVersion)
            {
                errorMessage = $"النسخة الاحتياطية من إصدار أحدث للبرنامج (إصدار القاعدة {backupVersion} وإصدار هذا البرنامج {DatabaseContext.LatestSchemaVersion}). حدّث البرنامج أولاً.";
                return false;
            }

            string dbPath = _dbContext.DatabasePath;
            string emergencyBackup = dbPath + ".pre_restore.bak";
            bool replaced = false;

            try
            {
                // أخذ نسخة وقائية متّسقة من الحالي قبل الاستبدال (VACUUM INTO يشمل WAL)، وبديلها نسخ الملف
                try
                {
                    if (File.Exists(emergencyBackup)) File.Delete(emergencyBackup);
                    VacuumInto(_dbContext, emergencyBackup + ".db");
                    File.Move(emergencyBackup + ".db", emergencyBackup);
                }
                catch
                {
                    SQLiteConnection.ClearAllPools();
                    if (File.Exists(dbPath)) File.Copy(dbPath, emergencyBackup, overwrite: true);
                }

                // إغلاق أي اتصالات معلقة، وحذف ملفات WAL/SHM القديمة قبل وضع الملف الجديد (عشان ما تتطبقش على القاعدة المستعادة)
                SQLiteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                DeleteIfExists(dbPath + "-wal");
                DeleteIfExists(dbPath + "-shm");

                File.Copy(backupFilePath, dbPath, overwrite: true);
                replaced = true;

                // إعادة تشغيل الاتصال بالقاعدة وتطبيق أي migrations ناقصة لو النسخة أقدم
                SQLiteConnection.ClearAllPools();
                _dbContext.InitializeAndMigrate(seedData: false);

                int finalVersion = ReadSchemaVersion(dbPath);
                if (finalVersion != DatabaseContext.LatestSchemaVersion)
                {
                    throw new InvalidOperationException($"إصدار القاعدة بعد الاستعادة {finalVersion} بدل {DatabaseContext.LatestSchemaVersion}.");
                }

                Logger.LogInfo($"تم استعادة قاعدة البيانات بنجاح من: {backupFilePath} (إصدار النسخة {backupVersion})", "BackupService.RestoreBackup");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Logger.LogException(ex, "BackupService.RestoreBackup");

                // فشل بعد الاستبدال: رجّع القاعدة الأصلية من النسخة الوقائية
                if (replaced && File.Exists(emergencyBackup))
                {
                    try
                    {
                        SQLiteConnection.ClearAllPools();
                        DeleteIfExists(dbPath + "-wal");
                        DeleteIfExists(dbPath + "-shm");
                        File.Copy(emergencyBackup, dbPath, overwrite: true);
                        errorMessage += " (تم الرجوع للقاعدة السابقة تلقائياً)";
                    }
                    catch (Exception ex2)
                    {
                        Logger.LogException(ex2, "BackupService.RestoreBackup.Rollback");
                        errorMessage += " (تعذر الرجوع التلقائي؛ النسخة الوقائية: " + emergencyBackup + ")";
                    }
                }
                return false;
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
