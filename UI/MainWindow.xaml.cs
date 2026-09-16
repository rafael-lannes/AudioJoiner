using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioJoiner.Models;
using AudioJoiner.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfMessageBox = System.Windows.MessageBox;

namespace AudioJoiner.UI;

public partial class MainWindow : Window
{
    private readonly IAudioEngineService _audioService;
    private readonly SettingsService _settingsService;
    private readonly TrayIconManager _trayIconManager;
    private readonly DispatcherTimer _uiUpdateTimer;
    private bool _isExplicitClose;
    private bool _isInitializing = true;

    public MainWindow(IAudioEngineService audioService, SettingsService settingsService, bool startMinimized = false)
    {
        InitializeComponent();

        _audioService = audioService;
        _settingsService = settingsService;

        DataContext = _audioService;

        _trayIconManager = new TrayIconManager(_audioService, ShowAndActivateWindow, CloseApplicationPermanently, ShowAboutDialog);

        // Data bindings
        ListOutputDevices.ItemsSource = _audioService.Devices;
        CmbSourceDevice.ItemsSource = _audioService.Devices;
        CmbEqPreset.ItemsSource = _audioService.EqualizerPresets;

        RestoreUiSettings();

        _audioService.StateChanged += OnAudioEngineStateChanged;
        _audioService.ErrorOccurred += OnAudioEngineError;
        _audioService.DeviceStatusChanged += OnDeviceStatusChanged;

        _uiUpdateTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS
        };
        _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
        _uiUpdateTimer.Start();

        _isInitializing = false;

        UpdateUiState();

