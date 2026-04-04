using Microsoft.Xaml.Behaviors;
using System.Windows;
using TwitchChatOverlay.Infrastructure;

namespace TwitchChatOverlay.Behaviours
{
    internal class LoadedEventBehaviour : TriggerAction<FrameworkElement>
    {
        protected override void Invoke(object parameter)
        {
            if (this.AssociatedObject.DataContext is IInitialized initialized)
            {
                initialized.Initialize();
            }
        }
    }
}
