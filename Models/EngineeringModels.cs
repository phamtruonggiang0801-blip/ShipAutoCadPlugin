using System.Collections.Generic;
using System.Collections.ObjectModel;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json; // Bổ sung thư viện JSON

namespace ShipAutoCadPlugin.Models
{
    /// <summary>
    /// Class gốc (Base Class) tối giản chuẩn CAD
    /// </summary>
    public class BaseEngNode
    {
        public string Name { get; set; }
        public int Qty { get; set; } // Thêm thuộc tính Số lượng (Quantity) độc lập

        // Danh sách các đối tượng con
        public ObservableCollection<BaseEngNode> Children { get; set; } = new ObservableCollection<BaseEngNode>();
    }

    /// <summary>
    /// Mức 1: Đối tượng Panel
    /// </summary>
    public class PanelNode : BaseEngNode
    {
        public double Area { get; set; }
        public Extents3d WcsBounds { get; set; } 
        public List<Point2d> WcsPolygonPoints { get; set; } 

        // =========================================================
        // [BỔ SUNG] LIÊN KẾT FITTING TỪ MODULE BOM PREVIEW
        // =========================================================
        
        /// <summary>
        /// Danh sách các Fitting (vật tư) được quét và gán cho Panel này.
        /// Danh sách này sẽ tự động được Serialize và lưu vào file dự án JSON.
        /// </summary>
        public List<BomHarvestRecord> AssociatedFittings { get; set; } = new List<BomHarvestRecord>();

        /// <summary>
        /// Thuộc tính đếm số lượng để hiển thị nhanh (Binding) lên DataGrid.
        /// Thuộc tính này sẽ KHÔNG BỊ LƯU vào file JSON (tránh rác) nhờ tag [JsonIgnore].
        /// </summary>
        [JsonIgnore]
        public int FittingCount => AssociatedFittings?.Count ?? 0;
    }

    /// <summary>
    /// Mức 2: Đối tượng Detail
    /// </summary>
    public class DetailNode : BaseEngNode
    {
        public Extents3d WcsBounds { get; set; } 

        // Tương lai: Khi làm phần Detail Harvester, chúng ta cũng sẽ copy 2 thuộc tính trên xuống đây!
    }

    /// <summary>
    /// Mức 3: Đối tượng Fitting
    /// </summary>
    public class FittingNode : BaseEngNode
    {
    }
}