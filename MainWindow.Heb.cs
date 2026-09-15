using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace PersonalInventory;

public partial class MainWindow
{
    private readonly IHebShoppingListImporter _hebImporter = new HebShoppingListImporter();

    private async void BtnImportHeb_Click(object sender, RoutedEventArgs e)
    {
        var url = TxtHebUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Paste an H-E-B shared shopping-list URL first.", "H-E-B Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnImportHeb.IsEnabled = false;
        BtnImportHeb.Content = "Opening H-E-B...";
        try
        {
            var list = await ImportHebWithBrowserAsync(url);
            var preview = new HebImportWindow(list, this);
            if (preview.ShowDialog() != true) return;

            foreach (var row in preview.SelectedRows)
            {
                var existing = _items.FirstOrDefault(i =>
                    i.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase) &&
                    i.Location == row.Location);

                if (existing != null)
                {
                    existing.IsMarkedForShopping = true;
                }
                else
                {
                    _items.Add(new InventoryItem
                    {
                        Name = row.Name,
                        Location = row.Location,
                        Quantity = 0,
                        Unit = "pcs",
                        MinimumThreshold = 0,
                        IsMarkedForShopping = true
                    });
                }
            }

            _repository.Save(_items);
            RefreshGrid(_items);
            MessageBox.Show($"Imported {preview.SelectedRows.Count} item(s) from H-E-B.", "H-E-B Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"H-E-B import failed:\n\n{ex.Message}", "H-E-B Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnImportHeb.Content = "Import H-E-B List";
            BtnImportHeb.IsEnabled = true;
        }
    }

    private async Task<HebShoppingList> ImportHebWithBrowserAsync(string url)
    {
        var webView = new WebView2();
        var host = new Window
        {
            Title = "H-E-B Shopping List",
            Width = 1100,
            Height = 750,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = true,
            ShowActivated = true,
            Owner = this,
            Content = webView
        };

        try
        {
            await webView.EnsureCoreWebView2Async();

            var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
            {
                navigation.TrySetResult(args.IsSuccess);
            }

            webView.NavigationCompleted += OnNavigationCompleted;
            host.Show();
            webView.CoreWebView2.Navigate(url);

            if (!await navigation.Task.WaitAsync(TimeSpan.FromSeconds(45)))
                throw new InvalidOperationException("H-E-B did not finish loading within 45 seconds.");

            webView.NavigationCompleted -= OnNavigationCompleted;

            var hasItems = false;
            for (var attempt = 0; attempt < 240; attempt++)
            {
                var readyJson = await webView.CoreWebView2.ExecuteScriptAsync(
                    "document.body && document.body.innerText.includes('Qty:')");
                if (readyJson.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    hasItems = true;
                    break;
                }

                await Task.Delay(500);
            }

            if (!hasItems)
                throw new InvalidOperationException("H-E-B did not expose the shopping-list items. Please finish any page verification and try again.");

            var htmlJson = await webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            var html = JsonSerializer.Deserialize<string>(htmlJson);
            if (string.IsNullOrWhiteSpace(html))
                throw new InvalidOperationException("H-E-B loaded, but no page content was returned.");

            using var client = new HttpClient(new StaticHtmlHandler(html));
            var browserImporter = new HebShoppingListImporter(client);
            return await browserImporter.ImportAsync(url);
        }
        finally
        {
            host.Close();
        }
    }

    private sealed class StaticHtmlHandler : HttpMessageHandler
    {
        private readonly string _html;
        public StaticHtmlHandler(string html) => _html = html;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(_html, Encoding.UTF8, "text/html")
            };
            return Task.FromResult(response);
        }
    }
}
