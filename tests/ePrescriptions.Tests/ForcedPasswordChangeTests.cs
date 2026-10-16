using System;
using System.IO;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Core.Security;
using ePrescriptions.Data;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>
    /// خطوة 46: الدخول بكلمة مرور افتراضية معروفة (pharmacy_owner / ChangeMe@Owner1 في التثبيتات القديمة والتجريبية)
    /// لازم يودّي مباشرة لتغيير كلمة المرور، ومفيش طريق لتجاوزها.
    /// </summary>
    public class ForcedPasswordChangeTests
    {
        private const string Default = "ChangeMe@Owner1";

        private class Ctx
        {
            public DatabaseContext Db = null!;
            public UserRepository Users = null!;
            public UserSessionService Session = null!;
            public LoginViewModel Vm = null!;
            public int LoginSucceededCount;
            public string DbPath = "";
        }

        private static Ctx Setup(bool withRepoInViewModel = true)
        {
            var c = new Ctx { DbPath = Path.Combine(Path.GetTempPath(), $"test_forcepw_{Guid.NewGuid():N}.db") };
            c.Db = new DatabaseContext(c.DbPath);
            c.Db.InitializeAndMigrate(seedData: true); // seedData:true = Owner بالكلمة الافتراضية (مسار الاختبارات)
            c.Users = new UserRepository(c.Db);
            c.Session = new UserSessionService(c.Users, new ShiftRepository(c.Db));
            c.Vm = withRepoInViewModel ? new LoginViewModel(c.Session, c.Users) : new LoginViewModel(c.Session);
            c.Vm.LoginSucceeded += () => c.LoginSucceededCount++;
            return c;
        }

        private static void Cleanup(Ctx c)
        {
            try { if (File.Exists(c.DbPath)) File.Delete(c.DbPath); } catch (IOException) { }
        }

        private static void TryLogin(Ctx c, string user, string pass)
        {
            c.Vm.Username = user;
            c.Vm.Password = pass;
            c.Vm.SubmitCommand.Execute(null);
        }

        [Fact]
        public void KnownDefaultDetector_ShouldMatchOnlyTheExactDefault()
        {
            DefaultCredentials.IsKnownDefaultPassword(Default).Should().BeTrue();
            DefaultCredentials.IsKnownDefaultPassword("changeme@owner1").Should().BeFalse();
            DefaultCredentials.IsKnownDefaultPassword("ChangeMe@Owner12").Should().BeFalse();
            DefaultCredentials.IsKnownDefaultPassword("").Should().BeFalse();
            DefaultCredentials.IsKnownDefaultPassword(null).Should().BeFalse();
            DatabaseContext.DefaultOwnerPassword.Should().Be(DefaultCredentials.OwnerPassword, "مصدر واحد للكلمة الافتراضية");
        }

        [Fact]
        public void LoginWithDefaultOwnerPassword_ShouldNotEnterTheSystem_AndShouldForcePasswordChange()
        {
            var c = Setup();
            try
            {
                TryLogin(c, DatabaseContext.OwnerUsername, Default);

                c.Vm.IsForcedPasswordChange.Should().BeTrue();
                c.LoginSucceededCount.Should().Be(0, "ما ينفعش يكمّل للنظام بالكلمة الافتراضية");
                c.Vm.Password.Should().BeEmpty();
            }
            finally { Cleanup(c); }
        }

        [Fact]
        public void ForcedChange_ShouldRejectMismatchEmptyShortAndDefaultPasswords_AndStayForced()
        {
            var c = Setup();
            try
            {
                TryLogin(c, DatabaseContext.OwnerUsername, Default);

                void Attempt(string np, string cf)
                {
                    c.Vm.NewPassword = np;
                    c.Vm.ConfirmNewPassword = cf;
                    c.Vm.SubmitCommand.Execute(null);
                    c.Vm.IsForcedPasswordChange.Should().BeTrue("فشل التغيير ما يفكّش الإجبار: " + np);
                    c.Vm.LoginErrorMessage.Should().NotBeEmpty();
                    c.LoginSucceededCount.Should().Be(0);
                }

                Attempt("", "");
                Attempt("NewStrong#1", "Different#2");
                Attempt("short1", "short1");
                Attempt(Default, Default);

                // كلمة المرور في القاعدة لسه الافتراضية (ما اتغيّرتش)
                c.Users.Authenticate(DatabaseContext.OwnerUsername, Default).Should().NotBeNull();
            }
            finally { Cleanup(c); }
        }

        [Fact]
        public void ForcedChange_WithValidPassword_ShouldChangeIt_EndSession_AndRequireLoginWithTheNewOne()
        {
            var c = Setup();
            try
            {
                TryLogin(c, DatabaseContext.OwnerUsername, Default);
                c.Vm.NewPassword = "MyOwn#Pass9";
                c.Vm.ConfirmNewPassword = "MyOwn#Pass9";
                c.Vm.SubmitCommand.Execute(null);

                c.Vm.IsForcedPasswordChange.Should().BeFalse();
                c.Session.IsAuthenticated.Should().BeFalse("الجلسة بتتقفل علشان الـ hash القديم ما يفضلش في الذاكرة");
                c.LoginSucceededCount.Should().Be(0, "لسه محتاج دخول بالكلمة الجديدة");
                c.Users.Authenticate(DatabaseContext.OwnerUsername, Default).Should().BeNull();
                c.Users.Authenticate(DatabaseContext.OwnerUsername, "MyOwn#Pass9").Should().NotBeNull();

                // الدخول بالكلمة القديمة يرجّع للإجبار تاني؟ لا: الكلمة القديمة اتشالت أصلاً
                TryLogin(c, DatabaseContext.OwnerUsername, Default);
                c.Vm.IsForcedPasswordChange.Should().BeFalse();
                c.LoginSucceededCount.Should().Be(0);

                // الدخول بالجديدة يعدّي عادي
                TryLogin(c, DatabaseContext.OwnerUsername, "MyOwn#Pass9");
                c.Vm.IsForcedPasswordChange.Should().BeFalse();
                c.LoginSucceededCount.Should().Be(1);
                c.Session.IsAuthenticated.Should().BeTrue();
            }
            finally { Cleanup(c); }
        }

        [Fact]
        public void CancelDuringForcedChange_ShouldEndTheSession_WithoutAnyBypass()
        {
            var c = Setup();
            try
            {
                TryLogin(c, DatabaseContext.OwnerUsername, Default);
                c.Session.IsAuthenticated.Should().BeTrue(); // الجلسة مفتوحة تقنياً لحد ما التغيير يخلص أو يتلغي

                c.Vm.CancelForcedPasswordChange();

                c.Vm.IsForcedPasswordChange.Should().BeFalse();
                c.Session.IsAuthenticated.Should().BeFalse("الإغلاق/الخروج لازم يقفل الجلسة");
                c.LoginSucceededCount.Should().Be(0);

                // ExitAppCommand بيلغي برضه (نفس المسار)
                TryLogin(c, DatabaseContext.OwnerUsername, Default);
                c.Vm.ExitAppCommand.Execute(null);
                c.Session.IsAuthenticated.Should().BeFalse();
            }
            finally { Cleanup(c); }
        }

        [Fact]
        public void NormalPasswords_ShouldLoginDirectly_WithoutForcedChange()
        {
            var c = Setup();
            try
            {
                c.Users.SaveStaffUser(0, "cashier1", "كاشير", "Cashier#Pass1", UserRole.Cashier, true);

                TryLogin(c, "cashier1", "Cashier#Pass1");

                c.Vm.IsForcedPasswordChange.Should().BeFalse();
                c.LoginSucceededCount.Should().Be(1);
            }
            finally { Cleanup(c); }
        }

        [Fact]
        public void AnyAccountUsingTheKnownDefaultPassword_ShouldBeForced_NotOnlyOwner()
        {
            var c = Setup();
            try
            {
                c.Users.SaveStaffUser(0, "pharm1", "صيدلي", Default, UserRole.Pharmacist, true);

                TryLogin(c, "pharm1", Default);

                c.Vm.IsForcedPasswordChange.Should().BeTrue();
                c.LoginSucceededCount.Should().Be(0);
            }
            finally { Cleanup(c); }
        }

        [Fact]
        public void WithoutARepository_DefaultPasswordLogin_ShouldBeRefused_NotAllowed()
        {
            var c = Setup(withRepoInViewModel: false);
            try
            {
                TryLogin(c, DatabaseContext.OwnerUsername, Default);

                c.Vm.IsForcedPasswordChange.Should().BeFalse();
                c.Session.IsAuthenticated.Should().BeFalse("مفيش وسيلة تغيير = رفض، مش سماح");
                c.LoginSucceededCount.Should().Be(0);
                c.Vm.LoginErrorMessage.Should().NotBeEmpty();
            }
            finally { Cleanup(c); }
        }

        [Fact]
        public void ChangeOwnPassword_ShouldRejectDefaultAndUnchangedPasswords()
        {
            var c = Setup();
            try
            {
                var owner = c.Users.Authenticate(DatabaseContext.OwnerUsername, Default)!;

                c.Users.ChangeOwnPassword(owner.Id, Default, Default, out string e1).Should().BeFalse();
                e1.Should().NotBeEmpty();

                c.Users.ChangeOwnPassword(owner.Id, Default, "Another#Pass7", out _).Should().BeTrue();
                c.Users.ChangeOwnPassword(owner.Id, "Another#Pass7", "Another#Pass7", out string e2).Should().BeFalse();
                e2.Should().NotBeEmpty();
                c.Users.ChangeOwnPassword(owner.Id, "Another#Pass7", Default, out _).Should().BeFalse("ممنوع الرجوع للافتراضية");
            }
            finally { Cleanup(c); }
        }
    }
}
