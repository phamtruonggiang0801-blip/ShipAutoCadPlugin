using System.Collections.Generic;
using ShipAutoCadPlugin.Models;

namespace ShipAutoCadPlugin.Services
{
    public static class ChecklistDatabase
    {
        // 1. Danh sách các Bộ môn để Kỹ sư chọn ở màn hình Dropdown
        public static readonly List<string> Disciplines = new List<string>
        {
            "Structure (Panel)",
            "Layout / Interface",
            "Mechanical"
        };

        // 2. Hàm gọi Template câu hỏi chuẩn dựa trên Bộ môn
        public static List<ChecklistItem> GetDefaultItems(string discipline)
        {
            var items = new List<ChecklistItem>();

            if (discipline == "Structure (Panel)")
            {
                items.Add(new ChecklistItem("All panel dimensions and plate thicknesses match the 3D model."));
                items.Add(new ChecklistItem("Welding symbols are correctly placed, scaled, and typed."));
                items.Add(new ChecklistItem("Material grades and part numbers are strictly verified."));
                items.Add(new ChecklistItem("Lifting lugs are properly positioned and rated for Safe Working Load (SWL)."));
                items.Add(new ChecklistItem("BOM Matrix is successfully exported and quantities match the drawing."));
            }
            else if (discipline == "Layout / Interface")
            {
                items.Add(new ChecklistItem("Clearances for maintenance and safe operation are fully respected."));
                items.Add(new ChecklistItem("Interferences with existing ship structures (Hull/Deck) are resolved."));
                items.Add(new ChecklistItem("Coordinate systems, Ship Centerline (CL), and deck elevations are correct."));
                items.Add(new ChecklistItem("All connection interfaces (bolted/welded) to the deck are detailed."));
            }
            else if (discipline == "Mechanical")
            {
                items.Add(new ChecklistItem("All mechanical fittings have proper POS_NUM balloons assigned."));
                items.Add(new ChecklistItem("Wire rope routing and sheave alignments are verified without clashes."));
                items.Add(new ChecklistItem("Fastener list (bolts, nuts, washers) is fully captured in the Sub-BOM."));
                items.Add(new ChecklistItem("Moving parts collision check and stroke limits are passed."));
                items.Add(new ChecklistItem("Hydraulic / Pneumatic port connections are clearly identified."));
            }
            else
            {
                // Default fallback (Phòng hờ lỗi)
                items.Add(new ChecklistItem("General title block information and project details are correct."));
                items.Add(new ChecklistItem("Revision history is updated with the latest changes."));
            }

            return items;
        }
    }
}