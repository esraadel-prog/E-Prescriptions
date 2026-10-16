using System;
using System.Collections.Generic;
using System.Text;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// ترميز Code 128 (منطق بحت بدون رسم): Set C للأرقام (رقمين في رمز) وSet B لغير ذلك.
    /// الأرقام بتتحول لـ Set C لأنها بتصغّر عرض الباركود للنصف تقريباً (EAN-13: 178 → 123 وحدة)،
    /// والقارئ بيرجّع نفس النص في الحالتين.
    /// </summary>
    public static class Code128Encoder
    {
        public const int CodeB = 100;
        public const int CodeC = 99;
        public const int StartB = 104;
        public const int StartC = 105;
        public const int Stop = 106;

        /// <summary>أنماط العروض (بار/فراغ بالتبادل) للرموز 0-106</summary>
        public static readonly string[] Patterns =
        {
            "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
            "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
            "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
            "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
            "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
            "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
            "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
            "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
            "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
            "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
            "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
        };

        /// <summary>
        /// أرقام فقط (4 أو أكتر) → Set C؛ لو العدد فردي الرقم الأخير بعد تحويل لـ Set B.
        /// غير ذلك → Set B (حروف خارج 32..127 بتتحول لمسافة زي السلوك القديم).
        /// </summary>
        public static List<int> Encode(string text)
        {
            if (string.IsNullOrEmpty(text)) text = "000000";

            var codes = new List<int>();
            if (text.Length >= 4 && AllDigits(text))
            {
                codes.Add(StartC);
                int pairs = text.Length / 2;
                for (int i = 0; i < pairs; i++)
                    codes.Add((text[2 * i] - '0') * 10 + (text[2 * i + 1] - '0'));
                if (text.Length % 2 == 1)
                {
                    codes.Add(CodeB);
                    codes.Add(text[text.Length - 1] - 32);
                }
            }
            else
            {
                codes.Add(StartB);
                foreach (char c in text)
                {
                    int v = c - 32;
                    codes.Add(v < 0 || v > 95 ? 0 : v);
                }
            }

            int sum = codes[0];
            for (int i = 1; i < codes.Count; i++) sum += codes[i] * i;
            codes.Add(sum % 103);
            codes.Add(Stop);
            return codes;
        }

        private static bool AllDigits(string s)
        {
            foreach (char c in s)
                if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>سلسلة العروض الكاملة (بار، فراغ، بار...) لرموز الترميز</summary>
        public static string Pattern(IEnumerable<int> codes)
        {
            var sb = new StringBuilder();
            foreach (int c in codes) sb.Append(Patterns[c]);
            return sb.ToString();
        }

        /// <summary>إجمالي الوحدات (modules) بدون هوامش الأمان</summary>
        public static int TotalModules(string text)
        {
            int total = 0;
            foreach (char ch in Pattern(Encode(text))) total += ch - '0';
            return total;
        }

        /// <summary>فك سلسلة العروض لنص (للاختبار والتحقق: يفحص البداية والـ checksum والنهاية)</summary>
        public static bool TryDecode(string pattern, out string text)
        {
            text = string.Empty;
            var symbols = new List<int>();
            int pos = 0;
            while (pos < pattern.Length)
            {
                int found = -1;
                for (int v = 0; v < Patterns.Length; v++)
                {
                    if (string.CompareOrdinal(pattern, pos, Patterns[v], 0, Patterns[v].Length) != 0) continue;
                    if (v == Stop && pos + Patterns[v].Length != pattern.Length) continue;
                    found = v;
                    break;
                }
                if (found < 0) return false;
                symbols.Add(found);
                pos += Patterns[found].Length;
            }

            if (symbols.Count < 4 || symbols[symbols.Count - 1] != Stop) return false;
            int start = symbols[0];
            if (start != StartB && start != StartC) return false;

            int checksum = start;
            for (int i = 1; i < symbols.Count - 2; i++) checksum += symbols[i] * i;
            if (checksum % 103 != symbols[symbols.Count - 2]) return false;

            var sb = new StringBuilder();
            bool setC = start == StartC;
            for (int i = 1; i < symbols.Count - 2; i++)
            {
                int s = symbols[i];
                if (setC)
                {
                    if (s == CodeB) { setC = false; continue; }
                    if (s > 99) return false;
                    sb.Append(s.ToString("00"));
                }
                else
                {
                    if (s == CodeC) { setC = true; continue; }
                    if (s > 95) return false;
                    sb.Append((char)(s + 32));
                }
            }
            text = sb.ToString();
            return true;
        }
    }
}
