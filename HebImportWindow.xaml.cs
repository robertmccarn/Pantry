using System.Collections.ObjectModel;
using System.Windows;

namespace PersonalInventory;

public sealed class HebImportRow
{
    public bool IsSelected { get; set; } = true;
    public string Name { get; init; } = string.Empty;
    public double Quantity { get; init; }
    public string Category { get; init; } = string.Empty;
    public StorageLocation Location { get; set; }
}

public partial class HebImportWindow : Window
{
    public IReadOnlyList<StorageLocation> Locations { get; } = Enum.GetValues<StorageLocation>();
    public ObservableCollection<HebImportRow> Rows { get; } = new();

    public HebImportWindow(HebShoppingList list, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = this;
        TxtListTitle.Text = list.Name;
        TxtItemCount.Text = $"{list.Items.Count} items found. Select the items you want to add to your shopping list.";

        foreach (var item in list.Items)
        {
            Rows.Add(new HebImportRow
            {
                Name = item.Name,
                Quantity = item.Quantity,
                Category = item.Category,
                Location = SuggestLocation(item)
            });
        }

        GridImport.ItemsSource = Rows;
    }

    public List<HebImportRow> SelectedRows => Rows.Where(x => x.IsSelected).ToList();

    private static StorageLocation SuggestLocation(HebShoppingListItem item)
    {
        var text = $"{item.Category} {item.Name}";
        if (ContainsAny(text, "frozen", "ice cream", "pizza", "freezer")) return StorageLocation.Freezer;
        if (ContainsAny(text, "produce", "meat", "dairy", "egg", "milk", "cheese", "yogurt", "butter", "deli", "refrigerated")) return StorageLocation.Fridge;
        return StorageLocation.Pantry;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (!Rows.Any(x => x.IsSelected))
        {
            MessageBox.Show("Select at least one item to import.", "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
