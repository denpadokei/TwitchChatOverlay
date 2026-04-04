using System.Collections.Generic;
using System.ComponentModel;
using Prism.Mvvm;
using TwitchChatOverlay.Infrastructure;

namespace TwitchChatOverlay.ViewModels
{
    public abstract class TabViewModelBase : BindableBase, IInitialized
    {
        private readonly HashSet<string> _forwardedPropertyNames;
        private bool _initialized;

        protected TabViewModelBase(MainWindowViewModel mainWindowViewModel, params string[] forwardedPropertyNames)
        {
            MainWindowViewModel = mainWindowViewModel;
            _forwardedPropertyNames = new HashSet<string>(forwardedPropertyNames);
            MainWindowViewModel.PropertyChanged += OnMainWindowViewModelPropertyChanged;
        }

        protected MainWindowViewModel MainWindowViewModel { get; }

        public void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            OnInitialize();

            foreach (var propertyName in _forwardedPropertyNames)
                RaisePropertyChanged(propertyName);
        }

        protected virtual void OnInitialize()
        {
        }

        private void OnMainWindowViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || _forwardedPropertyNames.Contains(e.PropertyName))
                RaisePropertyChanged(e.PropertyName);
        }
    }
}