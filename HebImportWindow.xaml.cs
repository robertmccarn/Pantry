using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace PersonalInventory;

public sealed class HebImportRow : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string Name { get; init; } = string.Empty;
    public double Quantity { get; init; }
    public string Category { get; init; } = string.Empty;
    public StorageLocation Location { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class HebImportWindow : Window
{
    public IReadOnlyList<StorageLocation> Locations { get; } = Enum.GetValues<StorageLocation>();
    public ObservableCollection<HebImportRow> Rows { get; } = new();
    public List<HebImportRow> SelectedRows { get; private set; } = new();

    public HebImportWindow(HebShoppingList list, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = this;
        TxtListTitle.Text = list.Name;

        foreach (var item in list.Items)
        {
            var row = new HebImportRow
            {
                Name = item.Name,
                Quantity = item.Quantity,
                Category = item.Category,
                Location = SuggestLocation(item)
            };
            row.PropertyChanged += ImportRow_PropertyChanged;
            Rows.Add(row);
        }

        GridImport.ItemsSource = Rows;
        UpdateProgress();
    }

    private void ImportRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HebImportRow.IsSelected))
            UpdateProgress();
    }

    private void UpdateProgress()
    {
        var total = Rows.Count;
        var selected = Rows.Count(x => x.IsSelected);
        ImportProgress.Value = total == 0 ? 0 : selected * 100.0 / total;
        TxtProgressCount.Text = $"{selected}/{total}";
        TxtItemCount.Text = total == 1
            ? "1 item found. Select the items you want to add to your shopping list."
            : $"{total} items found. Select the items you want to add to your shopping list.";
    }

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
        // Finish the current edit so checkbox changes are pushed to the row.
        GridImport.CommitEdit(DataGridEditingUnit.Cell, true);
        GridImport.CommitEdit(DataGridEditingUnit.Row, true);

        SelectedRows = Rows.Where(x => x.IsSelected).ToList();
        if (SelectedRows.Count == 0)
        {
            MessageBox.Show("Select at least one item to import.", "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        SelectedRows.Clear();
        DialogResult = false;
    }
}