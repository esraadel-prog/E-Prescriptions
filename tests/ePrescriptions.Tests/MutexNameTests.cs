using System;
using System.IO;
using System.Text.RegularExpressions;
using ePrescriptions.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 14: اسم الـ Mutex في التطبيق والمثبّت متطابق حرفياً</summary>
    public class MutexNameTests
    {
        private static string FindInstallerScript()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "installer", "e_prescriptions_installer.iss");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("تعذر العثور على installer/e_prescriptions_installer.iss");
        }

        [Fact]
        public void InstallerMutexName_ShouldMatchApplicationMutexNameExactly()
        {
            string iss = File.ReadAllText(FindInstallerScript());
            var matches = Regex.Matches(iss, @"CheckForMutexes\('([^']+)'\)");

            matches.Count.Should().BeGreaterThan(0);
            foreach (Match m in matches)
                m.Groups[1].Value.Should().Be(SingleInstanceMutex.MutexBaseName);

            SingleInstanceMutex.DefaultMutexName.EndsWith(SingleInstanceMutex.MutexBaseName).Should().BeTrue();
            iss.Contains("e_prescriptions_SingleInstance_App_Mutex").Should().BeFalse();
        }
    }
}
