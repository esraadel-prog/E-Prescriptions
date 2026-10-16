using System;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Domain.Entities
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Pharmacist;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>نص التكوين المخزّن للصلاحيات المخصصة (null يعني اتباع الدور الافتراضي)</summary>
        public string? CustomPermissionsConfig { get; set; }

        /// <summary>الصلاحيات الفعلية المطبقة على هذا المستخدم (مخصصة أو افتراضية تتبع الدور)</summary>
        public UserPermissions EffectivePermissions => UserPermissions.Resolve(Role, CustomPermissionsConfig);

        /// <summary>ملخص نوع الصلاحيات للعرض في الجداول والتقارير</summary>
        public string PermissionsSummary
        {
            get
            {
                if (Role == UserRole.Owner) return "صاحب الصيدلية";
                if (EffectivePermissions.IsCustom) return "صلاحيات مخصصة";
                return Role switch
                {
                    UserRole.Admin => "افتراضي (مدير)",
                    UserRole.Pharmacist => "افتراضي (صيدلي)",
                    UserRole.Cashier => "افتراضي (كاشير)",
                    _ => "افتراضي"
                };
            }
        }
    }


    public class Shift
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }

        public long OpeningBalancePiastres { get; set; }
        public long TotalSalesCashPiastres { get; set; }
        public long TotalSalesNetworkPiastres { get; set; }
        public long TotalSalesOnAccountPiastres { get; set; }
        public long TotalReturnsCashPiastres { get; set; }
        public long ExpectedCashPiastres { get; set; }
        public long? ActualCashPiastres { get; set; }
        public long? DifferencePiastres { get; set; }

        public ShiftStatus Status { get; set; } = ShiftStatus.Open;
        public string? Notes { get; set; }
    }

    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? NationalId { get; set; }

        public long CreditLimitPiastres { get; set; }
        public long CurrentBalancePiastres { get; set; }

        public PriceCategory PriceCategory { get; set; } = PriceCategory.Retail;
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>العميل النقدي العام (واحد فقط): مفيش رصيد ولا حد ائتمان عليه</summary>
        public bool IsDefaultCash { get; set; }
    }

    public class Supplier
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? TaxNumber { get; set; }
        public string? Address { get; set; }

        public long BalancePiastres { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
