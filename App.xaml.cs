using System.IO;
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
        // Garantir instância única com suporte a mutex abandonado
        bool isNewInstance;
        try
        {
            _mutex = new Mutex(true, AppMutexName, out isNewInstance);
        }
        catch (AbandonedMutexException)
        {
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            System.Windows.MessageBox.Show(
                "O AudioJoiner já está em execução no sistema.\nVerifique o ícone na área de notificação (bandeja ao lado do relógio).",
                "AudioJoiner",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Tratamento global de exceções para diagnóstico e estabilidade
        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioJoiner");
                Directory.CreateDirectory(appDataDir);
                string crashPath = Path.Combine(appDataDir, "crash.log");
                File.AppendAllText(crashPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI Exception:\n{args.Exception}\n\n");
            }
            catch { }

            System.Diagnostics.Debug.WriteLine($"Unhandled UI Exception: {args.Exception}");
            System.Windows.MessageBox.Show($"Ocorreu um erro no AudioJoiner:\n\n{args.Exception.Message}\n\nDetalhes gravados em %APPDATA%\\AudioJoiner\\crash.log", "AudioJoiner - Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                try
                {
                    string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioJoiner");
                    Directory.CreateDirectory(appDataDir);
                    string crashPath = Path.Combine(appDataDir, "crash.log");
                    File.AppendAllText(crashPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Domain Exception:\n{ex}\n\n");
                }
                catch { }

                System.Diagnostics.Debug.WriteLine($"Unhandled Domain Exception: {ex.Message}");
            }
        };

        bool startMinimized = e.Args.Contains("--minimized");

        try
        {
            // Inicializar serviços
            _settingsService = new SettingsService();
            _audioService = new AudioEngineService(_settingsService, Dispatcher);

            // Inicializar janela principal
            _mainWindow = new MainWindow(_audioService, _settingsService, startMinimized);
            MainWindow = _mainWindow;

            if (startMinimized)
            {
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.Show();
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.Show();
                _mainWindow.Activate();
                _mainWindow.Focus();
            }
        }
        catch (Exception ex)
        {
            try
            {
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioJoiner");
                Directory.CreateDirectory(appDataDir);
                string crashPath = Path.Combine(appDataDir, "crash.log");
                File.AppendAllText(crashPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Startup Exception:\n{ex}\n\n");
            }
            catch { }

            System.Windows.MessageBox.Show($"Falha ao iniciar o AudioJoiner:\n\n{ex.Message}\n\n{ex.StackTrace}", "AudioJoiner - Falha na Inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
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
