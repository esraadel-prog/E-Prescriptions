using System;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Data.Repositories;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.Data.Setup
{
    /// <summary>
    /// معالج الإعداد الأول: بيحدد إن كان ده أول تشغيل (مفيش حساب Owner)، ويُنشئ حساب صاحب الصيدلية
    /// بكلمة مرور يختارها بنفسه، ويحفظ بيانات الصيدلية، ويحمّل بيانات تجريبية فقط لو طُلب ذلك صراحة.
    /// </summary>
    public class FirstRunSetupService
    {
        public const int MinPasswordLength = 8;

        private readonly DatabaseContext _dbContext;
        private readonly SettingsRepository _settingsRepository;

        public FirstRunSetupService(DatabaseContext dbContext, SettingsRepository settingsRepository)
        {
            _dbContext = dbContext;
            _settingsRepository = settingsRepository;
        }

        /// <summary>أول تشغيل = لا يوجد حساب Owner بعد</summary>
        public bool IsFirstRun() => !_dbContext.OwnerExists();

        public static bool ValidateOwnerPassword(string? password, string? confirm, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrEmpty(password))
            {
                error = "اكتب كلمة مرور لحساب صاحب الصيدلية.";
                return false;
            }
            if (password.Length < MinPasswordLength)
            {
                error = $"كلمة المرور لازم تكون {MinPasswordLength} حروف/أرقام على الأقل.";
                return false;
            }
            if (string.Equals(password, DatabaseContext.DefaultOwnerPassword, StringComparison.Ordinal) ||
                string.Equals(password, DatabaseContext.OwnerUsername, StringComparison.OrdinalIgnoreCase))
            {
                error = "اختار كلمة مرور غير معروفة مسبقاً (مش كلمة المرور الافتراضية ولا اسم المستخدم).";
                return false;
            }
            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                error = "كلمتا المرور غير متطابقتين.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// إتمام الإعداد الأول. اسم الصيدلية والرقم الضريبي اختياريان (يُعدَّلان لاحقاً من الإعدادات).
        /// </summary>
        public bool CompleteSetup(string? pharmacyName, string? taxNumber, string? password, string? confirm, bool loadDemoData, out string error)
        {
            error = string.Empty;

            if (!IsFirstRun())
            {
                error = "الإعداد الأول تم قبل كده.";
                return false;
            }
            if (!ValidateOwnerPassword(password, confirm, out error)) return false;

            string tax = (taxNumber ?? string.Empty).Trim();
            if (tax.Length > 0 && !EgyptQrHelper.ValidateTaxRegistrationNumber(tax, 9))
            {
                error = "الرقم الضريبي لازم يكون 9 أرقام (أو سيبه فاضي وكمّله بعدين من الإعدادات).";
                return false;
            }

            string name = (pharmacyName ?? string.Empty).Trim();

            try
            {
                _dbContext.EnsureCashCustomer();

                if (loadDemoData)
                {
                    _dbContext.LoadDemoData();
                    var demoSettings = _settingsRepository.GetSettings();
                    if (name.Length > 0) demoSettings.PharmacyNameAr = name;
                    if (tax.Length > 0) demoSettings.TaxNumber = tax;
                    _settingsRepository.SaveSettings(demoSettings);
                }
                else
                {
                    // تثبيت نظيف: بدون أي بيانات افتراضية بتاعة حد تاني على الفواتير
                    _settingsRepository.SaveSettings(new PharmacySettings
                    {
                        PharmacyNameAr = name.Length > 0 ? name : PharmacySettings.DefaultPharmacyName,
                        PharmacyNameEn = string.Empty,
                        TaxNumber = tax,
                        CommercialRegister = string.Empty,
                        LicenseNumber = string.Empty,
                        PharmacistInCharge = string.Empty,
                        Phone = string.Empty,
                        Address = string.Empty
                    });
                }

                _dbContext.CreateOwnerAccount(password!);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "FirstRunSetupService.CompleteSetup");
                error = $"تعذر إتمام الإعداد: {ex.Message}";
                return false;
            }
        }
    }
}
