using System.Windows;
using System.Windows.Controls;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App.Views
{
    public partial class OwnerLogView : UserControl
    {
        public OwnerLogView() { InitializeComponent(); }

        // PasswordBox.Password غير قابل للربط مباشرة (Binding) لأسباب أمنية في WPF، فنمرره يدوياً لنفس نمط LoginWindow
        private void PwdCurrent_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is OwnerLogViewModel vm && sender is PasswordBox pb) vm.CurrentPassword = pb.Password;
        }

        private void PwdNew_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is OwnerLogViewModel vm && sender is PasswordBox pb) vm.NewPassword = pb.Password;
        }

        private void PwdConfirm_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is OwnerLogViewModel vm && sender is PasswordBox pb) vm.ConfirmNewPassword = pb.Password;
        }
    }
}
