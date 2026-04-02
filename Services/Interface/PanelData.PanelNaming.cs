using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE 2: PANEL NAMING (Phân nhóm & Vẽ Nhãn Tên/Diện tích)
        // ====================================================================

        /// <summary>
        /// Thuật toán ma trận phân nhóm Panel theo trục X và gán phân loại P/S/C
        /// </summary>
        public List<PanelData> CalculateAndAssignPanelNames(List<PanelData> validPanels, Point3d clStart, Vector3d clVector, Point3d clEnd)
        {
            // Sắp xếp lại danh sách Panel theo trục X giảm dần (Từ Aft về Forward)
            var sortedPanels = validPanels.OrderByDescending(p => p.CogPoint.X).ToList();
            List<List<PanelData>> rows = new List<List<PanelData>>();

            // Gộp nhóm các Panel có sự giao thoa về tọa độ X (Cùng nằm trên 1 sườn/frame)
            foreach (var panel in sortedPanels)
            {
                bool addedToRow = false;
                foreach (var row in rows)
                {
                    if (panel.MinX <= row.Max(p => p.MaxX) && panel.MaxX >= row.Min(p => p.MinX))
                    {
                        row.Add(panel);
                        addedToRow = true;
                        break;
                    }
                }
                if (!addedToRow) rows.Add(new List<PanelData> { panel });
            }

            // Xử lý Ma trận: Đánh số Group và gán tiền tố (P1, P2, C, S1, S2...)
            int groupNum = 1;
            foreach (var row in rows)
            {
                var centerPanels = row.Where(p => p.IntersectsCenterline).ToList();
                var portPanels = new List<PanelData>();
                var stbdPanels = new List<PanelData>();

                foreach (var panel in row)
                {
                    if (panel.IntersectsCenterline) continue;
                    
                    // Dùng tích có hướng (Cross Product 2D) để xác định Trái (Port) hay Phải (Starboard)
                    Vector3d panelVec = panel.CogPoint - clStart;
                    if ((clVector.X * panelVec.Y) - (clVector.Y * panelVec.X) > 0) portPanels.Add(panel);
                    else stbdPanels.Add(panel);
                }

                bool hasCenter = centerPanels.Count > 0;
                foreach (var c in centerPanels) { c.Classification = "C"; c.GroupNumber = groupNum; }

                // Xử lý nhánh Port (Trái)
                portPanels = portPanels.OrderBy(p => DistanceToLine(p.CogPoint, clStart, clEnd)).ToList();
                for (int i = 0; i < portPanels.Count; i++) portPanels[i].GroupNumber = groupNum; 
                
                if (portPanels.Count == 1) portPanels[0].Classification = "P";
                else if (portPanels.Count == 2)
                {
                    if (hasCenter) { portPanels[0].Classification = "CP"; portPanels[1].Classification = "P"; }
                    else { portPanels[0].Classification = "P1"; portPanels[1].Classification = "P2"; }
                }
                else if (portPanels.Count >= 3)
                {
                    if (hasCenter) { portPanels[0].Classification = "CP"; for (int i = 1; i < portPanels.Count; i++) portPanels[i].Classification = "P" + i; }
                    else for (int i = 0; i < portPanels.Count; i++) portPanels[i].Classification = "P" + (i + 1);
                }

                // Xử lý nhánh Starboard (Phải)
                stbdPanels = stbdPanels.OrderBy(p => DistanceToLine(p.CogPoint, clStart, clEnd)).ToList();
                for (int i = 0; i < stbdPanels.Count; i++) stbdPanels[i].GroupNumber = groupNum; 
                
                if (stbdPanels.Count == 1) stbdPanels[0].Classification = "S";
                else if (stbdPanels.Count == 2)
                {
                    if (hasCenter) { stbdPanels[0].Classification = "CS"; stbdPanels[1].Classification = "S"; }
                    else { stbdPanels[0].Classification = "S1"; stbdPanels[1].Classification = "S2"; }
                }
                else if (stbdPanels.Count >= 3)
                {
                    if (hasCenter) { stbdPanels[0].Classification = "CS"; for (int i = 1; i < stbdPanels.Count; i++) stbdPanels[i].Classification = "S" + i; }
                    else for (int i = 0; i < stbdPanels.Count; i++) stbdPanels[i].Classification = "S" + (i + 1);
                }
                groupNum++;
            }

            return sortedPanels; // Trả về danh sách đã được sắp xếp và gán thuộc tính hoàn chỉnh
        }

        /// <summary>
        /// Hàm vẽ nhãn Tên Panel và Diện tích m2 vào bản vẽ
        /// </summary>
        public void DrawPanelLabels(BlockTableRecord currentSpace, Transaction tr, PanelData panel, string deckNumber)
        {
            // Vẽ Tên Panel (VD: 601P)
            DBText nameText = new DBText();
            nameText.TextString = "%%u" + deckNumber + panel.GroupNumber.ToString("00") + panel.Classification; 
            nameText.Height = 500;
            nameText.ColorIndex = 2; // Màu vàng
            nameText.Layer = "0"; 
            nameText.Position = new Point3d(panel.CogPoint.X - 750, panel.CogPoint.Y - 1250, 0); 
            nameText.HorizontalMode = TextHorizontalMode.TextCenter;
            nameText.VerticalMode = TextVerticalMode.TextVerticalMid;
            nameText.AlignmentPoint = nameText.Position; 
            
            currentSpace.AppendEntity(nameText);
            tr.AddNewlyCreatedDBObject(nameText, true);

            // Vẽ Diện tích Panel (VD: 15m2)
            DBText areaText = new DBText();
            areaText.TextString = (panel.Area / 1000000.0).ToString("F0") + "m2";
            areaText.Height = 500;
            areaText.ColorIndex = 2; 
            areaText.Layer = "0";
            areaText.Position = new Point3d(panel.CogPoint.X - 750, panel.CogPoint.Y + 750, 0);
            areaText.HorizontalMode = TextHorizontalMode.TextCenter;
            areaText.VerticalMode = TextVerticalMode.TextVerticalMid;
            areaText.AlignmentPoint = areaText.Position;
            
            currentSpace.AppendEntity(areaText);
            tr.AddNewlyCreatedDBObject(areaText, true);
        }
    }
}