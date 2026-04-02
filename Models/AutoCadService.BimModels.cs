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
            // Giúp phân nhánh Cây thư mục (Tree Categories) trên giao diện Library
            public string BomType { get; set; } 
            
            public string FilePath { get; set; } // Đường dẫn vật lý đến file .dwg

            // ====================================================================
            // [CẬP NHẬT MỚI]: Vị trí tĩnh được đánh sẵn từ Project Library
            // ====================================================================
            public string ProjectPosNum { get; set; } 
        }
    }
}