using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ShipAutoCadPlugin.Services;

namespace ShipAutoCadPlugin.UI
{
    public partial class NewAccessoryWindow : Window
    {
        private AutoCadService _acService;
        private readonly string _masterCatalogPath = @"C:\Temp_BIM_Library\MasterCatalog.json";
        public string CreatedPartId { get; private set; } 

        public NewAccessoryWindow(AutoCadService service)
        {
            InitializeComponent();
            _acService = service;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtPartID.Text))
            {
                MessageBox.Show("Part ID is required!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPartID.Focus();
                return;
            }

            CreatedPartId = TxtPartID.Text.Trim();

            // Đọc giá trị BOM Type từ ComboBox
            string bomType = "DETAIL";
            if (CboBomType.SelectedItem is ComboBoxItem selectedType)
            {
                bomType = selectedType.Content.ToString();
            }

            // Tạo CatalogItem thuần túy (Accessory)
            var newItem = new AutoCadService.CatalogItem
            {
                PartNumber = CreatedPartId,
                Description = TxtDesc.Text.Trim(),
                
                Title = string.IsNullOrWhiteSpace(TxtXClass.Text) ? "Accessory" : TxtXClass.Text.Trim(), 
                
                // [CẬP NHẬT MỚI]: Gán BOM Type
                BomType = bomType,
                
                EntityType = "Accessory", 
                UoM = "pcs",
                BlockName = ""
            };

            try
            {
                _acService.AddItemsToProjectCatalog(_masterCatalogPath, new List<AutoCadService.CatalogItem> { newItem });
                this.DialogResult = true; // Báo thành công
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving accessory: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}