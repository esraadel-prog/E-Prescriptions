using System.Windows;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App.Views
{
    public partial class SmartAlertsWindow : Window
    {
        private readonly SmartAlertsViewModel _viewModel;

        public SmartAlertsWindow(SmartAlertsViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            _viewModel.RequestClose += () =>
            {
                DialogResult = true;
                Close();
            };
        }
    }
}
