using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace ShipAutoCadPlugin.Models
{
    // Đại diện cho 1 dòng dữ liệu thô thu hoạch được từ bản vẽ (Tương đương Sheet "Data")
    public class BomHarvestRecord
    {
        public string PanelName { get; set; }     
        public string VaultName { get; set; }     
        public string PartId { get; set; }        
        public string XClass { get; set; }
        public string Description { get; set; }
        public int Quantity { get; set; }
        public string ParentBlockName { get; set; }
        
        // [BỔ SUNG] Lưu lại vị trí Balloon sau khi Kỹ sư ấn Auto-Assign (Dành cho Panel BOM)
        public string Position { get; set; } 

        // ====================================================================
        // [CẬP NHẬT MỚI]: Vị trí tĩnh được đánh sẵn từ thư viện (Dành cho Detail/Hull BOM)
        // ====================================================================
        public string ProjectPosNum { get; set; } 

        // ====================================================================
        // [TÍNH NĂNG MỚI]: "Túi" chứa định danh vật lý của các Block trên mặt bằng CAD
        // ====================================================================
        public List<ObjectId> InstanceIds { get; set; } = new List<ObjectId>();
    }

    // Class tiện ích để chứa các hằng số kích hoạt logic (Triggers)
    public static class BomTriggers
    {
        public const string WIRE_ROPE_PART_ID = "400288625";
        public const string THIMBLE_PART_ID = "400288975";
        public const string CLAMP_PART_ID = "400288785";
        public const string WIRE_ROPE_ASSY_PART_ID = "400281671";

        public const string CAS_150_DEDUCT_1 = "CAS-0031266";
        public const string CAS_150_DEDUCT_2 = "CAS-0030971";
        public const string CAS_NO_DEDUCT = "CAS-0060377";
    }
}