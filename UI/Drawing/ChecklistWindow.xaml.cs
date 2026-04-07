using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ShipAutoCadPlugin.Services;
using ShipAutoCadPlugin.Models;

namespace ShipAutoCadPlugin.UI
{
    public partial class ChecklistWindow : Window
    {
        private AutoCadService _acService;
        private InventorService _invService;
        private bool _isAutoCad;
        
        private ChecklistDocument _doc;
        public ObservableCollection<ChecklistItem> ChecklistData { get; set; }

        public ChecklistWindow(AutoCadService acService, InventorService invService, bool isAutoCad, ChecklistDocument existingDoc, string discipline)
        {
            InitializeComponent();
            _acService = acService;
            _invService = invService;
            _isAutoCad = isAutoCad;

            TxtHeader.Text = $"{discipline.ToUpper()} CHECKLIST";

            if (existingDoc != null) _doc = existingDoc;
            else
            {
                _doc = new ChecklistDocument { Discipline = discipline };
                _doc.Items = ChecklistDatabase.GetDefaultItems(discipline);
            }

            ChecklistData = new ObservableCollection<ChecklistItem>(_doc.Items);
            ListItems.ItemsSource = ChecklistData;

            if (_doc.Status == "APPROVED")
            {
                ListItems.IsEnabled = false;
                TxtNewItem.IsEnabled = false;
                BtnAddItem.IsEnabled = false;
                BtnSaveDraft.Visibility = Visibility.Collapsed;
                BtnSignApprove.Content = "ALREADY APPROVED";
            }

            UpdateProgress();
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e) { UpdateProgress(); }

        private void UpdateProgress()
        {
            if (ChecklistData == null || ChecklistData.Count == 0) return;

            int total = ChecklistData.Count;
            int checkedCount = ChecklistData.Count(x => x.IsChecked);
            double percentage = ((double)checkedCount / total) * 100;

            ProgBar.Value = percentage;
            TxtProgress.Text = $"{checkedCount} / {total} ({Math.Round(percentage)}%)";

            if (_doc.Status != "APPROVED")
            {
                BtnSignApprove.IsEnabled = (checkedCount == total);
                if (checkedCount == total)
                {
                    BtnSignApprove.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF005E9E"));
                }
                else
                {
                    BtnSignApprove.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4CAF50"));
                }
            }
        }

        private void BtnAddItem_Click(object sender, RoutedEventArgs e)
        {
            string newContent = TxtNewItem.Text.Trim();
            if (string.IsNullOrEmpty(newContent)) return;

            var newItem = new ChecklistItem(newContent, isCustom: true);
            ChecklistData.Add(newItem);
            
            TxtNewItem.Clear();
            UpdateProgress();
        }

        private void BtnDeleteCustomItem_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null || btn.Tag == null) return;

            string idToDelete = btn.Tag.ToString();
            var item = ChecklistData.FirstOrDefault(x => x.Id == idToDelete);
            
            if (item != null)
            {
                ChecklistData.Remove(item);
                UpdateProgress();
            }
        }

        private void BtnSaveDraft_Click(object sender, RoutedEventArgs e)
        {
            _doc.Items = ChecklistData.ToList();
            
            // Rẽ nhánh khi Lưu nháp
            bool success = _isAutoCad ? _acService.SaveChecklistToDwg(_doc) : _invService.SaveChecklistToInventor(_doc);
            
            if(success)
            {
                this.DialogResult = true;
                this.Close();
            }
        }

        private void BtnSignApprove_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Approving this drawing will generate a QA Stamp and lock the checklist.\n\nDo you want to proceed?", "Confirm Approval", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _doc.Items = ChecklistData.ToList();
                _doc.Status = "APPROVED";
                _doc.ApprovedBy = Environment.UserName; 
                _doc.ApprovedDate = DateTime.Now.ToString("dd/MMM/yyyy HH:mm");

                // Rẽ nhánh khi Lưu dữ liệu Approve
                bool success = _isAutoCad ? _acService.SaveChecklistToDwg(_doc) : _invService.SaveChecklistToInventor(_doc);

                if (success)
                {
                    // Rẽ nhánh khi gọi hàm Đập con dấu
                    if (_isAutoCad) _acService.GenerateQaStamp();
                    else _invService.GenerateQaStampInventor();

                    MessageBox.Show("Drawing successfully Approved and Signed!", "QA Passed", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    this.DialogResult = true; 
                    this.Close();
                }
            }
        }
    }
}