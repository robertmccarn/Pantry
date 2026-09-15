using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace PersonalInventory
{
    public enum StorageLocation
    {
        Pantry,
        Fridge,
        Freezer
    }

    public class InventoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = string.Empty;
        public StorageLocation Location { get; set; }
        public double Quantity { get; set; }
        public string Unit { get; set; } = "pcs";
        public double MinimumThreshold { get; set; }
        public bool IsMarkedForShopping { get; set; }

        public bool NeedsRestock => Quantity <= MinimumThreshold || IsMarkedForShopping;
    }

    public interface IInventoryRepository
    {
        List<InventoryItem> Load();
        void Save(List<InventoryItem> items);
    }

    public class JsonInventoryRepository : IInventoryRepository
    {
        private readonly string _filePath;

        public JsonInventoryRepository(string filePath = "inventory.json")
        {
            _filePath = filePath;
        }

        public List<InventoryItem> Load()
        {
            if (!File.Exists(_filePath)) return new List<InventoryItem>();
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<InventoryItem>>(json) ?? new List<InventoryItem>();
        }

        public void Save(List<InventoryItem> items)
        {
            string json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
    }

    public partial class MainWindow : Window
    {
        private readonly IInventoryRepository _repository;
        private List<InventoryItem> _items;

        public MainWindow()
        {
            InitializeComponent();
            _repository = new JsonInventoryRepository();
            _items = _repository.Load();

            CmbLocation.ItemsSource = Enum.GetValues(typeof(StorageLocation));
            CmbLocation.SelectedIndex = 0;

            RefreshGrid(_items);
        }

        private void RefreshGrid(IEnumerable<InventoryItem> displayItems)
        {
            GridInventory.ItemsSource = null;
            GridInventory.ItemsSource = displayItems.ToList();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtName.Text))
            {
                MessageBox.Show("Please enter an item name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double.TryParse(TxtQuantity.Text, out double qty);
            double.TryParse(TxtThreshold.Text, out double threshold);
            var location = (StorageLocation)CmbLocation.SelectedItem;

            var existing = _items.FirstOrDefault(i => i.Name.Equals(TxtName.Text.Trim(), StringComparison.OrdinalIgnoreCase) && i.Location == location);
            if (existing != null)
            {
                existing.Quantity += qty;
            }
            else
            {
                _items.Add(new InventoryItem
                {
                    Name = TxtName.Text.Trim(),
                    Location = location,
                    Quantity = qty,
                    Unit = string.IsNullOrWhiteSpace(TxtUnit.Text) ? "pcs" : TxtUnit.Text.Trim(),
                    MinimumThreshold = threshold
                });
            }

            _repository.Save(_items);
            TxtName.Clear();
            RefreshGrid(_items);
        }

        private void BtnToggleShopping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is InventoryItem item)
            {
                item.IsMarkedForShopping = !item.IsMarkedForShopping;
                _repository.Save(_items);
                RefreshGrid(_items);
            }
        }

        private void BtnFilterAll_Click(object sender, RoutedEventArgs e) => RefreshGrid(_items);

        private void BtnFilterLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && Enum.TryParse<StorageLocation>(btn.Tag?.ToString(), out var loc))
            {
                RefreshGrid(_items.Where(i => i.Location == loc));
            }
        }

        private void BtnFilterShopping_Click(object sender, RoutedEventArgs e)
        {
            RefreshGrid(_items.Where(i => i.NeedsRestock));
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            string fileName = "Inventory_Export.csv";
            using (var writer = new StreamWriter(fileName))
            {
                writer.WriteLine("ID,Item Name,Storage Location,Current Quantity,Unit,Minimum Threshold,Marked for Shopping,Needs Restock");
                foreach (var item in _items)
                {
                    writer.WriteLine($"\"{item.Id}\",\"{item.Name}\",\"{item.Location}\",{item.Quantity},\"{item.Unit}\",{item.MinimumThreshold},{item.IsMarkedForShopping},{item.NeedsRestock}");
                }
            }
            MessageBox.Show($"Exported successfully to:\n{Path.GetFullPath(fileName)}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            App app = new App();
            app.Run(new MainWindow());
        }
    }
}
