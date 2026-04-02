using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ShipAutoCadPlugin.Models;
using ShipAutoCadPlugin.Services; 

namespace ShipAutoCadPlugin.UI
{
    public partial class EngineeringDashboard : Window
    {
        private List<PanelNode> _allPanels;
        private AutoCadService _acService; 

        // Nhận trực tiếp Service từ Palette truyền sang
        public EngineeringDashboard(AutoCadService acService)
        {
            InitializeComponent();
            _acService = acService; 
            
            // Kéo dữ liệu RAM Static có sẵn lên bảng ngay khi mở Dashboard
            _allPanels = AutoCadService.ExtractedPanelNodes ?? new List<PanelNode>();
            GridMasterPanels.ItemsSource = _allPanels;
        }

        private void GridMasterPanels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedPanel = GridMasterPanels.SelectedItem as PanelNode;
            if (selectedPanel != null)
            {
                // 1. Đổ danh sách Detail vào bảng trên (TreeDetails)
                TreeDetails.ItemsSource = selectedPanel.Children;
                
                // 2. [BỔ SUNG] Đổ danh sách Fitting vào bảng dưới (GridFittings)
                GridFittings.ItemsSource = selectedPanel.AssociatedFittings; 
            }
            else
            {
                // Xóa trắng cả 2 bảng nếu không có Panel nào được chọn
                TreeDetails.ItemsSource = null;
                GridFittings.ItemsSource = null;
            }
        }

        private void BtnScanAndAudit_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            try
            {
                List<PanelNode> auditResults = _acService.ScanAndMapPanelDetails();

                if (auditResults != null && auditResults.Count > 0)
                {
                    _allPanels = auditResults;

                    GridMasterPanels.ItemsSource = null;
                    GridMasterPanels.ItemsSource = _allPanels;
                    
                    // Reset lại 2 bảng bên phải sau khi quét mới
                    TreeDetails.ItemsSource = null;
                    GridFittings.ItemsSource = null; 

                    MessageBox.Show($"Audit Complete! Successfully mapped Details to {_allPanels.Count} Panels.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Audit execution error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}