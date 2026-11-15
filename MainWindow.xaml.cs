using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Docnet.Core.Exceptions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using InvoiceManager.Services;
using MessageBox = System.Windows.MessageBox;

namespace InvoiceManager;

public sealed class RegisteredInvoice
{
    public string NewName { get; init; } = string.Empty;
    public string OriginalName { get; init; } = string.Empty;
    public string StatusNote { get; init; } = string.Empty;
    public string DestinationPath { get; init; } = string.Empty;
}

public partial class MainWindow : Window
{
    public ObservableCollection<string> PendingInvoices { get; } = new();
    public ObservableCollection<RegisteredInvoice> RegisteredInvoices { get; } = new();

    private readonly IPdfInvoiceReader _pdfInvoiceReader = new PdfInvoiceReader();
    private readonly ScannerService _scannerService = new();
    private readonly OllamaService _ollamaService = new();
    private readonly UserSettingsService _settingsService = new();
    private readonly SemaphoreSlim _analysisSemaphore = new(1, 1);
    private readonly System.Collections.Generic.Dictionary<string, Task<InvoiceAnalysisResult>> _analysisTasks =
        new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings = AppSettings.CreateDefault();
    private System.Collections.Generic.List<ScannerDevice> _availableScanners = new();
    private InvoiceRegistrationWindow? _registrationDialog;
    private CancellationTokenSource? _analysisCancellation;
    private bool _allowWindowClose;
    private string _lastInputFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private int _lastSelectedYear = DateTime.Now.Year;
    private int _lastSelectedMonth = DateTime.Now.Month;

