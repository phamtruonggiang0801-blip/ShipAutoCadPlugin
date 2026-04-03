using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Microsoft.Win32;
using ShipAutoCadPlugin.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace ShipAutoCadPlugin.UI
{
    public class CategoryNode
    {
        public string CategoryName { get; set; }
        public string CountLabel { get; set; }
        public List<CategoryNode> Children { get; set; }
        public List<AutoCadService.CatalogItem> Items { get; set; }

        public CategoryNode()
        {
            Children = new List<CategoryNode>();
            Items = new List<AutoCadService.CatalogItem>();
        }
    }

    public partial class FittingLibraryWindow : Window
    {
        private AutoCadService _acService;
        private List<AutoCadService.CatalogItem> _fullCatalog;
        private string _libraryPath = @"C:\Temp_BIM_Library";
        
        private string _currentProjectFile = "";

        public FittingLibraryWindow(AutoCadService service)
        {
            InitializeComponent();
            _acService = service;
            LoadCatalog(Path.Combine(_libraryPath, "MasterCatalog.json"));
            
            ColProjectPos.IsReadOnly = true; 
            BtnAutoAssignPos.IsEnabled = false; 
        }

        private void LoadCatalog(string catalogFilePath)
        {
            _fullCatalog = new List<AutoCadService.CatalogItem>();
            if (File.Exists(catalogFilePath))
            {
                try
                {
                    string json = File.ReadAllText(catalogFilePath);
                    _fullCatalog = JsonConvert.DeserializeObject<List<AutoCadService.CatalogItem>>(json) ?? new List<AutoCadService.CatalogItem>();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Cannot load library: " + ex.Message);
                }
            }
            
            BuildCategoryTree();
            ApplyFilters();
        }

        private void BuildCategoryTree()
        {
            var rootNodes = new List<CategoryNode>();
            
            var allNode = new CategoryNode { 
                CategoryName = "All Fittings", 
                CountLabel = $"({_fullCatalog.Count})", 
                Items = _fullCatalog 
            };
            rootNodes.Add(allNode);

            var bomGroups = _fullCatalog
                .GroupBy(x => 
                {
                    if (string.IsNullOrWhiteSpace(x.BomType)) return "Uncategorized (Legacy)";
                    
                    string type = x.BomType.ToUpper();
                    if (type == "PANEL") return "Fitting In Panel";
                    if (type == "DETAIL" || type == "HULL") return "Fitting In Detail";
                    
                    return "Uncategorized (Legacy)";
                })
                .OrderBy(g => g.Key);

            foreach (var bg in bomGroups)
            {
                var bomNode = new CategoryNode
                {
                    CategoryName = bg.Key,
                    CountLabel = $"({bg.Count()})",
                    Items = bg.ToList() 
                };

                var catGroups = bg.GroupBy(x => string.IsNullOrWhiteSpace(x.Title) ? "Uncategorized" : x.Title.Trim())
                                  .OrderBy(g => g.Key);

                foreach (var cg in catGroups)
                {
                    var catNode = new CategoryNode
                    {
                        CategoryName = cg.Key,
                        CountLabel = $"({cg.Count()})",
                        Items = cg.ToList() 
                    };
                    
                    bomNode.Children.Add(catNode);
                }

                rootNodes.Add(bomNode);
            }

            TreeCategories.ItemsSource = rootNodes;
        }

        private void TreeCategories_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            ApplyFilters();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (_fullCatalog == null) return;

            IEnumerable<AutoCadService.CatalogItem> sourceList = _fullCatalog;
            if (TreeCategories.SelectedItem is CategoryNode selectedNode && selectedNode.CategoryName != "All Fittings")
            {
                sourceList = selectedNode.Items;
            }

            string searchText = TxtSearch.Text?.ToLower() ?? "";
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                GridCatalog.ItemsSource = sourceList.ToList();
                return;
            }

            string[] keywords = searchText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var filtered = sourceList.Where(i => 
                keywords.All(kw => 
                    (i.PartNumber != null && i.PartNumber.ToLower().Contains(kw)) || 
                    (i.BlockName != null && i.BlockName.ToLower().Contains(kw)) ||
                    (i.Description != null && i.Description.ToLower().Contains(kw)) ||
                    (i.Title != null && i.Title.ToLower().Contains(kw)) ||
                    (i.Designer != null && i.Designer.ToLower().Contains(kw)) ||
                    (i.EntityType != null && i.EntityType.ToLower().Contains(kw)) // Tìm thêm theo loại (Line, Block...)
                )
            ).ToList();

            GridCatalog.ItemsSource = filtered;
        }

        // ====================================================================
        // [BƯỚC 5]: TÍCH HỢP TÍNH NĂNG VIRTUAL BOM VÀ GEOMETRIC
        // ====================================================================

        // ====================================================================
        // [PHƯƠNG ÁN CUỐI CÙNG]: Dùng Application.Idle để thoát hoàn toàn WPF Thread
        // ====================================================================
        private void BtnAddFromCad_Click(object sender, RoutedEventArgs e)
        {
            if (RadioProjectMode.IsChecked == true)
            {
                MessageBox.Show("Please switch to 'Master Library' mode to add new fittings.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 1. Ẩn cửa sổ WPF ngay lập tức
            this.Hide();

            // 2. Trả Focus về màn hình vẽ của AutoCAD
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            // 3. Đặt lịch hẹn: "Khi nào AutoCAD rảnh rỗi (Idle), hãy chạy hàm OnAutoCadIdle"
            Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnAutoCadIdle;
            
            // Hàm Click kết thúc tại đây! WPF không còn giữ luồng nữa.
        }

        // 4. Hàm này sẽ được AutoCAD tự động gọi khi nó đã rảnh rỗi 100%
        private void OnAutoCadIdle(object sender, EventArgs e)
        {
            // CỰC KỲ QUAN TRỌNG: Hủy đăng ký ngay lập tức để hàm này chỉ chạy 1 lần duy nhất
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnAutoCadIdle;

            try
            {
                // Lúc này, AutoCAD đang hoàn toàn nắm quyền, gọi hàm Pick sẽ cực kỳ mượt mà
                var draftItem = _acService.PickGeometricFeatureFromCad();

                if (draftItem != null)
                {
                    VirtualItemWindow virtualWin = new VirtualItemWindow(_acService, draftItem);
                    virtualWin.Owner = this; 
                    if (virtualWin.ShowDialog() == true)
                    {
                        LoadCatalog(System.IO.Path.Combine(_libraryPath, "MasterCatalog.json"));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error picking object: " + ex.Message, "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Dù thành công hay văng lỗi, phải hiển thị lại cửa sổ Thư viện
                this.Show();
            }
        }

        private void BtnManageAccessory_Click(object sender, RoutedEventArgs e)
        {
            if (RadioProjectMode.IsChecked == true)
            {
                MessageBox.Show("Accessory management must be done in 'Master Library' mode.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedItems = GridCatalog.SelectedItems.Cast<AutoCadService.CatalogItem>().ToList();
            
            if (selectedItems.Count != 1)
            {
                MessageBox.Show("Please select EXACTLY ONE Fitting from the Master Library to manage its accessories.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedFitting = selectedItems[0];

            try
            {
                AccessoryManagerWindow accWin = new AccessoryManagerWindow(_acService, selectedFitting);
                if (accWin.ShowDialog() == true)
                {
                    LoadCatalog(Path.Combine(_libraryPath, "MasterCatalog.json"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening Accessory Manager: " + ex.Message, "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ====================================================================
        // CÁC HÀM CŨ ĐƯỢC GIỮ NGUYÊN
        // ====================================================================

        private void BtnInsert_Click(object sender, RoutedEventArgs e)
        {
            InsertSelected();
        }

        private void GridCatalog_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ColProjectPos.IsReadOnly == false && GridCatalog.CurrentColumn == ColProjectPos) return;
            InsertSelected();
        }

        private void InsertSelected()
        {
            var selectedItems = GridCatalog.SelectedItems.Cast<AutoCadService.CatalogItem>().ToList();
            if (selectedItems == null || selectedItems.Count == 0) return;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try
            {
                foreach (var item in selectedItems)
                {
                    // Chặn không cho Insert các vật tư tuyến tính hoặc phụ kiện (Chỉ Block mới Insert được)
                    if (item.EntityType != "Block")
                    {
                        MessageBox.Show($"Item '{item.PartNumber}' is a {item.EntityType} and cannot be inserted as a Block.", "Action Restricted", MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }
                    _acService.InsertBlockFromLibrary(item.FilePath, item.BlockName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Insert failed: " + ex.Message);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (RadioProjectMode.IsChecked == true && !string.IsNullOrEmpty(_currentProjectFile))
                LoadCatalog(_currentProjectFile);
            else
                LoadCatalog(Path.Combine(_libraryPath, "MasterCatalog.json"));
        }

        private void BtnUpdateLibrary_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            ObjectId selectedId = ObjectId.Null;

            PromptSelectionResult psr = ed.SelectImplied();
            if (psr.Status == PromptStatus.OK && psr.Value.Count > 0)
            {
                selectedId = psr.Value[0].ObjectId;
            }
            else
            {
                this.Visibility = System.Windows.Visibility.Hidden;
                PromptEntityOptions peo = new PromptEntityOptions("\n[Fitting Library] Select the updated Fitting Block to push to library: ");
                peo.SetRejectMessage("\nMust be a Block Reference.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                
                PromptEntityResult res = ed.GetEntity(peo);
                if (res.Status == PromptStatus.OK) selectedId = res.ObjectId;
            }

            if (selectedId != ObjectId.Null)
            {
                try
                {
                    using (Transaction tr = doc.TransactionManager.StartTransaction())
                    {
                        BlockReference blkRef = tr.GetObject(selectedId, OpenMode.ForRead) as BlockReference;
                        if (blkRef != null)
                        {
                            string blockName = blkRef.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(blkRef.DynamicBlockTableRecord, OpenMode.ForRead)).Name : blkRef.Name;
                            var catalogItem = _fullCatalog?.FirstOrDefault(x => x.BlockName.Equals(blockName, StringComparison.OrdinalIgnoreCase));
                            
                            if (catalogItem != null)
                            {
                                string exportPath = Path.Combine(_libraryPath, blockName + ".dwg");
                                ObjectId btrId = blkRef.IsDynamicBlock ? blkRef.DynamicBlockTableRecord : blkRef.BlockTableRecord;

                                using (Database exportDb = db.Wblock(btrId))
                                {
                                    exportDb.Insunits = UnitsValue.Millimeters;
                                    if (File.Exists(exportPath)) File.Delete(exportPath);
                                    exportDb.SaveAs(exportPath, DwgVersion.Current);
                                }
                                MessageBox.Show($"Successfully updated geometry for '{blockName}'.", "Library Updated", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                MessageBox.Show($"Block '{blockName}' is not part of the current Catalog.", "Invalid Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        tr.Commit();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to push update: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            this.Visibility = System.Windows.Visibility.Visible;
            ed.SetImpliedSelection(new ObjectId[0]);
        }
        
        private void RadioMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return; 
            
            if (RadioMasterMode.IsChecked == true)
            {
                if (BtnAddToProject != null) BtnAddToProject.IsEnabled = true; 
                if (ColProjectPos != null) ColProjectPos.IsReadOnly = true; 
                if (BtnAutoAssignPos != null) BtnAutoAssignPos.IsEnabled = false; 
                
                // Mở khóa các tính năng của Master
                if (BtnAddFromCad != null) BtnAddFromCad.IsEnabled = true;
                if (BtnManageAccessory != null) BtnManageAccessory.IsEnabled = true;

                LoadCatalog(Path.Combine(_libraryPath, "MasterCatalog.json"));
            }
            else if (RadioProjectMode.IsChecked == true)
            {
                if (BtnAddToProject != null) BtnAddToProject.IsEnabled = false; 
                
                // Khóa các tính năng sửa đổi Thư viện Master
                if (BtnAddFromCad != null) BtnAddFromCad.IsEnabled = false;
                if (BtnManageAccessory != null) BtnManageAccessory.IsEnabled = false;

                if (string.IsNullOrEmpty(_currentProjectFile))
                {
                    MessageBox.Show("Please Load or Create a Project Library first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    RadioMasterMode.IsChecked = true; 
                }
                else
                {
                    if (ColProjectPos != null) ColProjectPos.IsReadOnly = false; 
                    if (BtnAutoAssignPos != null) BtnAutoAssignPos.IsEnabled = true; 
                    LoadCatalog(_currentProjectFile);
                }
            }
        }

        private void BtnLoadProject_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Select Project Library";
            ofd.Filter = "JSON Files (*.json)|*.json";

            if (ofd.ShowDialog() == true)
            {
                _currentProjectFile = ofd.FileName;
                TxtCurrentProject.Text = Path.GetFileNameWithoutExtension(_currentProjectFile);
                RadioProjectMode.IsChecked = true; 
                ColProjectPos.IsReadOnly = false; 
                BtnAutoAssignPos.IsEnabled = true;
                LoadCatalog(_currentProjectFile);
            }
        }

        private void BtnCreateProject_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Title = "Create New Project Library";
            sfd.Filter = "JSON Files (*.json)|*.json";
            sfd.FileName = "New_Project_Catalog.json";

            if (sfd.ShowDialog() == true)
            {
                _currentProjectFile = sfd.FileName;
                TxtCurrentProject.Text = Path.GetFileNameWithoutExtension(_currentProjectFile);
                File.WriteAllText(_currentProjectFile, "[]");
                RadioProjectMode.IsChecked = true; 
                ColProjectPos.IsReadOnly = false; 
                BtnAutoAssignPos.IsEnabled = true;
                LoadCatalog(_currentProjectFile);
                MessageBox.Show($"Project Library '{TxtCurrentProject.Text}' created successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnAddToProject_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentProjectFile) || !File.Exists(_currentProjectFile))
            {
                MessageBox.Show("Please load or create a Project Library first.", "Action Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedItems = GridCatalog.SelectedItems.Cast<AutoCadService.CatalogItem>().ToList();
            if (selectedItems.Count == 0) return;

            try
            {
                var stats = _acService.AddItemsToProjectCatalog(_currentProjectFile, selectedItems);
                MessageBox.Show($"Successfully added {stats.Item1 + stats.Item2} items to project '{TxtCurrentProject.Text}'.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add to project: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GridCatalog_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column == ColProjectPos && RadioProjectMode.IsChecked == true && !string.IsNullOrEmpty(_currentProjectFile))
            {
                var editedItem = e.Row.Item as AutoCadService.CatalogItem;
                if (editedItem != null)
                {
                    TextBox t = e.EditingElement as TextBox;  
                    string newValue = t.Text;

                    if (editedItem.ProjectPosNum == newValue) return;

                    editedItem.ProjectPosNum = newValue;
                    try
                    {
                        string newJson = JsonConvert.SerializeObject(_fullCatalog, Formatting.Indented);
                        File.WriteAllText(_currentProjectFile, newJson);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to save Position: " + ex.Message, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = GridCatalog.SelectedItems.Cast<AutoCadService.CatalogItem>().ToList();
            if (selectedItems.Count == 0) return;

            if (RadioProjectMode.IsChecked == true)
            {
                if (MessageBox.Show($"Remove {selectedItems.Count} item(s) from the Project Library?", "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    foreach (var item in selectedItems) _fullCatalog.Remove(item);
                    File.WriteAllText(_currentProjectFile, JsonConvert.SerializeObject(_fullCatalog, Formatting.Indented));
                    BuildCategoryTree();
                    ApplyFilters();
                }
            }
            else
            {
                if (MessageBox.Show($"WARNING: You are about to remove {selectedItems.Count} item(s) from the MASTER Library.\n\nThis will affect all future projects. Do you want to proceed?", "CRITICAL WARNING", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    foreach (var item in selectedItems) _fullCatalog.Remove(item);
                    File.WriteAllText(Path.Combine(_libraryPath, "MasterCatalog.json"), JsonConvert.SerializeObject(_fullCatalog, Formatting.Indented));
                    BuildCategoryTree();
                    ApplyFilters();
                    MessageBox.Show("Items removed from Master Library.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnAutoAssignPos_Click(object sender, RoutedEventArgs e)
        {
            if (RadioProjectMode.IsChecked != true || string.IsNullOrEmpty(_currentProjectFile)) return;

            var detailFittings = _fullCatalog.Where(x => 
                !string.IsNullOrWhiteSpace(x.BomType) && 
                (x.BomType.ToUpper() == "DETAIL" || x.BomType.ToUpper() == "HULL")
            ).ToList();

            if (detailFittings.Count == 0) return;

            var groupedByPartId = detailFittings
                .Where(x => !string.IsNullOrEmpty(x.PartNumber))
                .GroupBy(x => x.PartNumber)
                .OrderBy(g => g.Key)
                .ToList();

            int posCounter = 1;
            int updatedCount = 0;

            foreach (var group in groupedByPartId)
            {
                string posString = posCounter.ToString("D3");
                foreach (var item in group)
                {
                    item.ProjectPosNum = posString;
                    updatedCount++;
                }
                posCounter++;
            }

            try
            {
                File.WriteAllText(_currentProjectFile, JsonConvert.SerializeObject(_fullCatalog, Formatting.Indented));
                GridCatalog.Items.Refresh(); 
                MessageBox.Show($"Auto-assigned positions for {groupedByPartId.Count} unique Part IDs ({updatedCount} items total).", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save auto-assigned positions: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}