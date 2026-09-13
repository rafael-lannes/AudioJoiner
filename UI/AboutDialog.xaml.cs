using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using WpfMessageBox = System.Windows.MessageBox;

namespace AudioJoiner.UI;

public partial class AboutDialog : Window
{
    private const string GithubUrl = "https://rafael-lannes.github.io";

    public AboutDialog()
    {
        InitializeComponent();
    }

    private void BtnOpenGithub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(GithubUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Não foi possível abrir o link: {ex.Message}", "AudioJoiner", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}
