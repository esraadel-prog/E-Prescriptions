using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.App.Services;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Security;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Data.Repositories;

namespace ePrescriptions.App.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly IUserSessionService _sessionService;
        private readonly UserRepository? _userRepository;
        private readonly SettingsRepository? _settingsRepository;

        // خطوة 46: كلمة المرور الافتراضية اللي دخل بيها (بنحتاجها كـ"الحالية" وقت التغيير الإجباري)
        private string _pendingCurrentPassword = string.Empty;

        private string _username = string.Empty; // لا قيمة افتراضية (ما نكشفش اسم حساب)
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private bool _isLockMode = false;
        public bool IsLockMode
        {
            get => _isLockMode;
            set => SetProperty(ref _isLockMode, value);
        }

        private string _currentUserDisplay = string.Empty;
        public string CurrentUserDisplay
        {
            get => _currentUserDisplay;
            set => SetProperty(ref _currentUserDisplay, value);
        }

        private string _loginErrorMessage = string.Empty;
        public string LoginErrorMessage
        {
            get => _loginErrorMessage;
            set => SetProperty(ref _loginErrorMessage, value);
        }

        private string _pharmacyName = PharmacySettings.DefaultPharmacyName;
        /// <summary>اسم الصيدلية المعروض في شاشة الدخول: نفس المكتوب في الإعدادات (أو اللي اتكتب في أول شاشة بعد التثبيت)</summary>
        public string PharmacyName
        {
            get => _pharmacyName;
            private set => SetProperty(ref _pharmacyName, value);
        }

        /// <summary>يقرأ الاسم من الإعدادات (بيتنادى كل ما الشاشة تتفتح/تتحدّث علشان أي تعديل في الإعدادات يظهر)</summary>
        public void RefreshPharmacyName()
        {
            string name = string.Empty;
            try { name = _settingsRepository?.GetSettings().PharmacyNameAr ?? string.Empty; }
            catch (Exception ex) { Logger.LogException(ex, "LoginViewModel.RefreshPharmacyName"); }
            PharmacyName = string.IsNullOrWhiteSpace(name) ? PharmacySettings.DefaultPharmacyName : name.Trim();
        }

        private bool _isForcedPasswordChange;
        /// <summary>دخل بكلمة مرور افتراضية: لازم يغيّرها قبل أي حاجة تانية (مفيش تجاوز)</summary>
        public bool IsForcedPasswordChange
        {
            get => _isForcedPasswordChange;
            private set => SetProperty(ref _isForcedPasswordChange, value);
        }

        private string _newPassword = string.Empty;
        public string NewPassword
        {
            get => _newPassword;
            set => SetProperty(ref _newPassword, value);
        }

        private string _confirmNewPassword = string.Empty;
        public string ConfirmNewPassword
        {
            get => _confirmNewPassword;
            set => SetProperty(ref _confirmNewPassword, value);
        }

        public IRelayCommand SubmitCommand { get; }
        public IRelayCommand SwitchUserCommand { get; }
        public IRelayCommand ExitAppCommand { get; }

        public event Action? LoginSucceeded;

        public LoginViewModel(IUserSessionService sessionService, UserRepository? userRepository = null, SettingsRepository? settingsRepository = null)
        {
            _sessionService = sessionService;
            _userRepository = userRepository;
            _settingsRepository = settingsRepository;
            RefreshPharmacyName();
            Title = "تسجيل الدخول وقفل الشاشة";

            SubmitCommand = new RelayCommand(ExecuteSubmit);
            SwitchUserCommand = new RelayCommand(ExecuteSwitchUser);
            ExitAppCommand = new RelayCommand(() => { CancelForcedPasswordChange(); Application.Current?.Shutdown(); });

            UpdateState();
            _sessionService.LockStateChanged += locked => UpdateState();
        }

        public void SetMode(bool lockMode)
        {
            IsLockMode = lockMode;
            RefreshPharmacyName();
            Password = string.Empty;
            LoginErrorMessage = string.Empty;
            UpdateState();
        }

        private void UpdateState()
        {
            if (_sessionService.IsLocked && _sessionService.CurrentUser != null)
            {
                IsLockMode = true;
                CurrentUserDisplay = _sessionService.CurrentUser.FullName;
                Username = _sessionService.CurrentUser.Username;
            }
            else
            {
                IsLockMode = false;
                CurrentUserDisplay = string.Empty;
            }
        }

        private void ExecuteSubmit()
        {
            LoginErrorMessage = string.Empty;

            if (IsForcedPasswordChange)
            {
                ExecuteForcedPasswordChange();
                return;
            }

            if (IsLockMode)
            {
                if (_sessionService.Unlock(Password, out string error))
                {
                    Password = string.Empty;
                    LoginSucceeded?.Invoke();
                }
                else
                {
                    LoginErrorMessage = error;
                }
            }
            else
            {
                if (_sessionService.Login(Username, Password, out string error))
                {
                    // خطوة 46: كلمة مرور افتراضية معروفة = ممنوع الدخول للنظام قبل تغييرها
                    if (DefaultCredentials.IsKnownDefaultPassword(Password))
                    {
                        if (_userRepository == null)
                        {
                            // مفيش وسيلة لتغيير الكلمة هنا: نرفض الدخول بدل ما نسمح بكلمة معروفة
                            _sessionService.Logout();
                            Password = string.Empty;
                            LoginErrorMessage = "كلمة المرور افتراضية ولا يمكن تغييرها من هنا. تعذّر الدخول.";
                            return;
                        }
                        _pendingCurrentPassword = Password;
                        Password = string.Empty;
                        IsForcedPasswordChange = true;
                        LoginErrorMessage = "كلمة المرور الحالية افتراضية ومعروفة للجميع. اختر كلمة مرور جديدة للمتابعة.";
                        return;
                    }

                    Password = string.Empty;
                    LoginSucceeded?.Invoke();
                }
                else
                {
                    LoginErrorMessage = error;
                }
            }
        }

        private void ExecuteForcedPasswordChange()
        {
            var user = _sessionService.CurrentUser;
            if (user == null || _userRepository == null)
            {
                CancelForcedPasswordChange();
                LoginErrorMessage = "انتهت الجلسة. سجّل الدخول من جديد.";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewPassword) || string.IsNullOrWhiteSpace(ConfirmNewPassword))
            {
                LoginErrorMessage = "يرجى إدخال كلمة المرور الجديدة وتأكيدها.";
                return;
            }
            if (NewPassword != ConfirmNewPassword)
            {
                LoginErrorMessage = "كلمة المرور الجديدة وتأكيدها غير متطابقين.";
                return;
            }

            if (!_userRepository.ChangeOwnPassword(user.Id, _pendingCurrentPassword, NewPassword, out string error))
            {
                LoginErrorMessage = error;
                return;
            }

            // الجلسة بتحتفظ بالـ hash القديم (بيتستخدم في فك القفل)، فنقفل الجلسة ونطلب دخول بالكلمة الجديدة
            _pendingCurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmNewPassword = string.Empty;
            IsForcedPasswordChange = false;
            _sessionService.Logout();
            Password = string.Empty;
            LoginErrorMessage = "تم تغيير كلمة المرور. سجّل الدخول بكلمة المرور الجديدة.";
        }

        /// <summary>
        /// إلغاء التغيير الإجباري (إغلاق النافذة/خروج): بنقفل الجلسة فوراً وبنقفل البرنامج،
        /// علشان مفيش طريق للدخول بالكلمة الافتراضية من غير تغييرها.
        /// </summary>
        public void CancelForcedPasswordChange()
        {
            if (!IsForcedPasswordChange) return;
            _pendingCurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmNewPassword = string.Empty;
            IsForcedPasswordChange = false;
            _sessionService.Logout();
            Application.Current?.Shutdown();
        }

        private void ExecuteSwitchUser()
        {
            _sessionService.Logout();
            IsLockMode = false;
            Username = string.Empty;
            Password = string.Empty;
            LoginErrorMessage = string.Empty;
        }
    }
}
