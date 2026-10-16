using System;
using System.Collections.Generic;
using System.Globalization;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// تحليل أمر ESC/POS المكتوب في الإعدادات (مثل أمر فتح الدرج) إلى بايتات.
    /// الصيغة المقبولة: أرقام عشرية 0-255 مفصولة بفاصلة أو مسافة أو فاصلة منقوطة (27,112,0,25,250)،
    /// أو أرقام hex بسابقة 0x (0x1B 0x70 0x00 0x19 0xFA). أي رمز غير مفهوم يرفض الأمر كله:
    /// الإسقاط الصامت كان بيبعت بايتات مختلفة عن المقصود (مثلاً "1B 70 00 19 FA" بتتحول لـ 70,0,19).
    /// </summary>
    public static class EscPosCommand
    {
        /// <summary>ESC p 0 25 250 — نبضة فتح الدرج القياسية (المسمار الأول)</summary>
        public static readonly byte[] DefaultDrawerPulse = { 27, 112, 0, 25, 250 };

        public const int MaxLength = 64;

        /// <summary>
        /// فاضي = الأمر الافتراضي (ينجح). غير ذلك: يرجع false مع رسالة عربية تذكر الرمز المرفوض.
        /// </summary>
        public static bool TryParse(string? text, out byte[] bytes, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                bytes = (byte[])DefaultDrawerPulse.Clone();
                return true;
            }

            var list = new List<byte>();
            var tokens = NormalizeDigits(text!).Split(new[] { ',', ' ', ';', '\t', '\r', '\n', '،', '؛' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in tokens)
            {
                string t = raw.Trim();
                byte value;
                bool ok;
                if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    ok = byte.TryParse(t.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
                else
                    ok = byte.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out value);

                if (!ok)
                {
                    bytes = Array.Empty<byte>();
                    error = $"أمر الدرج فيه قيمة غير صالحة: \"{t}\". اكتب أرقام عشرية من 0 إلى 255 مفصولة بفاصلة (مثال: 27,112,0,25,250) أو hex بسابقة 0x (مثال: 0x1B).";
                    return false;
                }
                list.Add(value);
            }

            if (list.Count == 0 || list.Count > MaxLength)
            {
                bytes = Array.Empty<byte>();
                error = $"أمر الدرج لازم يكون من 1 إلى {MaxLength} بايت.";
                return false;
            }

            bytes = list.ToArray();
            return true;
        }

        /// <summary>الأرقام العربية-الهندية (٠-٩) والفارسية بتتحول لأرقام لاتينية (الكيبورد العربي بيكتبها كده)</summary>
        private static string NormalizeDigits(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= '\u0660' && c <= '\u0669') chars[i] = (char)('0' + (c - '\u0660'));
                else if (c >= '\u06F0' && c <= '\u06F9') chars[i] = (char)('0' + (c - '\u06F0'));
            }
            return new string(chars);
        }
    }
}
