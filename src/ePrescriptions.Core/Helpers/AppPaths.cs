using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// مسارات بيانات البرنامج. القاعدة والنسخ الاحتياطية والسجلات والتصدير كلها في %ProgramData%\ePrescriptions
    /// عشان كل مستخدمي ويندوز على نفس الجهاز يشوفوا نفس البيانات (بدل %AppData% الخاص بكل مستخدم).
    /// </summary>
    public static class AppPaths
    {
        public const string DataFolderName = "ePrescriptions";
        public const string LegacyDataFolderName = "e_prescriptions";
        public const string DatabaseFileName = "e_prescriptions.db";

        /// <summary>%ProgramData%\ePrescriptions</summary>
        public static string DataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), DataFolderName);

        /// <summary>المسار القديم الخاص بالمستخدم: %AppData%\e_prescriptions</summary>
        public static string LegacyDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyDataFolderName);

        public static string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);
        public static string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public static string BackupsDirectory => Path.Combine(DataDirectory, "backups");
        public static string ExportsDirectory => Path.Combine(DataDirectory, "exports");

        /// <summary>
        /// إنشاء المجلد لو مش موجود، ومحاولة إعطاء مجموعة Users صلاحية التعديل عليه (أول تشغيل بدون المثبّت)
        /// حتى يقدر كل مستخدمي الجهاز يكتبوا في نفس القاعدة. أي فشل في ضبط الصلاحيات لا يوقف البرنامج.
        /// </summary>
        public static string EnsureDirectory(string path)
        {
            if (Directory.Exists(path)) return path;

            Directory.CreateDirectory(path);
            try
            {
                var info = new DirectoryInfo(path);
                var security = info.GetAccessControl();
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                security.AddAccessRule(new FileSystemAccessRule(
                    users,
                    FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                info.SetAccessControl(security);
            }
            catch (Exception ex)
            {
                // غير ويندوز أو بدون صلاحية تعديل الـ ACL: المثبّت هو المسؤول الأساسي عن ضبط الصلاحيات
                Logger.LogInfo("تعذّر ضبط صلاحيات مجلد البيانات (غير حرج): " + ex.Message, "AppPaths.EnsureDirectory");
            }
            return path;
        }
    }

    public enum LegacyMigrationResult
    {
        NothingToMigrate,
        AlreadyMigrated,
        Migrated
    }

    /// <summary>
    /// ترحيل بيانات التثبيتات القديمة من %AppData%\e_prescriptions إلى %ProgramData%\ePrescriptions.
    /// بينسخ فقط (ما بيحذفش القديم) ومبيكتبش فوق قاعدة موجودة في المسار الجديد.
    /// </summary>
    public static class LegacyDataMigrator
    {
        public const string MarkerFileName = "MIGRATED_TO_PROGRAMDATA.txt";

        public static LegacyMigrationResult MigrateIfNeeded() => Migrate(AppPaths.LegacyDataDirectory, AppPaths.DataDirectory);

        public static LegacyMigrationResult Migrate(string legacyDir, string newDir)
        {
            string legacyDb = Path.Combine(legacyDir, AppPaths.DatabaseFileName);
            string newDb = Path.Combine(newDir, AppPaths.DatabaseFileName);

            if (!File.Exists(legacyDb)) return LegacyMigrationResult.NothingToMigrate;
            if (File.Exists(newDb)) return LegacyMigrationResult.AlreadyMigrated;

            AppPaths.EnsureDirectory(newDir);

            // نسخ ملف القاعدة مع ملفي WAL وSHM معاً حتى لا تضيع تغييرات لم تُدمج بعد
            foreach (string suffix in new[] { "", "-wal", "-shm" })
            {
                string src = legacyDb + suffix;
                if (File.Exists(src)) File.Copy(src, newDb + suffix, overwrite: false);
            }

            // نسخ النسخ الاحتياطية القديمة
            string legacyBackups = Path.Combine(legacyDir, "backups");
            if (Directory.Exists(legacyBackups))
            {
                string newBackups = AppPaths.EnsureDirectory(Path.Combine(newDir, "backups"));
                foreach (string file in Directory.GetFiles(legacyBackups))
                {
                    string target = Path.Combine(newBackups, Path.GetFileName(file));
                    if (!File.Exists(target)) File.Copy(file, target, overwrite: false);
                }
            }

            try
            {
                File.WriteAllText(Path.Combine(legacyDir, MarkerFileName),
                    $"تم نسخ قاعدة البيانات إلى {newDir} بتاريخ {DbDate.Minutes(DateTime.Now)}.\r\n" +
                    "النسخة هنا لم تُحذف (احتياطية). البرنامج يستخدم المسار الجديد فقط.");
            }
            catch (Exception ex) { Logger.LogInfo("تعذّر كتابة ملف علامة الترحيل (غير حرج): " + ex.Message, "LegacyDataMigrator"); }

            return LegacyMigrationResult.Migrated;
        }
    }
}
