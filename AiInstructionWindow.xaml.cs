using System.Collections.Generic;
using System.Linq;
using System.Windows;
using InvoiceManager.Services;
using MessageBox = System.Windows.MessageBox;

namespace InvoiceManager;

public partial class AiInstructionWindow : Window
{
    public string Instruction { get; private set; } = string.Empty;
    public IReadOnlyList<int> ImageDpiOptions => PdfPageImageRenderer.SupportedDpiValues;
    public int SelectedImageDpi { get; set; }

    public AiInstructionWindow(string defaultInstruction, int defaultImageDpi)
    {
        InitializeComponent();
        InstructionTextBox.Text = defaultInstruction;
        SelectedImageDpi = PdfPageImageRenderer.SupportedDpiValues.Contains(defaultImageDpi)
            ? defaultImageDpi
            : PdfPageImageRenderer.DefaultDpi;
        DataContext = this;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InstructionTextBox.Text))
        {
            MessageBox.Show(
                this,
                "Az AI-utasítás nem lehet üres.",
                "AI-elemzés újrafuttatása",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Instruction = InstructionTextBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
