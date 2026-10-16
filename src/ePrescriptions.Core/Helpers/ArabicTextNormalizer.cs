using System;
using System.Text;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// تطبيع النص العربي للبحث الموحّد (خطوة 24): نفس القاعدة تُطبَّق على النص المخزّن وعلى كلمة البحث،
    /// فيتطابق "أسبرين" و"اسبرين"، و"مُسكّن" و"مسكن"، و"صيدلية" و"صيدليه"، و"ربى" و"ربي".
    /// القواعد: إزالة التشكيل والتطويل، أ/إ/آ/ٱ ← ا، ة ← ه، ى ← ي، ؤ ← و، ئ ← ي،
    /// الأرقام العربية/الفارسية ← لاتينية، الحروف اللاتينية ← صغيرة، وضغط المسافات.
    /// </summary>
    public static class ArabicTextNormalizer
    {
        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var sb = new StringBuilder(text!.Length);
            bool lastWasSpace = true; // يشيل المسافات الأولية
            foreach (char ch in text)
            {
                char c = ch;

                // التشكيل (فتحة...سكون + ألف خنجرية) والتطويل
                if ((c >= '\u064B' && c <= '\u065F') || c == '\u0670' || c == '\u0640') continue;

                switch (c)
                {
                    case 'أ': case 'إ': case 'آ': case 'ٱ': c = 'ا'; break;
                    case 'ة': c = 'ه'; break;
                    case 'ى': c = 'ي'; break;
                    case 'ؤ': c = 'و'; break;
                    case 'ئ': c = 'ي'; break;
                }

                // الأرقام العربية-الهندية ٠-٩ والفارسية ۰-۹
                if (c >= '\u0660' && c <= '\u0669') c = (char)('0' + (c - '\u0660'));
                else if (c >= '\u06F0' && c <= '\u06F9') c = (char)('0' + (c - '\u06F0'));

                c = char.ToLowerInvariant(c);

                if (char.IsWhiteSpace(c))
                {
                    if (lastWasSpace) continue;
                    sb.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }

            if (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
            return sb.ToString();
        }

        /// <summary>تحويل الأرقام العربية-الهندية (٠-٩) والفارسية (۰-۹) إلى أرقام لاتينية قياسية (0-9)</summary>
        public static string NormalizeDigits(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text!.Length);
            foreach (char ch in text)
            {
                if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
                else if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>تحويل الأرقام اللاتينية (0-9) إلى أرقام عربية-هندية (٠-٩)</summary>
        public static string ToArabicIndicDigits(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text!.Length);
            foreach (char ch in text)
            {
                if (ch >= '0' && ch <= '9') sb.Append((char)('\u0660' + (ch - '0')));
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>تقسيم كلمة البحث لكلمات مطبّعة (بحث AND على كل كلمة)</summary>
        public static string[] Tokens(string? query)
        {
            string n = Normalize(query);
            return n.Length == 0 ? new string[0] : n.Split(' ');
        }
    }
}
