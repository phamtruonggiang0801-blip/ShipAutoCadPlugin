using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using Microsoft.Win32;
using ClosedXML.Excel;
using Newtonsoft.Json;
using ShipAutoCadPlugin.Services;
using ShipAutoCadPlugin.Models;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

// ====================================================================
// [FIX]: Đặt bí danh (Alias) để tránh đụng độ thư viện với AutoCAD API
// ====================================================================
using DataTable = System.Data.DataTable;
using DataColumn = System.Data.DataColumn;
using DataRow = System.Data.DataRow;

namespace ShipAutoCadPlugin.UI
{
    public partial class BomPreviewWindow : Window
    {
        private AutoCadService _acService;
        private DataTable _bomDataTable;
        private List<BomHarvestRecord> _lastScanResults;

        public BomPreviewWindow(AutoCadService service)
        {
            InitializeComponent();
            _acService = service;
            InitializeEmptyGrid();
        }

        private void InitializeEmptyGrid()
        {
            _bomDataTable = new DataTable();
            _bomDataTable.Columns.Add("Vault Name", typeof(string));
            _bomDataTable.Columns.Add("Part ID", typeof(string));
            _bomDataTable.Columns.Add("XClass", typeof(string));
            _bomDataTable.Columns.Add("Description", typeof(string));
            GridBomMatrix.ItemsSource = _bomDataTable.DefaultView;
        }

