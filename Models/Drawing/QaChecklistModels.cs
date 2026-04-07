using System;
using System.Collections.Generic;

namespace ShipAutoCadPlugin.Models
{
    // Đại diện cho 1 dòng (1 câu hỏi) trong Checklist
    public class ChecklistItem
    {
        public string Id { get; set; } // Mã định danh duy nhất (Guid) để quản lý
        public string Content { get; set; } // Nội dung câu hỏi
        public bool IsChecked { get; set; } // Trạng thái Tick box
        public bool IsCustom { get; set; } // True = Kỹ sư tự thêm (Được xóa) | False = Mặc định (Khóa)

        public ChecklistItem()
        {
            Id = Guid.NewGuid().ToString(); // Tự động sinh mã ID khi tạo mới
            IsChecked = false;
            IsCustom = false;
        }

        public ChecklistItem(string content, bool isCustom = false) : this()
        {
            Content = content;
            IsCustom = isCustom;
        }
    }

    // Đại diện cho toàn bộ Hồ sơ Checklist nhúng vào bản vẽ
    public class ChecklistDocument
    {
        public string Discipline { get; set; } // Bộ môn (Structure, Mech, Layout...)
        public string Status { get; set; } = "PENDING"; // Trạng thái: PENDING hoặc APPROVED
        
        public string ApprovedBy { get; set; } // Tên User Windows đã ký duyệt
        public string ApprovedDate { get; set; } // Ngày giờ ký duyệt
        
        public List<ChecklistItem> Items { get; set; } // Danh sách các câu hỏi

        public ChecklistDocument()
        {
            Items = new List<ChecklistItem>();
        }
    }
}