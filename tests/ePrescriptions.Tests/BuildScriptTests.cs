using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 30: build.bat لازم يقف (exit /b 1) عند فشل أي اختبار بدل ما يكمّل ويعلن "اكتمل البناء بنجاح"</summary>
    public class BuildScriptTests
    {
        private static string ReadBuildBat()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string c = Path.Combine(dir.FullName, "build.bat");
                if (File.Exists(c)) return File.ReadAllText(c);
                dir = dir.Parent;
            }
            throw new FileNotFoundException("build.bat");
        }

        // كتلة: if %errorlevel% neq 0 ( ... ) لحد السطر اللي فيه ")" لوحدها
        private static readonly Regex FailureBlock = new Regex(
            @"if %errorlevel% neq 0 \((?<body>.*?)\r?\n\)", RegexOptions.Singleline);

        [Fact]
        public void TestRunFailure_ShouldStopTheBuild_WithNonZeroExitCode()
        {
            string bat = ReadBuildBat();
            int start = bat.IndexOf("[3/5]", StringComparison.Ordinal);
            int end = bat.IndexOf("[4/5]", StringComparison.Ordinal);
            start.Should().BeGreaterThan(0);
            end.Should().BeGreaterThan(start);

            string testSection = bat.Substring(start, end - start);
            testSection.Should().Contain("dotnet test");
            testSection.Should().Contain("vstest.console.exe");

            var m = FailureBlock.Match(testSection);
            m.Success.Should().BeTrue("لازم فحص errorlevel بعد تشغيل الاختبارات");
            m.Groups["body"].Value.Should().Contain("exit /b 1", "فشل الاختبارات لازم يوقف البناء");
            m.Groups["body"].Value.Should().NotContain("[تحذير]", "فشل الاختبار خطأ مش تحذير");
        }

        [Fact]
        public void EveryErrorlevelFailureBlock_InBuildBat_ShouldExit()
        {
            string bat = ReadBuildBat();
            var matches = FailureBlock.Matches(bat);

            matches.Count.Should().BeGreaterThanOrEqualTo(3); // restore, build, tests
            foreach (Match m in matches)
                m.Groups["body"].Value.Should().Contain("exit /b", "كتلة فشل بدون خروج: " + m.Groups["body"].Value.Trim());
        }

        [Fact]
        public void FailureBlockDetector_ShouldDistinguishStoppingFromWarningOnlyBlocks()
        {
            string stopping = "if %errorlevel% neq 0 (\n    echo x\n    exit /b 1\n)\n";
            string warnOnly = "if %errorlevel% neq 0 (\n    echo [تحذير] x\n)\n";

            FailureBlock.Match(stopping).Groups["body"].Value.Should().Contain("exit /b");
            FailureBlock.Match(warnOnly).Groups["body"].Value.Should().NotContain("exit /b");
        }
    }
}
