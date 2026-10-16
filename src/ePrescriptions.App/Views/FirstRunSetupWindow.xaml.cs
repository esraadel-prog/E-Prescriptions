using System.Windows;
using System.Windows.Controls;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App.Views
{
    public partial class FirstRunSetupWindow : Window
    {
        private readonly FirstRunSetupViewModel _viewModel;

        public FirstRunSetupWindow(FirstRunSetupViewModel viewModel)
        {
            InitializeComponent();

            // ما يزيدش عن الشاشة المتاحة، والمحتوى جواه ScrollViewer
            var work = SystemParameters.WorkArea;
            Height = System.Math.Min(Height, work.Height - 20);
            Width = System.Math.Min(Width, work.Width - 20);
            MinHeight = System.Math.Min(MinHeight, Height);
            MinWidth = System.Math.Min(MinWidth, Width);

            _viewModel = viewModel;
            DataContext = _viewModel;

            _viewModel.SetupCompleted += () =>
            {
                DialogResult = true;
                Close();
            };
        }

        private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                _viewModel.Password = pb.Password;
            }
        }

        private void TxtConfirm_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                _viewModel.ConfirmPassword = pb.Password;
            }
        }
    }
}
