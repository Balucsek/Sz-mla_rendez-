using System.Windows;

namespace InvoiceManager;

public enum DuplicateFileAction
{
    Overwrite,
    Append,
    DeleteCurrent
}

public partial class DuplicateInvoiceWindow : Window
{
    public DuplicateFileAction SelectedAction { get; private set; }

    public DuplicateInvoiceWindow(string destinationPath)
    {
        InitializeComponent();
        DestinationLabel.Text = $"Már létezik ez a fájl:\n{destinationPath}";
    }

    private void OverwriteButton_Click(object sender, RoutedEventArgs e)
    {
        SelectAction(DuplicateFileAction.Overwrite);
    }

    private void AppendButton_Click(object sender, RoutedEventArgs e)
    {
        SelectAction(DuplicateFileAction.Append);
    }

    private void DeleteCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        SelectAction(DuplicateFileAction.DeleteCurrent);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SelectAction(DuplicateFileAction action)
    {
        SelectedAction = action;
        DialogResult = true;
    }
}
