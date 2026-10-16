using System;
using ePrescriptions.App.ViewModels;

namespace ePrescriptions.App.Services
{
    public interface INavigationService
    {
        ViewModelBase? CurrentViewModel { get; }
        event Action<ViewModelBase>? CurrentViewModelChanged;
        void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
        void NavigateTo(ViewModelBase viewModel);
    }

    public class NavigationService : INavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private ViewModelBase? _currentViewModel;

        public ViewModelBase? CurrentViewModel => _currentViewModel;

        public event Action<ViewModelBase>? CurrentViewModelChanged;

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
        {
            var vm = (ViewModelBase)_serviceProvider.GetService(typeof(TViewModel));
            NavigateTo(vm);
        }

        public void NavigateTo(ViewModelBase viewModel)
        {
            if (_currentViewModel == viewModel) return;

            _currentViewModel?.OnNavigatedFrom();
            _currentViewModel = viewModel;
            _currentViewModel.OnNavigatedTo();

            CurrentViewModelChanged?.Invoke(_currentViewModel);
        }
    }
}
