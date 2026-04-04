using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
            this.MainWindowViewModel = mainWindowViewModel;
            this._forwardedPropertyNames = [.. forwardedPropertyNames];
            this.MainWindowViewModel.PropertyChanged += this.OnMainWindowViewModelPropertyChanged;
        }

        protected MainWindowViewModel MainWindowViewModel { get; }

        public void Initialize()
        {
            if (this._initialized)
            {
                return;
            }

            this._initialized = true;
            this.OnInitialize();

            foreach (var propertyName in this._forwardedPropertyNames)
            {
                this.RaisePropertyChanged(propertyName);
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this._disposed)
            {
                return;
            }

            if (disposing)
            {
                this.MainWindowViewModel.PropertyChanged -= this.OnMainWindowViewModelPropertyChanged;
            }

            this._disposed = true;
        }

        protected virtual void OnInitialize()
        {
        }

        private void OnMainWindowViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || this._forwardedPropertyNames.Contains(e.PropertyName))
            {
                this.RaisePropertyChanged(e.PropertyName);
            }
        }
    }
}