using System.Windows;
using UtiliTech.Services;
using UtiliTech.Views;

namespace UtiliTech;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Info($"UtiliTech {Updater.CurrentText} iniciado em {Environment.MachineName}");

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error("Erro não tratado na tela", args.Exception);
            MessageBox.Show(args.Exception.Message + $"\n\nDetalhes em {Logger.Folder}", "UtiliTech — erro", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("Erro fatal", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Erro em tarefa de fundo", args.Exception);
            args.SetObserved();
        };

        ThemeManager.Apply(AppSettings.Current.LightTheme);
        Updater.CleanupPreviousVersion();
    }

    // Garante que o driver de passagem sai do PC mesmo se o app fechar por outro caminho
    protected override void OnExit(ExitEventArgs e)
    {
        SensorHub.Shutdown();
        base.OnExit(e);
    }
}
