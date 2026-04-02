using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ShipAutoCadPlugin.Services;

namespace ShipAutoCadPlugin.UI
{
    public partial class FittingToolsView : UserControl
    {
        private AutoCadService _acService;

        public FittingToolsView()
        {
            InitializeComponent();
            _acService = new AutoCadService();
        }

        private void BtnBatchImportInventor_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Inventor Drawings (*.idw;*.dwg)|*.idw;*.dwg";
            openFileDialog.Multiselect = true; 
            openFileDialog.Title = "Select Inventor Drawings";

            if (openFileDialog.ShowDialog() == true)
            {
                string[] selectedFiles = openFileDialog.FileNames;
                if (selectedFiles.Length == 0) return;

                MessageBox.Show($"Selected {selectedFiles.Length} file(s).", "Information", MessageBoxButton.OK, MessageBoxImage.Information);

                try
                {
                    _acService.BatchProcessInventorFiles(selectedFiles);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex.Message, "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnImportJson_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "JSON Files (*.json)|*.json";
            openFileDialog.Multiselect = true; 
            openFileDialog.Title = "Select JSON files for Fittings";

            if (openFileDialog.ShowDialog() == true)
            {
                string[] selectedFiles = openFileDialog.FileNames;
                if (selectedFiles.Length == 0) return;

                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                try
                {
                    // Đọc sự lựa chọn của Leader trên giao diện WPF
                    string targetBomType = RadioPanelFitting.IsChecked == true ? "PANEL" : "DETAIL";

                    // Truyền tham số targetBomType vào lõi Harvester
                    _acService.BatchImportBimFittings(selectedFiles, targetBomType);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnOpenLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FittingLibraryWindow libraryWindow = new FittingLibraryWindow(_acService);
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(libraryWindow);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening Library: " + ex.Message, "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // SỰ KIỆN MỞ CỬA SỔ BOM PREVIEW
        // =========================================================
        private void BtnOpenBomPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Khởi tạo và hiển thị Cửa sổ BOM Preview (Modeless)
                BomPreviewWindow bomWindow = new BomPreviewWindow(_acService);
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(bomWindow);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening BOM Export window: " + ex.Message, "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // BLOCK UTILITIES EVENTS
        // =========================================================

        private void BtnRedefineBlocks_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try
            {
                _acService.RedefineBlocksFromLibrary();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSmartReplace_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try
            {
                _acService.SmartReplaceBlocks();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnChangeBasePoint_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try
            {
                _acService.ChangeBlockBasePoint();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddToBlock_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try
            {
                _acService.AddEntitiesToBlock();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExtractFromBlock_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try
            {
                _acService.ExtractEntitiesFromBlock();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}