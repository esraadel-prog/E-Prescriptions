using System;
using ePrescriptions.Core.Helpers;
using System.Data.SQLite;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.Data.Repositories
{
    public class SettingsRepository
    {
        private readonly DatabaseContext _dbContext;

        public SettingsRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public PharmacySettings GetSettings()
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM PharmacySettings WHERE Id = 1 LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var s = new PharmacySettings
                {
                    Id = 1,
                    PharmacyNameAr = reader["PharmacyNameAr"]?.ToString() ?? PharmacySettings.DefaultPharmacyName,
                    PharmacyNameEn = reader["PharmacyNameEn"]?.ToString() ?? string.Empty,
                    TaxNumber = reader["TaxNumber"]?.ToString() ?? "123456789",
                    CommercialRegister = reader["CommercialRegister"]?.ToString() ?? "1010998877",
                    LicenseNumber = reader["LicenseNumber"]?.ToString() ?? "PH-99201",
                    Phone = reader["Phone"]?.ToString() ?? "0112345678",
                    Address = reader["Address"]?.ToString() ?? "جمهورية مصر العربية - القاهرة",
                    DefaultVatPercent = Convert.ToInt32(reader["DefaultVatPercent"] ?? 14),
                    ReceiptPrinterName = reader["ReceiptPrinterName"]?.ToString() ?? string.Empty,
                    A4PrinterName = reader["A4PrinterName"]?.ToString() ?? string.Empty,
                    LabelPrinterName = reader["LabelPrinterName"]?.ToString() ?? string.Empty,
                    ReceiptPaperWidthMm = Convert.ToInt32(reader["ReceiptPaperWidthMm"] ?? 80),
                    LabelWidthMm = Convert.ToInt32(reader["LabelWidthMm"] ?? 50),
                    LabelHeightMm = Convert.ToInt32(reader["LabelHeightMm"] ?? 25),
                    EnableCashDrawerOnPrint = Convert.ToInt32(reader["EnableCashDrawerOnPrint"] ?? 1) == 1,
                    CashDrawerEscPosBytes = reader["CashDrawerEscPosBytes"]?.ToString() ?? "27,112,0,25,250",
                    BarcodeScanThresholdMs = Convert.ToInt32(reader["BarcodeScanThresholdMs"] ?? 45)
                };

                // الأعمدة الإضافية للتوطين المصري إذا توفرت في الجدول
                try
                {
                    if (HasColumn(reader, "TaxNumberLength"))
                        s.TaxNumberLength = Convert.ToInt32(reader["TaxNumberLength"]);
                    if (HasColumn(reader, "ResponsiblePharmacist"))
                        s.ResponsiblePharmacist = reader["ResponsiblePharmacist"]?.ToString() ?? string.Empty;
                    if (HasColumn(reader, "EnableQrCode"))
                        s.EnableQrCode = Convert.ToInt32(reader["EnableQrCode"]) == 1;
                }
                catch (Exception ex)
                {
                    // الأعمدة الاختيارية فشلت قراءتها: نكمل بالقيم الافتراضية بس نسجّل السبب
                    Logger.LogException(ex, "SettingsRepository.GetSettings (قراءة أعمدة اختيارية)");
                }

                return s;
            }

            return new PharmacySettings();
        }

        private static bool HasColumn(SQLiteDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void SaveSettings(PharmacySettings settings)
        {
            using var conn = _dbContext.CreateConnection();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO PharmacySettings (
                    Id, PharmacyNameAr, PharmacyNameEn, TaxNumber, TaxNumberLength,
                    CommercialRegister, LicenseNumber, ResponsiblePharmacist, Phone, Address,
                    DefaultVatPercent, EnableQrCode,
                    ReceiptPrinterName, A4PrinterName, LabelPrinterName,
                    ReceiptPaperWidthMm, LabelWidthMm, LabelHeightMm,
                    EnableCashDrawerOnPrint, CashDrawerEscPosBytes, BarcodeScanThresholdMs
                ) VALUES (
                    1, @ar, @en, @tax, @taxLen,
                    @cr, @lic, @pic, @ph, @addr,
                    @vat, @qrEnabled,
                    @rPrn, @aPrn, @lPrn,
                    @rW, @lW, @lH,
                    @drw, @drwCmd, @bcThresh
                );";

            cmd.Parameters.AddWithValue("@ar", settings.PharmacyNameAr);
            cmd.Parameters.AddWithValue("@en", settings.PharmacyNameEn);
            cmd.Parameters.AddWithValue("@tax", settings.TaxNumber);
            cmd.Parameters.AddWithValue("@taxLen", settings.TaxNumberLength);
            cmd.Parameters.AddWithValue("@cr", settings.CommercialRegister);
            cmd.Parameters.AddWithValue("@lic", settings.LicenseNumber);
            cmd.Parameters.AddWithValue("@pic", settings.PharmacistInCharge);
            cmd.Parameters.AddWithValue("@ph", settings.Phone);
            cmd.Parameters.AddWithValue("@addr", settings.Address);
            cmd.Parameters.AddWithValue("@vat", settings.DefaultVatPercent);
            cmd.Parameters.AddWithValue("@qrEnabled", settings.EnableQrCode ? 1 : 0);
            cmd.Parameters.AddWithValue("@rPrn", settings.ReceiptPrinterName);
            cmd.Parameters.AddWithValue("@aPrn", settings.A4PrinterName);
            cmd.Parameters.AddWithValue("@lPrn", settings.LabelPrinterName);
            cmd.Parameters.AddWithValue("@rW", settings.ReceiptPaperWidthMm);
            cmd.Parameters.AddWithValue("@lW", settings.LabelWidthMm);
            cmd.Parameters.AddWithValue("@lH", settings.LabelHeightMm);
            cmd.Parameters.AddWithValue("@drw", settings.EnableCashDrawerOnPrint ? 1 : 0);
            cmd.Parameters.AddWithValue("@drwCmd", settings.CashDrawerEscPosBytes);
            cmd.Parameters.AddWithValue("@bcThresh", settings.BarcodeScanThresholdMs);

            cmd.ExecuteNonQuery();
        }
    }
}
