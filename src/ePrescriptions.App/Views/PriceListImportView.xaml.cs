using System.Windows;
using System.Windows.Controls;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App.Views
{
    public partial class PriceListImportView : UserControl
    {
        public PriceListImportView()
        {
            InitializeComponent();
        }

        // اختيار الملف في الواجهة، والقراءة والمعاينة في الـ ViewModel (عشان تتختبر)
        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PriceListImportViewModel vm) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "اختر ملف قائمة أسعار المورد",
                Filter = "ملفات Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm"
            };

            if (dialog.ShowDialog() == true)
                vm.LoadFile(dialog.FileName);
        }
    }
}
