using System;
using System.Collections.Generic;
using System.ComponentModel;
using Prism.Mvvm;
using TwitchChatOverlay.Infrastructure;

namespace TwitchChatOverlay.ViewModels
{
    public abstract class TabViewModelBase : BindableBase, IInitialized, IDisposable
    {
        private readonly HashSet<string> _forwardedPropertyNames;
        private bool _initialized;
        private bool _disposed;

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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
                MainWindowViewModel.PropertyChanged -= OnMainWindowViewModelPropertyChanged;

            _disposed = true;
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