using System.Threading;
using System.Windows;
using AudioJoiner.Services;
using AudioJoiner.UI;

namespace AudioJoiner;

public partial class App : System.Windows.Application
{
    private const string AppMutexName = "AudioJoiner_SingleInstance_AppMutex_2026";
    private static Mutex? _mutex;
    private MainWindow? _mainWindow;
    private SettingsService? _settingsService;
    private AudioEngineService? _audioService;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Garantir instância única
        _mutex = new Mutex(true, AppMutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            System.Windows.MessageBox.Show("O AudioJoiner já está em execução na bandeja do sistema.", "AudioJoiner", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Tratamento global de exceções para estabilidade contínua
        DispatcherUnhandledException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled UI Exception: {args.Exception.Message}");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled Domain Exception: {ex.Message}");
            }
        };

        bool startMinimized = e.Args.Contains("--minimized");

        // Inicializar serviços
        _settingsService = new SettingsService();
        _audioService = new AudioEngineService(_settingsService, Dispatcher);

        // Inicializar janela principal
        _mainWindow = new MainWindow(_audioService, _settingsService, startMinimized);

        if (!startMinimized && !_settingsService.CurrentSettings.StartMinimized)
        {
            _mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _audioService?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
