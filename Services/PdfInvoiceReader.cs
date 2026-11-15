using System;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace InvoiceManager.Services;

public interface IPdfInvoiceReader
{
    string ReadText(string path);
    string SuggestFileName(string originalPath, string extractedText);
}

public sealed class PdfInvoiceReader : IPdfInvoiceReader
{
    private static readonly Regex DatePattern = new(
        @"(?<!\d)(?<date>\d{4}[.\-/]\d{1,2}[.\-/]\d{1,2}|\d{1,2}[.\-/]\d{1,2}[.\-/]\d{4})(?!\d)",
        RegexOptions.Compiled);

    public string ReadText(string path)
    {
        using var document = PdfDocument.Open(path);
        return string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
    }

    public string SuggestFileName(string originalPath, string extractedText)
    {
        var originalName = System.IO.Path.GetFileNameWithoutExtension(originalPath);
        var match = DatePattern.Match(extractedText);
        if (!match.Success)
        {
            return originalName;
        }

        var rawDate = match.Groups["date"].Value.Replace('-', '.').Replace('/', '.');
        var formats = new[] { "yyyy.M.d", "yyyy.MM.dd", "d.M.yyyy", "dd.MM.yyyy", "d.M.yy", "dd.MM.yy" };
        if (!DateTime.TryParseExact(
                rawDate,
                formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var invoiceDate))
        {
            return originalName;
        }

        return $"{invoiceDate:yyyy-MM-dd}_{originalName}";
    }
}
