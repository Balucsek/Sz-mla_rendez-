using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace InvoiceManager.Services;

public sealed class OllamaModelOption
{
    public string Name { get; init; } = string.Empty;
    public bool IsInstalled { get; set; }
    public string DisplayName => IsInstalled ? Name : $"{Name} (nincs telepítve - kattintás a letöltéshez)";
}

public static class OllamaModelOptions
{
    public static IReadOnlyList<OllamaModelOption> All { get; } = new[]
    {
        new OllamaModelOption { Name = "granite3.2-vision" },
        new OllamaModelOption { Name = "qwen2.5vl:3b" },
        new OllamaModelOption { Name = "qwen2.5vl:7b" }
    };
}

public sealed class OllamaPullProgress
{
    public string Status { get; init; } = string.Empty;
    public long? Completed { get; init; }
    public long? Total { get; init; }
}

public sealed class OllamaService
{
    public const string DefaultAnalysisInstruction =
        "Elemezd a számlát a megadott PDF-szöveg és az oldalképek alapján. Keresd meg az eladót, a vevőt, " +
        "a számla dátumát, a fizetendő teljes összeget és a számlaszámot. A dátumnál a kiállítás dátumát " +
        "részesítsd előnyben; ha nincs feltüntetve, használd a teljesítés dátumát, majd ennek hiányában " +
        "a fizetési határidőt. A dátumot mindig ÉÉÉÉ-HH-NN formátumban add vissza (például 2024-12-03). " +
        "Az összeghez a teljes fizetendő összeget és a pénznemet add meg. " +
        "Ne találj ki hiányzó adatot: az olvashatatlan vagy nem szereplő mező értéke legyen null.";

    internal const string PreviousDefaultAnalysisInstruction =
        "Elemezd a számlát a megadott PDF-szöveg és az oldalképek alapján. Keresd meg az eladót, a vevőt, " +
        "a számla dátumát, a fizetendő teljes összeget és a számlaszámot. A dátumnál a kiállítás dátumát " +
        "részesítsd előnyben; ha nincs feltüntetve, használd a teljesítés dátumát, majd ennek hiányában " +
        "a fizetési határidőt. Az összeghez a teljes fizetendő összeget és a pénznemet add meg. " +
        "Ne találj ki hiányzó adatot: az olvashatatlan vagy nem szereplő mező értéke legyen null.";

    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:11434/"),
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PdfPageImageRenderer _imageRenderer = new();

    public async Task<HashSet<string>> GetInstalledModelsAsync(CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync("api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.TryGetProperty("models", out var models) &&
            models.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in models.EnumerateArray())
            {
                if (model.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    names.Add(name.GetString() ?? string.Empty);
                }
            }
        }

