namespace SForce.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "SFORCE", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);

        if (e.Args.Contains("--restore", StringComparer.OrdinalIgnoreCase))
        {
            var result = new Services.WindowsFontManager(new Services.BackupStore()).Restore();
            MessageBox.Show(result.Message, "SFORCE Emergency Restore", MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
            Shutdown(result.Success ? 0 : 1);
            return;
        }

        new MainWindow().Show();
    }
}
