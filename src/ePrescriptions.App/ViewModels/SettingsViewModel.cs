using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Drawing.Printing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ePrescriptions.App.Services;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Core.Printing;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly IBackupService _backupService;
        private readonly Data.Repositories.OwnerAuditLogRepository? _ownerAuditLogRepository;
        private readonly Data.Repositories.StaffAuditLogRepository? _staffAuditLogRepository;
        private readonly IUserSessionService? _sessionService;
        private readonly IBackupAutomationService? _backupAutomation;
        private readonly BackupSchedulerHost? _backupHost;
        private readonly BackupStatusStore? _backupStatusStore;

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // بيانات المنشأة
        private string _pharmacyNameAr = PharmacySettings.DefaultPharmacyName;
        public string PharmacyNameAr
        {
            get => _pharmacyNameAr;
            set => SetProperty(ref _pharmacyNameAr, value);
        }

        private string _pharmacyNameEn = string.Empty;
        public string PharmacyNameEn
        {
            get => _pharmacyNameEn;
            set => SetProperty(ref _pharmacyNameEn, value);
        }

        private string _taxNumber = "123456789";
        public string TaxNumber
        {
            get => _taxNumber;
            set => SetProperty(ref _taxNumber, value);
        }

        private string _commercialRegister = "1010998877";
        public string CommercialRegister
        {
            get => _commercialRegister;
            set => SetProperty(ref _commercialRegister, value);
        }

        private string _licenseNumber = "PH-99201";
        public string LicenseNumber
        {
            get => _licenseNumber;
            set => SetProperty(ref _licenseNumber, value);
        }

        private string _phone = "0112345678";
        public string Phone
        {
            get => _phone;
            set => SetProperty(ref _phone, value);
        }

        private string _address = "جمهورية مصر العربية - القاهرة";
        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        private int _defaultVatPercent = 14;
        public int DefaultVatPercent
        {
            get => _defaultVatPercent;
            set => SetProperty(ref _defaultVatPercent, value);
        }

        private string _currencyDisplay = "الجنيه المصري (ج.م / EGP)";
        public string CurrencyDisplay
        {
            get => _currencyDisplay;
            set => SetProperty(ref _currencyDisplay, value);
        }

        // إعدادات الطابعات
        public ObservableCollection<string> InstalledPrinters { get; } = new();

        private string _receiptPrinterName = string.Empty;
        public string ReceiptPrinterName
        {
            get => _receiptPrinterName;
            set => SetProperty(ref _receiptPrinterName, value);
        }

        private string _a4PrinterName = string.Empty;
        public string A4PrinterName
        {
            get => _a4PrinterName;
            set => SetProperty(ref _a4PrinterName, value);
        }

        private string _labelPrinterName = string.Empty;
        public string LabelPrinterName
        {
            get => _labelPrinterName;
            set => SetProperty(ref _labelPrinterName, value);
        }

        private int _receiptPaperWidthMm = 80;
        public int ReceiptPaperWidthMm
        {
            get => _receiptPaperWidthMm;
            set => SetProperty(ref _receiptPaperWidthMm, value);
        }

        private int _labelWidthMm = 50;
        public int LabelWidthMm
        {
            get => _labelWidthMm;
            set => SetProperty(ref _labelWidthMm, value);
        }

        private int _labelHeightMm = 25;
        public int LabelHeightMm
        {
            get => _labelHeightMm;
            set => SetProperty(ref _labelHeightMm, value);
        }

        // درج النقود والباركود
        private bool _enableCashDrawerOnPrint = true;
        public bool EnableCashDrawerOnPrint
        {
            get => _enableCashDrawerOnPrint;
            set => SetProperty(ref _enableCashDrawerOnPrint, value);
        }

        private string _cashDrawerEscPosBytes = "27,112,0,25,250";
        public string CashDrawerEscPosBytes
        {
            get => _cashDrawerEscPosBytes;
            set => SetProperty(ref _cashDrawerEscPosBytes, value);
        }

        private int _barcodeScanThresholdMs = 45;
        public int BarcodeScanThresholdMs
        {
            get => _barcodeScanThresholdMs;
            set => SetProperty(ref _barcodeScanThresholdMs, value);
        }

        // النسخ الاحتياطي
        private string _backupFolder = string.Empty;
        public string BackupFolder
        {
            get => _backupFolder;
            set => SetProperty(ref _backupFolder, value);
        }

        private string _externalBackupFolder = string.Empty;
        /// <summary>مجلد/قرص خارجي منفصل (USB) لنسخة ثانية؛ فاضي = معطّل</summary>
        public string ExternalBackupFolder
        {
            get => _externalBackupFolder;
            set => SetProperty(ref _externalBackupFolder, value);
        }

        private int _backupRetentionCount = BackupSettings.DefaultRetentionCount;
        public int BackupRetentionCount
        {
            get => _backupRetentionCount;
            set => SetProperty(ref _backupRetentionCount, value);
        }

        private bool _autoBackupAfterShift = true;
        public bool AutoBackupAfterShift
        {
            get => _autoBackupAfterShift;
            set => SetProperty(ref _autoBackupAfterShift, value);
        }

        // خطوة 41: الجدولة اليومية واختبار الاستعادة الدوري
        private bool _dailyBackupEnabled = true;
        public bool DailyBackupEnabled
        {
            get => _dailyBackupEnabled;
            set => SetProperty(ref _dailyBackupEnabled, value);
        }

        private string _dailyBackupTime = BackupSettings.DefaultDailyBackupTime;
        /// <summary>ميعاد النسخة اليومية بصيغة HH:mm</summary>
        public string DailyBackupTime
        {
            get => _dailyBackupTime;
            set => SetProperty(ref _dailyBackupTime, value);
        }

        private bool _restoreTestEnabled = true;
        public bool RestoreTestEnabled
        {
            get => _restoreTestEnabled;
            set => SetProperty(ref _restoreTestEnabled, value);
        }

        private int _restoreTestEveryDays = BackupSettings.DefaultRestoreTestEveryDays;
        public int RestoreTestEveryDays
        {
            get => _restoreTestEveryDays;
            set => SetProperty(ref _restoreTestEveryDays, value);
        }

        private string _backupHealthInfo = string.Empty;
        /// <summary>آخر نسخة ناجحة + آخر اختبار استعادة + أي تنبيه حالي</summary>
        public string BackupHealthInfo
        {
            get => _backupHealthInfo;
            set => SetProperty(ref _backupHealthInfo, value);
        }

        private string _lastBackupInfo = "لم يتم إنشاء نسخة في هذه الجلسة.";
        public string LastBackupInfo
        {
            get => _lastBackupInfo;
            set => SetProperty(ref _lastBackupInfo, value);
        }

        // الأوامر
        public IRelayCommand SaveSettingsCommand { get; }
        public IRelayCommand TestReceiptPrinterCommand { get; }
        public IRelayCommand TestCashDrawerCommand { get; }
        public IRelayCommand CreateBackupCommand { get; }
        public IRelayCommand RestoreBackupCommand { get; }
        public IRelayCommand SaveBackupSettingsCommand { get; }
        public IRelayCommand TestRestoreNowCommand { get; }

        public SettingsViewModel(
            SettingsRepository settingsRepository,
            IBackupService backupService,
            Data.Repositories.OwnerAuditLogRepository? ownerAuditLogRepository = null,
            Data.Repositories.StaffAuditLogRepository? staffAuditLogRepository = null,
            IUserSessionService? sessionService = null,
            IBackupAutomationService? backupAutomation = null,
            BackupSchedulerHost? backupHost = null,
            BackupStatusStore? backupStatusStore = null)
        {
            _settingsRepository = settingsRepository;
            _backupService = backupService;
            _ownerAuditLogRepository = ownerAuditLogRepository;
            _staffAuditLogRepository = staffAuditLogRepository;
            _sessionService = sessionService;
            _backupAutomation = backupAutomation;
            _backupHost = backupHost;
            _backupStatusStore = backupStatusStore;

            Title = "إعدادات النظام والملحقات";
            var backupSettings = BackupSettingsStore.Load();
            BackupFolder = string.IsNullOrWhiteSpace(backupSettings.PrimaryFolder) ? _backupService.GetDefaultBackupFolder() : backupSettings.PrimaryFolder;
            ExternalBackupFolder = backupSettings.ExternalFolder;
            BackupRetentionCount = backupSettings.EffectiveRetention;
            AutoBackupAfterShift = backupSettings.AutoBackupAfterShift;
            DailyBackupEnabled = backupSettings.DailyBackupEnabled;
            DailyBackupTime = backupSettings.DailyBackupTimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            RestoreTestEnabled = backupSettings.RestoreTestEnabled;
            RestoreTestEveryDays = backupSettings.EffectiveRestoreTestEveryDays;

            SaveSettingsCommand = new RelayCommand(SaveSettings);
            TestReceiptPrinterCommand = new RelayCommand(TestReceiptPrinter);
            TestCashDrawerCommand = new RelayCommand(TestCashDrawer);
            CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
            RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync);
            SaveBackupSettingsCommand = new RelayCommand(() => SaveBackupSettings());
            TestRestoreNowCommand = new AsyncRelayCommand(TestRestoreNowAsync);

            LoadInstalledPrinters();
            LoadCurrentSettings();
            RefreshBackupHealthInfo();
        }

        private void LoadInstalledPrinters()
        {
            InstalledPrinters.Clear();
            InstalledPrinters.Add("(الطابعة الافتراضية للويندوز)");
            try
            {
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    InstalledPrinters.Add(printer);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "LoadInstalledPrinters");
            }
        }

        private void LoadCurrentSettings()
        {
            try
            {
                var s = _settingsRepository.GetSettings();
                PharmacyNameAr = string.IsNullOrWhiteSpace(s.PharmacyNameAr) ? PharmacySettings.DefaultPharmacyName : s.PharmacyNameAr;
                PharmacyNameEn = s.PharmacyNameEn;
                TaxNumber = s.TaxNumber;
                CommercialRegister = s.CommercialRegister;
                LicenseNumber = s.LicenseNumber;
                Phone = s.Phone;
                Address = s.Address;
                DefaultVatPercent = s.DefaultVatPercent;

                ReceiptPrinterName = s.ReceiptPrinterName;
                A4PrinterName = s.A4PrinterName;
                LabelPrinterName = s.LabelPrinterName;
                ReceiptPaperWidthMm = s.ReceiptPaperWidthMm;
                LabelWidthMm = s.LabelWidthMm;
                LabelHeightMm = s.LabelHeightMm;

                EnableCashDrawerOnPrint = s.EnableCashDrawerOnPrint;
                CashDrawerEscPosBytes = s.CashDrawerEscPosBytes;
                BarcodeScanThresholdMs = s.BarcodeScanThresholdMs;

                StatusMessage = "تم تحميل إعدادات النظام الحالية بنجاح.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"تعذر تحميل الإعدادات: {ex.Message}";
            }
        }

        private void SaveSettings()
        {
            try
            {
                // نبدأ من الإعدادات المحفوظة حتى لا تُفقد قيم غير معروضة في الشاشة (QR، طول الرقم الضريبي، الصيدلي المسؤول)
                var previous = _settingsRepository.GetSettings();
                var s = _settingsRepository.GetSettings();
                s.Id = 1;
                s.PharmacyNameAr = PharmacyNameAr.Trim();
                s.PharmacyNameEn = PharmacyNameEn.Trim();
                s.TaxNumber = TaxNumber.Trim();
                s.CommercialRegister = CommercialRegister.Trim();
                s.LicenseNumber = LicenseNumber.Trim();
                s.Phone = Phone.Trim();
                s.Address = Address.Trim();
                s.DefaultVatPercent = DefaultVatPercent;
                s.ReceiptPrinterName = ReceiptPrinterName ?? string.Empty;
                s.A4PrinterName = A4PrinterName ?? string.Empty;
                s.LabelPrinterName = LabelPrinterName ?? string.Empty;
                s.ReceiptPaperWidthMm = ReceiptPaperWidthMm;
                s.LabelWidthMm = LabelWidthMm;
                s.LabelHeightMm = LabelHeightMm;
                s.EnableCashDrawerOnPrint = EnableCashDrawerOnPrint;
                s.CashDrawerEscPosBytes = CashDrawerEscPosBytes.Trim();
                s.BarcodeScanThresholdMs = BarcodeScanThresholdMs;

                LogSettingsAuditIfChanged(previous, s);

                _settingsRepository.SaveSettings(s);
                StatusMessage = "تم حفظ جميع إعدادات النظام بنجاح.";
                MessageBox.Show(
                    "تم حفظ الإعدادات بنجاح في قاعدة البيانات المحلية.",
                    "تأكيد الحفظ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                StatusMessage = $"خطأ أثناء حفظ الإعدادات: {ex.Message}";
                MessageBox.Show(
                    $"فشل حفظ الإعدادات:\n{ex.Message}",
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// يسجّل أي حقل حسّاس تغيّر في إعدادات الصيدلية: لصاحب الصيدلية في سجله الخاص المخفي عن الجميع،
        /// ولأي دور آخر في سجل الموظفين العام المرئي لـ Admin وOwner فقط.
        /// </summary>
        private void LogSettingsAuditIfChanged(PharmacySettings previous, PharmacySettings updated)
        {
            var user = _sessionService?.CurrentUser;
            if (user == null) return;

            const string entityType = "PharmacySettings";
            const string entityId = "1";
            const string label = "إعدادات الصيدلية";

            void Log(string field, string? oldVal, string? newVal)
            {
                if (string.Equals(oldVal, newVal, StringComparison.Ordinal)) return;

                if (user.Role == ePrescriptions.Domain.Enums.UserRole.Owner)
                    _ownerAuditLogRepository?.LogChange(user.Id, entityType, entityId, label, field, oldVal, newVal);
                else
                    _staffAuditLogRepository?.LogChange(user.Id, user.FullName, user.Role.ToString(), entityType, entityId, label, field, oldVal, newVal);
            }

            Log("PharmacyNameAr", previous.PharmacyNameAr, updated.PharmacyNameAr);
            Log("TaxNumber", previous.TaxNumber, updated.TaxNumber);
            Log("DefaultVatPercent", previous.DefaultVatPercent.ToString(), updated.DefaultVatPercent.ToString());
            Log("Phone", previous.Phone, updated.Phone);
            Log("Address", previous.Address, updated.Address);
            Log("EnableCashDrawerOnPrint", previous.EnableCashDrawerOnPrint.ToString(), updated.EnableCashDrawerOnPrint.ToString());
        }

        private void TestReceiptPrinter()
        {
            try
            {
                string prn = string.IsNullOrWhiteSpace(ReceiptPrinterName) || ReceiptPrinterName.Contains("الافتراضية")
                    ? string.Empty
                    : ReceiptPrinterName;

                if (ThermalReceiptPrinter.PrintTestTicket(prn, PharmacyNameAr, out string printError))
                {
                    StatusMessage = "تم إرسال تذكرة اختبارية إلى الطابعة الحرارية.";
                }
                else
                {
                    StatusMessage = $"فشل اختبار الطابعة: {printError}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"خطأ في فحص الطابعة: {ex.Message}";
                MessageBox.Show(
                    $"تعذر إرسال أمر الطباعة:\n{ex.Message}",
                    "تنبيه الطابعة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private void TestCashDrawer()
        {
            try
            {
                string prn = string.IsNullOrWhiteSpace(ReceiptPrinterName) || ReceiptPrinterName.Contains("الافتراضية")
                    ? string.Empty
                    : ReceiptPrinterName;

                if (ThermalReceiptPrinter.OpenCashDrawer(prn, CashDrawerEscPosBytes, out string drawerError))
                {
                    StatusMessage = "تم إرسال نبضة فتح درج النقود بنجاح.";
                }
                else
                {
                    StatusMessage = $"فشل فتح درج النقود: {drawerError}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"فشل فتح درج النقود: {ex.Message}";
                MessageBox.Show(
                    $"تعذر فتح درج النقود:\n{ex.Message}",
                    "درج النقود",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private BackupSettings CurrentBackupSettings() => new BackupSettings
        {
            PrimaryFolder = BackupFolder?.Trim() ?? string.Empty,
            ExternalFolder = ExternalBackupFolder?.Trim() ?? string.Empty,
            RetentionCount = BackupRetentionCount,
            AutoBackupAfterShift = AutoBackupAfterShift,
            DailyBackupEnabled = DailyBackupEnabled,
            DailyBackupTime = DailyBackupTime?.Trim() ?? string.Empty,
            RestoreTestEnabled = RestoreTestEnabled,
            RestoreTestEveryDays = RestoreTestEveryDays
        };

        private static bool IsValidTimeOfDay(string? text) =>
            TimeSpan.TryParseExact((text ?? string.Empty).Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var t)
            && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1);

        /// <summary>يعرض آخر نسخة ناجحة وآخر اختبار استعادة وأي تنبيه حالي (من ملف الحالة). لا يرمي استثناءات.</summary>
        private void RefreshBackupHealthInfo()
        {
            try
            {
                if (_backupStatusStore == null) { BackupHealthInfo = string.Empty; return; }
                var st = _backupStatusStore.Load();
                var lines = new List<string>
                {
                    "آخر نسخة احتياطية ناجحة: " + (st.LastBackupSuccess.HasValue ? DbDate.Time(st.LastBackupSuccess.Value) : "لا يوجد"),
                    "آخر اختبار استعادة ناجح: " + (st.LastRestoreTestSuccess.HasValue ? DbDate.Time(st.LastRestoreTestSuccess.Value) : "لم يتم بعد")
                };
                lines.AddRange(BackupHealth.Evaluate(DateTime.Now, st, CurrentBackupSettings()));
                BackupHealthInfo = string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SettingsViewModel.RefreshBackupHealthInfo");
            }
        }

        /// <summary>زر "اختبار الاستعادة الآن": بيفتح أحدث نسخة في قاعدة مؤقتة ويعرض النتيجة (القاعدة الحية ما بتتلمسش)</summary>
        private async Task TestRestoreNowAsync()
        {
            if (IsBusy) return;
            if (_backupAutomation == null)
            {
                StatusMessage = "خدمة اختبار الاستعادة غير متاحة.";
                return;
            }

            IsBusy = true;
            try
            {
                SaveBackupSettings();
                var verify = await _backupAutomation.RunRestoreTestNowAsync(CurrentBackupSettings(), DateTime.Now);
                StatusMessage = verify.Success ? "✔ " + verify.Message : "⚠ فشل اختبار الاستعادة: " + verify.Message;
                MessageBox.Show(
                    verify.Success
                        ? $"{verify.Message}\n\n{System.IO.Path.GetFileName(verify.BackupPath)}"
                        : $"فشل اختبار استعادة أحدث نسخة:\n{verify.Message}\n\nخذ نسخة جديدة الآن، ولا تعتمد على هذه النسخة.",
                    "اختبار الاستعادة",
                    MessageBoxButton.OK,
                    verify.Success ? MessageBoxImage.Information : MessageBoxImage.Warning
                );
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SettingsViewModel.TestRestoreNowAsync");
                StatusMessage = $"تعذر اختبار الاستعادة: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                RefreshBackupHealthInfo();
                _backupHost?.RefreshAlerts();
            }
        }

        /// <summary>حفظ إعدادات النسخ في ملف مستقل (backup_settings.ini) مش في قاعدة البيانات</summary>
        private bool SaveBackupSettings()
        {
            if (!IsValidTimeOfDay(DailyBackupTime))
            {
                StatusMessage = "ميعاد النسخة اليومية لازم يكون بصيغة HH:mm مثل 22:00.";
                return false;
            }

            try
            {
                var settings = CurrentBackupSettings();
                BackupSettingsStore.Save(settings);
                BackupRetentionCount = settings.EffectiveRetention;
                RestoreTestEveryDays = settings.EffectiveRestoreTestEveryDays;
                DailyBackupTime = settings.DailyBackupTimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
                StatusMessage = "تم حفظ إعدادات النسخ الاحتياطي.";
                RefreshBackupHealthInfo();
                _backupHost?.RefreshAlerts();
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"تعذر حفظ إعدادات النسخ الاحتياطي: {ex.Message}";
                return false;
            }
        }

        /// <summary>خطوة 28: النسخ على خيط خلفي عشان الشاشة ما تتجمدش (خصوصاً مع القرص الخارجي/USB)</summary>
        private async Task CreateBackupAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                SaveBackupSettings();
                var result = await _backupService.RunConfiguredBackupAsync(CurrentBackupSettings());
                ShowBackupResult(result);
                RefreshBackupHealthInfo();
                _backupHost?.RefreshAlerts();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SettingsViewModel.CreateBackupAsync");
                StatusMessage = $"تعذر إنشاء النسخة الاحتياطية: {ex.Message}";
                MessageBox.Show(
                    $"تعذر إنشاء النسخة الاحتياطية:\n{ex.Message}",
                    "خطأ النسخ الاحتياطي",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ShowBackupResult(BackupRunResult result)
        {
            if (result.Success)
            {
                LastBackupInfo = $"آخر نسخة: {Path.GetFileName(result.PrimaryPath)} في {DbDate.Time(DateTime.Now)}";
                StatusMessage = result.Message;
                string details = $"تم أخذ نسخة احتياطية كاملة وموثوقة لقاعدة البيانات:\n{result.PrimaryPath}";
                if (!string.IsNullOrEmpty(result.ExternalPath)) details += $"\n\nونسخة على القرص الخارجي:\n{result.ExternalPath}";
                if (result.ExternalFailed) details += $"\n\n⚠ {result.Message}";
                MessageBox.Show(
                    details,
                    "النسخ الاحتياطي",
                    MessageBoxButton.OK,
                    result.ExternalFailed ? MessageBoxImage.Warning : MessageBoxImage.Information
                );
            }
            else
            {
                StatusMessage = result.Message;
                MessageBox.Show(
                    $"تعذر إنشاء النسخة الاحتياطية:\n{result.Message}",
                    "خطأ النسخ الاحتياطي",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async Task RestoreBackupAsync()
        {
            if (IsBusy) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "اختر ملف النسخة الاحتياطية لاستعادتها",
                Filter = "ملفات قواعد بيانات SQLite (*.db;*.bak)|*.db;*.bak|جميع الملفات (*.*)|*.*",
                InitialDirectory = BackupFolder
            };

            if (dialog.ShowDialog() == true)
            {
                var confirm = MessageBox.Show(
                    $"تحذير هـام جداً:\nسيتم استبدال قاعدة البيانات الحالية بالكامل بالملف:\n{dialog.FileName}\n\nهل ترغب بالاستمرار وتطبيق الاستعادة؟",
                    "تأكيد استعادة قاعدة البيانات",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (confirm == MessageBoxResult.Yes)
                {
                    // خطوة 28: الاستعادة على خيط خلفي، و IsBusy بيمنع أي ضغطة ثانية أثناءها
                    IsBusy = true;
                    _backupHost?.Suspend(); // الجدولة ما تشتغلش وقت استبدال ملف القاعدة
                    BackupRestoreResult restore;
                    try
                    {
                        restore = await _backupService.RestoreBackupAsync(dialog.FileName);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "SettingsViewModel.RestoreBackupAsync");
                        restore = new BackupRestoreResult { Success = false, ErrorMessage = ex.Message };
                    }
                    finally
                    {
                        _backupHost?.Resume();
                        IsBusy = false;
                    }

                    string error = restore.ErrorMessage;
                    if (restore.Success)
                    {
                        StatusMessage = "تمت استعادة قاعدة البيانات بنجاح.";
                        MessageBox.Show(
                            "تمت استعادة قاعدة البيانات بنجاح وتأكيد سلامة الجداول، وتم تطبيق أي تحديثات ناقصة على هيكل القاعدة.\nأعد تشغيل البرنامج لتحميل البيانات المستعادة في كل الشاشات.",
                            "تمت الاستعادة",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                    else
                    {
                        StatusMessage = $"فشلت الاستعادة: {error}";
                        MessageBox.Show(
                            $"تعذر استعادة قاعدة البيانات:\n{error}",
                            "خطأ في الاستعادة",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }
            }
        }
    }
}
