using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// مطابقة المواد الفعالة للبدائل (خطوة 35 - تعديل): المطابقة باسم المادة فقط وبتتجاهل التركيز والوحدات،
    /// وأي صنف بيشترك مع صنف تاني في اسم مادة واحدة على الأقل يعتبر بديل ليه.
    /// مثال: "Paracetamol" و"Paracetamol 500mg" و"Paracetamol 1000 mg" و"Paracetamol + Vitamin C" كلهم بدائل لبعض.
    /// </summary>
    public static class ActiveIngredientMatcher
    {
        // فواصل بين المواد: + / , ; & | و كلمة and أو "و" (كلمة منفصلة) أو "مع"
        private static readonly Regex SplitRegex = new Regex(
            @"[+/,;&|\\]|\s(?:and|with|و|مع)\s", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // كمية/تركيز: رقم (اختياري عشري) اختياري متبوع بوحدة، أو وحدة لوحدها
        private static readonly Regex DoseTokenRegex = new Regex(
            @"^(?:\d+(?:\.\d+)?)?(?:mg|g|gm|gr|mcg|ug|µg|ml|l|iu|mmol|meq|%|w/v|w/w)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ParenRegex = new Regex(@"\([^)]*\)", RegexOptions.Compiled);

        /// <summary>أسماء المواد الفعالة المطبّعة في الحقل (مجموعة فاضية لو الحقل فاضي)</summary>
        public static HashSet<string> ExtractNames(string? activeIngredient)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            string text = ArabicTextNormalizer.Normalize(activeIngredient);
            if (text.Length == 0) return result;

            text = ParenRegex.Replace(text, " ");
            foreach (var part in SplitRegex.Split(text))
            {
                var words = part
                    .Split(new[] { ' ', '\t', '-', '.', ':' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => !DoseTokenRegex.IsMatch(w))
                    .ToArray();
                if (words.Length == 0) continue;
                result.Add(string.Join(" ", words));
            }
            return result;
        }

        /// <summary>هل الحقلين بيشتركوا في اسم مادة واحدة على الأقل؟</summary>
        public static bool SharesIngredient(string? a, string? b)
        {
            var na = ExtractNames(a);
            if (na.Count == 0) return false;
            var nb = ExtractNames(b);
            return na.Overlaps(nb);
        }
    }
}
