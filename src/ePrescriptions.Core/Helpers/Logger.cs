using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// مسجل سجلات الأخطاء في ملف نصي داخل مسار ProgramData\ePrescriptions\logs
    /// </summary>
    public static class Logger
    {
        private static readonly object LockObj = new();

        /// <summary>أقصى حجم لملف سجل واحد (5 ميجا). لو الملف عدّاه بيتحوّل لجزء مؤرشف قبل الكتابة الجديدة.</summary>
        public const long MaxFileBytes = 5L * 1024 * 1024;

        /// <summary>أقصى عدد أجزاء مؤرشفة لنفس اليوم/النوع (error_2026-10-12.1.log ...). الأقدم بيتحذف.</summary>
        public const int MaxRolledFilesPerDay = 3;

        /// <summary>السجلات الأقدم من كده (بالأيام) بتتحذف تلقائياً.</summary>
        public const int RetentionDays = 30;

        // اسم ملف السجل: error_yyyy-MM-dd.log أو info_yyyy-MM-dd.log أو بجزء مؤرشف .N قبل .log
        private static readonly Regex LogNameRegex = new Regex(
            @"^(error|info)_(\d{4}-\d{2}-\d{2})(\.(\d+))?\.log$", RegexOptions.Compiled);

        private static DateTime _lastCleanupDate = DateTime.MinValue;

        public static string LogDirectory => AppPaths.LogsDirectory;

        /// <summary>
        /// كتابة نص في ملف سجل مع التدوير: لو الملف وصل للحد الأقصى بيتنقل لـ name.1.log (والأجزاء الأقدم بتتزحزح
        /// وأقدمها فوق MaxRolledFilesPerDay بيتحذف)، وبعدها الكتابة في ملف جديد. بيُستدعى جوّا الـ lock.
        /// </summary>
        public static void AppendWithRotation(string filePath, string text, long maxFileBytes = MaxFileBytes, int maxRolled = MaxRolledFilesPerDay)
        {
            if (File.Exists(filePath) && new FileInfo(filePath).Length >= maxFileBytes)
            {
                string dir = Path.GetDirectoryName(filePath) ?? ".";
                string stem = Path.GetFileNameWithoutExtension(filePath);
                string ext = Path.GetExtension(filePath);

                string Rolled(int n) => Path.Combine(dir, $"{stem}.{n}{ext}");

                if (File.Exists(Rolled(maxRolled))) File.Delete(Rolled(maxRolled));
                for (int n = maxRolled - 1; n >= 1; n--)
                {
                    if (File.Exists(Rolled(n))) File.Move(Rolled(n), Rolled(n + 1));
                }
                File.Move(filePath, Rolled(1));
            }
            File.AppendAllText(filePath, text);
        }

        /// <summary>
        /// حذف ملفات السجل (error_/info_ بصيغة الاسم المعروفة فقط) اللي تاريخها أقدم من retentionDays.
        /// أي ملف تاني في المجلد ما بيتلمسش. بترجّع عدد الملفات المحذوفة.
        /// </summary>
        public static int CleanOldLogs(string directory, DateTime now, int retentionDays = RetentionDays)
        {
            if (!Directory.Exists(directory)) return 0;
            DateTime cutoff = now.Date.AddDays(-retentionDays);
            int deleted = 0;
            foreach (var file in Directory.GetFiles(directory, "*.log"))
            {
                var m = LogNameRegex.Match(Path.GetFileName(file));
                if (!m.Success) continue;
                if (!DateTime.TryParseExact(m.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime fileDate)) continue;
                if (fileDate >= cutoff) continue;
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("Logger.CleanOldLogs failed: " + ex.Message);
                }
            }
            return deleted;
        }

        // تنظيف السجلات القديمة مرة واحدة لكل يوم (أول كتابة في اليوم/تشغيل البرنامج)
        private static void CleanupOncePerDay()
        {
            DateTime today = DateTime.Now.Date;
            if (_lastCleanupDate == today) return;
            _lastCleanupDate = today;
            CleanOldLogs(LogDirectory, DateTime.Now);
        }

        public static void LogException(Exception ex, string? context = null)
        {
            try
            {
                lock (LockObj)
                {
                    if (!Directory.Exists(LogDirectory))
                    {
                        Directory.CreateDirectory(LogDirectory);
                    }

                    CleanupOncePerDay();
                    string fileName = $"error_{DbDate.Date(DateTime.Now)}.log";
                    string filePath = Path.Combine(LogDirectory, fileName);
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
                    string message = $"[{timestamp}] [{context ?? "General"}] {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";

                    if (ex.InnerException != null)
                    {
                        message += $" --> InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}{Environment.NewLine}{ex.InnerException.StackTrace}{Environment.NewLine}";
                    }

                    message += new string('-', 80) + Environment.NewLine;
                    AppendWithRotation(filePath, message);
                }
            }
            catch (Exception writeEx)
            {
                // منع التوقف إذا فشلت كتابة السجل (الـ Logger نفسه ما ينفعش يسجّل فشله في نفسه)
                System.Diagnostics.Trace.WriteLine("Logger.LogException failed: " + writeEx.Message);
            }
        }

        public static void LogInfo(string message, string? context = null)
        {
            try
            {
                lock (LockObj)
                {
                    if (!Directory.Exists(LogDirectory))
                    {
                        Directory.CreateDirectory(LogDirectory);
                    }

                    CleanupOncePerDay();
                    string fileName = $"info_{DbDate.Date(DateTime.Now)}.log";
                    string filePath = Path.Combine(LogDirectory, fileName);
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
                    string logLine = $"[{timestamp}] [{context ?? "Info"}] {message}{Environment.NewLine}";
                    AppendWithRotation(filePath, logLine);
                }
            }
            catch (Exception writeEx)
            {
                // منع التوقف
                System.Diagnostics.Trace.WriteLine("Logger.LogInfo failed: " + writeEx.Message);
            }
        }
    }
}
