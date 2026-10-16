using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// طلب صاحب المشروع: الاسم الشخصي القديم ما يفضلش في أي ملف مصدر/توثيق (إلا اسم حساب pharmacy_owner)،
    /// وفي أسفل البرنامج فوتر ثابت: E-Prescriptions by Maged Fayez | (C) all rights reserved
    /// </summary>
    public class NoPersonalNameAndFooterTests
    {
        public const string FooterText = "E-Prescriptions by Maged Fayez | (C) all rights reserved";

        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "e_prescriptions.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("e_prescriptions.sln");
        }

        [Fact]
        public void OldPersonalName_ShouldNotAppearInAnyTextFileOfTheProject()
        {
            // الأنماط مبنية بالتجميع علشان الاختبار نفسه ما يحتويش الاسم حرفياً
            string[] patterns = { "ماجد" + " القبان", "Al-" + "Qab" + "bani", "Qab" + "bani" };
            string[] exts = { ".cs", ".xaml", ".sql", ".iss", ".bat", ".ps1", ".md", ".json", ".ts", ".txt", ".yml", ".csproj" };

            var offenders = Directory.EnumerateFiles(Root(), "*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
                .Where(f => { string t = File.ReadAllText(f); return patterns.Any(p => t.Contains(p)); })
                .Select(f => f.Substring(Root().Length))
                .ToList();

            offenders.Should().BeEmpty("الاسم القديم لسه موجود في: " + string.Join(", ", offenders));
        }

        [Fact]
        public void Footer_ShouldBeAtTheBottomOfMainWindowAndLoginWindow()
        {
            string main = File.ReadAllText(Path.Combine(Root(), "src", "ePrescriptions.App", "MainWindow.xaml"));
            string login = File.ReadAllText(Path.Combine(Root(), "src", "ePrescriptions.App", "Views", "LoginWindow.xaml"));

            main.Should().Contain("Text=\"" + FooterText + "\"");
            login.Should().Contain("Text=\"" + FooterText + "\"");

            // الفوتر في آخر صف من الشبكة الرئيسية (صف 3 بعد الصفوف 0-2)
            main.Should().Contain("<Border Grid.Row=\"3\"");
            int rows = System.Text.RegularExpressions.Regex.Matches(main.Substring(0, main.IndexOf("</Grid.RowDefinitions>", StringComparison.Ordinal)), "<RowDefinition").Count;
            rows.Should().BeGreaterThanOrEqualTo(4);
        }

        [Fact]
        public void OwnerAccountUsername_ShouldRemain()
        {
            ePrescriptions.Data.DatabaseContext.OwnerUsername.Should().Be("pharmacy_owner");
        }
    }
}
