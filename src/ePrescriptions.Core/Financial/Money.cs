using System;
using System.Globalization;

namespace ePrescriptions.Core.Financial
{
    /// <summary>
    /// يمثل القيمة المالية بالوحدة الصغرى (القرش) كقيمة عددية صحيحة (long) لمنع أخطاء الفاصلة العائمة نهائياً.
    /// 1 جنيه مصري = 100 قرش.
    /// يعتمد قاعدة التقريب للنصف للأعلى (AwayFromZero / Half-Up) على مستوى كل بند.
    /// </summary>
    public readonly struct Money : IEquatable<Money>, IComparable<Money>
    {
        public static readonly Money Zero = new(0);

        /// <summary>
        /// القيمة بالقروش كعدد صحيح دقيق (long).
        /// 1 جنيه مصري = 100 قرش.
        /// </summary>
        public long Piastres { get; }

        public Money(long piastres)
        {
            Piastres = piastres;
        }

        public static Money FromPiastres(long piastres) => new(piastres);

        /// <summary>
        /// تحويل من قيمة بالجنيه المصري إلى قروش مع تقريب النصف للأعلى (Half-Up).
        /// يستخدم فقط في إدخال المستخدم قبل التخزين.
        /// </summary>
        public static Money FromPounds(decimal pounds)
        {
            long piastres = (long)Math.Round(pounds * 100m, 0, MidpointRounding.AwayFromZero);
            return new Money(piastres);
        }

        public static Money FromPounds(double pounds)
        {
            long piastres = (long)Math.Round(pounds * 100.0, 0, MidpointRounding.AwayFromZero);
            return new Money(piastres);
        }

        /// <summary>
        /// القيمة بالجنيه المصري للعرض في واجهة المستخدم والتقارير.
        /// </summary>
        public decimal ToPounds() => Piastres / 100m;

        /// <summary>
        /// حساب ضريبة القيمة المضافة بنسبة مئوية صحيحة (0% أو 5% أو 14% أو ما يحدده الصنف والإعدادات)
        /// باستخدام تقريب Half-Up صحيح بالقروش.
        /// القاعدة: (المبلغ بالقروش * النسبة + 50) / 100
        /// </summary>
        public static Money CalculateVat(long amountPiastres, int vatPercent = 14)
        {
            if (amountPiastres <= 0 || vatPercent <= 0)
                return Zero;

            long numerator = (amountPiastres * vatPercent) + 50;
            long vatPiastres = numerator / 100;
            return new Money(vatPiastres);
        }

        /// <summary>
        /// حساب الخصم بنسبة مئوية صحيحة مع تقريب Half-Up.
        /// </summary>
        public static Money CalculatePercentageDiscount(long amountPiastres, int discountPercent)
        {
            if (amountPiastres <= 0 || discountPercent <= 0)
                return Zero;

            if (discountPercent >= 100)
                return new Money(amountPiastres);

            long numerator = (amountPiastres * discountPercent) + 50;
            long discountPiastres = numerator / 100;
            return new Money(discountPiastres);
        }

        /// <summary>
        /// استخراج المبلغ قبل الضريبة من مبلغ شامل للضريبة:
        /// Base = Round(Gross * 100 / (100 + vatPercent))
        /// </summary>
        public static Money ExtractNetFromGross(long grossPiastres, int vatPercent = 14)
        {
            if (grossPiastres <= 0)
                return Zero;

            long factor = 100 + vatPercent;
            long basePiastres = ((grossPiastres * 100) + (factor / 2)) / factor;
            return new Money(basePiastres);
        }

        public static Money operator +(Money a, Money b) => new(a.Piastres + b.Piastres);
        public static Money operator -(Money a, Money b) => new(a.Piastres - b.Piastres);
        public static Money operator *(Money a, long factor) => new(a.Piastres * factor);
        public static Money operator *(Money a, int factor) => new(a.Piastres * factor);
        public static Money operator /(Money a, long divisor)
        {
            if (divisor == 0) throw new DivideByZeroException();
            long res = (a.Piastres + (divisor / 2)) / divisor;
            return new Money(res);
        }

        public static bool operator ==(Money a, Money b) => a.Piastres == b.Piastres;
        public static bool operator !=(Money a, Money b) => a.Piastres != b.Piastres;
        public static bool operator >(Money a, Money b) => a.Piastres > b.Piastres;
        public static bool operator <(Money a, Money b) => a.Piastres < b.Piastres;
        public static bool operator >=(Money a, Money b) => a.Piastres >= b.Piastres;
        public static bool operator <=(Money a, Money b) => a.Piastres <= b.Piastres;

        public bool Equals(Money other) => Piastres == other.Piastres;
        public override bool Equals(object? obj) => obj is Money other && Equals(other);
        public override int GetHashCode() => Piastres.GetHashCode();
        public int CompareTo(Money other) => Piastres.CompareTo(other.Piastres);

        /// <summary>
        /// تنسيق القيمة المالية بالجنيه المصري مع أرقام لاتينية ثابتة (مثال: 125.50 ج.م).
        /// </summary>
        public string ToFormattedString(bool includeCurrency = true)
        {
            decimal pounds = ToPounds();
            string numberPart = pounds.ToString("N2", CultureInfo.InvariantCulture);
            return includeCurrency ? $"{numberPart} ج.م" : numberPart;
        }

        public override string ToString() => ToFormattedString(true);
    }
}
