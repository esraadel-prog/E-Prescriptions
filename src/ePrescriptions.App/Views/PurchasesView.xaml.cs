using System.Windows.Controls;

namespace ePrescriptions.App.Views
{
    public partial class PurchasesView : UserControl
    {
        public PurchasesView() { InitializeComponent(); }

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == System.Windows.Input.Key.F4)
            {
                if (DataContext is ViewModels.PurchasesViewModel vm)
                {
                    vm.OpenItemSearch();
                    PurchaseSearchBox?.Focus();
                    PurchaseSearchBox?.SelectAll();
                    e.Handled = true;
                }
            }
            else if (e.Key == System.Windows.Input.Key.Delete)
            {
                // إذا لم يكن المستخدم يكتب داخل TextBox أو DatePicker
                if (!(System.Windows.Input.Keyboard.FocusedElement is TextBox) &&
                    !(System.Windows.Input.Keyboard.FocusedElement is PasswordBox))
                {
                    if (DataContext is ViewModels.PurchasesViewModel vm && vm.CartItems.Count > 0)
                    {
                        vm.RemoveSelectedItem();
                        e.Handled = true;
                    }
                }
            }
        }
    }
}
