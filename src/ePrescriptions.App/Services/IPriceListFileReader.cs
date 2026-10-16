using System;
using System.Collections.Generic;
using System.IO;

namespace ePrescriptions.App.Services
{
    /// <summary>خطأ مفهوم للمستخدم أثناء قراءة ملف قائمة الأسعار (رسالته عربية وجاهزة للعرض)</summary>
    public class PriceListFileException : Exception
    {
        public PriceListFileException(string message) : base(message) { }
    }

    /// <summary>قارئ ملف قائمة أسعار المورد: بيرجّع الجدول كنصوص (صف × عمود) لـ PriceListParser</summary>
    public interface IPriceListFileReader
    {
        IReadOnlyList<string[]> ReadGrid(string path);
    }

    /// <summary>
    /// حدود وفحوص ملف قائمة الأسعار قبل فتحه (بدون أي اعتماد على مكتبة Excel عشان تتختبر).
    /// خطوة 40 جزء ب: xlsx/xlsm فقط، 25 ميجا، 50,000 صف.
    /// </summary>
    public static class PriceListFileGuard
    {
        public const long MaxFileBytes = 25L * 1024 * 1024;
        public const int MaxRows = 50000;

        /// <summary>يرمي <see cref="PriceListFileException"/> لو الملف مش صالح للقراءة</summary>
        public static void CheckFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new PriceListFileException("اختار ملف Excel الأول.");

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".xls")
                throw new PriceListFileException("صيغة .xls القديمة مش مدعومة. افتح الملف في Excel واحفظه بصيغة .xlsx ثم أعد المحاولة.");
            if (ext != ".xlsx" && ext != ".xlsm")
                throw new PriceListFileException("الملف لازم يكون Excel بصيغة .xlsx أو .xlsm.");

            if (!File.Exists(path))
                throw new PriceListFileException("الملف مش موجود: " + path);

            long size = new FileInfo(path).Length;
            if (size > MaxFileBytes)
                throw new PriceListFileException($"حجم الملف كبير ({size / (1024 * 1024)} ميجا). الحد الأقصى {MaxFileBytes / (1024 * 1024)} ميجا.");
        }

        public static void CheckRowCount(int rows)
        {
            if (rows > MaxRows)
                throw new PriceListFileException($"عدد الصفوف ({rows:N0}) أكبر من الحد المسموح ({MaxRows:N0}). قسّم الملف لأجزاء.");
        }
    }
}
