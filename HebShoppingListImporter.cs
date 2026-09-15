using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace PersonalInventory;

public sealed class HebShoppingListItem
{
    public string Name { get; init; } = string.Empty;
    public double Quantity { get; init; } = 1;
    public string Unit { get; init; } = "pcs";
    public string Category { get; init; } = string.Empty;
}

public sealed class HebShoppingList
{
    public string Name { get; init; } = "H-E-B Shopping List";
    public List<HebShoppingListItem> Items { get; init; } = new();
}

public interface IHebShoppingListImporter
{
    Task<HebShoppingList> ImportAsync(string url, CancellationToken cancellationToken = default);
}

public sealed class HebShoppingListImporter : IHebShoppingListImporter
{
    private static readonly Regex SharedListUrl = new(
        "^https://(?:www\\.)?heb\\.com/shopping-list/shared/[A-Za-z0-9-]+/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QuantityRegex = new(
        "Qty:\\s*([0-9]+(?:[.,][0-9]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;

    public HebShoppingListImporter(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140 Safari/537.36");
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<HebShoppingList> ImportAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || !SharedListUrl.IsMatch(uri.ToString()))
            throw new ArgumentException("Please enter a valid H-E-B shared shopping-list URL.", nameof(url));

        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static HebShoppingList Parse(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var title = CleanText(document.DocumentNode.SelectSingleNode("//h1")?.InnerText) ?? "H-E-B Shopping List";
        var items = new List<HebShoppingListItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var quantityNodes = document.DocumentNode.SelectNodes(
            "//*[contains(normalize-space(.), 'Qty:') and not(.//*[contains(normalize-space(.), 'Qty:')])]" );

        if (quantityNodes == null)
            throw new InvalidOperationException("The H-E-B page was reached, but no shopping-list items could be read.");

        foreach (var node in quantityNodes)
        {
            var text = CleanText(node.InnerText);
            if (string.IsNullOrWhiteSpace(text)) continue;
            var quantityMatch = QuantityRegex.Match(text);
            if (!quantityMatch.Success) continue;

            var quantityText = quantityMatch.Groups[1].Value.Replace(',', '.');
            if (!double.TryParse(quantityText, NumberStyles.Float, CultureInfo.InvariantCulture, out var quantity)) quantity = 1;

            var name = FindProductName(node);
            if (string.IsNullOrWhiteSpace(name)) continue;
            name = CleanProductName(name);
            if (name.Length < 2 || !seen.Add(name)) continue;

            items.Add(new HebShoppingListItem
            {
                Name = name,
                Quantity = quantity,
                Unit = InferUnit(name),
                Category = FindCategory(node)
            });
        }

        if (items.Count == 0)
            throw new InvalidOperationException("The H-E-B page was reached, but no shopping-list items could be read.");

        return new HebShoppingList { Name = title, Items = items };
    }

    private static string? FindProductName(HtmlNode node)
    {
        foreach (var selector in new[] { ".//h2", ".//h3", ".//h4", ".//a[contains(@href, '/p/') ]" })
        {
            var candidate = CleanText(node.SelectSingleNode(selector)?.InnerText);
            if (IsPlausibleProductName(candidate)) return candidate;
        }

        foreach (var line in node.InnerText.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(CleanText))
            if (IsPlausibleProductName(line)) return line;

        return null;
    }

    private static string FindCategory(HtmlNode node)
    {
        for (HtmlNode? current = node.ParentNode; current != null; current = current.ParentNode)
        {
            var headings = current.SelectNodes(".//h2");
            if (headings == null) continue;
            foreach (var heading in headings)
            {
                var category = CleanText(heading.InnerText);
                if (!string.IsNullOrWhiteSpace(category) && category.Length < 80) return category;
            }
        }
        return string.Empty;
    }

    private static bool IsPlausibleProductName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2 || text.Length > 250) return false;
        if (text.Contains("Qty:", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.StartsWith("Select ", StringComparison.OrdinalIgnoreCase)) text = text[7..];
        if (Regex.IsMatch(text, "^\\$?\\d+(?:\\.\\d+)?(?:\\s|$)")) return false;
        if (text.Contains("SNAP EBT", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Contains("Aisle ", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string CleanProductName(string value)
    {
        value = CleanText(value) ?? string.Empty;
        return Regex.Replace(value, "^Select\\s+", string.Empty, RegexOptions.IgnoreCase).Trim();
    }

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Regex.Replace(WebUtility.HtmlDecode(value), "\\s+", " ").Trim();
    }

    private static string InferUnit(string name)
    {
        if (Regex.IsMatch(name, "\\b(?:lb|lbs|pound|pounds)\\b", RegexOptions.IgnoreCase)) return "lbs";
        if (Regex.IsMatch(name, "\\b(?:oz|ounce|ounces)\\b", RegexOptions.IgnoreCase)) return "oz";
        if (Regex.IsMatch(name, "\\b(?:gal|gallon|gallons)\\b", RegexOptions.IgnoreCase)) return "gal";
        if (Regex.IsMatch(name, "\\b(?:qt|quart|quarts)\\b", RegexOptions.IgnoreCase)) return "qt";
        return "pcs";
    }
}
