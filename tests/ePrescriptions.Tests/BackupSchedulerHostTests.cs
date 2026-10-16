using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using ePrescriptions.App.Services;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data;
using FluentAssertions;
using Xunit;

namespace ePrescriptions.Tests
{
    /// <summary>خطوة 41 (جزء ب): مضيف الجدولة (BackupSchedulerHost): جولة المؤقّت، التنبيهات، الإيقاف المؤقت أثناء الاستعادة</summary>
    public class BackupSchedulerHostTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ep_bkhost_" + Guid.NewGuid().ToString("N"));
        private readonly DatabaseContext _db;
        private readonly BackupStatusStore _store;
        private readonly BackupService _svc;
        private readonly BackupAutomationService _auto;
        private readonly BackupSettings _settings;
        private DateTime _now = new DateTime(2026, 10, 14, 23, 30, 0);

        public BackupSchedulerHostTests()
        {
            Directory.CreateDirectory(_root);
            _db = new DatabaseContext(Path.Combine(_root, "live.db"));
            _db.InitializeAndMigrate(seedData: false);
            _store = new BackupStatusStore(Path.Combine(_root, "backup_status.ini"));
            _svc = new BackupService(_db, _store, () => _now);
            _auto = new BackupAutomationService(_svc, _store);
            _settings = new BackupSettings { PrimaryFolder = Path.Combine(_root, "primary") };
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_root, true); } catch { }
        }

        private BackupSchedulerHost NewHost(Func<BackupSettings>? settings = null) =>
            new BackupSchedulerHost(_auto, settings ?? (() => _settings), () => _now);

        private int BackupCount() =>
            Directory.Exists(_settings.PrimaryFolder) ? Directory.GetFiles(_settings.PrimaryFolder, BackupService.BackupFilePattern).Length : 0;

        /// <summary>أتمتة وهمية بتوقف الجولة لحد ما الاختبار يفرج عنها (لاختبار حارس "جولة واحدة")</summary>
        private class BlockingAutomation : IBackupAutomationService
        {
            public readonly TaskCompletionSource<BackupAutomationResult> Gate = new TaskCompletionSource<BackupAutomationResult>();
            public int Calls;
            public BackupAutomationResult RunDueTasks(BackupSettings s, DateTime now) => throw new NotSupportedException();
            public Task<BackupAutomationResult> RunDueTasksAsync(BackupSettings s, DateTime now) { Calls++; return Gate.Task; }
            public BackupVerifyResult RunRestoreTestNow(BackupSettings s, DateTime now) => throw new NotSupportedException();
            public Task<BackupVerifyResult> RunRestoreTestNowAsync(BackupSettings s, DateTime now) => throw new NotSupportedException();
            public IReadOnlyList<string> GetAlerts(BackupSettings s, DateTime now) => new List<string>();
        }

        [Fact]
        public void Tick_ShouldTakeDueBackup_AndLeaveNoAlerts()
        {
            var host = NewHost();
            int raised = 0;
            host.AlertsChanged += () => raised++;

            var r = host.RunTickAsync().GetAwaiter().GetResult();

            r.Should().NotBeNull();
            r!.BackupAttempted.Should().BeTrue();
            BackupCount().Should().Be(1);
            host.HasAlerts.Should().BeFalse();
            host.AlertText.Should().BeEmpty();
            raised.Should().Be(0);
        }

        [Fact]
        public void Tick_WhenBackupFails_ShouldRaiseAlertOnce_ThenClearItWhenFixed()
        {
            string blocker = Path.Combine(_root, "iam_a_file");
            File.WriteAllText(blocker, "x");
            var broken = new BackupSettings { PrimaryFolder = Path.Combine(blocker, "sub") };
            BackupSettings current = broken;
            var host = NewHost(() => current);
            int raised = 0;
            host.AlertsChanged += () => raised++;

            host.RunTickAsync().GetAwaiter().GetResult();
            host.HasAlerts.Should().BeTrue();
            host.AlertText.Should().Contain("فشلت");
            raised.Should().Be(1);

            // نفس الحالة تاني (جولة تانية بعد فاصل إعادة المحاولة وفشل تاني) ما تعيدش إطلاق الحدث لو النص نفسه
            host.RefreshAlerts();
            raised.Should().Be(1);

            _now = _now.AddMinutes(31);
            current = _settings;
            host.RunTickAsync().GetAwaiter().GetResult();
            host.HasAlerts.Should().BeFalse();
            raised.Should().Be(2);
        }

        [Fact]
        public void Suspend_ShouldSkipTicks_UntilResumed_AndSupportNesting()
        {
            var host = NewHost();
            host.Suspend();
            host.Suspend();
            host.RunTickAsync().GetAwaiter().GetResult().Should().BeNull();
            BackupCount().Should().Be(0);

            host.Resume();
            host.IsSuspended.Should().BeTrue();
            host.RunTickAsync().GetAwaiter().GetResult().Should().BeNull();

            host.Resume();
            host.IsSuspended.Should().BeFalse();
            host.RunTickAsync().GetAwaiter().GetResult().Should().NotBeNull();
            BackupCount().Should().Be(1);

            host.Resume(); // استدعاء زيادة ما يخليش العدّاد سالب
            host.IsSuspended.Should().BeFalse();
        }

        [Fact]
        public void Tick_ShouldRunOnlyOneRoundAtATime()
        {
            var blocking = new BlockingAutomation();
            var host = new BackupSchedulerHost(blocking, () => _settings, () => _now);

            var first = host.RunTickAsync();                      // واقفة على البوابة
            var second = host.RunTickAsync().GetAwaiter().GetResult();
            second.Should().BeNull();
            blocking.Calls.Should().Be(1);

            blocking.Gate.SetResult(new BackupAutomationResult { Alerts = new List<string> { "⚠ تجربة" } });
            first.GetAwaiter().GetResult().Should().NotBeNull();
            host.AlertText.Should().Be("⚠ تجربة");

            host.RunTickAsync().GetAwaiter().GetResult(); // بعد الانتهاء الجولة التالية مسموحة
            blocking.Calls.Should().Be(2);
        }

        [Fact]
        public void Tick_ShouldNeverThrow_WhenSettingsProviderFails()
        {
            var host = NewHost(() => throw new InvalidOperationException("boom"));
            host.RunTickAsync().GetAwaiter().GetResult().Should().BeNull();
            host.RefreshAlerts(); // ما يرميش
            host.HasAlerts.Should().BeFalse();
        }

        [Fact]
        public void RefreshAlerts_ShouldPickUpStoredFailureFromEarlierSession_AndStartShouldBeIdempotent()
        {
            _store.RecordBackupFailure(_now.AddHours(-1), "القرص ممتلئ");
            var host = NewHost();
            int raised = 0;
            host.AlertsChanged += () => raised++;

            host.Start();
            host.Start(); // مرتين بدون مشاكل
            host.HasAlerts.Should().BeTrue();
            host.AlertText.Should().Contain("القرص ممتلئ");
            raised.Should().Be(1);

            host.Stop();
            host.Stop();
        }
    }
}
