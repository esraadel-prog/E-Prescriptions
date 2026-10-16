using System;
using System.Windows;
using ePrescriptions.App.ViewModels;
using ePrescriptions.Core.Helpers;

namespace ePrescriptions.App.Views
{
    public partial class StandaloneWindow : Window
    {
        public ViewModelBase CurrentViewModel { get; }
        public string WindowTitle { get; }

        public StandaloneWindow(ViewModelBase viewModel, string title)
        {
            CurrentViewModel = viewModel;
            WindowTitle = title;
            Title = $"e_prescriptions - {title}";

            InitializeComponent();
            DataContext = this;

            // ضبط الحجم ليناسب الشاشة مع الحفاظ على التوسيط التام
            try
            {
                var workArea = SystemParameters.WorkArea;
                if (Width > workArea.Width * 0.95)
                {
                    Width = Math.Max(MinWidth, workArea.Width * 0.90);
                }
                if (Height > workArea.Height * 0.95)
                {
                    Height = Math.Max(MinHeight, workArea.Height * 0.90);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StandaloneWindow.CtorDimensions");
            }

            SourceInitialized += (s, e) => CenterOnScreen();

            Loaded += (s, e) =>
            {
                CenterOnScreen();
                try
                {
                    CurrentViewModel.OnNavigatedTo();
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "StandaloneWindow.Loaded");
                }
            };

            Closed += (s, e) =>
            {
                try
                {
                    CurrentViewModel.OnNavigatedFrom();
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "StandaloneWindow.Closed");
                }
            };
        }

        public void CenterOnScreen()
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                if (Owner != null && Owner.IsVisible && Owner.WindowState != WindowState.Minimized)
                {
                    Left = Owner.Left + Math.Max(0, (Owner.ActualWidth - (ActualWidth > 0 ? ActualWidth : Width)) / 2);
                    Top = Owner.Top + Math.Max(0, (Owner.ActualHeight - (ActualHeight > 0 ? ActualHeight : Height)) / 2);
                }
                else
                {
                    double w = ActualWidth > 0 ? ActualWidth : Width;
                    double h = ActualHeight > 0 ? ActualHeight : Height;
                    Left = workArea.Left + Math.Max(0, (workArea.Width - w) / 2);
                    Top = workArea.Top + Math.Max(0, (workArea.Height - h) / 2);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "StandaloneWindow.CenterOnScreen");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
