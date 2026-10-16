using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ePrescriptions.Core.Helpers;

namespace ePrescriptions.App.Services
{
    /// <summary>
    /// خطوة 41 (جزء ب): بيشغّل جدولة النسخ في التطبيق. مؤقّت على خيط الواجهة بيستدعي
    /// <see cref="IBackupAutomationService"/> (الشغل الفعلي على خيط خلفي) وبيحتفظ بتنبيهات الصحة للشريط في الشاشة الرئيسية.
    /// أول فحص بعد دقيقة من التشغيل (عشان ما نبطّأش فتح البرنامج)، وبعدها كل 5 دقايق.
    /// </summary>
    public class BackupSchedulerHost
    {
        public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

        private readonly IBackupAutomationService _automation;
        private readonly Func<BackupSettings> _settingsProvider;
        private readonly Func<DateTime> _clock;
        private DispatcherTimer? _timer;
        private int _busy;
        private int _suspended;
        private IReadOnlyList<string> _alerts = new List<string>();

        /// <summary>بيتطلق بعد أي تغيير في التنبيهات (على خيط الواجهة لو الاستدعاء جه منه)</summary>
        public event Action? AlertsChanged;

        public BackupSchedulerHost(IBackupAutomationService automation, Func<BackupSettings>? settingsProvider = null, Func<DateTime>? clock = null)
        {
            _automation = automation;
            _settingsProvider = settingsProvider ?? (() => BackupSettingsStore.Load());
            _clock = clock ?? (() => DateTime.Now);
        }

        public IReadOnlyList<string> Alerts => _alerts;

        public bool HasAlerts => _alerts.Count > 0;

        /// <summary>نص الشريط: كل تنبيه في سطر</summary>
        public string AlertText => string.Join(Environment.NewLine, _alerts);

        public bool IsSuspended => Volatile.Read(ref _suspended) > 0;

        /// <summary>بدء المؤقّت (آمن للاستدعاء أكتر من مرة)</summary>
        public void Start()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer { Interval = FirstCheckDelay };
            _timer.Tick += OnTimerTick;
            _timer.Start();
            RefreshAlerts(); // لو فيه تنبيه محفوظ من جلسة سابقة يظهر فوراً
        }

        public void Stop()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        /// <summary>إيقاف مؤقت للجدولة (مثلاً أثناء استعادة قاعدة البيانات اللي بتستبدل الملف). لازم يتقابل مع <see cref="Resume"/>.</summary>
        public void Suspend() => Interlocked.Increment(ref _suspended);

        public void Resume()
        {
            if (Interlocked.Decrement(ref _suspended) < 0) Interlocked.Exchange(ref _suspended, 0);
        }

        private async void OnTimerTick(object? sender, EventArgs e)
        {
            try
            {
                if (_timer != null && _timer.Interval != CheckInterval) _timer.Interval = CheckInterval;
                await RunTickAsync();
            }
            catch (Exception ex)
            {
                // async void: أي استثناء هنا كان هيوقّع البرنامج كله
                Logger.LogException(ex, "BackupSchedulerHost.OnTimerTick");
            }
        }

        /// <summary>
        /// جولة واحدة: يرجّع null لو اتجاهلت (جولة شغّالة أو الجدولة موقوفة مؤقتاً). لا ترمي استثناءات.
        /// </summary>
        public async Task<BackupAutomationResult?> RunTickAsync()
        {
            if (IsSuspended) return null;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return null;

            try
            {
                var settings = _settingsProvider();
                var result = await _automation.RunDueTasksAsync(settings, _clock());
                SetAlerts(result.Alerts);
                return result;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupSchedulerHost.RunTickAsync");
                return null;
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        /// <summary>إعادة قراءة التنبيهات من ملف الحالة (بعد نسخة يدوية أو حفظ إعدادات أو اختبار استعادة يدوي)</summary>
        public void RefreshAlerts()
        {
            try
            {
                SetAlerts(_automation.GetAlerts(_settingsProvider(), _clock()));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "BackupSchedulerHost.RefreshAlerts");
            }
        }

        private void SetAlerts(IReadOnlyList<string> alerts)
        {
            bool changed = alerts.Count != _alerts.Count;
            if (!changed)
            {
                for (int i = 0; i < alerts.Count; i++)
                {
                    if (!string.Equals(alerts[i], _alerts[i], StringComparison.Ordinal)) { changed = true; break; }
                }
            }
            _alerts = alerts;
            if (changed) AlertsChanged?.Invoke();
        }
    }
}
