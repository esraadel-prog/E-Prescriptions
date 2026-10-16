using System.Windows;
using System.Windows.Controls;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;

        public LoginWindow(LoginViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            _viewModel.LoginSucceeded += () =>
            {
                DialogResult = true;
                Close();
            };

            // خطوة 46: قفل النافذة (Alt+F4 مثلاً) أثناء التغيير الإجباري = إنهاء الجلسة وقفل البرنامج، مش تجاوز
            Closing += (s, e) =>
            {
                if (_viewModel.IsForcedPasswordChange) _viewModel.CancelForcedPasswordChange();
            };
        }

        // سحب النافذة من أي مكان فاضي (مش من الحقول أو الأزرار)
        private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
            var d = e.OriginalSource as DependencyObject;
            while (d != null && !(d is Window))
            {
                if (d is System.Windows.Controls.Primitives.TextBoxBase || d is PasswordBox ||
                    d is System.Windows.Controls.Primitives.ButtonBase) return;
                d = (d is System.Windows.Media.Visual || d is System.Windows.Media.Media3D.Visual3D)
                    ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            try { DragMove(); } catch (System.InvalidOperationException ex) { ePrescriptions.Core.Helpers.Logger.LogException(ex, "LoginWindow.DragMove"); }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void TxtNewPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb) _viewModel.NewPassword = pb.Password;
        }

        private void TxtConfirmNewPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb) _viewModel.ConfirmNewPassword = pb.Password;
        }

        private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                _viewModel.Password = pb.Password;
            }
        }
    }
}
