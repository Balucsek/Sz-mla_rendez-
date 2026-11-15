using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using InvoiceManager.Services;

namespace InvoiceManager;

public enum RegistrationAction
{
    Register,
    AppendToPrevious
}

public partial class InvoiceRegistrationWindow : Window
{
    public event EventHandler? CancelRequested;
    public event EventHandler? RegistrationRequested;
    public event EventHandler? AppendRequested;
    public event EventHandler? AiRetryRequested;
    public event EventHandler? AiRetryWithInstructionsRequested;

    public sealed class MonthOption
    {
        public int Number { get; init; }
        public string DisplayName { get; init; } = string.Empty;
    }

    public List<int> Years { get; }
    public List<MonthOption> Months { get; }
    public int SelectedYear { get; set; }
    public int SelectedMonth { get; set; }
    public RegistrationAction RequestedAction { get; private set; } = RegistrationAction.Register;
    public string NewFileName => FileNameTextBox.Text.Trim();

    private readonly bool _aiAnalysisEnabled;
    private IReadOnlyList<string> _namePattern = Array.Empty<string>();
    private bool _updatingInvoiceFields;
    private bool _updatingFileName;
    private bool _userEditedFileName;

    public InvoiceRegistrationWindow(
        string sourcePath,
        string extractedText,
        string suggestedName,
        IReadOnlyList<string> namePattern,
        int selectedYear,
        int selectedMonth,
        bool canAppend,
        bool aiAnalysisEnabled)
    {
        InitializeComponent();
        _aiAnalysisEnabled = aiAnalysisEnabled;
        _namePattern = namePattern;

        var currentYear = DateTime.Now.Year;
        Years = Enumerable.Range(currentYear - 10, 16).ToList();
        if (!Years.Contains(selectedYear))
        {
            Years.Add(selectedYear);
            Years.Sort();
        }

        Months = System.Globalization.CultureInfo.GetCultureInfo("hu-HU")
            .DateTimeFormat.MonthNames
            .Take(12)
            .Select((name, index) => new MonthOption
            {
                Number = index + 1,
                DisplayName = $"{index + 1:00} - {name}"
            })
            .ToList();

        SelectedYear = selectedYear;
        SelectedMonth = selectedMonth;
        DataContext = this;
        SourceNameLabel.Text = Path.GetFileName(sourcePath);
        _updatingFileName = true;
        FileNameTextBox.Text = suggestedName;
        _updatingFileName = false;
        AppendButton.IsEnabled = canAppend;
        AiAnalysisPanel.Visibility = aiAnalysisEnabled ? Visibility.Visible : Visibility.Collapsed;
        AiProcessingPanel.Visibility = aiAnalysisEnabled ? Visibility.Visible : Visibility.Collapsed;
        InvoiceFieldsPanel.Visibility = Visibility.Collapsed;
        ExtractedTextBlock.Text = string.IsNullOrWhiteSpace(extractedText)
            ? "A PDF-ből nem sikerült szöveget kiolvasni. Szkennelt dokumentum esetén OCR szükséges."
            : extractedText.Length > 8000
                ? extractedText[..8000] + Environment.NewLine + "… (a szöveg előnézete rövidítve)"
                : extractedText;
        UpdateFieldStatus(InvoiceDateTextBox, InvoiceDateStatusTextBlock);
        UpdateFieldStatus(BuyerTextBox, BuyerStatusTextBlock);
        UpdateFieldStatus(SellerTextBox, SellerStatusTextBlock);
        UpdateFieldStatus(TotalAmountTextBox, TotalAmountStatusTextBlock);
        UpdateFieldStatus(InvoiceNumberTextBox, InvoiceNumberStatusTextBlock);
    }

    public void ShowAiAnalysisError(string message)
    {
        if (!_aiAnalysisEnabled)
        {
            return;
        }

        AiProcessingPanel.Visibility = Visibility.Collapsed;
        AiAnalysisErrorTextBlock.Text = message;
        AiAnalysisErrorTextBlock.Visibility = Visibility.Visible;
        AiRetryActionsPanel.Visibility = Visibility.Visible;
        InvoiceFieldsPanel.Visibility = Visibility.Visible;
    }

    public void StartAiAnalysis()
    {
        if (!_aiAnalysisEnabled)
        {
            return;
        }

        AiAnalysisErrorTextBlock.Visibility = Visibility.Collapsed;
        AiRetryActionsPanel.Visibility = Visibility.Collapsed;
        InvoiceFieldsPanel.Visibility = Visibility.Collapsed;
        AiProcessingPanel.Visibility = Visibility.Visible;
    }

