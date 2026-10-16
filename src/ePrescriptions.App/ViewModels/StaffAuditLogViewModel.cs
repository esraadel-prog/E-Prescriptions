using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Data.Repositories;

namespace ePrescriptions.App.ViewModels
{
    public class StaffAuditLogEntryDisplay
    {
        public string FormattedDate { get; set; } = string.Empty;
        public string ActorUserName { get; set; } = string.Empty;
        public string ActorRole { get; set; } = string.Empty;
        public string EntityLabel { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
    }

    /// <summary>
    /// شاشة سجل تعديلات الموظفين (المدير والصيدلي والكاشير). مرئية لدوري Admin وOwner فقط
    /// (IsAdminOrOwner في MainViewModel). هذا السجل منفصل تماماً عن سجل صاحب الصيدلية الخاص
    /// (OwnerLogViewModel)؛ لا يحتوي على أي تعديلات قام بها Owner.
    /// </summary>
    public class StaffAuditLogViewModel : ViewModelBase
    {
        private readonly StaffAuditLogRepository _staffAuditLogRepository;

        public ObservableCollection<StaffAuditLogEntryDisplay> Entries { get; } = new();

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public IRelayCommand RefreshCommand { get; }

        public StaffAuditLogViewModel(StaffAuditLogRepository staffAuditLogRepository)
        {
            Title = "سجل نشاط الموظفين";
            _staffAuditLogRepository = staffAuditLogRepository;

            RefreshCommand = new RelayCommand(LoadData);
            LoadData();
        }

        public void LoadData()
        {
            Entries.Clear();
            var logs = _staffAuditLogRepository.GetRecent(300);

            foreach (var log in logs.OrderByDescending(l => l.ChangedAt))
            {
                Entries.Add(new StaffAuditLogEntryDisplay
                {
                    FormattedDate = log.ChangedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    ActorUserName = log.ActorUserName,
                    ActorRole = log.ActorRole,
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