        if (_settingsService.CurrentSettings.AutoStartMirroring)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    bool hasActiveOutputs = _audioService.Devices.Any(d => d.IsMirrorEnabled && !d.IsSource && d.IsAvailable);
                    if (!_audioService.IsRunning && hasActiveOutputs)
                    {
                        _audioService.StartMirroring();
                    }
                }
                catch { }
            }), DispatcherPriority.Background);
        }
    }

    private void RestoreUiSettings()
    {
        var settings = _settingsService.CurrentSettings;

        SliderMasterVolume.Value = settings.MasterVolume;
        TxtMasterVolumePercent.Text = $"{(int)Math.Round(settings.MasterVolume * 100)}%";

        ChkStartWithWindows.IsChecked = _settingsService.IsConfiguredToStartWithWindows();
        ChkStartMinimized.IsChecked = settings.StartMinimized;

        if (_audioService.SelectedSourceDevice != null)
        {
            CmbSourceDevice.SelectedItem = _audioService.SelectedSourceDevice;
        }

        foreach (ComboBoxItem item in CmbLatency.Items)
        {
            if (item.Tag is string tagStr && int.TryParse(tagStr, out int latency) && latency == settings.LatencyMs)
            {
                CmbLatency.SelectedItem = item;
                break;
            }
        }

        // Equalizer settings
        ChkEqEnable.IsChecked = _audioService.IsEqualizerEnabled;
        if (_audioService.SelectedEqualizerPreset != null)
        {
            CmbEqPreset.SelectedItem = _audioService.SelectedEqualizerPreset;
        }
    }

    private void UpdateUiState()
    {
        bool isRunning = _audioService.IsRunning;
        int totalActive = _audioService.Devices.Count(d => d.IsMirrorEnabled && !d.IsSource && d.IsAvailable);

        TxtActiveDestCount.Text = $"{totalActive} ativa{(totalActive != 1 ? "s" : "")}";

        if (isRunning)
        {
            StatusDot.Fill = (SolidColorBrush)FindResource("AccentEmeraldBrush");
            StatusBadgeText.Text = $"Espelhando ({totalActive} saídas)";
            StatusBadgeText.Foreground = (SolidColorBrush)FindResource("AccentEmeraldBrush");

            BtnToggleMirror.Style = (Style)FindResource("DangerButton");
            TxtToggleBtn.Text = "Parar Espelhamento";
            IconToggleBtn.Data = Geometry.Parse("M6 6h12v12H6z");
            CmbSourceDevice.IsEnabled = false;
        }
        else
        {
            StatusDot.Fill = (SolidColorBrush)FindResource("TextMutedBrush");
            StatusBadgeText.Text = "Parado";
            StatusBadgeText.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");

            BtnToggleMirror.Style = (Style)FindResource("PrimaryButton");
            TxtToggleBtn.Text = "Iniciar Espelhamento";
            IconToggleBtn.Data = Geometry.Parse("M8 5v14l11-7z");
            CmbSourceDevice.IsEnabled = true;
        }

        _trayIconManager.UpdateTrayState();
    }

    private void UiUpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (_audioService.IsRunning)
        {
            double maxBarWidth = 160.0;
            double targetWidth = Math.Clamp(_audioService.MasterPeakLevel * maxBarWidth * 1.2, 0, maxBarWidth);
            MasterVuBar.Width = targetWidth;
        }
        else
        {
            MasterVuBar.Width = 0;
        }

        long memoryBytes = Process.GetCurrentProcess().WorkingSet64;
        double memoryMb = memoryBytes / (1024.0 * 1024.0);
        TxtMemoryUsage.Text = $"RAM: {memoryMb:0.0} MB";
    }

    private void OnAudioEngineStateChanged()
    {
        Dispatcher.Invoke(UpdateUiState);
    }

    private void OnAudioEngineError(string errorMessage)
    {
        Dispatcher.Invoke(() =>
        {
            _trayIconManager.ShowNotification("AudioJoiner - Alerta", errorMessage, System.Windows.Forms.ToolTipIcon.Warning);
        });
    }

    private void OnDeviceStatusChanged(string statusMessage)
    {
        Dispatcher.Invoke(() =>
        {
            _trayIconManager.ShowNotification("AudioJoiner", statusMessage, System.Windows.Forms.ToolTipIcon.Info);
        });
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _trayIconManager.ShowNotification("AudioJoiner", "O AudioJoiner continua em execução em segundo plano na bandeja do sistema.", System.Windows.Forms.ToolTipIcon.Info);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        ShowAboutDialog();
    }

    public void ShowAboutDialog()
    {
        var aboutDialog = new AboutDialog
        {
            Owner = this
        };
        aboutDialog.ShowDialog();
    }

    private void BtnToggleMirror_Click(object sender, RoutedEventArgs e)
    {
        if (_audioService.IsRunning)
        {
            _audioService.StopMirroring();
        }
        else
        {
            _audioService.StartMirroring();
        }
    }

    private void CmbSourceDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (CmbSourceDevice.SelectedItem is AudioDeviceInfo selected)
        {
            _audioService.SelectedSourceDevice = selected;
            _settingsService.CurrentSettings.SelectedSourceDeviceId = selected.Id;
            _settingsService.SaveSettings();
            UpdateUiState();
        }
    }

    private void SliderMasterVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;

        float vol = (float)e.NewValue;
        TxtMasterVolumePercent.Text = $"{(int)Math.Round(vol * 100)}%";
        _audioService.MasterVolume = vol;
    }

    private void DeviceToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        if (sender is WpfCheckBox chk && chk.Tag is string deviceId)
        {
            bool isChecked = chk.IsChecked == true;
            _audioService.SetDeviceMirrorState(deviceId, isChecked);
            UpdateUiState();
        }
    }

    private void DeviceVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;

        if (sender is Slider slider && slider.Tag is string deviceId)
        {
            _audioService.SetDeviceVolume(deviceId, (float)e.NewValue);
        }
    }

    #region Equalizer UI Handlers

    private void CmbEqPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (CmbEqPreset.SelectedItem is EqualizerPreset preset)
        {
            _audioService.ApplyEqualizerPreset(preset);
        }
    }

    private void ChkEqEnable_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        _audioService.IsEqualizerEnabled = ChkEqEnable.IsChecked == true;
    }

    private void EqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;

        if (sender is Slider slider && slider.Tag is int bandIndex)
        {
            _audioService.SetEqualizerBandGain(bandIndex, (float)e.NewValue);

            // If preset was something else and user moves slider, switch preset selection to Custom
            if (CmbEqPreset.SelectedItem is EqualizerPreset preset && preset.Name != "Personalizado")
            {
                var customPreset = _audioService.EqualizerPresets.FirstOrDefault(p => p.Name == "Personalizado");
                if (customPreset != null)
                {
                    CmbEqPreset.SelectedItem = customPreset;
                }
            }
        }
    }

    private void BtnResetEq_Click(object sender, RoutedEventArgs e)
    {
        _audioService.ResetEqualizer();
        CmbEqPreset.SelectedItem = _audioService.SelectedEqualizerPreset;
    }

    #endregion

    private void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        _audioService.RefreshDevices();
        if (_audioService.SelectedSourceDevice != null)
        {
            CmbSourceDevice.SelectedItem = _audioService.SelectedSourceDevice;
        }
        UpdateUiState();
    }

    private void ChkStartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        bool enable = ChkStartWithWindows.IsChecked == true;
        _settingsService.CurrentSettings.StartWithWindows = enable;
        _settingsService.SetStartWithWindows(enable, _settingsService.CurrentSettings.StartMinimized);
        _settingsService.SaveSettings();
    }

    private void ChkStartMinimized_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        bool startMin = ChkStartMinimized.IsChecked == true;
        _settingsService.CurrentSettings.StartMinimized = startMin;
        if (ChkStartWithWindows.IsChecked == true)
        {
            _settingsService.SetStartWithWindows(true, startMin);
        }
        _settingsService.SaveSettings();
    }

    private void CmbLatency_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (CmbLatency.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && int.TryParse(tagStr, out int latency))
        {
            _audioService.LatencyMs = latency;
            _settingsService.CurrentSettings.LatencyMs = latency;
            _settingsService.SaveSettings();
        }
    }

    public void ShowAndActivateWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Focus();
    }

    public void CloseApplicationPermanently()
    {
        _isExplicitClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _uiUpdateTimer.Stop();
        _trayIconManager.Dispose();
        _audioService.Dispose();

        base.OnClosing(e);
    }
}
