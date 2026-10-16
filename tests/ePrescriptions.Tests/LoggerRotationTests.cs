using System;
using System.IO;
using System.Linq;
using ePrescriptions.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 31: تدوير ملفات الـ Logger (حد حجم + أجزاء مؤرشفة + حذف الأقدم من مدة الاحتفاظ)</summary>
    public class LoggerRotationTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "logrot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void AppendWithRotation_ShouldRollFile_WhenSizeLimitReached()
        {
            string dir = NewTempDir();
            try
            {
                string path = Path.Combine(dir, "error_2026-10-12.log");
                File.WriteAllText(path, new string('x', 100));

                Logger.AppendWithRotation(path, "NEW", maxFileBytes: 100, maxRolled: 3);

                File.ReadAllText(path).Should().Be("NEW");
                File.Exists(Path.Combine(dir, "error_2026-10-12.1.log")).Should().BeTrue();
                new FileInfo(Path.Combine(dir, "error_2026-10-12.1.log")).Length.Should().Be(100);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void AppendWithRotation_ShouldNotRoll_WhenUnderLimit()
        {
            string dir = NewTempDir();
            try
            {
                string path = Path.Combine(dir, "info_2026-10-12.log");
                File.WriteAllText(path, "abc");

                Logger.AppendWithRotation(path, "def", maxFileBytes: 100, maxRolled: 3);

                File.ReadAllText(path).Should().Be("abcdef");
                Directory.GetFiles(dir).Length.Should().Be(1);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void AppendWithRotation_ShouldKeepOnlyMaxRolledParts_AndDropOldest()
        {
            string dir = NewTempDir();
            try
            {
                string path = Path.Combine(dir, "error_2026-10-12.log");
                // 4 دورات متتالية: محتوى كل دورة مميّز عشان نتأكد مين اتحذف
                for (int i = 1; i <= 4; i++)
                {
                    File.WriteAllText(path, "gen" + i + new string('x', 100));
                    Logger.AppendWithRotation(path, "cur", maxFileBytes: 100, maxRolled: 2);
                }

                File.Exists(Path.Combine(dir, "error_2026-10-12.3.log")).Should().BeFalse();
                File.ReadAllText(Path.Combine(dir, "error_2026-10-12.1.log")).StartsWith("gen4").Should().BeTrue();
                File.ReadAllText(Path.Combine(dir, "error_2026-10-12.2.log")).StartsWith("gen3").Should().BeTrue();
                File.ReadAllText(path).Should().Be("cur");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void CleanOldLogs_ShouldDeleteOnlyOldLogFiles_AndLeaveOthersAlone()
        {
            string dir = NewTempDir();
            try
            {
                var now = new DateTime(2026, 10, 12);
                string old1 = Path.Combine(dir, "error_2026-08-01.log");
                string old2 = Path.Combine(dir, "info_2026-07-15.2.log");
                string recent = Path.Combine(dir, "error_2026-10-01.log");
                string edge = Path.Combine(dir, "info_2026-09-12.log");     // بالظبط 30 يوم: يفضل
                string foreign = Path.Combine(dir, "notes_2020-01-01.log"); // مش بصيغة الـ Logger: ما يتلمسش
                foreach (var f in new[] { old1, old2, recent, edge, foreign }) File.WriteAllText(f, "x");

                int deleted = Logger.CleanOldLogs(dir, now, retentionDays: 30);

                deleted.Should().Be(2);
                File.Exists(old1).Should().BeFalse();
                File.Exists(old2).Should().BeFalse();
                File.Exists(recent).Should().BeTrue();
                File.Exists(edge).Should().BeTrue();
                File.Exists(foreign).Should().BeTrue();
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void CleanOldLogs_ShouldReturnZero_WhenDirectoryMissing()
        {
            Logger.CleanOldLogs(Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N")), DateTime.Now)
                .Should().Be(0);
        }
    }
}
