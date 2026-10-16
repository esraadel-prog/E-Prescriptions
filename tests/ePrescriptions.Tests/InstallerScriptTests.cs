using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ePrescriptions.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// فحص نصي لسكريبت المثبّت (ما فيش مترجم Inno على بيئة الاختبار): متطلب .NET 4.8 وليس 4.8.1، رسالة ويندوز 7،
    /// وسؤال حذف البيانات عند الإزالة (الافتراضي لا، وتأكيد مزدوج، ولا حذف في الإزالة الصامتة).
    /// </summary>
    public class InstallerScriptTests
    {
        private static string Iss()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "installer", "e_prescriptions_installer.iss");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                dir = dir.Parent;
            }
            throw new FileNotFoundException("installer/e_prescriptions_installer.iss");
        }

        [Fact]
        public void Requirement_ShouldBeNet48NotNet481_AndWindows7Sp1Minimum()
        {
            string iss = Iss();
            iss.Contains("ReleaseValue >= 528040").Should().BeTrue();       // 4.8
            iss.Contains("533320").Should().BeFalse();                      // 4.8.1 (ما يتطلبهوش)
            iss.Contains("MinVersion=6.1sp1").Should().BeTrue();
        }

        [Fact]
        public void Windows7Message_ShouldListPrerequisiteUpdatesAndSayNet481IsNotSupported()
        {
            string iss = Iss();
            iss.Contains("KB4474419").Should().BeTrue();
            iss.Contains("KB4490628").Should().BeTrue();
            iss.Contains("TLS 1.2").Should().BeTrue();
            iss.Contains("4.8.1").Should().BeTrue();                        // مذكور فقط في الرسالة التوضيحية
            iss.Contains("IsWindows7()").Should().BeTrue();
        }

        [Fact]
        public void UninstallDataPrompt_ShouldDefaultToNo_ConfirmTwice_SkipWhenSilent_AndOnlyDeleteTheDataFolder()
        {
            string iss = Iss();
            int step = iss.IndexOf("procedure CurUninstallStepChanged", StringComparison.Ordinal);
            step.Should().BeGreaterThan(0);
            string body = iss.Substring(step);

            body.Contains("usPostUninstall").Should().BeTrue();
            body.Contains("UninstallSilent()").Should().BeTrue();
            Regex.Matches(body, "MB_DEFBUTTON2").Count.Should().Be(2);       // سؤالان، الافتراضي "لا" فيهما
            Regex.Matches(body, "DelTree\\(").Count.Should().Be(1);

            // الحذف بعد السؤالين، ومسار البيانات هو نفس مجلد التطبيق
            body.LastIndexOf("MB_DEFBUTTON2", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf("DelTree(", StringComparison.Ordinal));
            body.Contains("{commonappdata}\\" + System.IO.Path.GetFileName(AppPaths.DataDirectory)).Should().BeTrue();

            // مجلدات البيانات تفضل محمية من الإزالة العادية
            Regex.Matches(iss, "uninsneveruninstall").Count.Should().BeGreaterThan(0);
        }

        [Fact]
        public void OptionalNet48Redist_ShouldBeConsistentAndPreprocessorBlocksBalanced()
        {
            string iss = Iss();
            int opens = Regex.Matches(iss, @"^\s*#(ifdef|ifexist|ifndef|if)\b", RegexOptions.Multiline).Count;
            int closes = Regex.Matches(iss, @"^\s*#endif\b", RegexOptions.Multiline).Count;
            opens.Should().Be(closes);

            string name = Regex.Match(iss, "#define Net48RedistName \"([^\"]+)\"").Groups[1].Value;
            name.Should().Be("ndp48-x86-x64-allos-enu.exe");
            iss.Contains("#ifexist \"redist\\" + name + "\"").Should().BeTrue();
            iss.Contains("ExtractTemporaryFile('{#Net48RedistName}')").Should().BeTrue();
            iss.Contains("Flags: dontcopy").Should().BeTrue();

            // begin/end متوازنة في كود Pascal (ما عدا بلوكات الـ #ifdef اللي بتتبدّل مع بعض)
            int codeStart = iss.IndexOf("[Code]", StringComparison.Ordinal);
            string code = iss.Substring(codeStart);
            var tokens = Regex.Matches(code, @"\b(begin|end|case)\b", RegexOptions.IgnoreCase).Cast<Match>().Select(m => m.Value.ToLowerInvariant());
            int begins = tokens.Count(t => t == "begin" || t == "case");
            int ends = tokens.Count(t => t == "end");
            begins.Should().Be(ends);
        }
    }
}
