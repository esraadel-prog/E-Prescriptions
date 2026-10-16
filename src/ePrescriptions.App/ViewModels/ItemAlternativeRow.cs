using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;

namespace ePrescriptions.App.ViewModels
{
    /// <summary>
    /// خطوة 35: سطر عرض بديل الصنف بالمادة الفعالة مع السعر والكمية المتاحة
    /// </summary>
    public class ItemAlternativeRow
    {
        public Item Item { get; }
        public int Id => Item.Id;
        public string Code => Item.Code;
        public string NameAr => Item.NameAr;
        public string NameEn => Item.NameEn;
        public string ActiveIngredient => Item.ActiveIngredient;
        public decimal RetailPricePounds => Item.RetailPriceBoxPiastres / 100.0m;
        public int TotalStockSmallestUnits => Item.TotalStockSmallestUnits;
        public string FormattedStock => UnitBreakdownHelper.FormatStockDetailed(Item.TotalStockSmallestUnits, Item.StripsPerBox, Item.PiecesPerStrip);

        public ItemAlternativeRow(Item item)
        {
            Item = item;
        }
    }
}
