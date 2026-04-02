using Autodesk.AutoCAD.DatabaseServices;

namespace ShipAutoCadPlugin.Models
{
    // =========================================================
    // LƯU TRỮ LỊCH SỬ (HISTORY) TRONG CAD
    // =========================================================
    public class RevisionHistory
    {
        public string Rev { get; set; }
        public string Date { get; set; }
        public string Description { get; set; }
        public ObjectId BlockId { get; set; } // Lưu ID để xóa Block
    }

    // =========================================================
    // LƯU TRỮ DỮ LIỆU CHO BẢNG CHÍNH (MASTER GRID WPF)
    // =========================================================
    public class SheetRowData
    {
        public string SheetNo { get; set; }
        public string Content { get; set; }
        public string Rev { get; set; }
        public string Date { get; set; }
        public string AmendmentDescription { get; set; }

        public int RawNumericSheetNo { get; set; }
        
        public ObjectId SheetContentBlockId { get; set; }
        public ObjectId AmendmentBlockId { get; set; }
        public ObjectId A1BlockId { get; set; } // Lưu ID khung A1 để dò lịch sử
    }

    // =========================================================
    // LƯU TRỮ LỊCH SỬ TỪ EXCEL (DÙNG CHO VAULT SYNC)
    // =========================================================
    public class ExcelRevHistory
    {
        public string SheetNo { get; set; }
        public string Rev { get; set; }
        public string Date { get; set; }
        public string Description { get; set; }
    }
}