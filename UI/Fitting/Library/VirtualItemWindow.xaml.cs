using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using ShipAutoCadPlugin.Services;

namespace ShipAutoCadPlugin.UI
{
    public partial class VirtualItemWindow : Window
    {
        private AutoCadService _acService;
        private AutoCadService.CatalogItem _draftItem;
        private readonly string _masterCatalogPath = @"C:\Temp_BIM_Library\MasterCatalog.json";

        public VirtualItemWindow(AutoCadService service, AutoCadService.CatalogItem draftItem)
        {
            InitializeComponent();
            _acService = service;
            _draftItem = draftItem;

            LoadDraftDataToUI();
        }

        private void LoadDraftDataToUI()
        {
            if (_draftItem == null) return;

            // 1. Đổ dữ liệu Hình học (Read-only)
            // Hiển thị BlockName (nếu là block) hoặc EntityType
            TxtEntityType.Text = _draftItem.EntityType == "Block" ? $"Block: {_draftItem.BlockName}" : _draftItem.EntityType;
            TxtLayer.Text = _draftItem.TriggerLayer;
            TxtColor.Text = _draftItem.TriggerColor;
            TxtUoM.Text = _draftItem.UoM;

            // 2. Gợi ý dữ liệu Metadata (Nếu có)
            TxtPartID.Text = _draftItem.PartNumber; // [CẬP NHẬT]: Điền sẵn mã CAS bóc được từ Block
            TxtDesc.Text = _draftItem.Description; 
            TxtMass.Text = _draftItem.Mass ?? "0";

            if (!string.IsNullOrEmpty(_draftItem.BomType))
            {
                foreach (ComboBoxItem item in CboBomType.Items)
                {
                    if (item.Content.ToString().Equals(_draftItem.BomType, StringComparison.OrdinalIgnoreCase))
                    {
                        CboBomType.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtPartID.Text))
            {
                MessageBox.Show("Part ID is required!", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPartID.Focus();
                return;
            }

            _draftItem.PartNumber = TxtPartID.Text.Trim();
            _draftItem.Title = TxtTitle.Text.Trim();
            _draftItem.Description = TxtDesc.Text.Trim();
            _draftItem.Mass = TxtMass.Text.Trim();

            if (CboBomType.SelectedItem is ComboBoxItem selectedType)
            {
                _draftItem.BomType = selectedType.Content.ToString();
            }
            else
            {
                _draftItem.BomType = "DETAIL"; 
            }
            
            // [QUAN TRỌNG]: Nếu là Block thì phải giữ lại thuộc tính BlockName
            // Chỉ xóa BlockName nếu nó là các nét vẽ (Line, Polyline...)
            if (_draftItem.EntityType != "Block") 
            {
                _draftItem.BlockName = ""; 
            }
            _draftItem.FilePath = ""; 

            List<AutoCadService.CatalogItem> itemsToSave = new List<AutoCadService.CatalogItem> { _draftItem };

            try
            {
                var result = _acService.AddItemsToProjectCatalog(_masterCatalogPath, itemsToSave);
                
                string action = result.Item1 > 0 ? "Added new" : "Updated existing";
                MessageBox.Show($"{action} Item [{_draftItem.PartNumber}] to Master Library successfully!", "Save Success", MessageBoxButton.OK, MessageBoxImage.Information);
                
                this.DialogResult = true; 
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save to Master Library:\n{ex.Message}", "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}