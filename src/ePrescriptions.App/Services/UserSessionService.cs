using System;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Security;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.App.Services
{
    public interface IUserSessionService
    {
        User? CurrentUser { get; }
        Shift? CurrentShift { get; }
        bool IsAuthenticated { get; }
        bool IsLocked { get; }

        event Action? SessionChanged;
        event Action<bool>? LockStateChanged;

        bool Login(string username, string password, out string errorMessage);
        void Logout();
        void Lock();
        bool Unlock(string password, out string errorMessage);
        void SetCurrentShift(Shift? shift);
    }

    public class UserSessionService : IUserSessionService
    {
        private readonly UserRepository _userRepository;
        private readonly ShiftRepository _shiftRepository;

        private User? _currentUser;
        public User? CurrentUser => _currentUser;

        private Shift? _currentShift;
        public Shift? CurrentShift => _currentShift;

        public bool IsAuthenticated => _currentUser != null;

        /// <summary>عدد محاولات فك قفل الشاشة الخاطئة المسموحة قبل إجبار الخروج لتسجيل الدخول الكامل (اللي ليه قفل الحساب)</summary>
        public const int MaxUnlockAttempts = 5;
        private int _failedUnlockAttempts;

        private bool _isLocked = false;
        public bool IsLocked => _isLocked;

        public event Action? SessionChanged;
        public event Action<bool>? LockStateChanged;

        public UserSessionService(UserRepository userRepository, ShiftRepository shiftRepository)
        {
            _userRepository = userRepository;
            _shiftRepository = shiftRepository;
        }

        public bool Login(string username, string password, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                errorMessage = "يرجى إدخال اسم المستخدم وكلمة المرور.";
                return false;
            }

            var user = _userRepository.Authenticate(username, password, out string lockMessage);
            if (user == null)
            {
                errorMessage = lockMessage.Length > 0 ? lockMessage : "اسم المستخدم أو كلمة المرور غير صحيحة.";
                Logger.LogInfo($"محاولة تسجيل دخول فاشلة للمستخدم: {username}", "UserSessionService.Login");
                return false;
            }
            _failedUnlockAttempts = 0;

            _currentUser = user;
            _isLocked = false;
            _failedUnlockAttempts = 0;

            // جلب الوردية المفتوحة الحالية إن وجدت
            _currentShift = _shiftRepository.GetCurrentOpenShift(user.Id);

            Logger.LogInfo($"تسجيل دخول ناجح: {user.FullName} ({user.Role})", "UserSessionService.Login");
            SessionChanged?.Invoke();
            LockStateChanged?.Invoke(false);
            return true;
        }

        public void Logout()
        {
            if (_currentUser != null)
            {
                Logger.LogInfo($"تسجيل خروج للمستخدم: {_currentUser.FullName}", "UserSessionService.Logout");
            }

            _currentUser = null;
            _currentShift = null;
            _isLocked = false;

            SessionChanged?.Invoke();
            LockStateChanged?.Invoke(false);
        }

        public void Lock()
        {
            if (!IsAuthenticated) return;
            _isLocked = true;
            Logger.LogInfo($"تم قفل شاشة الصيدلية بواسطة: {_currentUser?.FullName}", "UserSessionService.Lock");
            LockStateChanged?.Invoke(true);
        }

        public bool Unlock(string password, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (_currentUser == null)
            {
                errorMessage = "لا يوجد مستخدم نشط.";
                return false;
            }

            if (PasswordHasher.VerifyPassword(password, _currentUser.PasswordHash))
            {
                _failedUnlockAttempts = 0;
                _isLocked = false;
                Logger.LogInfo($"تم إلغاء قفل الشاشة بواسطة: {_currentUser.FullName}", "UserSessionService.Unlock");
                LockStateChanged?.Invoke(false);
                return true;
            }

            _failedUnlockAttempts++;
            if (_failedUnlockAttempts >= MaxUnlockAttempts)
            {
                Logger.LogInfo($"تجاوز محاولات فك القفل للمستخدم: {_currentUser.FullName}، تم تسجيل الخروج.", "UserSessionService.Unlock");
                _failedUnlockAttempts = 0;
                Logout();
                errorMessage = "تكررت المحاولات الخاطئة. تم تسجيل الخروج؛ سجّل الدخول من جديد.";
                return false;
            }

            errorMessage = "كلمة المرور غير صحيحة.";
            return false;
        }

        public void SetCurrentShift(Shift? shift)
        {
            _currentShift = shift;
            SessionChanged?.Invoke();
        }
    }
}

namespace ePrescriptions.App.Services
{
    /// <summary>
    /// قفل تلقائي بعد خمول: الواجهة بتبلّغه بأي نشاط (NotifyActivity) ومؤقّت بيستدعي Tick كل شوية.
    /// لو المستخدم مسجّل دخوله والشاشة مش مقفولة ومرّ عليه Timeout بدون نشاط، بيتقفل. Timeout = صفر يعطّل الميزة.
    /// </summary>
    public class IdleLockMonitor
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

        private readonly IUserSessionService _session;
        private DateTime _lastActivity;

        public Func<DateTime> Clock { get; set; } = () => DateTime.Now;
        public TimeSpan Timeout { get; set; } = DefaultTimeout;

        public IdleLockMonitor(IUserSessionService session)
        {
            _session = session;
            _lastActivity = Clock();
        }

        public void NotifyActivity() => _lastActivity = Clock();

        /// <summary>يرجع true لو قفل الشاشة في هذا الاستدعاء</summary>
        public bool Tick()
        {
            if (Timeout <= TimeSpan.Zero) return false;
            if (!_session.IsAuthenticated || _session.IsLocked) { _lastActivity = Clock(); return false; }
            if (Clock() - _lastActivity < Timeout) return false;

            _session.Lock();
            _lastActivity = Clock();
            return true;
        }
    }
}
