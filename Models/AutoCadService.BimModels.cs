using System.Collections.Generic;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: FITTING MODELS (Định nghĩa cấu trúc dữ liệu Metadata)
        // ====================================================================

        /// <summary>
        /// Chứa thông tin hình học của từng hình chiếu trích xuất từ Inventor.
        /// </summary>
        public class ViewMetadata
        {
            public string Name { get; set; }
            public double CenterX { get; set; }
            public double CenterY { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
        }

        /// <summary>
        /// Cấu trúc file JSON thô được trích xuất từ công cụ Inventor Extractor.
        /// </summary>
        public class FittingMetadata
        {
            public string PartNumber { get; set; }
            public string Description { get; set; }
            public string Revision { get; set; }
            public string Mass { get; set; }
            public string Material { get; set; }
            
            // Bổ sung iProperties lấy từ file .idw
            public string Designer { get; set; } 
            public string Title { get; set; }
            
            public List<ViewMetadata> Views { get; set; } 
        }

        // ====================================================================
        // [CẤU TRÚC MỚI]: Đại diện cho 1 loại phụ kiện đi kèm (Accessory)
        // ====================================================================
        public class AccessoryItem
        {
            public string PartId { get; set; }
            public int Quantity { get; set; } // Số lượng phụ kiện CẦN THIẾT cho 1 Fitting chính
        }

        /// <summary>
        /// Cấu trúc mục lục cho file MasterCatalog.json trong thư viện trung tâm.
        /// Giúp quản lý và tìm kiếm Fitting nhanh chóng.
        /// </summary>
        public class CatalogItem
        {
            public string BlockName { get; set; }
            public string PartNumber { get; set; }
            public string Description { get; set; }
            public string Material { get; set; }
            public string Mass { get; set; }
            public string Revision { get; set; }
            
            // Phục vụ cho DataGrid và Thanh tìm kiếm (Search)
            public string Designer { get; set; }
            
            // [QUAN TRỌNG]: Trường Title này được sử dụng làm 'XClass' khi VLOOKUP xuất BOM
            public string Title { get; set; }
            
            // Thuộc tính phân loại (PANEL hoặc DETAIL) do Leader quyết định lúc Import
            public string BomType { get; set; } 
            
            public string FilePath { get; set; } // Đường dẫn vật lý đến file .dwg

            public string ProjectPosNum { get; set; } 

            // ====================================================================
            // [TÍNH NĂNG MỚI]: GEOMETRIC PROPERTIES & VIRTUAL BOM
            // ====================================================================
            
            // Định dạng đối tượng: "Block" (Mặc định), "Polyline", "Line", "Circle", "Accessory"
            public string EntityType { get; set; } = "Block"; 
            
            public string TriggerLayer { get; set; } // Ví dụ: "Mechanical-AM_7"
            public string TriggerColor { get; set; } // Ví dụ: "Red" hoặc Index màu
            
            // Đơn vị tính: "pcs" (cái), "m" (mét), "kg"
            public string UoM { get; set; } = "pcs"; 

            // Danh sách các phụ kiện (Bu-lông, đai ốc...) đi kèm với Fitting này
            public List<AccessoryItem> Accessories { get; set; } = new List<AccessoryItem>();
        }
    }
}