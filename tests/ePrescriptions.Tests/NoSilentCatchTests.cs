using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 27: ممنوع catch فاضية (أو فيها تعليق بس) في كود src؛ لازم تسجيل الخطأ على الأقل</summary>
    public class NoSilentCatchTests
    {
        private static string FindSrc()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string c = Path.Combine(dir.FullName, "src");
                if (Directory.Exists(Path.Combine(c, "ePrescriptions.Data"))) return c;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("src");
        }

        // catch / catch (T) / catch (T x) ثم { } فاضية أو فيها تعليقات فقط
        private static readonly Regex EmptyCatch = new Regex(
            @"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\)\s*)?\{(\s|//[^\n]*\n|/\*.*?\*/)*\}", RegexOptions.Singleline);

        [Fact]
        public void NoEmptyCatchBlocks_InSourceCode()
        {
            foreach (var file in Directory.GetFiles(FindSrc(), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
                string text = File.ReadAllText(file);
                var m = EmptyCatch.Match(text);
                int line = m.Success ? text.Substring(0, m.Index).Split('\n').Length : 0;
                m.Success.Should().BeFalse($"catch فاضية في {Path.GetFileName(file)} سطر {line}");
            }
        }

        [Fact]
        public void EmptyCatchDetector_ShouldCatchTheKnownPatterns()
        {
            EmptyCatch.IsMatch("try { x(); } catch { }").Should().BeTrue();
            EmptyCatch.IsMatch("try { x(); } catch (Exception) { /* تجاهل */ }").Should().BeTrue();
            EmptyCatch.IsMatch("try {x();}\ncatch (ObjectDisposedException)\n{\n  // لا شيء\n}").Should().BeTrue();
            EmptyCatch.IsMatch("try { x(); } catch (Exception ex) { Logger.LogException(ex, \"c\"); }").Should().BeFalse();
        }
    }
}
