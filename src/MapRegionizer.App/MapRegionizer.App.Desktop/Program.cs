using Avalonia;
using Avalonia.Controls;
using ReactiveUI.Avalonia;
using System;

namespace MapRegionizer.App.Desktop
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new Win32PlatformOptions { OverlayPopups = true })
                .WithInterFont()
                .UseReactiveUI(_ => { })
                .LogToTrace();
    }
}
