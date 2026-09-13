using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AudioJoiner.Models;
using AudioJoiner.Services;

namespace AudioJoiner.UI;

public class SelectableDeviceItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public AudioDeviceInfo Device { get; set; } = null!;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class GroupEditDialog : Window
{
    private readonly VirtualDeviceService _virtualService = new();
    public string GroupName { get; private set; } = string.Empty;
    public string? SelectedVirtualDeviceId { get; private set; }
    public string? SelectedVirtualDeviceName { get; private set; }
    public List<string> SelectedDeviceIds { get; private set; } = new();
    public ObservableCollection<SelectableDeviceItem> SelectableDevices { get; } = new();

    public GroupEditDialog(IEnumerable<AudioDeviceInfo> availableDevices, AudioGroupInfo? existingGroup = null)
    {
        InitializeComponent();

        if (existingGroup != null)
        {
            TxtDialogTitle.Text = "Editar Grupo de Dispositivos";
            TxtGroupName.Text = existingGroup.Name;
        }
        else
        {
            TxtDialogTitle.Text = "Unir Dispositivos em Saída do Windows";
            TxtGroupName.Text = "Áudio dos Monitores";
        }

        // Popular lista de dispositivos virtuais do Windows disponíveis
        PopulateVirtualDevices(existingGroup?.VirtualDeviceId);

        var existingMemberIds = existingGroup?.Members.Select(m => m.Id).ToHashSet() ?? new HashSet<string>();

        foreach (var dev in availableDevices.Where(d => !d.IsSource && d.IsAvailable))
        {
            SelectableDevices.Add(new SelectableDeviceItem
            {
                Device = dev,
                IsSelected = existingMemberIds.Contains(dev.Id)
            });
        }

        ListSelectableDevices.ItemsSource = SelectableDevices;
    }

    private void PopulateVirtualDevices(string? preselectedId)
    {
        CmbVirtualDevice.Items.Clear();

        var noneItem = new ComboBoxItem
        {
            Content = "Nenhum (Usar captura padrão do sistema)",
            Tag = null
        };
        CmbVirtualDevice.Items.Add(noneItem);

        var virtualDevices = _virtualService.GetAvailableVirtualDevices();
        ComboBoxItem? selectedItem = noneItem;

        foreach (var vDev in virtualDevices)
        {
            var item = new ComboBoxItem
            {
                Content = $"🔗 {vDev.Name} (Dispositivo do Windows)",
                Tag = vDev.Id
            };
            CmbVirtualDevice.Items.Add(item);

            if (preselectedId != null && vDev.Id == preselectedId)
            {
                selectedItem = item;
            }
        }

        if (preselectedId == null && virtualDevices.Count > 0)
        {
            selectedItem = (ComboBoxItem)CmbVirtualDevice.Items[1];
        }

        CmbVirtualDevice.SelectedItem = selectedItem;

        PnlInstallDriver.Visibility = virtualDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CmbVirtualDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbVirtualDevice.SelectedItem is ComboBoxItem item)
        {
            SelectedVirtualDeviceId = item.Tag as string;
            SelectedVirtualDeviceName = item.Content?.ToString()?.Replace("🔗 ", "").Replace(" (Dispositivo do Windows)", "");
        }
    }

    private async void BtnInstallDriver_Click(object sender, RoutedEventArgs e)
    {
        BtnInstallDriver.IsEnabled = false;
        TxtDriverStatus.Visibility = Visibility.Visible;

        bool success = await _virtualService.DownloadAndInstallVirtualDriverAsync(msg =>
        {
            Dispatcher.Invoke(() => TxtDriverStatus.Text = msg);
        });

        if (success)
        {
            await Task.Delay(3000);
            PopulateVirtualDevices(SelectedVirtualDeviceId);
        }

        BtnInstallDriver.IsEnabled = true;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtGroupName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowValidation("Por favor, digite um nome para o grupo.");
            return;
        }

        var selected = SelectableDevices.Where(d => d.IsSelected).Select(d => d.Device.Id).ToList();
        if (selected.Count < 2)
        {
            ShowValidation("Selecione pelo menos 2 dispositivos físicos para formar o grupo unificado.");
            return;
        }

        GroupName = name;
        SelectedDeviceIds = selected;

        if (CmbVirtualDevice.SelectedItem is ComboBoxItem item)
        {
            SelectedVirtualDeviceId = item.Tag as string;
            SelectedVirtualDeviceName = item.Tag != null ? SelectedVirtualDeviceName : null;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowValidation(string message)
    {
        TxtValidation.Text = message;
        TxtValidation.Visibility = Visibility.Visible;
    }
}
