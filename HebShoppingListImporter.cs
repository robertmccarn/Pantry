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

        var title = CleanText(document.DocumentNode.SelectSingleNode("//h1")?.InnerText)
                    ?? "H-E-B Shopping List";

        var items = new List<HebShoppingListItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // H-E-B product names are links to /product-detail/ product pages. This is much
        // safer than treating arbitrary DOM text or category headings as items.
        var productLinks = document.DocumentNode.SelectNodes(
            "//a[contains(@href, '/product-detail/') and normalize-space(string(.)) != '']");

        if (productLinks != null)
        {
            foreach (var link in productLinks)
            {
                var name = CleanProductName(CleanText(link.InnerText) ?? string.Empty);
                if (!IsPlausibleProductName(name) || !seen.Add(name))
                    continue;

                var card = FindProductCard(link);
                var cardText = card == null ? string.Empty : CleanText(card.InnerText) ?? string.Empty;

                var quantity = 1d;
                var quantityMatch = QuantityRegex.Match(cardText);
                if (quantityMatch.Success)
                {
                    var quantityText = quantityMatch.Groups[1].Value.Replace(',', '.');
                    if (!double.TryParse(quantityText, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity))
                        quantity = 1;
                }

                items.Add(new HebShoppingListItem
                {
                    Name = name,
                    Quantity = quantity,
                    Unit = InferUnit(name),
                    Category = FindCategory(card ?? link)
                });
            }
        }

        // Fallback for any future H-E-B markup where product links are not
        // present, but only accept text that is clearly product-like. Never
        // use category headings as product names.
        if (items.Count == 0)
        {
            var quantityNodes = document.DocumentNode.SelectNodes(
                "//*[contains(normalize-space(.), 'Qty:') and not(.//*[contains(normalize-space(.), 'Qty:')])]");

            if (quantityNodes != null)
            {
                foreach (var node in quantityNodes)
                {
                    var card = FindProductCard(node);
                    var name = card == null ? null : FindProductLinkName(card);

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    name = CleanProductName(name);
                    if (!IsPlausibleProductName(name) || !seen.Add(name))
                        continue;

                    var text = CleanText(node.InnerText) ?? string.Empty;
                    var quantityMatch = QuantityRegex.Match(text);
                    var quantity = 1d;
                    if (quantityMatch.Success)
                    {
                        var quantityText = quantityMatch.Groups[1].Value.Replace(',', '.');
                        if (!double.TryParse(quantityText, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity))
                            quantity = 1;
                    }

                    items.Add(new HebShoppingListItem
                    {
                        Name = name,
                        Quantity = quantity,
                        Unit = InferUnit(name),
                        Category = FindCategory(card ?? node)
                    });
                }
            }
        }

        if (items.Count == 0)
            throw new InvalidOperationException(
                "The H-E-B page was reached, but no shopping-list products could be read.");

        return new HebShoppingList { Name = title, Items = items };
    }

    private static HtmlNode? FindProductCard(HtmlNode node)
    {
        for (var current = node; current != null; current = current.ParentNode)
        {
            var text = CleanText(current.InnerText) ?? string.Empty;
            if (QuantityRegex.IsMatch(text) && ContainsProductLink(current))
                return current;

            if (current.ParentNode == null ||
                current.ParentNode.Name.Equals("body", StringComparison.OrdinalIgnoreCase))
                break;
        }

        return null;
    }

    private static bool ContainsProductLink(HtmlNode node) =>
        node.SelectSingleNode(".//a[contains(@href, '/product-detail/') and normalize-space(string(.)) != '']") != null;

    private static string? FindProductLinkName(HtmlNode node)
    {
        var link = node.SelectSingleNode(
            ".//a[contains(@href, '/product-detail/') and normalize-space(string(.)) != '']");
        return CleanText(link?.InnerText);
    }

    private static string FindCategory(HtmlNode node)
    {
        // The H-E-B category heading is a preceding section heading, not the
        // product's own name. Look for the nearest preceding h2 as we climb.
        for (var current = node; current != null; current = current.ParentNode)
        {
            var heading = current.SelectSingleNode("./preceding-sibling::h2[1]");
            var category = CleanText(heading?.InnerText);
            if (!string.IsNullOrWhiteSpace(category) && category.Length < 80)
                return category;
        }

        return string.Empty;
    }

    private static bool IsPlausibleProductName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2 || text.Length > 250)
            return false;

        if (text.Equals("Bakery & bread", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Beverages", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Dairy & eggs", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Deli & prepared food", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Everyday essentials", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Frozen food", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Fruit & vegetables", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Meat & seafood", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Pantry", StringComparison.OrdinalIgnoreCase))
            return false;

        if (text.Contains("Qty:", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.StartsWith("Select ", StringComparison.OrdinalIgnoreCase)) text = text[7..];
        if (Regex.IsMatch(text, "^\\$?\\d+(?:\\.\\d+)?(?:\\s|$)")) return false;
        if (text.Contains("SNAP EBT", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Contains("Aisle ", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Contains("In Produce", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Contains("In Meat Market", StringComparison.OrdinalIgnoreCase)) return false;
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