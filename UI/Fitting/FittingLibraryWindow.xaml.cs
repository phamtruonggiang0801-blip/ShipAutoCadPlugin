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
            BtnAutoAssignPos.IsEnabled = false; // Mặc định tắt
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
                    (i.Designer != null && i.Designer.ToLower().Contains(kw))
                )
            ).ToList();

            GridCatalog.ItemsSource = filtered;
        }

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
                if (res.Status == PromptStatus.OK)
                {
                    selectedId = res.ObjectId;
                }
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
                            string blockName = blkRef.IsDynamicBlock ? 
                                ((BlockTableRecord)tr.GetObject(blkRef.DynamicBlockTableRecord, OpenMode.ForRead)).Name : 
                                blkRef.Name;

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
        
        // ====================================================================
        // [CẬP NHẬT]: Điều phối trạng thái Enable/Disable các nút theo Mode
        // ====================================================================
        private void RadioMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return; 
            
            if (RadioMasterMode.IsChecked == true)
            {
                if (BtnAddToProject != null) BtnAddToProject.IsEnabled = true; 
                if (ColProjectPos != null) ColProjectPos.IsReadOnly = true; 
                if (BtnAutoAssignPos != null) BtnAutoAssignPos.IsEnabled = false; // Tắt Auto-Assign ở Master
                
                LoadCatalog(Path.Combine(_libraryPath, "MasterCatalog.json"));
            }
            else if (RadioProjectMode.IsChecked == true)
            {
                if (BtnAddToProject != null) BtnAddToProject.IsEnabled = false; 
                
                if (string.IsNullOrEmpty(_currentProjectFile))
                {
                    MessageBox.Show("Please Load or Create a Project Library first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    RadioMasterMode.IsChecked = true; 
                }
                else
                {
                    if (ColProjectPos != null) ColProjectPos.IsReadOnly = false; 
                    if (BtnAutoAssignPos != null) BtnAutoAssignPos.IsEnabled = true; // Bật Auto-Assign ở Project
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
                
                MessageBox.Show($"Project Library '{TxtCurrentProject.Text}' created successfully. You can now switch to Master Library and add fittings to this project.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnAddToProject_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentProjectFile) || !File.Exists(_currentProjectFile))
            {
                MessageBox.Show("Please load or create a Project Library using the 'Load Project' or 'Create Project' button first.", "Action Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedItems = GridCatalog.SelectedItems.Cast<AutoCadService.CatalogItem>().ToList();
            
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one Fitting to add to the project.", "No Items Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

        // ====================================================================
        // [TÍNH NĂNG MỚI 1]: Xóa dữ liệu an toàn (Delete CRUD)
        // ====================================================================
        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = GridCatalog.SelectedItems.Cast<AutoCadService.CatalogItem>().ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one Fitting to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
                // Cảnh báo đỏ khi xóa từ Master Library
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

        // ====================================================================
        // [TÍNH NĂNG MỚI 2]: Auto-Assign Pos thông minh gom nhóm theo Part ID
        // ====================================================================
        private void BtnAutoAssignPos_Click(object sender, RoutedEventArgs e)
        {
            if (RadioProjectMode.IsChecked != true || string.IsNullOrEmpty(_currentProjectFile)) return;

            // 1. Chỉ lấy đồ của Detail / Hull để đánh số
            var detailFittings = _fullCatalog.Where(x => 
                !string.IsNullOrWhiteSpace(x.BomType) && 
                (x.BomType.ToUpper() == "DETAIL" || x.BomType.ToUpper() == "HULL")
            ).ToList();

            if (detailFittings.Count == 0)
            {
                MessageBox.Show("No 'Detail' or 'Hull' fittings found in this project to assign positions.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 2. Nhóm các Fitting cùng Part ID lại với nhau
            var groupedByPartId = detailFittings
                .Where(x => !string.IsNullOrEmpty(x.PartNumber))
                .GroupBy(x => x.PartNumber)
                .OrderBy(g => g.Key)
                .ToList();

            int posCounter = 1;
            int updatedCount = 0;

            // 3. Rải số tuần tự
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

            // 4. Tự động lưu lại
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