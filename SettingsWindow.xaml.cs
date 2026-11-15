using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using InvoiceManager.Services;
using MessageBox = System.Windows.MessageBox;

namespace InvoiceManager;

public partial class SettingsWindow : Window
{
    private readonly ScannerService _scannerService = new();
    public AppSettings Settings { get; }
    public List<ScannerDevice> AvailableScanners { get; private set; } = new();

    public SettingsWindow(AppSettings currentSettings)
    {
        InitializeComponent();
        Settings = new AppSettings
        {
            KeepOriginalFiles = currentSettings.KeepOriginalFiles,
            DestinationFolder = currentSettings.DestinationFolder,
            ScanFolder = currentSettings.ScanFolder,
            ScannerDeviceId = currentSettings.ScannerDeviceId
        };
        KeepOriginalFilesCheckBox.IsChecked = Settings.KeepOriginalFiles;
        DestinationFolderTextBox.Text = Settings.DestinationFolder;
        ScanFolderTextBox.Text = Settings.ScanFolder;
        RefreshScanners(Settings.ScannerDeviceId, showError: false);
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Válassza ki az iktatott számlák célmappáját",
            SelectedPath = Directory.Exists(DestinationFolderTextBox.Text)
                ? DestinationFolderTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            DestinationFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void ChooseScanFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Válassza ki a gyors szkennelés mappáját",
            SelectedPath = Directory.Exists(ScanFolderTextBox.Text)
                ? ScanFolderTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ScanFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void RefreshScannersButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = ScannerComboBox.SelectedValue as string ?? Settings.ScannerDeviceId;
        RefreshScanners(selectedId, showError: true);
    }

    private void RefreshScanners(string selectedId, bool showError)
    {
        try
        {
            AvailableScanners = _scannerService.GetAvailableScanners();
        }
        catch (COMException ex)
        {
            AvailableScanners = new List<ScannerDevice>();
            if (showError)
            {
                MessageBox.Show(this, $"A szkennerek listájának lekérése nem sikerült.\n\n{ex.Message}", "Szkenner", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (InvalidOperationException ex)
        {
            AvailableScanners = new List<ScannerDevice>();
            if (showError)
            {
                MessageBox.Show(this, $"A szkennerek listájának lekérése nem sikerült.\n\n{ex.Message}", "Szkenner", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        ScannerComboBox.ItemsSource = AvailableScanners;
        if (AvailableScanners.Count == 0)
        {
            ScannerComboBox.SelectedIndex = -1;
            ScannerStatusTextBlock.Text = "Nem található WIA-kompatibilis szkenner.";
            return;
        }

        ScannerComboBox.SelectedValue = AvailableScanners.Exists(scanner => scanner.Id == selectedId)
            ? selectedId
            : AvailableScanners[0].Id;
        ScannerStatusTextBlock.Text = $"{AvailableScanners.Count} szkenner található.";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var destinationFolder = DestinationFolderTextBox.Text.Trim();
        var scanFolder = ScanFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(destinationFolder) || string.IsNullOrWhiteSpace(scanFolder))
        {
            MessageBox.Show(
                this,
                "Válassza ki a célmappát és a scan mappát a beállítások mentése előtt.",
                "Beállítások",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Settings.DestinationFolder = Path.GetFullPath(destinationFolder);
            Settings.ScanFolder = Path.GetFullPath(scanFolder);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, $"A célmappa elérési útja érvénytelen.\n\n{ex.Message}", "Beállítások", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        catch (NotSupportedException ex)
        {
            MessageBox.Show(this, $"A célmappa elérési útja nem támogatott.\n\n{ex.Message}", "Beállítások", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Settings.KeepOriginalFiles = KeepOriginalFilesCheckBox.IsChecked == true;
        if (ScannerComboBox.SelectedValue is string selectedScannerId)
        {
            Settings.ScannerDeviceId = selectedScannerId;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
