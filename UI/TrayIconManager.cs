using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using AudioJoiner.Services;
using Application = System.Windows.Application;

namespace AudioJoiner.UI;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly IAudioEngineService _audioService;
    private readonly Action _showWindowAction;
    private readonly Action _exitAppAction;
    private readonly Action? _showAboutAction;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _toggleMirrorMenuItem;
    private bool _isDisposed;

    public TrayIconManager(IAudioEngineService audioService, Action showWindowAction, Action exitAppAction, Action? showAboutAction = null)
    {
        _audioService = audioService;
        _showWindowAction = showWindowAction;
        _exitAppAction = exitAppAction;
        _showAboutAction = showAboutAction;

        _notifyIcon = new NotifyIcon();
        _notifyIcon.Icon = LoadAppIcon();
        _notifyIcon.Text = "AudioJoiner - Multi-Output Audio";
        _notifyIcon.Visible = true;

        // Context Menu
        var contextMenu = new ContextMenuStrip();
        contextMenu.ShowImageMargin = false;

        var headerItem = new ToolStripMenuItem("AudioJoiner v1.2")
        {
            Enabled = false,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        contextMenu.Items.Add(headerItem);

        _statusMenuItem = new ToolStripMenuItem("Status: Parado")
        {
            Enabled = false,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic)
        };
        contextMenu.Items.Add(_statusMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        _toggleMirrorMenuItem = new ToolStripMenuItem("▶ Iniciar Espelhamento", null, OnToggleMirrorClicked);
        contextMenu.Items.Add(_toggleMirrorMenuItem);

        var openWindowItem = new ToolStripMenuItem("⚙ Abrir Painel Principal", null, (s, e) => _showWindowAction());
        contextMenu.Items.Add(openWindowItem);

        if (_showAboutAction != null)
        {
            var aboutItem = new ToolStripMenuItem("ℹ Sobre o AudioJoiner", null, (s, e) => _showAboutAction());
            contextMenu.Items.Add(aboutItem);
        }

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("✖ Sair", null, (s, e) => _exitAppAction());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;

        _notifyIcon.DoubleClick += (s, e) => _showWindowAction();
        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _showWindowAction();
            }
        };

        _audioService.StateChanged += UpdateTrayState;
        UpdateTrayState();
    }

    private Icon LoadAppIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }

            var exeIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location);
            if (exeIcon != null) return exeIcon;
        }
        catch { }

        return SystemIcons.Application;
    }

    public void UpdateTrayState()
    {
        if (_isDisposed) return;

        bool isRunning = _audioService.IsRunning;
        int activeDirect = _audioService.Devices.Count(d => d.IsMirrorEnabled && !d.IsSource && d.IsAvailable);
        int activeGroups = _audioService.Groups.Where(g => g.IsEnabled).SelectMany(g => g.Members.Where(m => !m.IsSource && m.IsAvailable)).DistinctBy(m => m.Id).Count();
        int totalCount = activeDirect + activeGroups;

        if (isRunning)
        {
            _notifyIcon.Text = $"AudioJoiner: Espelhando ({totalCount} saídas)";
            _statusMenuItem.Text = $"● Espelhando ({totalCount} saídas)";
            _toggleMirrorMenuItem.Text = "⏹ Parar Espelhamento";
        }
        else
        {
            _notifyIcon.Text = "AudioJoiner: Parado";
            _statusMenuItem.Text = "○ Espelhamento Parado";
            _toggleMirrorMenuItem.Text = "▶ Iniciar Espelhamento";
        }
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_isDisposed) return;
        _notifyIcon.ShowBalloonTip(2500, title, message, icon);
    }

    private void OnToggleMirrorClicked(object? sender, EventArgs e)
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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _audioService.StateChanged -= UpdateTrayState;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
