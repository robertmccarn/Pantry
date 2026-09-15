using System.Windows;

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
        try
        {
            var list = await _hebImporter.ImportAsync(url);
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
            BtnImportHeb.IsEnabled = true;
        }
    }
}
