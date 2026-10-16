using System.Windows.Controls;
using System.Windows.Input;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App.Views
{
    public partial class StocktakeView : UserControl
    {
        public StocktakeView()
        {
            InitializeComponent();
            Loaded += (s, e) => ScanBox.Focus();
        }

        // قارئ الباركود بيكتب الرمز ثم Enter
        private void ScanBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is StocktakeViewModel vm)
            {
                vm.ScanCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
