using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MapRegionizer.App.Services;

/// <summary>Returns a modal result after native closing and ownership cleanup finish.</summary>
public static class OwnedWindowDialog
{
    public static async Task<TResult> ShowAsync<TResult>(Window dialog, Window owner)
    {
        var ownerWasEnabled = owner.IsEnabled;
        try
        {
            return await dialog.ShowDialog<TResult>(owner);
        }
        finally
        {
            // Avalonia completes ShowDialog from Closed, before removing the
            // child from its owner and enabling the native owner window. Never
            // dispose the editor, accept its result or reactivate its owner
            // inline in that callback. Let the closing stack unwind first.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!owner.IsVisible || owner.OwnedWindows.Any(child => child.IsDialog))
                    return;

                owner.SetCurrentValue(Window.IsEnabledProperty, ownerWasEnabled);
                if (ownerWasEnabled)
                    owner.Activate();
            }, DispatcherPriority.Background);
        }
    }
}