    public void ApplyAiAnalysis(InvoiceAnalysisResult result)
    {
        if (!_aiAnalysisEnabled)
        {
            return;
        }

        _updatingInvoiceFields = true;
        InvoiceDateTextBox.Text = result.InvoiceDate ?? string.Empty;
        BuyerTextBox.Text = result.Buyer ?? string.Empty;
        SellerTextBox.Text = result.Seller ?? string.Empty;
        TotalAmountTextBox.Text = result.TotalAmount ?? string.Empty;
        InvoiceNumberTextBox.Text = result.InvoiceNumber ?? string.Empty;
        _updatingInvoiceFields = false;
        if (TryParseInvoiceDate(result.InvoiceDate, out var invoiceDate))
        {
            SelectedYear = invoiceDate.Year;
            SelectedMonth = invoiceDate.Month;
            YearComboBox.SelectedItem = SelectedYear;
            MonthComboBox.SelectedValue = SelectedMonth;
        }

        AiProcessingPanel.Visibility = Visibility.Collapsed;
        AiAnalysisErrorTextBlock.Visibility = Visibility.Collapsed;
        AiRetryActionsPanel.Visibility = Visibility.Collapsed;
        InvoiceFieldsPanel.Visibility = Visibility.Visible;
        UpdateAllFieldStatuses();
        RefreshFileNameFromPattern();
    }

    private static bool TryParseInvoiceDate(string? value, out DateTime invoiceDate)
    {
        invoiceDate = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var dateText = value.Trim();
        var formats = new[]
        {
            "yyyy-MM-dd", "yyyy-M-d", "yyyy.MM.dd", "yyyy.M.d", "yyyy/MM/dd", "yyyy/M/d",
            "dd.MM.yyyy", "d.M.yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy",
            "yyyy-MM-dd HH:mm:ss", "yyyy.MM.dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss"
        };
        var hungarianCulture = CultureInfo.GetCultureInfo("hu-HU");
        return DateTime.TryParseExact(
                   dateText,
                   formats,
                   hungarianCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out invoiceDate) ||
               DateTime.TryParse(
                   dateText,
                   hungarianCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out invoiceDate) ||
               DateTime.TryParse(
                   dateText,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out invoiceDate);
    }

    private void InvoiceFieldTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updatingInvoiceFields)
        {
            return;
        }

        UpdateAllFieldStatuses();
        RefreshFileNameFromPattern();
    }

    private void FileNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_updatingFileName)
        {
            _userEditedFileName = true;
        }
    }

    private void UpdateAllFieldStatuses()
    {
        UpdateFieldStatus(InvoiceDateTextBox, InvoiceDateStatusTextBlock);
        UpdateFieldStatus(BuyerTextBox, BuyerStatusTextBlock);
        UpdateFieldStatus(SellerTextBox, SellerStatusTextBlock);
        UpdateFieldStatus(TotalAmountTextBox, TotalAmountStatusTextBlock);
        UpdateFieldStatus(InvoiceNumberTextBox, InvoiceNumberStatusTextBlock);
    }

    private static void UpdateFieldStatus(System.Windows.Controls.TextBox textBox, System.Windows.Controls.TextBlock status)
    {
        var found = !string.IsNullOrWhiteSpace(textBox.Text);
        status.Text = found ? "✓" : "⚠";
        status.Foreground = found
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 133, 67))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(214, 161, 0));
    }

    private void RefreshFileNameFromPattern()
    {
        if (!_aiAnalysisEnabled || _userEditedFileName || _namePattern.Count == 0)
        {
            return;
        }

        var result = new InvoiceAnalysisResult
        {
            InvoiceDate = InvoiceDateTextBox.Text,
            Buyer = BuyerTextBox.Text,
            Seller = SellerTextBox.Text,
            TotalAmount = TotalAmountTextBox.Text,
            InvoiceNumber = InvoiceNumberTextBox.Text
        };
        var name = InvoiceNameFields.BuildFileName(result, _namePattern);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _updatingFileName = true;
            FileNameTextBox.Text = name;
            _updatingFileName = false;
        }
    }

    private void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        RequestedAction = RegistrationAction.Register;
        RegistrationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AppendButton_Click(object sender, RoutedEventArgs e)
    {
        RequestedAction = RegistrationAction.AppendToPrevious;
        AppendRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RetryAiAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        AiRetryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RetryAiAnalysisWithInstructionsButton_Click(object sender, RoutedEventArgs e)
    {
        AiRetryWithInstructionsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
