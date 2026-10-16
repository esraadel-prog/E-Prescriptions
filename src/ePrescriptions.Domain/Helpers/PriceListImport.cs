using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ePrescriptions.Core.Financial;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.Domain.Helpers
{
    /// <summary>أعمدة ملف قائمة أسعار المورد اللي النظام بيفهمها</summary>
    public enum PriceListColumn
    {
        Barcode = 0,
        Code = 1,
        Name = 2,
        Retail = 3,
        Wholesale = 4,
        Cost = 5
    }

    /// <summary>الحقل السعري اللي ممكن يتحدّث على الصنف (كلها سعر العلبة بالقرش)</summary>
    public enum PriceField
    {
        Retail = 0,
        Wholesale = 1,
        Cost = 2
    }

    /// <summary>أماكن الأعمدة المكتشفة في الملف (فهرس الصف والأعمدة من صفر)</summary>
    public class PriceListMapping
    {
        public int HeaderRowIndex { get; set; }
        public Dictionary<PriceListColumn, int> Columns { get; } = new Dictionary<PriceListColumn, int>();

        public bool Has(PriceListColumn column) => Columns.ContainsKey(column);
        public bool HasIdentifier => Has(PriceListColumn.Barcode) || Has(PriceListColumn.Code) || Has(PriceListColumn.Name);
        public bool HasPrice => Has(PriceListColumn.Retail) || Has(PriceListColumn.Wholesale) || Has(PriceListColumn.Cost);
    }

    /// <summary>سطر مقروء من ملف الأسعار (بعد التحويل لقروش)</summary>
    public class PriceListRow
    {
        /// <summary>رقم الصف في ملف Excel (يبدأ من 1)</summary>
        public int RowNumber { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long? RetailPiastres { get; set; }
        public long? WholesalePiastres { get; set; }
        public long? CostPiastres { get; set; }

        /// <summary>أعمدة سعر فيها نص مش رقم صالح (أو صفر/سالب): اسم العمود → النص الأصلي</summary>
        public Dictionary<PriceField, string> InvalidPrices { get; } = new Dictionary<PriceField, string>();

        public bool HasAnyPrice => RetailPiastres.HasValue || WholesalePiastres.HasValue || CostPiastres.HasValue;
    }

    /// <summary>قراءة جدول نصوص (من Excel) وتحويله لسطور أسعار. بحت: مفيش ملفات ولا قاعدة بيانات.</summary>
    public static class PriceListParser
    {
        /// <summary>عدد الصفوف اللي بندوّر فيها على صف العناوين</summary>
        public const int HeaderSearchRows = 15;

        /// <summary>تصنيف عنوان عمود (بعد التطبيع العربي). null = مش مفهوم</summary>
        public static PriceListColumn? ClassifyHeader(string? header)
        {
            string h = ArabicTextNormalizer.Normalize(header);
            if (h.Length == 0) return null;

            // الترتيب مهم: الباركود قبل الكود، والأسعار قبل الأسماء
            if (h.Contains("باركود") || h.Contains("بار كود") || h.Contains("barcode") || h.Contains("bar code")
                || h == "ean" || h == "gtin" || h.Contains("ean13"))
                return PriceListColumn.Barcode;

            bool mentionsPrice = h.Contains("سعر") || h.Contains("price");
            if (h.Contains("جمهور") || h.Contains("public") || h.Contains("retail") || h.Contains("مستهلك") || h.Contains("سعر البيع") || h.Contains("سعر بيع"))
                return PriceListColumn.Retail;
            if (h.Contains("جمله") || h.Contains("wholesale"))
                return PriceListColumn.Wholesale;
            if (h.Contains("تكلفه") || h.Contains("cost") || h.Contains("شراء") || h.Contains("purchase")
                || h.Contains("صيدلي") || h.Contains("pharmacy price"))
                return PriceListColumn.Cost;
            // "السعر" / "Price" لوحدها = سعر الجمهور (والمستخدم بيشوف الأرقام قبل الاعتماد)
            if (mentionsPrice) return PriceListColumn.Retail;

            if (h.Contains("كود") || h == "code" || h.Contains("item code") || h == "sku" || h.Contains("رقم الصنف"))
                return PriceListColumn.Code;
            if (h.Contains("اسم") || h.Contains("صنف") || h.Contains("بيان") || h.Contains("وصف")
                || h == "name" || h == "item" || h.Contains("item name") || h.Contains("description") || h.Contains("product"))
                return PriceListColumn.Name;

            return null;
        }

        /// <summary>يدوّر على صف العناوين في أول صفوف الملف. null لو ملقاش (مفيش عمود تعريف + عمود سعر)</summary>
        public static PriceListMapping? DetectMapping(IReadOnlyList<string[]> grid)
        {
            int limit = Math.Min(grid.Count, HeaderSearchRows);
            for (int r = 0; r < limit; r++)
            {
                var mapping = new PriceListMapping { HeaderRowIndex = r };
                string[] cells = grid[r] ?? new string[0];
                for (int c = 0; c < cells.Length; c++)
                {
                    var kind = ClassifyHeader(cells[c]);
                    if (kind.HasValue && !mapping.Columns.ContainsKey(kind.Value))
                        mapping.Columns[kind.Value] = c;
                }
                if (mapping.HasIdentifier && mapping.HasPrice) return mapping;
            }
            return null;
        }

        /// <summary>
        /// يحوّل الصفوف اللي بعد صف العناوين لسطور أسعار. رقم الصف = فهرسه + 1 (القارئ لازم يحافظ على الصفوف الفاضية).
        /// الصفوف الفاضية بالكامل بتتتخطّى.
        /// </summary>
        public static List<PriceListRow> Parse(IReadOnlyList<string[]> grid, PriceListMapping mapping)
        {
            var rows = new List<PriceListRow>();
            for (int r = mapping.HeaderRowIndex + 1; r < grid.Count; r++)
            {
                string[] cells = grid[r] ?? new string[0];
                string Cell(PriceListColumn col) =>
                    mapping.Columns.TryGetValue(col, out int idx) && idx < cells.Length ? (cells[idx] ?? string.Empty).Trim() : string.Empty;

                var row = new PriceListRow
                {
                    RowNumber = r + 1,
                    Barcode = CleanIdentifier(Cell(PriceListColumn.Barcode)),
                    Code = CleanIdentifier(Cell(PriceListColumn.Code)),
                    Name = Cell(PriceListColumn.Name)
                };

                ReadPrice(Cell(PriceListColumn.Retail), PriceField.Retail, row, v => row.RetailPiastres = v);
                ReadPrice(Cell(PriceListColumn.Wholesale), PriceField.Wholesale, row, v => row.WholesalePiastres = v);
                ReadPrice(Cell(PriceListColumn.Cost), PriceField.Cost, row, v => row.CostPiastres = v);

                bool blank = row.Barcode.Length == 0 && row.Code.Length == 0 && row.Name.Length == 0
                             && !row.HasAnyPrice && row.InvalidPrices.Count == 0;
                if (!blank) rows.Add(row);
            }
            return rows;
        }

        private static void ReadPrice(string text, PriceField field, PriceListRow row, Action<long> set)
        {
            if (text.Length == 0) return;
            if (TryParsePiastres(text, out long piastres)) set(piastres);
            else row.InvalidPrices[field] = text;
        }

        /// <summary>
        /// الباركود/الكود اللي Excel بيحوّله لرقم علمي أو بيزوّد له ".0" بنشيل الزيادة (الأرقام الطويلة بتتقرأ كرقم).
        /// نص غير رقمي بيفضل زي ما هو.
        /// </summary>
        public static string CleanIdentifier(string? text)
        {
            string t = ArabicTextNormalizer.Normalize(text).Replace(" ", string.Empty);
            if (t.Length == 0) return string.Empty;
            if (t.EndsWith(".0", StringComparison.Ordinal)) t = t.Substring(0, t.Length - 2);
            if (t.IndexOf('e') > 0 && decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal sci)
                && sci == Math.Floor(sci) && sci < 1e18m)
                t = ((long)sci).ToString(CultureInfo.InvariantCulture);
            return t;
        }

        /// <summary>
        /// نص السعر (بالجنيه) → قروش. بيقبل أرقام عربية، "ج.م/EGP"، فاصلة عشرية "٫" أو "," أو ".".
        /// بيرفض النص الغريب والصفر والسالب.
        /// </summary>
        public static bool TryParsePiastres(string? text, out long piastres)
        {
            piastres = 0;
            string t = ArabicTextNormalizer.Normalize(text);
            if (t.Length == 0) return false;

            t = t.Replace('٫', '.').Replace('٬', ',');

            // شيل رموز العملة المعروفة والمسافات؛ أي حاجة غير الأرقام و , و . بعد كده = مش سعر صالح
            foreach (string token in CurrencyTokens) t = t.Replace(token, string.Empty);
            t = t.Replace(" ", string.Empty);
            if (t.Length == 0) return false;
            foreach (char c in t)
                if (!((c >= '0' && c <= '9') || c == '.' || c == ',')) return false;
            string s = t;
            if (!s.Any(ch => ch >= '0' && ch <= '9')) return false;

            int lastDot = s.LastIndexOf('.');
            int lastComma = s.LastIndexOf(',');
            if (lastDot >= 0 && lastComma >= 0)
            {
                // آخر فاصل هو العشري، والتاني آلاف
                char dec = lastDot > lastComma ? '.' : ',';
                char thou = dec == '.' ? ',' : '.';
                s = s.Replace(thou.ToString(), string.Empty);
                s = s.Replace(dec, '.');
            }
            else if (lastComma >= 0)
            {
                // فاصلة فقط: عشرية لو فاصلة واحدة وبعدها 1-2 رقم، غير كده آلاف
                int commas = s.Count(ch => ch == ',');
                int after = s.Length - lastComma - 1;
                if (commas == 1 && (after == 1 || after == 2)) s = s.Replace(',', '.');
                else s = s.Replace(",", string.Empty);
            }
            else if (lastDot >= 0 && s.Count(ch => ch == '.') > 1)
            {
                return false; // 1.234.567 غامض
            }

            if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal pounds)) return false;
            if (pounds <= 0 || pounds > 10000000m) return false;

            piastres = Money.FromPounds(pounds).Piastres;
            return piastres > 0;
        }

        // مرتّبة: الأطول الأول (l.e قبل le)
        private static readonly string[] CurrencyTokens = { "ج.م", "جنيه", "egp", "l.e", "جم", "le", "ج" };
    }

    // ───────────────────────────── خطة التحديث ─────────────────────────────

    /// <summary>تغيير سعر واحد مقترح لحقل واحد في صنف واحد</summary>
    public class ItemPriceChange
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemNameAr { get; set; } = string.Empty;
        public PriceField Field { get; set; }
        public long OldPiastres { get; set; }
        public long NewPiastres { get; set; }
        public int SourceRowNumber { get; set; }

        /// <summary>التغيير بالنسبة المئوية (الجديد - القديم) / القديم. 0 لو القديم صفر (سعر غير مضبوط)</summary>
        public decimal ChangePercent => OldPiastres > 0 ? (NewPiastres - OldPiastres) * 100m / OldPiastres : 0m;

        /// <summary>التغيير الكبير: يحتاج مراجعة قبل الاعتماد</summary>
        public bool IsLargeChange { get; set; }

        /// <summary>اسم الحقل في سجل التدقيق (نفس أسماء حقول شاشة الأصناف)</summary>
        public string AuditFieldName => Field switch
        {
            PriceField.Retail => "RetailPriceBoxPiastres",
            PriceField.Wholesale => "WholesalePriceBoxPiastres",
            _ => "CostPriceBoxPiastres"
        };

        public string FieldLabelAr => Field switch
        {
            PriceField.Retail => "سعر الجمهور",
            PriceField.Wholesale => "سعر الجملة",
            _ => "سعر التكلفة"
        };
    }

    /// <summary>تغيير اتخطّى وقت الاعتماد (مع السبب)</summary>
    public class PriceChangeSkip
    {
        public ItemPriceChange Change { get; set; } = new ItemPriceChange();
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>نتيجة اعتماد التغييرات على قاعدة البيانات</summary>
    public class PriceApplyResult
    {
        public List<ItemPriceChange> Applied { get; } = new List<ItemPriceChange>();
        public List<PriceChangeSkip> Skipped { get; } = new List<PriceChangeSkip>();
    }

    public enum PriceLineStatus
    {
        /// <summary>اتطابق وفيه تغيير فعلي في سعر واحد على الأقل</summary>
        WillUpdate = 0,
        /// <summary>اتطابق والأسعار زي ما هي</summary>
        Unchanged = 1,
        /// <summary>مفيش صنف مطابق في النظام</summary>
        NotFound = 2,
        /// <summary>أكتر من صنف مطابق (نفس الباركود/الكود/الاسم)</summary>
        Ambiguous = 3,
        /// <summary>نفس الصنف ظاهر في الملف بأسعار مختلفة</summary>
        Conflict = 4,
        /// <summary>خانة سعر فيها قيمة مش صالحة</summary>
        InvalidPrice = 5,
        /// <summary>السطر مفيهوش أي سعر</summary>
        NoPrice = 6
    }

    /// <summary>نتيجة مطابقة سطر واحد من الملف</summary>
    public class PriceListLine
    {
        public PriceListRow Row { get; set; } = new PriceListRow();
        public PriceLineStatus Status { get; set; }
        public Item? Item { get; set; }

        /// <summary>اتطابق بإيه: باركود / كود / اسم</summary>
        public string MatchedBy { get; set; } = string.Empty;
        public List<ItemPriceChange> Changes { get; } = new List<ItemPriceChange>();

        /// <summary>سعر الجمهور الناتج أقل من التكلفة الناتجة (تحذير)</summary>
        public bool RetailBelowCost { get; set; }

        public string Note { get; set; } = string.Empty;

        /// <summary>فيه حاجة تستاهل مراجعة: تغيير كبير أو بيع بأقل من التكلفة</summary>
        public bool RequiresReview => RetailBelowCost || Changes.Any(c => c.IsLargeChange);
    }

    public class PriceListOptions
    {
        public bool UpdateRetail { get; set; } = true;
        public bool UpdateWholesale { get; set; } = true;
        public bool UpdateCost { get; set; } = true;

        /// <summary>أي تغيير بنسبة (بالزيادة أو النقصان) أكبر من أو تساوي كده بيتعلّم "يحتاج مراجعة"</summary>
        public decimal LargeChangePercent { get; set; } = 30m;
    }

    public class PriceListPlan
    {
        public List<PriceListLine> Lines { get; } = new List<PriceListLine>();

        public IEnumerable<PriceListLine> UpdatableLines => Lines.Where(l => l.Status == PriceLineStatus.WillUpdate);
        public int TotalRows => Lines.Count;
        public int UpdateCount => Lines.Count(l => l.Status == PriceLineStatus.WillUpdate);
        public int ChangeCount => Lines.Where(l => l.Status == PriceLineStatus.WillUpdate).Sum(l => l.Changes.Count);
        public int UnchangedCount => Lines.Count(l => l.Status == PriceLineStatus.Unchanged);
        public int NotFoundCount => Lines.Count(l => l.Status == PriceLineStatus.NotFound);
        public int ProblemCount => Lines.Count(l => l.Status == PriceLineStatus.Ambiguous || l.Status == PriceLineStatus.Conflict
                                                       || l.Status == PriceLineStatus.InvalidPrice || l.Status == PriceLineStatus.NoPrice);
        public int ReviewCount => Lines.Count(l => l.Status == PriceLineStatus.WillUpdate && l.RequiresReview);
    }

    /// <summary>
    /// مطابقة سطور الملف بالأصناف وحساب التغييرات. للقراءة فقط (مبتغيّرش حاجة).
    /// أولوية المطابقة: باركود ← كود ← اسم مطبّع (مساواة تامة بعد التطبيع العربي، مش جزء من اسم).
    /// </summary>
    public static class PriceListPlanner
    {
        public static PriceListPlan Build(IReadOnlyList<PriceListRow> rows, IReadOnlyList<Item> items, PriceListOptions? options = null)
        {
            options ??= new PriceListOptions();
            var plan = new PriceListPlan();

            var byBarcode = new Dictionary<string, List<Item>>();
            var byCode = new Dictionary<string, List<Item>>();
            var byName = new Dictionary<string, List<Item>>();
            foreach (var item in items.Where(i => i.IsActive))
            {
                Index(byBarcode, PriceListParser.CleanIdentifier(item.Barcode), item);
                Index(byCode, PriceListParser.CleanIdentifier(item.Code), item);
                Index(byName, ArabicTextNormalizer.Normalize(item.NameAr), item);
                string en = ArabicTextNormalizer.Normalize(item.NameEn);
                if (en.Length > 0 && en != ArabicTextNormalizer.Normalize(item.NameAr)) Index(byName, en, item);
            }

            // أول سطر مطابق لكل صنف (لاكتشاف التكرار/التعارض)
            var firstLineByItem = new Dictionary<int, PriceListLine>();

            foreach (var row in rows)
            {
                var line = new PriceListLine { Row = row };
                plan.Lines.Add(line);

                if (row.InvalidPrices.Count > 0)
                {
                    line.Status = PriceLineStatus.InvalidPrice;
                    line.Note = "سعر غير صالح: " + string.Join("، ", row.InvalidPrices.Select(p => $"{p.Value}"));
                    continue;
                }
                if (!row.HasAnyPrice)
                {
                    line.Status = PriceLineStatus.NoPrice;
                    line.Note = "السطر مفيهوش سعر";
                    continue;
                }

                var match = Resolve(row, byBarcode, byCode, byName, out string matchedBy, out bool ambiguous);
                if (ambiguous)
                {
                    line.Status = PriceLineStatus.Ambiguous;
                    line.MatchedBy = matchedBy;
                    line.Note = "أكتر من صنف مطابق";
                    continue;
                }
                if (match == null)
                {
                    line.Status = PriceLineStatus.NotFound;
                    line.Note = "الصنف مش موجود في النظام";
                    continue;
                }

                line.Item = match;
                line.MatchedBy = matchedBy;

                AddChange(line, options.UpdateRetail, PriceField.Retail, match.RetailPriceBoxPiastres, row.RetailPiastres, options);
                AddChange(line, options.UpdateWholesale, PriceField.Wholesale, match.WholesalePriceBoxPiastres, row.WholesalePiastres, options);
                AddChange(line, options.UpdateCost, PriceField.Cost, match.CostPriceBoxPiastres, row.CostPiastres, options);

                if (firstLineByItem.TryGetValue(match.Id, out var first))
                {
                    if (SamePrices(first, line))
                    {
                        // تكرار مطابق: بنتجاهله
                        line.Changes.Clear();
                        line.Status = PriceLineStatus.Unchanged;
                        line.Note = $"مكرر (نفس الصنف في الصف {first.Row.RowNumber})";
                    }
                    else
                    {
                        // تعارض: بنوقف الاتنين بدل ما نخمّن أنهي سعر الصح
                        first.Changes.Clear();
                        first.Status = PriceLineStatus.Conflict;
                        first.RetailBelowCost = false;
                        first.Note = $"نفس الصنف بسعر مختلف في الصف {row.RowNumber}";
                        line.Changes.Clear();
                        line.Status = PriceLineStatus.Conflict;
                        line.Note = $"نفس الصنف بسعر مختلف في الصف {first.Row.RowNumber}";
                    }
                    continue;
                }

                firstLineByItem[match.Id] = line;

                if (line.Changes.Count == 0)
                {
                    line.Status = PriceLineStatus.Unchanged;
                    continue;
                }

                line.Status = PriceLineStatus.WillUpdate;

                long newRetail = row.RetailPiastres.HasValue && options.UpdateRetail ? row.RetailPiastres.Value : match.RetailPriceBoxPiastres;
                long newCost = row.CostPiastres.HasValue && options.UpdateCost ? row.CostPiastres.Value : match.CostPriceBoxPiastres;
                line.RetailBelowCost = newRetail > 0 && newCost > 0 && newRetail < newCost;
            }

            return plan;
        }

        private static void AddChange(PriceListLine line, bool enabled, PriceField field, long oldValue, long? newValue, PriceListOptions options)
        {
            if (!enabled || !newValue.HasValue || newValue.Value == oldValue) return;
            var item = line.Item!;
            var change = new ItemPriceChange
            {
                ItemId = item.Id,
                ItemCode = item.Code,
                ItemNameAr = item.NameAr,
                Field = field,
                OldPiastres = oldValue,
                NewPiastres = newValue.Value,
                SourceRowNumber = line.Row.RowNumber
            };
            change.IsLargeChange = oldValue > 0 && Math.Abs(change.ChangePercent) >= options.LargeChangePercent;
            line.Changes.Add(change);
        }

        private static bool SamePrices(PriceListLine a, PriceListLine b) =>
            a.Row.RetailPiastres == b.Row.RetailPiastres
            && a.Row.WholesalePiastres == b.Row.WholesalePiastres
            && a.Row.CostPiastres == b.Row.CostPiastres;

        private static Item? Resolve(PriceListRow row,
            Dictionary<string, List<Item>> byBarcode, Dictionary<string, List<Item>> byCode, Dictionary<string, List<Item>> byName,
            out string matchedBy, out bool ambiguous)
        {
            matchedBy = string.Empty;
            ambiguous = false;

            if (TryLookup(byBarcode, row.Barcode, "باركود", ref matchedBy, ref ambiguous, out var found)) return found;
            if (ambiguous) return null;
            if (TryLookup(byCode, row.Code, "كود", ref matchedBy, ref ambiguous, out found)) return found;
            if (ambiguous) return null;
            // الباركود ساعات بيكون في عمود الكود وبالعكس
            if (TryLookup(byBarcode, row.Code, "باركود", ref matchedBy, ref ambiguous, out found)) return found;
            if (ambiguous) return null;
            if (TryLookup(byCode, row.Barcode, "كود", ref matchedBy, ref ambiguous, out found)) return found;
            if (ambiguous) return null;
            if (TryLookup(byName, ArabicTextNormalizer.Normalize(row.Name), "اسم", ref matchedBy, ref ambiguous, out found)) return found;
            return null;
        }

        private static bool TryLookup(Dictionary<string, List<Item>> index, string key, string by, ref string matchedBy, ref bool ambiguous, out Item? found)
        {
            found = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (!index.TryGetValue(key, out var list)) return false;
            matchedBy = by;
            if (list.Count > 1)
            {
                ambiguous = true;
                return false;
            }
            found = list[0];
            return true;
        }

        private static void Index(Dictionary<string, List<Item>> index, string key, Item item)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!index.TryGetValue(key, out var list)) index[key] = list = new List<Item>();
            if (!list.Any(i => i.Id == item.Id)) list.Add(item);
        }
    }
}
