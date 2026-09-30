using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MapRegionizer.App.ViewModels;
using MapRegionizer.App.Views;

namespace MapRegionizer.App
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainViewModel = new MainViewModel();
                var mainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };

                desktop.MainWindow = mainWindow;
                desktop.Exit += (_, _) =>
                {
                    mainWindow.DataContext = null;
                    mainViewModel.Dispose();
                };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = new MainViewModel()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

    }
}
