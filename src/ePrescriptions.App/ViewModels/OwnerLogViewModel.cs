using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Data.Repositories;
using ePrescriptions.App.Services;

namespace ePrescriptions.App.ViewModels
{
    /// <summary>
    /// عرض سطر واحد من سجل نشاط صاحب الصيدلية بشكل مقروء
    /// </summary>
    public class OwnerLogEntryDisplay
    {
        public string FormattedDate { get; set; } = string.Empty;
        public string EntityLabel { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
    }

    /// <summary>
    /// شاشة سجل نشاط صاحب الصيدلية. يجب ألا تُضاف إلى أي DataTemplate أو زر تنقل يظهر لغير دور Owner.
    /// الحماية الحقيقية الإضافية تكمن في أن INavigationService لا يستدعي هذه الشاشة أصلاً إلا من زر
    /// محمي بـ IsOwner في MainViewModel، وهذا الزر غير مرئي لأي دور آخر.
    /// </summary>
    public class OwnerLogViewModel : ViewModelBase
    {
        private readonly OwnerAuditLogRepository _ownerAuditLogRepository;
        private readonly UserRepository _userRepository;
        private readonly IUserSessionService _sessionService;

        public ObservableCollection<OwnerLogEntryDisplay> Entries { get; } = new();

        // تغيير كلمة المرور (موصى به فوراً بعد أول دخول بكلمة المرور الافتراضية)
        private string _currentPassword = string.Empty;
        public string CurrentPassword { get => _currentPassword; set => SetProperty(ref _currentPassword, value); }

        private string _newPassword = string.Empty;
        public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }

        private string _confirmNewPassword = string.Empty;
        public string ConfirmNewPassword { get => _confirmNewPassword; set => SetProperty(ref _confirmNewPassword, value); }

        private string _passwordStatusMessage = string.Empty;
        public string PasswordStatusMessage { get => _passwordStatusMessage; set => SetProperty(ref _passwordStatusMessage, value); }

        public IRelayCommand ChangePasswordCommand { get; }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public IRelayCommand RefreshCommand { get; }

        public OwnerLogViewModel(OwnerAuditLogRepository ownerAuditLogRepository, UserRepository userRepository, IUserSessionService sessionService)
        {
            Title = "سجل نشاطي (مرئي لك فقط)";
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _userRepository = userRepository;
            _sessionService = sessionService;

            RefreshCommand = new RelayCommand(LoadData);
            ChangePasswordCommand = new RelayCommand(ChangePassword);
            LoadData();
        }

        private void ChangePassword()
        {
            if (_sessionService.CurrentUser == null)
            {
                PasswordStatusMessage = "تعذر تحديد المستخدم الحالي.";
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentPassword) || string.IsNullOrWhiteSpace(NewPassword))
            {
                PasswordStatusMessage = "يرجى تعبئة كلمة المرور الحالية والجديدة.";
                return;
            }

            if (NewPassword != ConfirmNewPassword)
            {
                PasswordStatusMessage = "كلمة المرور الجديدة وتأكيدها غير متطابقين.";
                return;
            }

            if (_userRepository.ChangeOwnPassword(_sessionService.CurrentUser.Id, CurrentPassword, NewPassword, out string error))
            {
                PasswordStatusMessage = "تم تغيير كلمة المرور بنجاح.";
                CurrentPassword = string.Empty;
                NewPassword = string.Empty;
                ConfirmNewPassword = string.Empty;
            }
            else
            {
                PasswordStatusMessage = error;
            }
        }

        public void LoadData()
        {
            Entries.Clear();
            var logs = _ownerAuditLogRepository.GetRecent(300);

            foreach (var log in logs.OrderByDescending(l => l.ChangedAt))
            {
                Entries.Add(new OwnerLogEntryDisplay
                {
                    FormattedDate = log.ChangedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    EntityLabel = log.EntityLabel,
                    FieldName = log.FieldName,
                    OldValue = log.OldValue ?? "-",
                    NewValue = log.NewValue ?? "-"
                });
            }

            StatusMessage = Entries.Count == 0
                ? "لا توجد تعديلات مسجّلة بعد."
                : $"عدد التعديلات المسجّلة: {Entries.Count}";
        }
    }
}
