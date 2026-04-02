using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: DETAIL VIEW HELPERS (Hỗ trợ UI DataGrid và Trích xuất Data)
        // ====================================================================

        /// <summary>
        /// Highlight (Nhấp nháy viền xanh) một Block trên màn hình CAD
        /// </summary>
        public void HighlightBlock(ObjectId blockId)
        {
            if (blockId == ObjectId.Null) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try
            {
                ed.SetImpliedSelection(new ObjectId[0]);
                ed.SetImpliedSelection(new ObjectId[] { blockId });
                ed.UpdateScreen();
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog("Highlight error: " + ex.Message);
            }
        }

        /// <summary>
        /// Highlight nhiều Block cùng lúc (Hỗ trợ Shift/Ctrl trên DataGrid)
        /// </summary>
        public void HighlightMultipleBlocks(List<ObjectId> blockIds)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            try
            {
                ed.SetImpliedSelection(new ObjectId[0]);

                if (blockIds != null && blockIds.Count > 0)
                {
                    ed.SetImpliedSelection(blockIds.ToArray());
                }

                ed.UpdateScreen();
            }
            catch 
            {
                // Bỏ qua lỗi ngầm để trải nghiệm quét chuột trên UI không bị gián đoạn
            }
        }

        /// <summary>
        /// Trích xuất danh sách các nhãn Detail nằm trong phạm vi khung A1
        /// </summary>
        public string GetDetailListString(Transaction tr, List<BlockReference> allBlocks, Extents3d a1Extents)
        {
            List<string> details = new List<string>();

            var detailBlocks = allBlocks.Where(b => GetEffectiveName(tr, b).ToUpper() == "DETAIL TITLE" && IsInsideExtents(b.GeometricExtents, a1Extents));
            
            foreach (var blk in detailBlocks)
            {
                string rawTitle = GetAttributeValue(tr, blk, "TITLE");
                if (!string.IsNullOrEmpty(rawTitle))
                {
                    string processed = rawTitle.ToUpper().Replace("%%U", "").Replace("DETAIL", "").Trim();
                    int dotIndex = processed.IndexOf(".");
                    if (dotIndex > 0) processed = processed.Substring(0, dotIndex).Trim();
                    else
                    {
                        int dashIndex = processed.IndexOf("-");
                        if (dashIndex > 0) processed = processed.Substring(0, dashIndex).Trim();
                    }

                    if (!string.IsNullOrEmpty(processed) && !details.Contains(processed))
                    {
                        details.Add(processed);
                    }
                }
            }

            if (details.Count > 0)
            {
                details.Sort(); // Tự động sắp xếp A-Z
                return string.Join(", ", details.Select(d => "DETAIL " + d));
            }
            return "";
        }
    }
}