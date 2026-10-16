using System;
using System.Collections.Generic;
using System.Globalization;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Domain.Entities
{
    /// <summary>
    /// مصفوفة الصلاحيات المخصصة للموظف (سواء كان يتبع الدور الافتراضي أو لديه صلاحيات مخصصة بشكل مستقل).
    /// </summary>
    public class UserPermissions
    {
        /// <summary>هل تم تفعيل صلاحيات مخصصة يدوياً للموظف كاستثناء عن دوره الافتراضي؟</summary>
        public bool IsCustom { get; set; }

        // ===== صلاحيات فتح الشاشات والتنقل =====
        public bool CanAccessSales { get; set; } = true;
        public bool CanAccessInventory { get; set; } = false;
        public bool CanAccessPurchases { get; set; } = false;
        public bool CanAccessCustomers { get; set; } = false;
        public bool CanAccessReports { get; set; } = false;
        public bool CanAccessFinance { get; set; } = false;
        public bool CanAccessStaff { get; set; } = false;
        public bool CanAccessSettings { get; set; } = false;
        public bool CanAccessPriceListImport { get; set; } = false;

        // ===== صلاحيات العمليات والرقابة الحساسة =====
        /// <summary>سقف الخصم الأقصى المسموح به للموظف (٪ من إجمالي البند قبل الخصم)</summary>
        public decimal MaxDiscountPercent { get; set; } = 5m;

        /// <summary>هل يُسمح له بعمل مرتجع مبيعات دون طلب موافقة إدارية؟</summary>
        public bool CanPerformReturnsWithoutApproval { get; set; } = false;

        /// <summary>هل يملك صلاحية اعتماد وتمرير تجاوزات الخصومات والمرتجعات للموظفين الآخرين؟</summary>
        public bool CanApproveOverrides { get; set; } = false;

        /// <summary>هل يُسمح له بتسجيل واعتماد تسويات الجرد المخزني وعجز/زيادة الأصناف؟</summary>
        public bool CanManageStocktake { get; set; } = false;

        /// <summary>هل يُسمح له بتعديل أسعار الشراء والبيع للأدوية في المخزون؟</summary>
        public bool CanEditItemPrices { get; set; } = false;

        /// <summary>
        /// توليد الصلاحيات الافتراضية التلقائية المقابلة للدور
        /// </summary>
        public static UserPermissions CreateDefault(UserRole role)
        {
            var p = new UserPermissions
            {
                IsCustom = false
            };

            switch (role)
            {
                case UserRole.Owner:
                case UserRole.Admin:
                    p.CanAccessSales = true;
                    p.CanAccessInventory = true;
                    p.CanAccessPurchases = true;
                    p.CanAccessCustomers = true;
                    p.CanAccessReports = true;
                    p.CanAccessFinance = true;
                    p.CanAccessStaff = true;
                    p.CanAccessSettings = true;
                    p.CanAccessPriceListImport = true;
                    p.MaxDiscountPercent = 100m;
                    p.CanPerformReturnsWithoutApproval = true;
                    p.CanApproveOverrides = true;
                    p.CanManageStocktake = true;
                    p.CanEditItemPrices = true;
                    break;

                case UserRole.Pharmacist:
                    p.CanAccessSales = true;
                    p.CanAccessInventory = true;
                    p.CanAccessPurchases = true;
                    p.CanAccessCustomers = true;
                    p.CanAccessReports = true;
                    p.CanAccessFinance = false;
                    p.CanAccessStaff = false;
                    p.CanAccessSettings = false;
                    p.CanAccessPriceListImport = false;
                    p.MaxDiscountPercent = 10m;
                    p.CanPerformReturnsWithoutApproval = false;
                    p.CanApproveOverrides = false;
                    p.CanManageStocktake = true;
                    p.CanEditItemPrices = false;
                    break;

                case UserRole.Cashier:
                default:
                    p.CanAccessSales = true;
                    p.CanAccessInventory = false;
                    p.CanAccessPurchases = false;
                    p.CanAccessCustomers = false;
                    p.CanAccessReports = false;
                    p.CanAccessFinance = false;
                    p.CanAccessStaff = false;
                    p.CanAccessSettings = false;
                    p.CanAccessPriceListImport = false;
                    p.MaxDiscountPercent = 5m;
                    p.CanPerformReturnsWithoutApproval = false;
                    p.CanApproveOverrides = false;
                    p.CanManageStocktake = false;
                    p.CanEditItemPrices = false;
                    break;
            }

            return p;
        }

        /// <summary>
        /// استرجاع الصلاحيات المطبقة فعلياً: إن وُجد تخصيص محفوظ يُطبّق، وإلا تتبع الدور الافتراضي.
        /// حساب صاحب الصيدلية يملك كامل الصلاحيات دائماً ولا يخضع لأي قيود.
        /// </summary>
        public static UserPermissions Resolve(UserRole role, string? config)
        {
            if (role == UserRole.Owner)
                return CreateDefault(UserRole.Owner);

            if (string.IsNullOrWhiteSpace(config))
                return CreateDefault(role);

            var custom = FromConfigString(config, role);
            return custom.IsCustom ? custom : CreateDefault(role);
        }

        /// <summary>
        /// تحويل الصلاحيات إلى نص تخزين نصي آمن وقابل للقراءة
        /// </summary>
        public string ToConfigString()
        {
            return string.Join(";", new[]
            {
                $"IsCustom={(IsCustom ? "1" : "0")}",
                $"CanAccessSales={(CanAccessSales ? "1" : "0")}",
                $"CanAccessInventory={(CanAccessInventory ? "1" : "0")}",
                $"CanAccessPurchases={(CanAccessPurchases ? "1" : "0")}",
                $"CanAccessCustomers={(CanAccessCustomers ? "1" : "0")}",
                $"CanAccessReports={(CanAccessReports ? "1" : "0")}",
                $"CanAccessFinance={(CanAccessFinance ? "1" : "0")}",
                $"CanAccessStaff={(CanAccessStaff ? "1" : "0")}",
                $"CanAccessSettings={(CanAccessSettings ? "1" : "0")}",
                $"CanAccessPriceListImport={(CanAccessPriceListImport ? "1" : "0")}",
                $"MaxDiscountPercent={MaxDiscountPercent.ToString(CultureInfo.InvariantCulture)}",
                $"CanPerformReturnsWithoutApproval={(CanPerformReturnsWithoutApproval ? "1" : "0")}",
                $"CanApproveOverrides={(CanApproveOverrides ? "1" : "0")}",
                $"CanManageStocktake={(CanManageStocktake ? "1" : "0")}",
                $"CanEditItemPrices={(CanEditItemPrices ? "1" : "0")}"
            });
        }

        /// <summary>
        /// استرجاع الصلاحيات من النص المخزن
        /// </summary>
        public static UserPermissions FromConfigString(string? config, UserRole fallbackRole)
        {
            var p = CreateDefault(fallbackRole);
            if (string.IsNullOrWhiteSpace(config)) return p;

            var pairs = config!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string key = pair.Substring(0, eq).Trim();
                string val = pair.Substring(eq + 1).Trim();

                bool isTrue = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);

                switch (key)
                {
                    case "IsCustom": p.IsCustom = isTrue; break;
                    case "CanAccessSales": p.CanAccessSales = isTrue; break;
                    case "CanAccessInventory": p.CanAccessInventory = isTrue; break;
                    case "CanAccessPurchases": p.CanAccessPurchases = isTrue; break;
                    case "CanAccessCustomers": p.CanAccessCustomers = isTrue; break;
                    case "CanAccessReports": p.CanAccessReports = isTrue; break;
                    case "CanAccessFinance": p.CanAccessFinance = isTrue; break;
                    case "CanAccessStaff": p.CanAccessStaff = isTrue; break;
                    case "CanAccessSettings": p.CanAccessSettings = isTrue; break;
                    case "CanAccessPriceListImport": p.CanAccessPriceListImport = isTrue; break;
                    case "MaxDiscountPercent":
                        if (decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
                            p.MaxDiscountPercent = Math.Max(0m, Math.Min(100m, d));
                        break;
                    case "CanPerformReturnsWithoutApproval": p.CanPerformReturnsWithoutApproval = isTrue; break;
                    case "CanApproveOverrides": p.CanApproveOverrides = isTrue; break;
                    case "CanManageStocktake": p.CanManageStocktake = isTrue; break;
                    case "CanEditItemPrices": p.CanEditItemPrices = isTrue; break;
                }
            }

            return p;
        }

        /// <summary>نسخ كائن الصلاحيات</summary>
        public UserPermissions Clone()
        {
            return new UserPermissions
            {
                IsCustom = this.IsCustom,
                CanAccessSales = this.CanAccessSales,
                CanAccessInventory = this.CanAccessInventory,
                CanAccessPurchases = this.CanAccessPurchases,
                CanAccessCustomers = this.CanAccessCustomers,
                CanAccessReports = this.CanAccessReports,
                CanAccessFinance = this.CanAccessFinance,
                CanAccessStaff = this.CanAccessStaff,
                CanAccessSettings = this.CanAccessSettings,
                CanAccessPriceListImport = this.CanAccessPriceListImport,
                MaxDiscountPercent = this.MaxDiscountPercent,
                CanPerformReturnsWithoutApproval = this.CanPerformReturnsWithoutApproval,
                CanApproveOverrides = this.CanApproveOverrides,
                CanManageStocktake = this.CanManageStocktake,
                CanEditItemPrices = this.CanEditItemPrices
            };
        }
    }
}
