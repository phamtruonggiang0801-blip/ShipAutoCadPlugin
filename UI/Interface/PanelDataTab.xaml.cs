using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ShipAutoCadPlugin.Models;
using ShipAutoCadPlugin.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace ShipAutoCadPlugin.UI
{
    public partial class PanelStructureView : UserControl
    {
        private AutoCadService _acService;
        private ObservableCollection<SheetRowData> _masterDataList;

        public PanelStructureView()
        {
            InitializeComponent();
            _acService = new AutoCadService();
            _masterDataList = new ObservableCollection<SheetRowData>();
            GridShared.ItemsSource = _masterDataList;
        }

        private void BtnSelectExcel_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Excel Files|*.xlsx;*.xls";
            if (openFileDialog.ShowDialog() == true) TxtExcelPath.Text = openFileDialog.FileName;
        }

        private void BtnPullExcel_Click(object sender, RoutedEventArgs e)
        {
            string excelPath = TxtExcelPath.Text;
            if (!System.IO.File.Exists(excelPath)) return;

            List<ExcelRevHistory> importedHistory;
            List<SheetRowData> importedData = _acService.ImportFromVaultExcel(excelPath, out importedHistory);
            
            if (importedData != null && importedData.Count > 0)
            {
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                MergeDataIntoGrid(importedData);
                _acService.SyncHistoryBlocksFromExcel(importedHistory, _masterDataList.ToList());
            }
        }

        private void BtnPushExcel_Click(object sender, RoutedEventArgs e)
        {
            GridShared.CommitEdit();
            GridShared.CommitEdit(DataGridEditingUnit.Row, true);
            string excelPath = TxtExcelPath.Text;
            if (string.IsNullOrWhiteSpace(excelPath)) return;

            bool pushTab1_2 = ChkSyncSheet.IsChecked == true;
            bool pushTab3 = ChkSyncPanel.IsChecked == true;

            try 
            { 
                if (pushTab1_2 && _masterDataList != null && _masterDataList.Count > 0) _acService.ExportToVaultExcel(_masterDataList.ToList(), excelPath); 
                if (pushTab3) _acService.ExportPanelDataToExcel(excelPath);
            }
            catch (Exception ex) { MessageBox.Show("Excel export error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnAutoNamePanels_Click(object sender, RoutedEventArgs e)
        {
            string deckNum = TxtDeckNumber.Text.Trim();
            if (string.IsNullOrEmpty(deckNum)) return;

            bool addLiftingLugs = true;
            int guidingType = RadGuide2.IsChecked == true ? 1 : 2;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _acService.SmartAutoNamePanels(deckNum, addLiftingLugs, guidingType); }
            catch (Exception ex) { MessageBox.Show("System error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnAddManualLugs_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _acService.AddManualLiftingPoints(); }
            catch (Exception ex) { MessageBox.Show("System error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnOpenDashboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EngineeringDashboard dashboard = new EngineeringDashboard(_acService);
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(dashboard);
            }
            catch (Exception ex) { MessageBox.Show("Error opening dashboard: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnGenerateDetails_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _acService.GenerateDetailLabels(); }
            catch (Exception ex) { MessageBox.Show("System error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnUpdateDetailName_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _acService.UpdateDetailNameByProximity(); }
            catch (Exception ex) { MessageBox.Show("System error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnAddSheetContent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                GridShared.CommitEdit();
                GridShared.CommitEdit(DataGridEditingUnit.Row, true);

                var currentData = _masterDataList.ToList();
                List<SheetRowData> updatedList = _acService.SyncAndAddSheetContent(currentData);

                if (updatedList != null)
                {
                    _masterDataList.Clear();
                    foreach (var item in updatedList) _masterDataList.Add(item);
                }
            }
            catch (Exception ex) { MessageBox.Show("Sync error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnCreateAmendment_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                GridShared.CommitEdit();
                GridShared.CommitEdit(DataGridEditingUnit.Row, true);

                var selectedItems = GridShared.SelectedItems.Cast<SheetRowData>().ToList();
                if (selectedItems == null || selectedItems.Count == 0) return;

                bool isGlobalBump = ChkGlobalRevBump.IsChecked == true;
                string masterRev = selectedItems[0].Rev;
                string masterDate = selectedItems[0].Date;

                int successCount = 0;
                var itemsToProcess = isGlobalBump ? _masterDataList.ToList() : selectedItems;

                foreach (var row in itemsToProcess)
                {
                    if (row.A1BlockId == ObjectId.Null) continue;

                    bool isSelectedByUser = selectedItems.Contains(row);
                    if (isGlobalBump)
                    {
                        row.Rev = masterRev;
                        row.Date = masterDate;
                        if (!isSelectedByUser) row.AmendmentDescription = "";
                    }

                    if (string.IsNullOrWhiteSpace(row.AmendmentDescription)) continue;

                    var newBlockId = _acService.CreateNewAmendmentBlock(row);
                    if (newBlockId != ObjectId.Null)
                    {
                        row.AmendmentBlockId = newBlockId;
                        successCount++;
                    }
                }

                GridShared.Items.Refresh();
                if (successCount > 0) GridHistory.ItemsSource = _acService.GetRevisionHistory(selectedItems[0].A1BlockId);
            }
            catch (Exception ex) { MessageBox.Show("System error occurred: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void GridShared_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItems = GridShared.SelectedItems;
            if (selectedItems != null && selectedItems.Count > 0)
            {
                List<ObjectId> selectedBlockIds = new List<ObjectId>();
                foreach (var item in selectedItems)
                {
                    var rowData = item as SheetRowData;
                    if (rowData != null && rowData.A1BlockId != ObjectId.Null) selectedBlockIds.Add(rowData.A1BlockId);
                }
                _acService.HighlightMultipleBlocks(selectedBlockIds);

                if (selectedItems.Count == 1)
                {
                    var singleRow = selectedItems[0] as SheetRowData;
                    GridHistory.ItemsSource = _acService.GetRevisionHistory(singleRow.A1BlockId);
                }
                else GridHistory.ItemsSource = null;
            }
            else
            {
                GridHistory.ItemsSource = null;
                _acService.HighlightMultipleBlocks(new List<ObjectId>());
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_masterDataList != null) _masterDataList.Clear();
            if (GridHistory != null) GridHistory.ItemsSource = null;
        }

        private void MenuDeleteRev_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            var selectedHistory = GridHistory.SelectedItem as RevisionHistory;
            if (selectedHistory == null || selectedHistory.BlockId == ObjectId.Null) return;

            if (MessageBox.Show($"Delete [{selectedHistory.Rev}]?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                if (_acService.DeleteBlock(selectedHistory.BlockId))
                {
                    var selectedMasterRow = GridShared.SelectedItem as SheetRowData;
                    if (selectedMasterRow != null) GridHistory.ItemsSource = _acService.GetRevisionHistory(selectedMasterRow.A1BlockId);
                }
            }
        }

        private void MergeDataIntoGrid(List<SheetRowData> newDataList)
        {
            if (newDataList == null || newDataList.Count == 0) return;
            foreach (var newItem in newDataList)
            {
                var existingItem = _masterDataList.FirstOrDefault(x => x.SheetNo == newItem.SheetNo);
                if (existingItem != null)
                {
                    existingItem.Content = newItem.Content ?? "";
                    existingItem.Rev = newItem.Rev ?? "";
                    existingItem.Date = newItem.Date ?? "";
                    existingItem.AmendmentDescription = newItem.AmendmentDescription ?? "";
                    if (newItem.SheetContentBlockId != ObjectId.Null) existingItem.SheetContentBlockId = newItem.SheetContentBlockId;
                    if (newItem.AmendmentBlockId != ObjectId.Null) existingItem.AmendmentBlockId = newItem.AmendmentBlockId;
                }
                else _masterDataList.Add(newItem);
            }
            var sortedList = _masterDataList.OrderBy(x => x.RawNumericSheetNo).ToList();
            _masterDataList.Clear();
            foreach (var item in sortedList) _masterDataList.Add(item);
            GridShared.Items.Refresh();
        }
    }
}