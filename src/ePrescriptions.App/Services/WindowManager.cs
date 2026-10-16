using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ePrescriptions.App.ViewModels;
using ePrescriptions.App.Views;
using ePrescriptions.Core.Helpers;

namespace ePrescriptions.App.Services
{
    public interface IWindowManager
    {
        Window OpenInSeparateWindow<TViewModel>() where TViewModel : ViewModelBase;
        Window OpenInSeparateWindow(ViewModelBase viewModel, string? title = null);
        void CloseAll();
    }

    public class WindowManager : IWindowManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<Type, Window> _openWindows = new();

        public WindowManager(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Window OpenInSeparateWindow<TViewModel>() where TViewModel : ViewModelBase
        {
            var type = typeof(TViewModel);
            if (_openWindows.TryGetValue(type, out var existingWindow) && existingWindow.IsLoaded)
            {
                if (existingWindow.WindowState == WindowState.Minimized)
                {
                    existingWindow.WindowState = WindowState.Normal;
                }
                existingWindow.Activate();
                existingWindow.Focus();
                return existingWindow;
            }

            var vm = _serviceProvider.GetRequiredService<TViewModel>();
            string title = GetDefaultTitleForViewModel(vm);
            var window = new StandaloneWindow(vm, title);
            if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                window.Owner = Application.Current.MainWindow;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            _openWindows[type] = window;

            window.Closed += (s, e) =>
            {
                _openWindows.Remove(type);
            };

            window.Show();
            return window;
        }

        public Window OpenInSeparateWindow(ViewModelBase viewModel, string? title = null)
        {
            var type = viewModel.GetType();
            if (_openWindows.TryGetValue(type, out var existingWindow) && existingWindow.IsLoaded)
            {
                if (existingWindow.WindowState == WindowState.Minimized)
                {
                    existingWindow.WindowState = WindowState.Normal;
                }
                existingWindow.Activate();
                existingWindow.Focus();
                return existingWindow;
            }

            string winTitle = title ?? GetDefaultTitleForViewModel(viewModel);
            var window = new StandaloneWindow(viewModel, winTitle);
            if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                window.Owner = Application.Current.MainWindow;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            _openWindows[type] = window;

            window.Closed += (s, e) =>
            {
                _openWindows.Remove(type);
            };

            window.Show();
            return window;
        }

        public void CloseAll()
        {
            var windows = new List<Window>(_openWindows.Values);
            _openWindows.Clear();
            foreach (var win in windows)
            {
                try
                {
                    if (win.IsLoaded)
                    {
                        win.Close();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "WindowManager.CloseAll");
                }
            }
        }

        public static string GetDefaultTitleForViewModel(ViewModelBase vm) => vm switch
        {
            SalesViewModel => "شاشة المبيعات ونقطة البيع (POS)",
            PurchasesViewModel => "شاشة المشتريات وفواتير الموردين",
            InventoryViewModel => "شاشة إدارة المخزون والأدوية",
            CustomersViewModel => "شاشة حسابات العملاء والآجل",
            PurchaseOrdersViewModel => "شاشة أوامر الشراء والتوريد",
            SupplierReturnsViewModel => "شاشة مرتجع وإتلاف البضائع",
            StocktakeViewModel => "شاشة الجرد السريع بالباركود",
            OpeningStockViewModel => "شاشة رصيد أول المدة (بضاعة أول المدة)",
            PriceListImportViewModel => "شاشة تحديث الأسعار من Excel",
            FinanceViewModel => "شاشة الحسابات والمالية والمصروفات",
            StaffViewModel => "شاشة الموظفين والورديات",
            ZakatViewModel => "شاشة حاسبة الزكاة الشرعية",
            ReportsViewModel => "شاشة التقارير والإحصاءات الشاملة",
            SettingsViewModel => "شاشة إعدادات النظام والطابعات",
            HelpViewModel => "شاشة دليل الاختصارات والتعليمات",
            OwnerLogViewModel => "سجل نشاط صاحب الصيدلية",
            StaffAuditLogViewModel => "سجل تدقيق نشاط الموظفين",
            _ => "نافذة مستقلة"
        };
    }
}
