namespace EchoTray;

internal static class Program
{
    private const string InstanceName = @"Local\EchoTray.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(initiallyOwned: true, InstanceName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Echo is already running. Look for the >|< icon in the system tray.",
                "Echo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);

        using var context = new EchoTrayContext();
        Application.Run(context);
    }

    private static void Report(Exception? ex)
    {
        MessageBox.Show(
            $"Echo hit an unexpected error and will close.\n\n{ex}",
            "Echo",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        Application.Exit();
    }
}
