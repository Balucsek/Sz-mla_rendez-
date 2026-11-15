using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using MessageBox = System.Windows.MessageBox;

namespace InvoiceManager;

public partial class OllamaInstructionsWindow : Window
{
    public OllamaInstructionsWindow()
    {
        InitializeComponent();
    }

    private void DownloadLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(
                this,
                $"A letöltési oldal megnyitása nem sikerült.\n\n{ex.Message}",
                "Ollama",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
