using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace InvoiceManager.Services;

public sealed class InvoiceAnalysisResult
{
    [JsonPropertyName("invoice_date")]
    public string? InvoiceDate { get; set; }

    [JsonPropertyName("buyer")]
    public string? Buyer { get; set; }

    [JsonPropertyName("seller")]
    public string? Seller { get; set; }

    [JsonPropertyName("total_amount")]
    public string? TotalAmount { get; set; }

    [JsonPropertyName("invoice_number")]
    public string? InvoiceNumber { get; set; }

    [JsonIgnore]
    public string? ErrorMessage { get; set; }

    public bool HasAllRequestedFields =>
        !string.IsNullOrWhiteSpace(InvoiceDate) &&
        !string.IsNullOrWhiteSpace(Buyer) &&
        !string.IsNullOrWhiteSpace(Seller) &&
        !string.IsNullOrWhiteSpace(TotalAmount) &&
        !string.IsNullOrWhiteSpace(InvoiceNumber);
}

public sealed class InvoiceNameField
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public static class InvoiceNameFields
{
    public const string InvoiceDate = "invoice_date";
    public const string Buyer = "buyer";
    public const string Seller = "seller";
    public const string TotalAmount = "total_amount";
    public const string InvoiceNumber = "invoice_number";

    private static readonly IReadOnlyList<InvoiceNameField> Fields = new[]
    {
        new InvoiceNameField { Key = InvoiceDate, DisplayName = "Számla dátuma" },
        new InvoiceNameField { Key = Buyer, DisplayName = "Vevő" },
        new InvoiceNameField { Key = Seller, DisplayName = "Eladó" },
        new InvoiceNameField { Key = TotalAmount, DisplayName = "Érték" },
        new InvoiceNameField { Key = InvoiceNumber, DisplayName = "Számlaszám" }
    };

    public static IReadOnlyList<InvoiceNameField> Available => Fields;

    public static List<string> CreateDefaultPattern()
    {
        return new List<string> { InvoiceDate, Seller, InvoiceNumber };
    }

    public static string GetValue(InvoiceAnalysisResult result, string key)
    {
        return key switch
        {
            InvoiceDate => result.InvoiceDate ?? string.Empty,
            Buyer => result.Buyer ?? string.Empty,
            Seller => result.Seller ?? string.Empty,
            TotalAmount => result.TotalAmount ?? string.Empty,
            InvoiceNumber => result.InvoiceNumber ?? string.Empty,
            _ => string.Empty
        };
    }

    public static InvoiceNameField? Find(string key)
    {
        return Fields.FirstOrDefault(field => field.Key == key);
    }

    public static string BuildFileName(InvoiceAnalysisResult result, IEnumerable<string> pattern)
    {
        var parts = pattern
            .Select(key => GetValue(result, key))
            .Select(SanitizeFileNamePart)
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join("_", parts);
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalidCharacters = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Where(character => !char.IsControl(character) && !invalidCharacters.Contains(character))
            .ToArray());
        return sanitized.Trim().TrimEnd('.');
    }
}
