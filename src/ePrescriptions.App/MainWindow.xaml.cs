using System;
using System.Windows;
using System.Windows.Threading;
using ePrescriptions.App.Services;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App
{
    public partial class MainWindow : Window
    {
        private readonly IdleLockMonitor? _idleMonitor;
        private readonly DispatcherTimer? _idleTimer;

        public MainWindow(MainViewModel viewModel, IdleLockMonitor? idleMonitor = null)
        {
            InitializeComponent();
            DataContext = viewModel;

            // الحجم ما يزيدش عن مساحة الشاشة المتاحة (بدون شريط المهام) عشان الأطراف والأزرار ما تتقطعش
            var work = SystemParameters.WorkArea;
            Width = System.Math.Min(Width, work.Width);
            Height = System.Math.Min(Height, work.Height);
            MinWidth = System.Math.Min(MinWidth, work.Width);
            MinHeight = System.Math.Min(MinHeight, work.Height);
            WindowState = WindowState.Maximized;

            // قفل تلقائي بعد خمول: أي حركة ماوس/كيبورد/ضغط بتصفّر العدّاد
            _idleMonitor = idleMonitor;
            if (_idleMonitor != null)
            {
                PreviewMouseMove += (_, __) => _idleMonitor.NotifyActivity();
                PreviewMouseDown += (_, __) => _idleMonitor.NotifyActivity();
                PreviewKeyDown += (_, __) => _idleMonitor.NotifyActivity();

                _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                _idleTimer.Tick += (_, __) => _idleMonitor.Tick();
                _idleTimer.Start();
            }
        }
    }
}
