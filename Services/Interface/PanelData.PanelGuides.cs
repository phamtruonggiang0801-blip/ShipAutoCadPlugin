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
        // MODULE 4: PANEL GUIDES (Vẽ Guide 2, Guide 3)
        // ====================================================================

        /// <summary>
        /// Xử lý logic chọn góc và vẽ Guide cho toàn bộ Panel (Dành cho luồng Auto)
        /// </summary>
        public void DrawPanelGuidesAuto(BlockTableRecord space, Transaction tr, PanelData panel, List<Point3d> liftingPoints, int guidingType, Vector3d clVector, Point3d clStart, Point3d clEnd)
        {
            List<Point3d> targetPts = new List<Point3d>();

            if (guidingType == 1) // Guide 2: 4 góc
            {
                targetPts = liftingPoints;
            }
            else if (guidingType == 2) // Guide 3: Phân bổ thông minh (Giáp P/C/S)
            {
                Func<Point3d, double> getSideValue = p => (clVector.X * (p.Y - clStart.Y)) - (clVector.Y * (p.X - clStart.X));

                if (panel.Classification.Contains("P"))
                {
                    targetPts = liftingPoints.OrderBy(p => DistanceToLine(p, clStart, clEnd)).Take(2).ToList();
                }
                else if (panel.Classification.Contains("C"))
                {
                    targetPts = liftingPoints.OrderByDescending(p => getSideValue(p)).Take(2).ToList();
                }
                else if (panel.Classification.Contains("S"))
                {
                    targetPts = liftingPoints.OrderBy(p => DistanceToLine(p, clStart, clEnd)).Take(2).ToList();
                }
            }

            foreach (Point3d pt in targetPts)
            {
                DrawGuiding(space, tr, pt, panel.CogPoint, guidingType, panel.Classification);
            }
        }

        /// <summary>
        /// Tạo Layer tiêu chuẩn cho mũi tên Guide
        /// </summary>
        public void EnsureGuidingLayerExists(Database db, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            if (!lt.Has("Mechanical-AM_5"))
            {
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = "Mechanical-AM_5";
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3);
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        /// <summary>
        /// Hàm điều phối vẽ Guide 2 (Vuông) hoặc Guide 3 (Ngàm kẹp 3 râu)
        /// </summary>
        public void DrawGuiding(BlockTableRecord space, Transaction tr, Point3d pt, Point3d cogPt, int guideType, string classification)
        {
            // Vector hướng VÀO TRONG lòng Panel
            Vector3d inDirX = pt.X > cogPt.X ? -Vector3d.XAxis : Vector3d.XAxis;
            Vector3d inDirY = pt.Y > cogPt.Y ? -Vector3d.YAxis : Vector3d.YAxis;
            
            // --- CẬP NHẬT KÍCH THƯỚC (IN A1) VÀ TỌA ĐỘ OFFSET THÔNG MINH ---
            double gap = 400.0;        // Mũi nhọn dừng lại cách điểm mốc 400mm (Gấp đôi cũ)
            double length = 800.0;     // Thân mũi tên dài 800mm (Gấp đôi cũ)
            
            double edgeOffset = 100.0; // Khoảng cách né viền Panel (Giữ nguyên cho đẹp)
            double shiftX = 2000.0;    // Dịch toàn bộ cụm mũi tên ra xa 2000mm theo trục X để né Block Detail
            double clampWidth = 400.0; // Ngàm kẹp: Khoảng cách giữa 2 râu Y1 và Y2 (Gấp đôi cũ)
            
            if (guideType == 1) // Guide 2 (Góc vuông)
            {
                // MŨI X: Chạy ngang. Dời 100mm theo Y để né viền, VÀ dời 2000mm theo X để đi cùng cụm mũi Y
                Point3d startPtX = pt + inDirX * shiftX;
                DrawOrthogonalArrow(space, tr, startPtX, inDirX, inDirY, edgeOffset, gap, length, true);
                
                // MŨI Y: Chạy dọc. Dời 2000mm theo X để né Detail Block
                DrawOrthogonalArrow(space, tr, pt, inDirY, inDirX, shiftX, gap, length, true);
            }
            else if (guideType == 2) // Guide 3 (Ngàm kẹp)
            {
                // MŨI X: Chạy ngang, dời X 2000mm, Y 100mm, đâm RA NGOÀI
                Point3d startPtX = pt + inDirX * shiftX;
                DrawOrthogonalArrow(space, tr, startPtX, inDirX, inDirY, edgeOffset, gap, length, true);
                
                // MŨI Y1 (Râu kẹp trong): Chạy dọc, dời X 2000mm, đâm RA NGOÀI
                DrawOrthogonalArrow(space, tr, pt, inDirY, inDirX, shiftX, gap, length, true);
                
                // MŨI Y2 (Râu kẹp ngoài): Chạy dọc, dời X 2400mm (2000 + 400), đâm VÀO TRONG tạo gọng kìm
                DrawOrthogonalArrow(space, tr, pt, inDirY, inDirX, shiftX + clampWidth, gap, length, false); 
            }
        }

        /// <summary>
        /// Hàm vẽ thực thể Polyline hình mũi tên với Offset tính toán bằng Toán học Vector
        /// </summary>
        private void DrawOrthogonalArrow(BlockTableRecord space, Transaction tr, Point3d cornerPt, Vector3d arrowDir, Vector3d offsetDir, double offsetVal, double gap, double length, bool pointsOutward)
        {
            // Tịnh tiến mũi tên để tạo offset
            Point3d aimLinePt = cornerPt + offsetDir * offsetVal;
            
            Point3d tipPt, tailPt;
            
            if (pointsOutward) // LỰC TÁC DỤNG: Đâm từ trong RA NGOÀI mép
            {
                tipPt = aimLinePt + arrowDir * gap;              
                tailPt = tipPt + arrowDir * length;              
            }
            else // NGÀM KẸP NGƯỢC: Đâm từ mép VÀO TRONG
            {
                tailPt = aimLinePt + arrowDir * gap;             
                tipPt = tailPt + arrowDir * length;              
            }

            Vector3d drawDir = (tipPt - tailPt).GetNormal();
            
            // --- TĂNG GẤP ĐÔI KÍCH THƯỚC NGÒI MŨI TÊN (IN A1) ---
            double arrowHeadLen = 300.0; // Cũ: 150
            double arrowWidth = 150.0;   // Cũ: 75
            Point3d arrowBasePt = tipPt - drawDir * arrowHeadLen;

            Polyline poly = new Polyline();
            poly.SetDatabaseDefaults();
            
            poly.AddVertexAt(0, new Point2d(tailPt.X, tailPt.Y), 0, 0, 0);
            poly.AddVertexAt(1, new Point2d(arrowBasePt.X, arrowBasePt.Y), 0, arrowWidth, 0);
            poly.AddVertexAt(2, new Point2d(tipPt.X, tipPt.Y), 0, 0, 0);

            poly.Layer = "Mechanical-AM_5";
            poly.ColorIndex = 3; // Màu xanh lá cây
            
            space.AppendEntity(poly);
            tr.AddNewlyCreatedDBObject(poly, true);
        }
    }
}