using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Newtonsoft.Json;
using ShipAutoCadPlugin.Services;

namespace ShipAutoCadPlugin.UI
{
    public partial class AccessoryManagerWindow : Window
    {
        private AutoCadService _acService;
        private AutoCadService.CatalogItem _parentItem;
        private List<AutoCadService.CatalogItem> _fullCatalog;
        private readonly string _masterCatalogPath = @"C:\Temp_BIM_Library\MasterCatalog.json";
        private ObservableCollection<AutoCadService.AccessoryItem> _localAccessories;

        public class ComboItem
        {
            public string PartNumber { get; set; }
            public string DisplayLabel { get; set; }
        }

        public AccessoryManagerWindow(AutoCadService service, AutoCadService.CatalogItem parentItem)
        {
            InitializeComponent();
            _acService = service;
            _parentItem = parentItem;

            if (_parentItem.Accessories == null) _parentItem.Accessories = new List<AutoCadService.AccessoryItem>();
            _localAccessories = new ObservableCollection<AutoCadService.AccessoryItem>(_parentItem.Accessories);
            GridAccessories.ItemsSource = _localAccessories;

            TxtParentName.Text = $"[{_parentItem.PartNumber}] - {_parentItem.Description}";

            LoadMasterCatalog();
        }

        private void LoadMasterCatalog()
        {
            _fullCatalog = _acService.GetMasterCatalogItems();
            
            // [FIX LỖI TRÙNG LẶP]: Gộp nhóm theo PartNumber để loại bỏ các View thừa
            var distinctCatalog = _fullCatalog
                .Where(x => x.PartNumber != _parentItem.PartNumber && !string.IsNullOrEmpty(x.PartNumber))
                .GroupBy(x => x.PartNumber)
                .Select(g => g.First())
                .ToList();

            var comboList = new List<ComboItem>();
            foreach (var item in distinctCatalog)
            {
                string desc = string.IsNullOrEmpty(item.Description) ? item.Title : item.Description;
                comboList.Add(new ComboItem 
                { 
                    PartNumber = item.PartNumber, 
                    DisplayLabel = $"[{item.PartNumber}] - {desc}" 
                });
            }

            CboAccessories.ItemsSource = comboList.OrderBy(x => x.PartNumber).ToList();
        }

        // [TÍNH NĂNG MỚI]: Lọc danh sách (Search) khi Kỹ sư gõ phím vào ComboBox
        private void CboAccessories_KeyUp(object sender, KeyEventArgs e)
        {
            var cmb = sender as ComboBox;
            if (cmb == null || cmb.ItemsSource == null) return;

            // Bỏ qua các phím điều hướng để tránh lỗi focus
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter || e.Key == Key.Escape) return;

            string searchText = cmb.Text;
            var view = CollectionViewSource.GetDefaultView(cmb.ItemsSource);
            
            if (string.IsNullOrEmpty(searchText))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = item =>
                {
                    var comboItem = item as ComboItem;
                    return comboItem.DisplayLabel.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                };
                cmb.IsDropDownOpen = true; // Tự động mở dropdown khi đang search
            }
        }

        private void BtnCreateNew_Click(object sender, RoutedEventArgs e)
        {
            NewAccessoryWindow newAccWin = new NewAccessoryWindow(_acService);
            newAccWin.Owner = this; 
            
            if (newAccWin.ShowDialog() == true)
            {
                LoadMasterCatalog();
                CboAccessories.SelectedValue = newAccWin.CreatedPartId;
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            // Vì ComboBox cho phép gõ, nên Value có thể nằm ở CboAccessories.Text
            string selectedPartId = "";
            if (CboAccessories.SelectedValue != null) 
            {
                selectedPartId = CboAccessories.SelectedValue.ToString();
            }
            else if (!string.IsNullOrWhiteSpace(CboAccessories.Text))
            {
                // Nếu người dùng gõ tay mã Part ID, thử bóc tách mã ra
                var match = System.Text.RegularExpressions.Regex.Match(CboAccessories.Text, @"\[(.*?)\]");
                selectedPartId = match.Success ? match.Groups[1].Value : CboAccessories.Text.Trim();
            }

            if (string.IsNullOrEmpty(selectedPartId))
            {
                MessageBox.Show("Please select or type an accessory Part ID.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtQty.Text, out int qty) || qty <= 0)
            {
                MessageBox.Show("Quantity must be a positive integer.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existingAcc = _localAccessories.FirstOrDefault(a => a.PartId.Equals(selectedPartId, StringComparison.OrdinalIgnoreCase));
            if (existingAcc != null)
            {
                existingAcc.Quantity += qty;
                GridAccessories.Items.Refresh();
            }
            else
            {
                _localAccessories.Add(new AutoCadService.AccessoryItem { PartId = selectedPartId, Quantity = qty });
            }

            TxtQty.Text = "1"; 
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = GridAccessories.SelectedItems.Cast<AutoCadService.AccessoryItem>().ToList();
            foreach (var item in selectedItems) _localAccessories.Remove(item);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _parentItem.Accessories = _localAccessories.ToList();

            try
            {
                if (File.Exists(_masterCatalogPath))
                {
                    string json = File.ReadAllText(_masterCatalogPath);
                    var catalog = JsonConvert.DeserializeObject<List<AutoCadService.CatalogItem>>(json) ?? new List<AutoCadService.CatalogItem>();

                    var oldItem = catalog.FirstOrDefault(x => x.PartNumber == _parentItem.PartNumber);
                    if (oldItem != null) catalog.Remove(oldItem);
                    
                    catalog.Add(_parentItem);

                    File.WriteAllText(_masterCatalogPath, JsonConvert.SerializeObject(catalog, Formatting.Indented));
                    
                    this.DialogResult = true;
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}