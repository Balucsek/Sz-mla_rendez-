using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

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

    public InvoiceRegistrationWindow(
        string sourcePath,
        string extractedText,
        string suggestedName,
        int selectedYear,
        int selectedMonth,
        bool canAppend)
    {
        InitializeComponent();

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
        FileNameTextBox.Text = suggestedName;
        AppendButton.IsEnabled = canAppend;
        ExtractedTextBlock.Text = string.IsNullOrWhiteSpace(extractedText)
            ? "A PDF-ből nem sikerült szöveget kiolvasni. Szkennelt dokumentum esetén OCR szükséges."
            : extractedText.Length > 8000
                ? extractedText[..8000] + Environment.NewLine + "… (a szöveg előnézete rövidítve)"
                : extractedText;
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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