        return names;
    }

    public async Task PullModelAsync(
        string model,
        IProgress<OllamaPullProgress> progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/pull")
        {
            Content = JsonContent.Create(new { model, stream = true })
        };
        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var message = JsonDocument.Parse(line);
            var root = message.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(error.GetString() ?? "Az Ollama modell letöltése sikertelen.");
            }

            progress.Report(new OllamaPullProgress
            {
                Status = ReadString(root, "status"),
                Completed = ReadInt64(root, "completed"),
                Total = ReadInt64(root, "total")
            });
        }
    }

    public async Task LoadModelAsync(string model, CancellationToken cancellationToken)
    {
        using var response = await Client.PostAsJsonAsync(
            "api/generate",
            new
            {
                model,
                prompt = " ",
                stream = false,
                keep_alive = "5m",
                options = new { num_predict = 1 }
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<InvoiceAnalysisResult> AnalyzeInvoiceAsync(
        string pdfPath,
        string extractedText,
        string instruction,
        string model,
        int imageDpi,
        CancellationToken cancellationToken)
    {
        var images = await Task.Run(
            () => _imageRenderer.RenderAsBase64Images(pdfPath, imageDpi, cancellationToken),
            cancellationToken);
        var combinedAnalysis = new InvoiceAnalysisResult();
        for (var pageIndex = 0; pageIndex < images.Count; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageAnalysis = await AnalyzePageAsync(
                images[pageIndex],
                pageIndex + 1,
                images.Count,
                pageIndex == 0 ? extractedText : string.Empty,
                instruction,
                model,
                cancellationToken);
            combinedAnalysis.InvoiceDate = FirstNonEmpty(combinedAnalysis.InvoiceDate, pageAnalysis.InvoiceDate);
            combinedAnalysis.Buyer = FirstNonEmpty(combinedAnalysis.Buyer, pageAnalysis.Buyer);
            combinedAnalysis.Seller = FirstNonEmpty(combinedAnalysis.Seller, pageAnalysis.Seller);
            if (!string.IsNullOrWhiteSpace(pageAnalysis.TotalAmount))
            {
                combinedAnalysis.TotalAmount = pageAnalysis.TotalAmount;
            }

            combinedAnalysis.InvoiceNumber = FirstNonEmpty(combinedAnalysis.InvoiceNumber, pageAnalysis.InvoiceNumber);
        }

        return combinedAnalysis;
    }

    private static async Task<InvoiceAnalysisResult> AnalyzePageAsync(
        string image,
        int pageNumber,
        int pageCount,
        string extractedText,
        string instruction,
        string model,
        CancellationToken cancellationToken)
    {
        var prompt =
            instruction + "\n\n" +
            $"Elemezd a PDF {pageNumber}. oldalát ({pageCount} oldal összesen).\n" +
            (pageNumber == 1
                ? "A PDF-ből kinyert teljes szöveg (ha van):\n" +
                  (string.IsNullOrWhiteSpace(extractedText) ? "[Nem található kinyerhető szöveg.]" : extractedText) + "\n\n"
                : string.Empty) +
            "A csatolt kép az aktuális oldal. A rendelkezésre álló szöveget és a képet együtt használd; " +
            "ha a szöveg hibás vagy hiányos, ellenőrizd az adatot a képen.\n" +
            "Kizárólag érvényes JSON objektummal válaszolj, pontosan ezekkel a mezőnevekkel: " +
            "invoice_date, buyer, seller, total_amount, invoice_number. Minden mező legyen jelen; " +
            "ha egy adat nem állapítható meg ezen az oldalon, annak értéke null legyen.";

        using var response = await Client.PostAsJsonAsync(
            "api/generate",
            new
            {
                model,
                prompt,
                images = new[] { image },
                format = new
                {
                    type = "object",
                    properties = new
                    {
                        invoice_date = new { type = new[] { "string", "null" } },
                        buyer = new { type = new[] { "string", "null" } },
                        seller = new { type = new[] { "string", "null" } },
                        total_amount = new { type = new[] { "string", "null" } },
                        invoice_number = new { type = new[] { "string", "null" } }
                    },
                    required = new[] { "invoice_date", "buyer", "seller", "total_amount", "invoice_number" }
                },
                stream = false,
                keep_alive = "5m",
                options = new { temperature = 0.1 }
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
            JsonOptions,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(result?.Response))
        {
            throw new InvalidDataException("Az Ollama nem adott vissza számlaadatokat.");
        }

        var analysis = JsonSerializer.Deserialize<InvoiceAnalysisResult>(result.Response, JsonOptions);
        return analysis ?? throw new InvalidDataException("Az Ollama válasza nem tartalmazott feldolgozható JSON adatokat.");
    }

    private static string? FirstNonEmpty(string? currentValue, string? pageValue)
    {
        return !string.IsNullOrWhiteSpace(currentValue) ? currentValue : pageValue;
    }

    public static bool IsInstalled(string model, IEnumerable<string> installedModels)
    {
        foreach (var installed in installedModels)
        {
            if (string.Equals(installed, model, StringComparison.OrdinalIgnoreCase) ||
                (model == "granite3.2-vision" &&
                 installed.StartsWith(model + ":", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long? ReadInt64(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.TryGetInt64(out var value)
            ? value
            : null;
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }
}
