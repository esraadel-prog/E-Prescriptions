using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.Data.Setup;

namespace ePrescriptions.App.ViewModels
{
    /// <summary>شاشة الإعداد الأول: بيانات الصيدلية + كلمة مرور صاحب الصيدلية + بيانات تجريبية اختيارية</summary>
    public class FirstRunSetupViewModel : ObservableObject
    {
        private readonly FirstRunSetupService _setupService;

        public event Action? SetupCompleted;

        private string _pharmacyName = string.Empty;
        public string PharmacyName { get => _pharmacyName; set => SetProperty(ref _pharmacyName, value); }

        private string _taxNumber = string.Empty;
        public string TaxNumber { get => _taxNumber; set => SetProperty(ref _taxNumber, value); }

        private string _password = string.Empty;
        public string Password { get => _password; set => SetProperty(ref _password, value); }

        private string _confirmPassword = string.Empty;
        public string ConfirmPassword { get => _confirmPassword; set => SetProperty(ref _confirmPassword, value); }

        private bool _loadDemoData;
        /// <summary>تحميل بيانات تجريبية (أصناف/عملاء/مورد) — اختياري ومش مفعّل افتراضياً</summary>
        public bool LoadDemoData { get => _loadDemoData; set => SetProperty(ref _loadDemoData, value); }

        private string _errorMessage = string.Empty;
        public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

        public IRelayCommand CompleteCommand { get; }

        public FirstRunSetupViewModel(FirstRunSetupService setupService)
        {
            _setupService = setupService;
            CompleteCommand = new RelayCommand(Complete);
        }

        public void Complete()
        {
            ErrorMessage = string.Empty;
            if (_setupService.CompleteSetup(PharmacyName, TaxNumber, Password, ConfirmPassword, LoadDemoData, out string error))
            {
                SetupCompleted?.Invoke();
            }
            else
            {
                ErrorMessage = error;
            }
        }
    }
}