    public MainWindow()
    {
        InitializeComponent();
        System.Windows.Application.Current.MainWindow = this;
        System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
        DataContext = this;
        try
        {
            _settings = _settingsService.Load();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException)
        {
            _settings = AppSettings.CreateDefault();
            MessageBox.Show(
                this,
                $"A beállításokat nem sikerült betölteni. Az alapértelmezett beállítások használatban maradnak.\n\n{ex.Message}",
                "Beállítások betöltése",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        RefreshAvailableScanners();
        PdfPreview.CoreWebView2InitializationCompleted += (_, args) =>
        {
            if (args.InitializationException is not null)
            {
                MessageBox.Show(
                    this,
                    "A PDF-előnézet indítása nem sikerült. Ellenőrizze, hogy telepítve van-e a Microsoft Edge WebView2 Runtime.\n\n" +
                    args.InitializationException.Message,
                    "PDF-előnézet hiba",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
    }

    private void StartRegistrationButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Válassza ki az iktatandó PDF számlákat tartalmazó mappát",
            SelectedPath = Directory.Exists(_lastInputFolder) ? _lastInputFolder : string.Empty
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            BeginRegistration(dialog.SelectedPath);
        }
    }

    private void QuickScanButton_Click(object sender, RoutedEventArgs e)
    {
        QuickScanButton.IsEnabled = false;
        try
        {
            var scannedPdfPath = _scannerService.ScanToPdf(_settings.ScanFolder, _settings.ScannerDeviceId);
            if (scannedPdfPath is null)
            {
                return;
            }

            var scanTime = DateTime.Now;
            var destinationPath = RegisterInvoice(
                scannedPdfPath,
                $"Szamla_{scanTime:yyyy-MM-dd_HHmmss}",
                scanTime.Year,
                scanTime.Month);

            MessageBox.Show(
                this,
                $"A beolvasott számla iktatása sikerült:\n{destinationPath}",
                "Gyors szkennelés",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (COMException ex)
        {
            ShowError("A szkenner nem érhető el vagy a beolvasás közben hiba történt.", ex);
        }
        catch (IOException ex)
        {
            ShowError("A beolvasott fájl mentése vagy iktatása nem sikerült.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError("Nincs jogosultság a scan vagy a célmappába történő mentéshez.", ex);
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message, ex);
        }
        catch (ArgumentException ex)
        {
            ShowError("A szkenner beállítása vagy a mentési útvonal érvénytelen.", ex);
        }
        catch (NotSupportedException ex)
        {
            ShowError("A szkenner által visszaadott képformátum nem támogatott.", ex);
        }
        finally
        {
            QuickScanButton.IsEnabled = true;
        }
    }

    private void BeginRegistration(string folder)
    {
        try
        {
            var pdfPaths = Directory.EnumerateFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _lastInputFolder = folder;
            PendingInvoices.Clear();
            RegisteredInvoices.Clear();
            foreach (var path in pdfPaths)
            {
                PendingInvoices.Add(Path.GetFileName(path));
            }

            _pendingPaths = pdfPaths;
            PrepareInvoiceAnalysisQueue();
            SourceFolderLabel.Text = folder;
            ShowPanel(IntakePanel);
            if (_pendingPaths.Count == 0)
            {
                CurrentPdfLabel.Text = "A kiválasztott mappában nincs PDF";
                PdfPreview.Source = null;
                ReturnToMenu();
                MessageBox.Show(
                    this,
                    "A kiválasztott mappában nincs iktatandó PDF számla.",
                    "Nincs feldolgozandó számla",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Dispatcher.BeginInvoke(new Action(ShowCurrentInvoice), DispatcherPriority.Background);
        }
        catch (IOException ex)
        {
            ShowError("A kiválasztott mappa nem olvasható.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError("Nincs jogosultság a kiválasztott mappa olvasásához.", ex);
        }
    }

    private System.Collections.Generic.List<string> _pendingPaths = new();

    private void ShowCurrentInvoice()
    {
        if (IntakePanel.Visibility != Visibility.Visible ||
            _pendingPaths.Count == 0 ||
            _registrationDialog is not null)
        {
            return;
        }

        var currentPath = _pendingPaths[0];
        CurrentPdfLabel.Text = Path.GetFileName(currentPath);
        PdfPreview.Source = new Uri(currentPath, UriKind.Absolute);

        string extractedText;
        try
        {
            extractedText = _pdfInvoiceReader.ReadText(currentPath);
        }
        catch (IOException ex)
        {
            ShowError("A PDF-fájl olvasása nem sikerült.", ex);
            extractedText = string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError("Nincs jogosultság a PDF olvasásához.", ex);
            extractedText = string.Empty;
        }
        catch (ArgumentException ex)
        {
            ShowError("A PDF-fájl formátuma nem olvasható.", ex);
            extractedText = string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            ShowError("A PDF-fájl szövegének kiolvasása nem sikerült.", ex);
            extractedText = string.Empty;
        }

        var dialog = new InvoiceRegistrationWindow(
            currentPath,
            extractedText,
            _pdfInvoiceReader.SuggestFileName(currentPath, extractedText),
            _settings.InvoiceNamePattern,
            _lastSelectedYear,
            _lastSelectedMonth,
            RegisteredInvoices.Any(invoice => File.Exists(invoice.DestinationPath)),
            _settings.AiAnalysisEnabled)
        {
            Owner = this
        };
        _registrationDialog = dialog;
        dialog.CancelRequested += (_, _) => CancelRegistrationDialog(dialog);
        dialog.RegistrationRequested += (_, _) => ProcessCurrentInvoice(dialog);
        dialog.AppendRequested += (_, _) => ProcessCurrentInvoice(dialog);
        dialog.AiRetryRequested += (_, _) => RetryCurrentInvoiceAnalysis(dialog, currentPath);
        dialog.AiRetryWithInstructionsRequested += (_, _) =>
            RetryCurrentInvoiceAnalysisWithInstructions(dialog, currentPath);
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_registrationDialog, dialog))
            {
                _registrationDialog = null;
                ReturnToMenu();
            }
        };
        dialog.Show();
        if (_settings.AiAnalysisEnabled)
        {
            _ = ApplyCurrentInvoiceAnalysisAsync(dialog, currentPath);
        }
    }

    private void PrepareInvoiceAnalysisQueue(
        string? currentInvoiceInstruction = null,
        int? currentInvoiceImageDpi = null)
    {
        CancelInvoiceAnalysisQueue();
        if (!_settings.AiAnalysisEnabled || _pendingPaths.Count == 0)
        {
            return;
        }

        _analysisCancellation = new CancellationTokenSource();
        var cancellationToken = _analysisCancellation.Token;
        for (var index = 0; index < _pendingPaths.Count; index++)
        {
            var path = _pendingPaths[index];
            var instruction = index == 0 && currentInvoiceInstruction is not null
                ? currentInvoiceInstruction
                : _settings.AiAnalysisInstruction;
            var imageDpi = index == 0 && currentInvoiceImageDpi.HasValue
                ? currentInvoiceImageDpi.Value
                : _settings.AiImageDpi;
            var analysisTask = AnalyzeQueuedInvoiceAsync(
                path,
                _settings.LlmModel,
                instruction,
                imageDpi,
                cancellationToken);
            _analysisTasks[path] = analysisTask;
            _ = analysisTask.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async void RetryCurrentInvoiceAnalysis(InvoiceRegistrationWindow dialog, string path)
    {
        if (!ReferenceEquals(_registrationDialog, dialog))
        {
            return;
        }

        dialog.StartAiAnalysis();
        PrepareInvoiceAnalysisQueue();
        await ApplyCurrentInvoiceAnalysisAsync(dialog, path);
    }

    private async void RetryCurrentInvoiceAnalysisWithInstructions(
        InvoiceRegistrationWindow dialog,
        string path)
    {
        if (!ReferenceEquals(_registrationDialog, dialog))
        {
            return;
        }

        var instructionDialog = new AiInstructionWindow(
            _settings.AiAnalysisInstruction,
            _settings.AiImageDpi)
        {
            Owner = dialog
        };
        if (instructionDialog.ShowDialog() != true ||
            !ReferenceEquals(_registrationDialog, dialog))
        {
            return;
        }

        dialog.StartAiAnalysis();
        PrepareInvoiceAnalysisQueue(
            instructionDialog.Instruction,
            instructionDialog.SelectedImageDpi);
        await ApplyCurrentInvoiceAnalysisAsync(dialog, path);
    }

    private async Task<InvoiceAnalysisResult> AnalyzeQueuedInvoiceAsync(
        string path,
        string model,
        string instruction,
        int imageDpi,
        CancellationToken cancellationToken)
    {
        await _analysisSemaphore.WaitAsync(cancellationToken);
        try
        {
            var extractedText = await Task.Run(
                () => _pdfInvoiceReader.ReadText(path),
                cancellationToken);
            return await _ollamaService.AnalyzeInvoiceAsync(
                path,
                extractedText,
                instruction,
                model,
                imageDpi,
                cancellationToken);
        }
        finally
        {
            _analysisSemaphore.Release();
        }
    }

    private async Task ApplyCurrentInvoiceAnalysisAsync(InvoiceRegistrationWindow dialog, string path)
    {
        if (!_analysisTasks.TryGetValue(path, out var analysisTask))
        {
            dialog.ShowAiAnalysisError("Az AI elemzési feladat nem indult el.");
            return;
        }

        try
        {
            var analysis = await analysisTask;
            if (ReferenceEquals(_registrationDialog, dialog))
            {
                dialog.ApplyAiAnalysis(analysis);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpRequestException ex)
        {
            if (ReferenceEquals(_registrationDialog, dialog))
            {
                dialog.ShowAiAnalysisError($"Az Ollama elemzése nem sikerült: {ex.Message}");
            }
        }
        catch (IOException ex)
        {
            if (ReferenceEquals(_registrationDialog, dialog))
            {
                dialog.ShowAiAnalysisError($"A PDF képpé alakítása nem sikerült: {ex.Message}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            if (ReferenceEquals(_registrationDialog, dialog))
            {
                dialog.ShowAiAnalysisError($"A PDF nem olvasható: {ex.Message}");
            }
        }
        catch (InvalidOperationException ex)
        {
            if (ReferenceEquals(_registrationDialog, dialog))
            {
                dialog.ShowAiAnalysisError($"Az AI elemzés nem sikerült: {ex.Message}");
            }
        }
        catch (JsonException ex)
        {
            if (ReferenceEquals(_registrationDialog, dialog))
            {
                dialog.ShowAiAnalysisError($"Az Ollama válasza nem értelmezhető: {ex.Message}");
            }
        }
        catch (NotSupportedException ex)
        {
            if (ReferenceEquals(_registrationDialog, dialog))
            {
                dialog.ShowAiAnalysisError($"A PDF képpé alakítása nem támogatott: {ex.Message}");
            }
        }
        catch (DocnetException ex)
        {
            if (ReferenceEquals(_registrationDialog, dialog))
            {
                dialog.ShowAiAnalysisError($"A PDF képpé alakítása nem sikerült: {ex.Message}");
            }
        }
        catch (ArgumentException ex)
        {
            if (ReferenceEquals(_registrationDialog, dialog))
            {
                dialog.ShowAiAnalysisError($"A PDF képpé alakítása nem sikerült: {ex.Message}");
            }
        }
    }

    private void CancelInvoiceAnalysisQueue()
    {
        if (_analysisCancellation is not null)
        {
            _analysisCancellation.Cancel();
            _analysisCancellation.Dispose();
            _analysisCancellation = null;
        }

        _analysisTasks.Clear();
    }

    private void ProcessCurrentInvoice(InvoiceRegistrationWindow dialog)
    {
        if (!ReferenceEquals(_registrationDialog, dialog) || _pendingPaths.Count == 0)
        {
            return;
        }

        var currentPath = _pendingPaths[0];
        _lastSelectedYear = dialog.SelectedYear;
        _lastSelectedMonth = dialog.SelectedMonth;
        try
        {
            if (dialog.RequestedAction == RegistrationAction.Register)
            {
                RegisterInvoice(currentPath, dialog.NewFileName, dialog.SelectedYear, dialog.SelectedMonth);
            }
            else
            {
                AppendToPreviousInvoice(currentPath);
            }
        }
        catch (IOException ex)
        {
            ShowError("A fájl iktatása nem sikerült. A számla a feldolgozási listán maradt.", ex, dialog);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError("Nincs jogosultság a fájl módosításához. A számla a feldolgozási listán maradt.", ex, dialog);
            return;
        }
        catch (ArgumentException ex)
        {
            ShowError("A megadott fájlnév nem használható.", ex, dialog);
            return;
        }
        catch (InvalidOperationException ex)
        {
            ShowError("A számla nem iktatható.", ex, dialog);
            return;
        }

        PendingInvoices.RemoveAt(0);
        _pendingPaths.RemoveAt(0);
        _registrationDialog = null;
        dialog.Close();
        if (_pendingPaths.Count == 0)
        {
            ReturnToMenu();
            MessageBox.Show(
                this,
                "A kiválasztott mappa minden PDF számlájának feldolgozása befejeződött.",
                "Iktatás kész",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Dispatcher.BeginInvoke(new Action(ShowCurrentInvoice), DispatcherPriority.Background);
    }

    private void CancelRegistrationDialog(InvoiceRegistrationWindow dialog)
    {
        if (!ReferenceEquals(_registrationDialog, dialog))
        {
            return;
        }

        ReturnToMenu();
    }

    private string RegisterInvoice(string sourcePath, string requestedName, int year, int month)
    {
        var fileName = SanitizePdfFileName(requestedName);
        var destinationDirectory = Path.Combine(_settings.DestinationFolder, year.ToString(), month.ToString("00"));
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = GetUniquePath(destinationDirectory, fileName);
        if (string.Equals(Path.GetFullPath(sourcePath), destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A forrás és a cél ugyanaz a fájl. Válasszon másik nevet vagy dátumot.");
        }

        if (_settings.KeepOriginalFiles)
        {
            File.Copy(sourcePath, destinationPath);
        }
        else
        {
            File.Move(sourcePath, destinationPath);
        }

        RegisteredInvoices.Insert(0, new RegisteredInvoice
        {
            NewName = Path.GetFileName(destinationPath),
            OriginalName = $"({Path.GetFileName(sourcePath)})",
            DestinationPath = destinationPath
        });
        return destinationPath;
    }

    private void AppendToPreviousInvoice(string sourcePath)
    {
        var previousInvoice = RegisteredInvoices.FirstOrDefault(invoice => File.Exists(invoice.DestinationPath));
        if (previousInvoice is null)
        {
            throw new InvalidOperationException("Nincs olyan korábban iktatott számla, amelyhez hozzáfűzhető lenne.");
        }

        MergePdfFiles(previousInvoice.DestinationPath, sourcePath);
        RegisteredInvoices.Insert(0, new RegisteredInvoice
        {
            NewName = previousInvoice.NewName,
            OriginalName = $"({Path.GetFileName(sourcePath)})",
            StatusNote = "Hozzáfűzve az előző számlához",
            DestinationPath = previousInvoice.DestinationPath
        });

        if (_settings.KeepOriginalFiles)
        {
            return;
        }

        try
        {
            File.Delete(sourcePath);
        }
        catch (IOException ex)
        {
            MessageBox.Show(
                this,
                "A PDF hozzáfűzése sikerült, de az eredeti bemeneti fájl törlése nem. Az eredeti fájl a feldolgozási mappában maradt.\n\n" +
                ex.Message,
                "Figyelmeztetés",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(
                this,
                "A PDF hozzáfűzése sikerült, de nincs jogosultság az eredeti bemeneti fájl törléséhez. Az eredeti fájl a feldolgozási mappában maradt.\n\n" +
                ex.Message,
                "Figyelmeztetés",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void MergePdfFiles(string destinationPath, string appendedPath)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(destinationPath)!,
            $".{Guid.NewGuid():N}.merge.tmp.pdf");
        using (var mergedDocument = new PdfDocument())
        {
            using (var originalDocument = PdfReader.Open(destinationPath, PdfDocumentOpenMode.Import))
            {
                foreach (var page in originalDocument.Pages)
                {
                    mergedDocument.AddPage(page);
                }
            }

            using (var appendedDocument = PdfReader.Open(appendedPath, PdfDocumentOpenMode.Import))
            {
                foreach (var page in appendedDocument.Pages)
                {
                    mergedDocument.AddPage(page);
                }
            }

            mergedDocument.Save(temporaryPath);
        }

        File.Replace(temporaryPath, destinationPath, null);
    }

    private static string SanitizePdfFileName(string requestedName)
    {
        var name = Path.GetFileName(requestedName.Trim());
        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidCharacter, '_');
        }

        name = name.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Adjon meg egy érvényes fájlnevet.");
        }

        return name + ".pdf";
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{name} ({suffix}){extension}");
            suffix++;
        }

        return candidate;
    }

    private void OpenSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchRootTextBox.Text = _settings.DestinationFolder;
        ShowPanel(SearchPanel);
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                _settingsService.Save(dialog.Settings);
                _settings = dialog.Settings;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowError("A beállításokat nem sikerült elmenteni. A korábbi beállítások maradtak érvényben.", ex);
            }
        }

        _availableScanners = dialog.AvailableScanners;
        UpdateQuickScanButton();
        ShowPanel(MenuPanel);
    }

    private void RefreshAvailableScanners()
    {
        try
        {
            _availableScanners = _scannerService.GetAvailableScanners();
        }
        catch (COMException)
        {
            _availableScanners = new System.Collections.Generic.List<ScannerDevice>();
        }
        catch (InvalidOperationException)
        {
            _availableScanners = new System.Collections.Generic.List<ScannerDevice>();
        }

        UpdateQuickScanButton();
    }

    private void UpdateQuickScanButton()
    {
        var selectedScannerAvailable = _availableScanners.Exists(scanner =>
            string.Equals(scanner.Id, _settings.ScannerDeviceId, StringComparison.Ordinal));
        if (!selectedScannerAvailable && _availableScanners.Count > 0)
        {
            _settings.ScannerDeviceId = _availableScanners[0].Id;
            selectedScannerAvailable = true;
        }

        QuickScanButton.IsEnabled = selectedScannerAvailable;
        QuickScanButton.ToolTip = selectedScannerAvailable
            ? "Egy oldal beolvasása és automatikus iktatása"
            : "Válasszon elérhető szkennert a Beállításokban";
    }

    private void ChooseSearchFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Válassza ki az iktatott számlákat tartalmazó mappát",
            SelectedPath = Directory.Exists(SearchRootTextBox.Text) ? SearchRootTextBox.Text : _settings.DestinationFolder
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SearchRootTextBox.Text = dialog.SelectedPath;
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        var root = SearchRootTextBox.Text;
        if (!Directory.Exists(root))
        {
            MessageBox.Show(this, "Először válasszon ki egy létező mappát.", "Keresés", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var query = SearchQueryTextBox.Text.Trim();
        SearchButton.IsEnabled = false;
        SearchStatusLabel.Text = "Keresés folyamatban…";
        SearchResultsList.ItemsSource = null;
        try
        {
            var results = await Task.Run(() =>
                Directory.EnumerateFiles(root, "*.pdf", SearchOption.AllDirectories)
                    .Where(path => Path.GetFileName(path).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                    .ToList());
            SearchResultsList.ItemsSource = results;
            SearchStatusLabel.Text = $"{results.Count} találat.";
        }
        catch (IOException ex)
        {
            ShowError("A keresés közben fájlkezelési hiba történt.", ex);
            SearchStatusLabel.Text = "A keresés nem fejeződött be.";
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowError("Nincs jogosultság a keresési mappa teljes bejárásához.", ex);
            SearchStatusLabel.Text = "A keresés nem fejeződött be.";
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void CancelRegistrationButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnToMenu();
    }

    private void BackToMenuButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnToMenu();
    }

    private void ReturnToMenu()
    {
        var dialog = _registrationDialog;
        _registrationDialog = null;
        System.Windows.Application.Current.MainWindow = this;
        PdfPreview.Source = null;
        ShowPanel(MenuPanel);
        WindowState = WindowState.Maximized;
        if (!IsVisible)
        {
            Show();
        }

        CancelInvoiceAnalysisQueue();
        dialog?.Close();
        Activate();
    }

    private void ShowPanel(UIElement panel)
    {
        MenuPanel.Visibility = Visibility.Collapsed;
        IntakePanel.Visibility = Visibility.Collapsed;
        SearchPanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    private void ShowError(string message, Exception exception, Window? owner = null)
    {
        MessageBox.Show(
            owner ?? this,
            $"{message}\n\n{exception.Message}",
            "Hiba",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _allowWindowClose = true;
        Close();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowWindowClose)
        {
            e.Cancel = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            return;
        }

        if (WindowState != WindowState.Maximized)
        {
            DragMove();
        }
    }
}
