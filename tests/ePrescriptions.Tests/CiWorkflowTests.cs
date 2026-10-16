using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// خطوة 45: .github/workflows/build.yml لازم يشتغل على windows-latest، يستعيد الحزم، يبني الحل كاملاً،
    /// يشغّل كل اختبارات ePrescriptions.Tests عبر dotnet test، ويفشل عند فشل أي خطوة.
    /// الاختبار بيقرأ الملف كنص (مش بيشغّل GitHub Actions)، فالتحقق الفعلي بيحصل أول push على GitHub.
    /// </summary>
    public class CiWorkflowTests
    {
        private static string ReadWorkflow()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string c = Path.Combine(dir.FullName, ".github", "workflows", "build.yml");
                if (File.Exists(c)) return File.ReadAllText(c);
                dir = dir.Parent;
            }
            throw new FileNotFoundException(".github/workflows/build.yml");
        }

        /// <summary>يشيل سطور التعليقات الكاملة (# ...) علشان الفحص ما يتخدعش بتعليق</summary>
        private static string WithoutComments(string yml) =>
            string.Join("\n", yml.Replace("\r\n", "\n").Split('\n').Where(l => !l.TrimStart().StartsWith("#")));

        /// <summary>بيقسّم الـ workflow لخطوات (كل واحدة تبدأ بـ "- name:")، بالترتيب</summary>
        private static List<string> Steps(string yml)
        {
            string body = WithoutComments(yml);
            int start = body.IndexOf("steps:", StringComparison.Ordinal);
            start.Should().BeGreaterThan(0, "لازم قسم steps");
            var parts = Regex.Split(body.Substring(start), @"(?m)^\s*- name:").Skip(1).Select(p => "name:" + p).ToList();
            return parts;
        }

        private static int IndexOfStep(List<string> steps, string text)
        {
            for (int i = 0; i < steps.Count; i++)
                if (steps[i].Contains(text)) return i;
            return -1;
        }

        [Fact]
        public void Workflow_ShouldRunOnWindowsLatest_OnPushPullRequestAndManually()
        {
            string yml = WithoutComments(ReadWorkflow());
            yml.Should().Contain("runs-on: windows-latest");
            yml.Should().Contain("push:");
            yml.Should().Contain("pull_request:");
            yml.Should().Contain("workflow_dispatch:");
        }

        [Fact]
        public void Workflow_ShouldRestoreBuildWholeSolution_ThenRunTestsWithDotnetTest_InThatOrder()
        {
            var steps = Steps(ReadWorkflow());

            int restore = IndexOfStep(steps, "/t:Restore");
            int build = steps.FindIndex(s => s.Contains("msbuild e_prescriptions.sln") && !s.Contains("/t:Restore"));
            int test = IndexOfStep(steps, "dotnet test tests/ePrescriptions.Tests/ePrescriptions.Tests.csproj");

            restore.Should().BeGreaterThanOrEqualTo(0, "لازم خطوة استعادة حزم");
            build.Should().BeGreaterThanOrEqualTo(0, "لازم بناء الحل كاملاً (e_prescriptions.sln)");
            test.Should().BeGreaterThanOrEqualTo(0, "لازم تشغيل الاختبارات بـ dotnet test");
            restore.Should().BeLessThan(build);
            build.Should().BeLessThan(test);

            steps[build].Should().Contain("Configuration=Release");
            steps[test].Should().Contain("--no-build", "الاختبار على نفس مخرجات البناء");
        }

        [Fact]
        public void Workflow_ShouldNeverSwallowFailures()
        {
            string yml = WithoutComments(ReadWorkflow());
            yml.Should().NotContain("continue-on-error", "أي خطوة تفشل لازم تفشل الـ workflow");
            yml.Should().NotContain("|| true");
            yml.Should().NotContain("ErrorAction SilentlyContinue", "ما نخفيش أخطاء الخطوات");
        }

        [Fact]
        public void EveryStepThatRunsANativeBuildOrTestCommand_ShouldContainExactlyOneOfThem()
        {
            // pwsh على GitHub بيبص على exit code آخر أمر في الخطوة بس، فدمج أمرين = فشل الأول بيعدّي بصمت
            foreach (string step in Steps(ReadWorkflow()))
            {
                if (!step.Contains("run:")) continue;
                int native = Regex.Matches(step, @"(?m)^\s*(msbuild|dotnet|nuget|vstest\.console\.exe)\s").Count;
                native.Should().BeLessThanOrEqualTo(1, "أمر native واحد فقط في الخطوة: " + step.Split('\n')[0]);
            }
        }

        [Fact]
        public void Workflow_ShouldNotDependOnLegacyPackagesFolder_BecauseProjectsUsePackageReference()
        {
            // الخطوة القديمة كانت بتعمل Get-ChildItem على packages\ (مش موجود) فتفشل
            string yml = WithoutComments(ReadWorkflow());
            Regex.IsMatch(yml, @"Path\s+""packages""").Should().BeFalse("مفيش مجلد packages/ في مشاريع SDK-style");
            yml.Should().NotContain("nuget restore");
        }

        [Fact]
        public void Workflow_ShouldFailWhenNoTestsExecutedOrAnyTestFailed()
        {
            var steps = Steps(ReadWorkflow());
            int test = IndexOfStep(steps, "dotnet test");
            int guard = IndexOfStep(steps, "Counters");

            guard.Should().BeGreaterThan(test, "حارس النتائج بعد تشغيل الاختبارات");
            steps[test].Should().Contain("trx", "لازم ملف نتائج يتفحص");
            steps[guard].Should().Contain("executed");
            steps[guard].Should().Contain("failed");
            steps[guard].Should().Contain("throw");
        }

        [Fact]
        public void Packaging_ShouldComeAfterTests_SoFailedTestsNeverProduceAnInstaller()
        {
            var steps = Steps(ReadWorkflow());
            int test = IndexOfStep(steps, "dotnet test");
            int iscc = IndexOfStep(steps, "ISCC.exe\" installer");
            int upload = IndexOfStep(steps, "dist/installer");

            iscc.Should().BeGreaterThan(test);
            upload.Should().BeGreaterThan(iscc);
            // رفع نتائج الاختبار فقط هو اللي مسموح له if: always()
            steps[iscc].Should().NotContain("if: always()");
            steps[upload].Should().NotContain("if: always()");
        }

        [Fact]
        public void StepDetector_ShouldSplitStepsAndIgnoreComments()
        {
            string sample = "jobs:\n  j:\n    steps:\n      # - name: Fake\n      - name: A\n        run: dotnet build\n      - name: B\n        run: dotnet test\n";
            var steps = Steps(sample);
            steps.Count.Should().Be(2);
            steps[0].Should().Contain("dotnet build");
            steps[1].Should().Contain("dotnet test");
        }
    }
}
