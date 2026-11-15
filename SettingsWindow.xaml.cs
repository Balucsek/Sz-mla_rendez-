using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InvoiceManager.Services;
using MessageBox = System.Windows.MessageBox;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using ListBox = System.Windows.Controls.ListBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace InvoiceManager;

public partial class SettingsWindow : Window
{
    private readonly ScannerService _scannerService = new();
    private readonly OllamaService _ollamaService = new();
    private readonly ObservableCollection<InvoiceNameField> _namePatternFields = new();
    private readonly ObservableCollection<OllamaModelOption> _modelOptions = new();
    private Point _dragStartPoint;
    private bool _modelListLoaded;
    private bool _updatingModelList;
    private bool _modelOperationInProgress;

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
            ScannerDeviceId = currentSettings.ScannerDeviceId,
            AiAnalysisEnabled = currentSettings.AiAnalysisEnabled,
            LlmModel = currentSettings.LlmModel,
            AiAnalysisInstruction = string.IsNullOrWhiteSpace(currentSettings.AiAnalysisInstruction)
                ? OllamaService.DefaultAnalysisInstruction
                : currentSettings.AiAnalysisInstruction,
            AiImageDpi = PdfPageImageRenderer.SupportedDpiValues.Contains(currentSettings.AiImageDpi)
                ? currentSettings.AiImageDpi
                : PdfPageImageRenderer.DefaultDpi,
            InvoiceNamePattern = currentSettings.InvoiceNamePattern is { Count: > 0 }
                ? new List<string>(currentSettings.InvoiceNamePattern)
                : InvoiceNameFields.CreateDefaultPattern()
        };
        KeepOriginalFilesCheckBox.IsChecked = Settings.KeepOriginalFiles;
        EnableAiAnalysisCheckBox.IsChecked = Settings.AiAnalysisEnabled;
        AiAnalysisInstructionTextBox.Text = Settings.AiAnalysisInstruction;
        AiImageDpiComboBox.ItemsSource = PdfPageImageRenderer.SupportedDpiValues;
        AiImageDpiComboBox.SelectedItem = Settings.AiImageDpi;
        DestinationFolderTextBox.Text = Settings.DestinationFolder;
        ScanFolderTextBox.Text = Settings.ScanFolder;
        AvailableNameFieldsList.ItemsSource = InvoiceNameFields.Available;
        NamePatternList.ItemsSource = _namePatternFields;
        foreach (var key in Settings.InvoiceNamePattern)
        {
            if (InvoiceNameFields.Find(key) is { } field)
            {
                _namePatternFields.Add(field);
            }
        }

        if (_namePatternFields.Count == 0)
        {
            ResetNamePattern();
        }

        UpdatePatternPreview();
        foreach (var model in OllamaModelOptions.All)
        {
            _modelOptions.Add(new OllamaModelOption { Name = model.Name });
        }

        LlmModelComboBox.ItemsSource = _modelOptions;
        RefreshScanners(Settings.ScannerDeviceId, showError: false);
        UpdateAiControls();
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var installedModels = await _ollamaService.GetInstalledModelsAsync(timeout.Token);
            foreach (var model in _modelOptions)
            {
                model.IsInstalled = OllamaService.IsInstalled(model.Name, installedModels);
            }

            var selectedModel = _modelOptions.FirstOrDefault(model =>
                                    model.Name == Settings.LlmModel && model.IsInstalled)
                                ?? _modelOptions.FirstOrDefault(model => model.IsInstalled)
                                ?? _modelOptions.FirstOrDefault(model => model.Name == Settings.LlmModel)
                                ?? _modelOptions.First();

            _updatingModelList = true;
            LlmModelComboBox.Items.Refresh();
            LlmModelComboBox.SelectedItem = selectedModel;
            _modelListLoaded = true;
            _updatingModelList = false;
            AiModelStatusTextBlock.Text = installedModels.Count == 0
                ? "Az Ollama elérhető, de még nincs telepített modell. Válasszon egy szürke modellt a letöltéshez."
                : $"{installedModels.Count} Ollama modell telepítve.";
            UpdateAiControls();

            if (EnableAiAnalysisCheckBox.IsChecked == true && selectedModel.IsInstalled)
            {
                await LoadModelAsync(selectedModel);
            }
        }
        catch (HttpRequestException)
        {
            SetOllamaUnavailableStatus();
        }
        catch (TaskCanceledException)
        {
            SetOllamaUnavailableStatus();
        }
        catch (JsonException ex)
        {
            AiModelStatusTextBlock.Text = $"Az Ollama válasza nem értelmezhető: {ex.Message}";
            SelectDefaultModel();
        }
        catch (InvalidOperationException ex)
        {
            AiModelStatusTextBlock.Text = ex.Message;
            SelectDefaultModel();
        }
    }

    private void SelectDefaultModel()
    {
        _updatingModelList = true;
        LlmModelComboBox.SelectedItem =
            _modelOptions.FirstOrDefault(model => model.Name == Settings.LlmModel) ??
            _modelOptions.FirstOrDefault();
        _modelListLoaded = true;
        _updatingModelList = false;
        UpdateAiControls();
    }

    private void SetOllamaUnavailableStatus()
    {
        foreach (var model in _modelOptions)
        {
            model.IsInstalled = false;
        }

        AiModelStatusTextBlock.Text = "Az Ollama nem érhető el. Telepítse vagy indítsa el, majd frissítse a beállításokat.";
        SelectDefaultModel();
        LlmModelComboBox.Items.Refresh();
    }

    private void EnableAiAnalysisCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAiControls();
        if (EnableAiAnalysisCheckBox.IsChecked == true &&
            _modelListLoaded &&
            LlmModelComboBox.SelectedItem is OllamaModelOption { IsInstalled: true } model)
        {
            _ = LoadModelAsync(model);
        }
    }

    private async void ModelActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (LlmModelComboBox.SelectedItem is not OllamaModelOption model)
        {
            return;
        }

        if (model.IsInstalled)
        {
            await LoadModelAsync(model);
        }
        else
        {
            await PullAndLoadModelAsync(model);
        }
    }

    private async void LlmModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingModelList ||
            !_modelListLoaded ||
            EnableAiAnalysisCheckBox.IsChecked != true ||
            LlmModelComboBox.SelectedItem is not OllamaModelOption model)
        {
            return;
        }

        if (model.IsInstalled)
        {
            await LoadModelAsync(model);
            return;
        }

        await PullAndLoadModelAsync(model);
    }

    private async Task PullAndLoadModelAsync(OllamaModelOption model)
    {
        BeginModelOperation($"{model.Name} letöltése...");
        try
        {
            var progress = new Progress<OllamaPullProgress>(update =>
            {
                AiModelStatusTextBlock.Text = update.Status;
                if (update.Total is > 0)
                {
                    AiModelProgressBar.IsIndeterminate = false;
                    AiModelProgressBar.Maximum = update.Total.Value;
                    AiModelProgressBar.Value = update.Completed ?? 0;
                }
                else
                {
                    AiModelProgressBar.IsIndeterminate = true;
                }
            });
            await _ollamaService.PullModelAsync(model.Name, progress, CancellationToken.None);
            model.IsInstalled = true;
            LlmModelComboBox.Items.Refresh();
            await _ollamaService.LoadModelAsync(model.Name, CancellationToken.None);
            AiModelStatusTextBlock.Text = $"{model.Name} telepítve és elindítva.";
        }
        catch (HttpRequestException)
        {
            SetOllamaUnavailableStatus();
        }
        catch (InvalidOperationException ex)
        {
            AiModelStatusTextBlock.Text = $"A modell telepítése nem sikerült: {ex.Message}";
        }
        catch (JsonException ex)
        {
            AiModelStatusTextBlock.Text = $"Az Ollama válasza nem értelmezhető: {ex.Message}";
        }
        finally
        {
            EndModelOperation();
        }
    }

    private async Task LoadModelAsync(OllamaModelOption model)
    {
        BeginModelOperation($"{model.Name} indítása...");
        try
        {
            await _ollamaService.LoadModelAsync(model.Name, CancellationToken.None);
            AiModelStatusTextBlock.Text = $"{model.Name} elindítva (5 percig marad betöltve).";
        }
        catch (HttpRequestException)
        {
            SetOllamaUnavailableStatus();
        }
        catch (JsonException ex)
        {
            AiModelStatusTextBlock.Text = $"Az Ollama válasza nem értelmezhető: {ex.Message}";
        }
        finally
        {
            EndModelOperation();
        }
    }

    private void BeginModelOperation(string status)
    {
        _modelOperationInProgress = true;
        AiModelProgressBar.IsIndeterminate = true;
        AiModelProgressBar.Value = 0;
        AiModelProgressBar.Visibility = Visibility.Visible;
        AiModelStatusTextBlock.Text = status;
        UpdateAiControls();
    }

    private void EndModelOperation()
    {
        _modelOperationInProgress = false;
        AiModelProgressBar.Visibility = Visibility.Collapsed;
        UpdateAiControls();
    }

    private void UpdateAiControls()
    {
        var canSelectModel = EnableAiAnalysisCheckBox.IsChecked == true &&
                             _modelListLoaded &&
                             !_modelOperationInProgress;
        LlmModelComboBox.IsEnabled = canSelectModel;
        ModelActionButton.IsEnabled = canSelectModel;
    }

    private void OpenOllamaInstructionsButton_Click(object sender, RoutedEventArgs e)
    {
        new OllamaInstructionsWindow
        {
            Owner = this
        }.ShowDialog();
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

    private void AvailableNameFieldsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void AvailableNameFieldsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var field = GetItemAt(AvailableNameFieldsList, e.OriginalSource as DependencyObject);
        if (field is not null)
        {
            DragDrop.DoDragDrop(AvailableNameFieldsList, field, DragDropEffects.Copy);
        }
    }

    private void NamePatternList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(InvoiceNameField))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void NamePatternList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(InvoiceNameField)) is InvoiceNameField field)
        {
            _namePatternFields.Add(field);
            UpdatePatternPreview();
        }
    }

    private void RemovePatternFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (NamePatternList.SelectedItem is InvoiceNameField field)
        {
            _namePatternFields.Remove(field);
            UpdatePatternPreview();
        }
    }

    private void ResetPatternButton_Click(object sender, RoutedEventArgs e)
    {
        ResetNamePattern();
        UpdatePatternPreview();
    }

    private void ResetNamePattern()
    {
        _namePatternFields.Clear();
        foreach (var key in InvoiceNameFields.CreateDefaultPattern())
        {
            if (InvoiceNameFields.Find(key) is { } field)
            {
                _namePatternFields.Add(field);
            }
        }
    }

    private void UpdatePatternPreview()
    {
        PatternPreviewTextBlock.Text = _namePatternFields.Count == 0
            ? "A minta üres."
            : $"Példa: {string.Join("_", _namePatternFields.Select(field => field.DisplayName))}.pdf";
    }

    private static InvoiceNameField? GetItemAt(ListBox listBox, DependencyObject? source)
    {
        if (source is null)
        {
            return null;
        }

        return ItemsControl.ContainerFromElement(listBox, source) is ListBoxItem item
            ? item.DataContext as InvoiceNameField
            : null;
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

        if (_namePatternFields.Count == 0)
        {
            MessageBox.Show(
                this,
                "A számla elnevezési mintának legalább egy mezőt tartalmaznia kell.",
                "Beállítások",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (EnableAiAnalysisCheckBox.IsChecked == true &&
            (!_modelListLoaded ||
             LlmModelComboBox.SelectedItem is not OllamaModelOption { IsInstalled: true }))
        {
            MessageBox.Show(
                this,
                "Az AI elemzéshez válasszon telepített modellt. A szürke modell kiválasztásával vagy a Letöltés / indítás gombbal telepíthet modellt.",
                "AI elemzés",
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
        Settings.AiAnalysisEnabled = EnableAiAnalysisCheckBox.IsChecked == true;
        Settings.AiAnalysisInstruction = string.IsNullOrWhiteSpace(AiAnalysisInstructionTextBox.Text)
            ? OllamaService.DefaultAnalysisInstruction
            : AiAnalysisInstructionTextBox.Text.Trim();
        if (AiImageDpiComboBox.SelectedItem is int imageDpi)
        {
            Settings.AiImageDpi = imageDpi;
        }

        Settings.InvoiceNamePattern = _namePatternFields.Select(field => field.Key).ToList();
        if (LlmModelComboBox.SelectedItem is OllamaModelOption selectedModel)
        {
            Settings.LlmModel = selectedModel.Name;
        }

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
