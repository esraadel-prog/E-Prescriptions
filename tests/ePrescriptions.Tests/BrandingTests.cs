using System;
using System.IO;
using ePrescriptions.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// هوية البرنامج: "E-Prescription by Maged Fayez" + حقوق النشر في الفوتر والمثبّت والإيصال الحراري وتقرير PDF وخصائص الملف.
    /// فحص نصي (الواجهة والطباعة الفعلية تتأكد على Windows).
    /// </summary>
    public class BrandingTests
    {
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

        private static string Read(params string[] parts)
        {
            var all = new string[parts.Length + 1];
            all[0] = Root();
            Array.Copy(parts, 0, all, 1, parts.Length);
            return File.ReadAllText(Path.Combine(all));
        }

        [Fact]
        public void Constants_ShouldProduceTheRequiredByline()
        {
            AppBranding.ProductByline.Should().Be("E-Prescriptions by Maged Fayez");
            AppBranding.ScreenFooter.Should().Be("E-Prescriptions by Maged Fayez | (C) all rights reserved");
            AppBranding.UnifiedBranding.Should().Be("E-Prescriptions by Maged Fayez | (C) all rights reserved");
            AppBranding.Copyright.Should().Contain("all rights reserved");
        }

        [Fact]
        public void ThermalReceipt_ShouldPrintBylineAndCopyrightInFooter()
        {
            string src = Read("src", "ePrescriptions.App", "Services", "ThermalReceiptPrinter.cs");
            src.Should().Contain("AppBranding.ProductByline");
            src.Should().Contain("AppBranding.Copyright");
            src.Should().NotContain("نظام إدارة الصيدليات e_prescriptions");
        }

        [Fact]
        public void InvoicePdf_ShouldPrintBylineAndCopyrightInFooter()
        {
            string src = Read("src", "ePrescriptions.App", "Services", "PdfReportService.cs");
            src.Should().Contain("AppBranding.ProductByline");
            src.Should().Contain("AppBranding.Copyright");
        }

        [Fact]
        public void Installer_ShouldCarryPublisherCopyrightAndProductName()
        {
            string iss = Read("installer", "e_prescriptions_installer.iss");
            iss.Should().Contain("#define MyAppPublisher \"Maged Fayez\"");
            iss.Should().Contain("#define MyAppDisplayName \"" + AppBranding.ProductByline + "\"");
            iss.Should().Contain("AppCopyright=");
            iss.Should().Contain("VersionInfoCompany=");
            iss.Should().Contain("VersionInfoCopyright=");
            iss.Should().Contain("Maged Fayez. All rights reserved.");
            iss.Should().Contain("AppVerName={#MyAppDisplayName}");
            // AppId ثابت علشان التحديث فوق نسخة قديمة يفضل شغّال
            iss.Should().Contain("AppId={{9F82A41C-328E-45A7-A279-99E8F8B4C011}");
        }

        [Theory]
        [InlineData("App")]
        [InlineData("Core")]
        [InlineData("Domain")]
        [InlineData("Data")]
        public void Projects_ShouldCarryCompanyProductAndCopyright(string project)
        {
            string csproj = Read("src", "ePrescriptions." + project, "ePrescriptions." + project + ".csproj");
            csproj.Should().Contain("<Company>Maged Fayez</Company>");
            csproj.Should().Contain("<Product>" + AppBranding.ProductByline + "</Product>");
            csproj.Should().Contain("<Copyright>" + AppBranding.Copyright + "</Copyright>");
        }

        [Fact]
        public void ScreensFooter_ShouldShowBylineAndCopyrightOnLoginAndMain()
        {
            string login = Read("src", "ePrescriptions.App", "Views", "LoginWindow.xaml");
            string main = Read("src", "ePrescriptions.App", "MainWindow.xaml");
            login.Should().Contain(AppBranding.ProductByline);
            login.Should().Contain(AppBranding.Copyright);
            main.Should().Contain(AppBranding.ProductByline);
            main.Should().Contain(AppBranding.Copyright);
            login.Should().Contain("Text=\"" + AppBranding.ScreenFooter + "\"");
            main.Should().Contain("Text=\"" + AppBranding.ScreenFooter + "\"");
        }
    }
}