        private void GridBomMatrix_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName == "Vault Name" || e.PropertyName == "Part ID" || e.PropertyName == "XClass" || e.PropertyName == "Description")
            {
                e.Column.IsReadOnly = true;
                if (e.PropertyName == "Description") e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            }
            else if (e.PropertyName.EndsWith(" Qty"))
            {
                e.Column.IsReadOnly = true; 
                Style cellStyle = new Style(typeof(DataGridCell));
                cellStyle.Setters.Add(new Setter(BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 248, 255))));
                e.Column.CellStyle = cellStyle;
            }
            else if (e.PropertyName.EndsWith(" Pos"))
            {
                e.Column.IsReadOnly = false; 
                Style cellStyle = new Style(typeof(DataGridCell));
                cellStyle.Setters.Add(new Setter(BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 250, 205))));
                e.Column.CellStyle = cellStyle;
            }
        }

        private void BtnScanDrawing_Click(object sender, RoutedEventArgs e)
        {
            // [FIXED] Chỉ định rõ System.Windows.Visibility
            this.Visibility = System.Windows.Visibility.Hidden;

            try
            {
                List<BomHarvestRecord> rawData;
                bool isInterfaceMode = RadioHull.IsChecked == true;
                string projectFile = "";

                // Yêu cầu nạp Thư viện Dự án nếu đang quét Hull (Detail)
                if (isInterfaceMode)
                {
                    OpenFileDialog ofd = new OpenFileDialog();
                    ofd.Title = "Select Project Library JSON (to load Project Pos Num)";
                    ofd.Filter = "JSON Files (*.json)|*.json";
                    if (ofd.ShowDialog() == true)
                    {
                        projectFile = ofd.FileName;
                    }
                    else
                    {
                        MessageBox.Show("No Project Library selected. Project Pos Num will be empty.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    
                    rawData = _acService.HarvestInterfaceBom();
                }
                else 
                {
                    rawData = _acService.HarvestStructureBom();
                }

                if (rawData == null || rawData.Count == 0)
                {
                    this.Visibility = System.Windows.Visibility.Visible;
                    TxtStatus.Text = "No valid blocks found or selection cancelled.";
                    return;
                }

                // VLOOKUP TỪ THƯ VIỆN: Kéo thông tin chuẩn, Lọc rác & Lấy Project Pos
                EnrichDataFromCatalog(rawData, isInterfaceMode, projectFile);

                _lastScanResults = rawData; 

                // TẠO CỘT ĐỘNG
                _bomDataTable.Clear();
                _bomDataTable.Columns.Clear();
                _bomDataTable.Columns.Add("Vault Name", typeof(string));
                _bomDataTable.Columns.Add("Part ID", typeof(string));
                _bomDataTable.Columns.Add("XClass", typeof(string));
                _bomDataTable.Columns.Add("Description", typeof(string));

                var uniqueContainers = rawData.Select(r => r.PanelName).Distinct().OrderBy(p => p).ToList();
                foreach (var container in uniqueContainers)
                {
                    _bomDataTable.Columns.Add($"{container} Qty", typeof(int));
                    _bomDataTable.Columns.Add($"{container} Pos", typeof(string));
                }

                // ĐIỀN DỮ LIỆU LÊN LƯỚI
                var groupedFittings = rawData.GroupBy(r => r.VaultName).OrderBy(g => g.Key);

                foreach (var group in groupedFittings)
                {
                    DataRow newRow = _bomDataTable.NewRow();
                    newRow["Vault Name"] = group.Key;
                    newRow["Part ID"] = group.First().PartId ?? "";
                    newRow["XClass"] = group.First().XClass ?? "N/A"; 
                    newRow["Description"] = group.First().Description ?? "Harvested from CAD";

                    foreach (var container in uniqueContainers)
                    {
                        int totalQty = group.Where(r => r.PanelName == container).Sum(r => r.Quantity);
                        
                        if (totalQty > 0) 
                        {
                            newRow[$"{container} Qty"] = totalQty;
                            
                            // Phủ Project Pos Num từ thư viện lên tất cả Detail tương ứng
                            string posNum = group.First().ProjectPosNum;
                            newRow[$"{container} Pos"] = !string.IsNullOrEmpty(posNum) ? posNum : ""; 
                            
                            // Cập nhật record thô luôn để khi Export Excel có số Pos chuẩn
                            var recordsToUpdate = group.Where(r => r.PanelName == container);
                            foreach(var r in recordsToUpdate)
                            {
                                r.Position = !string.IsNullOrEmpty(posNum) ? posNum : "";
                            }
                        }
                        else
                        {
                            newRow[$"{container} Pos"] = ""; 
                        }
                    }
                    _bomDataTable.Rows.Add(newRow);
                }

                GridBomMatrix.ItemsSource = null;
                GridBomMatrix.ItemsSource = _bomDataTable.DefaultView;
                
                string modeName = isInterfaceMode ? "Detail(s)" : "Panel(s)";
                TxtStatus.Text = $"Scan complete. Found {uniqueContainers.Count} {modeName} and {groupedFittings.Count()} unique Fitting(s).";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error during scan: " + ex.Message, "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Visibility = System.Windows.Visibility.Visible;
            }
        }

        // HÀM HELPER: VLOOKUP LẤY THÔNG TIN, LỌC RÁC & LẤY POS NUM
        private void EnrichDataFromCatalog(List<BomHarvestRecord> records, bool isInterfaceMode, string projectFile)
        {
            List<AutoCadService.CatalogItem> catalog = null;
            
            if (isInterfaceMode && !string.IsNullOrEmpty(projectFile) && File.Exists(projectFile))
            {
                try 
                {
                    string json = File.ReadAllText(projectFile);
                    catalog = JsonConvert.DeserializeObject<List<AutoCadService.CatalogItem>>(json);
                } 
                catch { }
            }
            
            if (catalog == null || catalog.Count == 0)
            {
                catalog = _acService.GetMasterCatalogItems(); 
            }

            if (catalog == null || catalog.Count == 0) return;

            for (int i = records.Count - 1; i >= 0; i--)
            {
                var record = records[i];
                string searchKey = record.VaultName?.ToUpper() ?? "";
                if (string.IsNullOrEmpty(searchKey)) continue;

                var matchedItem = catalog.FirstOrDefault(c => c.BlockName != null && c.BlockName.ToUpper().Contains(searchKey));
                
                if (matchedItem != null)
                {
                    string bType = (matchedItem.BomType ?? "").ToUpper();
                    
                    if (isInterfaceMode && bType == "PANEL")
                    {
                        records.RemoveAt(i); 
                        continue;
                    }
                    else if (!isInterfaceMode && (bType == "DETAIL" || bType == "HULL"))
                    {
                        records.RemoveAt(i); 
                        continue;
                    }

                    if (!string.IsNullOrEmpty(matchedItem.PartNumber)) record.PartId = matchedItem.PartNumber;
                    if (!string.IsNullOrEmpty(matchedItem.Description)) record.Description = matchedItem.Description;
                    if (!string.IsNullOrEmpty(matchedItem.Title)) record.XClass = matchedItem.Title;
                    
                    if (isInterfaceMode && !string.IsNullOrEmpty(matchedItem.ProjectPosNum))
                    {
                        record.ProjectPosNum = matchedItem.ProjectPosNum;
                    }
                }
            }
        }

        private void BtnAutoBalloon_Click(object sender, RoutedEventArgs e)
        {
            if (RadioHull.IsChecked == true)
            {
                MessageBox.Show("Auto-Assign is disabled in Interface (Hull Matrix) mode.\n\nPositions are Item-based and are pulled automatically from the Project Library to maintain consistency.", "Operation Restricted", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_bomDataTable.Columns.Count <= 4) return;
            int assignedCount = 0;

            foreach (DataColumn col in _bomDataTable.Columns)
            {
                if (col.ColumnName.EndsWith(" Qty"))
                {
                    string containerName = col.ColumnName.Replace(" Qty", "");
                    string posColName = $"{containerName} Pos";
                    int posCounter = 2; 

                    foreach (DataRow row in _bomDataTable.Rows)
                    {
                        if (row[col.ColumnName] != DBNull.Value && Convert.ToInt32(row[col.ColumnName]) > 0)
                        {
                            string finalPos = posCounter.ToString("D3");
                            row[posColName] = finalPos; 
                            
                            string vaultName = row["Vault Name"].ToString();
                            var recordsToUpdate = _lastScanResults.Where(r => r.PanelName == containerName && r.VaultName == vaultName).ToList();
                            
                            foreach(var record in recordsToUpdate)
                            {
                                record.Position = finalPos;
                                record.Quantity = Convert.ToInt32(row[col.ColumnName]);
                            }

                            posCounter++;
                            assignedCount++;
                        }
                    }
                }
            }
            TxtStatus.Text = $"Auto-Balloon assigned {assignedCount} positions successfully.";
        }

        // ====================================================================
        // Bơm ngược số Pos vào thẻ Attribute POS_NUM trên CAD
        // ====================================================================
        private void BtnSyncPosToCad_Click(object sender, RoutedEventArgs e)
        {
            if (_lastScanResults == null || _lastScanResults.Count == 0)
            {
                MessageBox.Show("No data to sync. Please 'Scan & Count' first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            int updatedBlocksCount = 0;
            this.Visibility = System.Windows.Visibility.Hidden;

            try
            {
                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = doc.TransactionManager.StartTransaction())
                    {
                        foreach (var record in _lastScanResults)
                        {
                            // Bỏ qua nếu không có số Pos hoặc không có Block ID nào được lưu
                            if (string.IsNullOrEmpty(record.Position) || record.InstanceIds == null || record.InstanceIds.Count == 0) continue;

                            foreach (ObjectId objId in record.InstanceIds)
                            {
                                if (objId.IsNull || objId.IsErased) continue;

                                BlockReference blkRef = tr.GetObject(objId, OpenMode.ForRead) as BlockReference;
                                if (blkRef == null || blkRef.AttributeCollection == null) continue;

                                // Tìm thẻ Attribute có tên "POS_NUM"
                                foreach (ObjectId attId in blkRef.AttributeCollection)
                                {
                                    AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                    if (attRef != null && attRef.Tag.Equals("POS_NUM", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Mở quyền Ghi và nhét số Pos vào
                                        attRef.UpgradeOpen();
                                        attRef.TextString = record.Position;
                                        updatedBlocksCount++;
                                        break; // Xong thẻ này thì thoát vòng lặp tìm Attribute
                                    }
                                }
                            }
                        }
                        tr.Commit();
                    }
                }

                MessageBox.Show($"Successfully synced {updatedBlocksCount} Position Number(s) to AutoCAD Blocks!", "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtStatus.Text = $"Synced {updatedBlocksCount} attributes to CAD.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error syncing positions to CAD: " + ex.Message, "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_bomDataTable == null || _bomDataTable.Rows.Count == 0 || _lastScanResults == null)
            {
                MessageBox.Show("No data to export. Please scan the drawing first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                Title = "Save BOM Export",
                FileName = "BOM_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    TxtStatus.Text = "Exporting to Excel...";
                    bool isInterfaceMode = RadioHull.IsChecked == true;

                    string matrixSheetName = isInterfaceMode ? "FittingInHull" : "FittingInPanel";
                    string dataSheetName = isInterfaceMode ? "Data" : "Part BOM";

                    using (var workbook = new XLWorkbook())
                    {
                        var wsMatrix = workbook.Worksheets.Add(matrixSheetName);
                        
                        for (int i = 0; i < _bomDataTable.Columns.Count; i++)
                        {
                            var cell = wsMatrix.Cell(1, i + 1);
                            cell.Value = _bomDataTable.Columns[i].ColumnName;
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                            cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                        }

                        for (int r = 0; r < _bomDataTable.Rows.Count; r++)
                        {
                            for (int c = 0; c < _bomDataTable.Columns.Count; c++)
                            {
                                var cellValue = _bomDataTable.Rows[r][c];
                                var cell = wsMatrix.Cell(r + 2, c + 1);
                                
                                if (cellValue != DBNull.Value)
                                {
                                    if (int.TryParse(cellValue.ToString(), out int numValue) && _bomDataTable.Columns[c].DataType == typeof(int))
                                        cell.Value = numValue;
                                    else
                                        cell.Value = cellValue.ToString();
                                }
                                
                                cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                                
                                if (_bomDataTable.Columns[c].ColumnName.EndsWith(" Qty"))
                                    cell.Style.Fill.BackgroundColor = XLColor.AliceBlue;
                                else if (_bomDataTable.Columns[c].ColumnName.EndsWith(" Pos"))
                                    cell.Style.Fill.BackgroundColor = XLColor.LemonChiffon;
                            }
                        }
                        wsMatrix.Columns().AdjustToContents();

                        var wsData = workbook.Worksheets.Add(dataSheetName);
                        string containerHeader = isInterfaceMode ? "Detail Name" : "Panel Name";
                        
                        var headers = new string[] { containerHeader, "Vault Name", "Part ID", "XClass", "Description", "Parent Block", "Quantity", "Position" };
                        
                        for (int i = 0; i < headers.Length; i++)
                        {
                            var cell = wsData.Cell(1, i + 1);
                            cell.Value = headers[i];
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
                            cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                        }

                        for (int r = 0; r < _lastScanResults.Count; r++)
                        {
                            var item = _lastScanResults[r];
                            wsData.Cell(r + 2, 1).Value = item.PanelName;
                            wsData.Cell(r + 2, 2).Value = item.VaultName;
                            wsData.Cell(r + 2, 3).Value = item.PartId;
                            wsData.Cell(r + 2, 4).Value = item.XClass;
                            wsData.Cell(r + 2, 5).Value = item.Description;
                            wsData.Cell(r + 2, 6).Value = item.ParentBlockName;
                            wsData.Cell(r + 2, 7).Value = item.Quantity;
                            wsData.Cell(r + 2, 8).Value = item.Position;
                            
                            for (int c = 1; c <= headers.Length; c++)
                            {
                                wsData.Cell(r + 2, c).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                            }
                        }
                        wsData.Columns().AdjustToContents();

                        workbook.SaveAs(sfd.FileName);
                    }

                    TxtStatus.Text = "Export completed successfully.";
                    MessageBox.Show($"BOM successfully exported to Excel!\nFile: {sfd.FileName}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error exporting to Excel. Make sure the file is not open by another program.\n\nDetails: " + ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    TxtStatus.Text = "Export failed.";
                }
            }
        }
    }
}