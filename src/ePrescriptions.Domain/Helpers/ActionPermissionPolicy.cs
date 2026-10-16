using System;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Domain.Helpers
{
    /// <summary>
    /// صلاحيات على مستوى الإجراء (مش الشاشة بس): سقف الخصم الأقصى لكل دور، وإلزام موافقة Admin/Owner
    /// على أي مرتجع (من كاشير/صيدلي) أو خصم يتجاوز سقف الدور. الأرقام قرار تصميمي وسهلة التعديل هنا.
    /// </summary>
    public static class ActionPermissionPolicy
    {
        /// <summary>سقف خصم الكاشير (٪ من إجمالي سعر البند قبل الخصم)</summary>
        public const decimal CashierMaxDiscountPercent = 5m;

        /// <summary>سقف خصم الصيدلي (٪)</summary>
        public const decimal PharmacistMaxDiscountPercent = 10m;

        /// <summary>السقف الأقصى للدور. Admin/Owner بلا سقف (100٪).</summary>
        public static decimal MaxDiscountPercent(UserRole role)
        {
            switch (role)
            {
                case UserRole.Cashier: return CashierMaxDiscountPercent;
                case UserRole.Pharmacist: return PharmacistMaxDiscountPercent;
                default: return 100m;
            }
        }

        /// <summary>السقف الأقصى لخصم المستخدم (يدعم الصلاحيات المخصصة للموظف)</summary>
        public static decimal MaxDiscountPercent(Entities.User? user)
        {
            if (user == null) return CashierMaxDiscountPercent;
            if (user.Role == UserRole.Owner || user.Role == UserRole.Admin) return 100m;
            if (user.EffectivePermissions.IsCustom) return user.EffectivePermissions.MaxDiscountPercent;
            return MaxDiscountPercent(user.Role);
        }

        /// <summary>هل الدور ده يقدر يوافق على إجراء متجاوز (Admin/Owner فقط)؟</summary>
        public static bool CanApprove(UserRole role) => role == UserRole.Admin || role == UserRole.Owner;

        /// <summary>هل المستخدم يقدر يوافق على إجراء متجاوز (Admin/Owner أو من لديه صلاحية الاعتماد)؟</summary>
        public static bool CanApprove(Entities.User? user)
        {
            if (user == null) return false;
            if (user.Role == UserRole.Admin || user.Role == UserRole.Owner) return true;
            return user.EffectivePermissions.IsCustom && user.EffectivePermissions.CanApproveOverrides;
        }

        /// <summary>نسبة الخصم المئوية من الإجمالي قبل الخصم (0 لو الإجمالي صفر).</summary>
        public static decimal DiscountPercent(long grossPiastres, long discountPiastres)
        {
            if (grossPiastres <= 0 || discountPiastres <= 0) return 0m;
            return discountPiastres * 100m / grossPiastres;
        }

        /// <summary>هل الخصم ده يتجاوز سقف الدور؟ (تجاوز فعلي فقط، المساواة مسموحة)</summary>
        public static bool DiscountExceedsRoleCap(UserRole role, long grossPiastres, long discountPiastres) =>
            DiscountPercent(grossPiastres, discountPiastres) > MaxDiscountPercent(role);

        /// <summary>هل الخصم يتجاوز سقف المستخدم المحدد (مع مراعاة الصلاحيات المخصصة)؟</summary>
        public static bool DiscountExceedsUserCap(Entities.User? user, long grossPiastres, long discountPiastres) =>
            DiscountPercent(grossPiastres, discountPiastres) > MaxDiscountPercent(user);

        /// <summary>أي مرتجع مبيعات من كاشير/صيدلي يحتاج موافقة Admin/Owner.</summary>
        public static bool ReturnRequiresApproval(UserRole role) => !CanApprove(role);

        /// <summary>هل يحتاج المرتجع موافقة إدارية للمستخدم الحالي (يدعم استثناء الموظف المخصص)؟</summary>
        public static bool ReturnRequiresApproval(Entities.User? user)
        {
            if (user == null) return true;
            if (user.Role == UserRole.Admin || user.Role == UserRole.Owner) return false;
            if (user.EffectivePermissions.IsCustom && user.EffectivePermissions.CanPerformReturnsWithoutApproval) return false;
            return true;
        }
    }
}

