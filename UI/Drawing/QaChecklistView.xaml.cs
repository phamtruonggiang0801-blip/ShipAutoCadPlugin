using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics; // Dùng để nhận diện phần mềm
using ShipAutoCadPlugin.Services;
using ShipAutoCadPlugin.Models;

namespace ShipAutoCadPlugin.UI
{
    public partial class QaChecklistView : UserControl
    {
        private AutoCadService _acService;
        private InventorService _invService;
        private ChecklistDocument _currentDoc;
        
        // Cờ nhận diện nền tảng
        private bool _isAutoCad;

        public QaChecklistView()
        {
            InitializeComponent();
            
            // TỰ ĐỘNG NHẬN DIỆN MÔI TRƯỜNG CHẠY (AutoCAD vs Inventor)
            string processName = Process.GetCurrentProcess().ProcessName.ToLower();
            _isAutoCad = processName.Contains("acad");

            // Khởi tạo Service tương ứng
            if (_isAutoCad) _acService = new AutoCadService();
            else _invService = new InventorService();
            
            // Đổ danh sách Discipline
            CboDiscipline.ItemsSource = ChecklistDatabase.Disciplines;
            if (CboDiscipline.Items.Count > 0) CboDiscipline.SelectedIndex = 0;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            // Tự động rẽ nhánh hàm Load
            _currentDoc = _isAutoCad ? _acService.LoadChecklistFromDwg() : _invService.LoadChecklistFromInventor();

            if (_currentDoc != null && _currentDoc.Status == "APPROVED")
            {
                CboDiscipline.SelectedItem = _currentDoc.Discipline;
                CboDiscipline.IsEnabled = false; 

                BorderStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8E6C9"));
                BorderStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                TxtStatus.Text = "APPROVED (READY FOR RELEASE)";
                TxtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B5E20"));

                GridApprovedInfo.Visibility = Visibility.Visible;
                TxtApprovedInfo.Text = $"{_currentDoc.ApprovedBy} at {_currentDoc.ApprovedDate}";
            }
            else
            {
                // [AUTO-PURGE]: Gọi hàm sát thủ diệt hàng giả theo nền tảng
                if (_isAutoCad) _acService.PurgeFakeQaStamps();
                else _invService.PurgeFakeQaStampsInventor();

                CboDiscipline.IsEnabled = true;
                ResetUIStatus();
            }
        }

        private void ResetUIStatus()
        {
            BorderStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFE0B2"));
            BorderStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFB300"));
            TxtStatus.Text = "PENDING / NOT STARTED";
            TxtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100"));
            GridApprovedInfo.Visibility = Visibility.Collapsed;
        }

        private void BtnOpenChecklist_Click(object sender, RoutedEventArgs e)
        {
            string selectedDisc = CboDiscipline.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedDisc)) return;

            try
            {
                // Truyền cả 2 Service và Cờ nền tảng sang cửa sổ con
                ChecklistWindow window = new ChecklistWindow(_acService, _invService, _isAutoCad, _currentDoc, selectedDisc);
                
                // TUYỆT CHIÊU: Dùng ShowDialog() chuẩn của WPF cho cả AutoCAD và Inventor.
                // Loại bỏ hoàn toàn chữ "Autodesk.AutoCAD..." để tránh làm Inventor văng!
                bool? result = window.ShowDialog();

                // Cập nhật lại UI sau khi đóng cửa sổ
                if (result == true)
                {
                    RefreshStatus();
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error opening checklist window: {ex.Message}", "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to clear all QA/QC data from this drawing?", "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                // Gọi hàm xóa tương ứng
                bool isDeleted = _isAutoCad ? _acService.DeleteChecklistFromDwg() : _invService.DeleteChecklistFromInventor();

                if (isDeleted)
                {
                    _currentDoc = null;
                    
                    // [MANUAL-PURGE]: Xóa nốt chữ hiển thị trên màn hình
                    if (_isAutoCad) _acService.PurgeFakeQaStamps();
                    else _invService.PurgeFakeQaStampsInventor();

                    RefreshStatus();
                    MessageBox.Show("QA/QC data and CAD stamps cleared successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}