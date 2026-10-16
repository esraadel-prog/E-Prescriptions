using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App.Views
{
    public partial class OpeningStockView : UserControl
    {
        public OpeningStockView()
        {
            InitializeComponent();
            Loaded += (_, __) =>
            {
                TxtScanInput?.Focus();
            };
        }

        private void TxtScanInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is OpeningStockViewModel vm)
                {
                    vm.HandleScanOrSearchCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        private void BtnImportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not OpeningStockViewModel vm) return;

            var dialog = new OpenFileDialog
            {
                Title = "اختيار ملف Excel لحصر رصيد أول المدة",
                Filter = "ملفات Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|جميع الملفات (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                vm.ImportExcelFromPathCommand.Execute(dialog.FileName);
            }
        }

        private void DgSearchResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is OpeningStockViewModel vm)
            {
                vm.SelectSearchResultCommand.Execute(null);
            }
        }

        private void DgPastVouchers_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is OpeningStockViewModel vm)
            {
                vm.LoadSelectedPastVoucherCommand.Execute(null);
            }
        }
    }
}
