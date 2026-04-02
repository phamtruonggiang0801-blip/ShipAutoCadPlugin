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
        // MODULE 3: PANEL LIFTING (Tính toán góc, chèn Balloon & Safe Push)
        // ====================================================================

        /// <summary>
        /// Hàm vẽ chuỗi Balloon và Force Symbol (mặc định T1) cho Panel
        /// </summary>
        public void DrawBalloonsAndSymbols(BlockTableRecord currentSpace, Transaction tr, Polyline polyObj, List<Point3d> liftingPoints, PanelData panel)
        {
            int lugIndex = 1;
            foreach (Point3d pt in liftingPoints)
            {
                // Tính tiến điểm Balloon vào an toàn bên trong Panel
                Point3d balloonCenter = GetBalloonSafeCenter(polyObj, pt, panel.CogPoint, tr);

                // Vẽ hình tròn Balloon
                Circle balloon = new Circle(balloonCenter, Vector3d.ZAxis, 600.0);
                balloon.ColorIndex = 4; // Màu Cyan
                balloon.Layer = "0";
                currentSpace.AppendEntity(balloon);
                tr.AddNewlyCreatedDBObject(balloon, true);

                // Vẽ số thứ tự
                DBText lugNum = new DBText();
                lugNum.TextString = lugIndex.ToString();
                lugNum.Height = 750;
                lugNum.ColorIndex = 4;
                lugNum.Layer = "0";
                lugNum.Position = balloonCenter;
                lugNum.HorizontalMode = TextHorizontalMode.TextCenter;
                lugNum.VerticalMode = TextVerticalMode.TextVerticalMid;
                lugNum.AlignmentPoint = balloonCenter; 
                
                currentSpace.AppendEntity(lugNum);
                tr.AddNewlyCreatedDBObject(lugNum, true);

                // Gọi hàm từ ForceSymbols.cs để vẽ ký hiệu lực mặc định (T1)
                DrawForceSymbol(currentSpace, tr, balloonCenter, "T1");

                lugIndex++;
            }
        }

        /// <summary>
        /// Lấy 4 đỉnh xa nhất của Bounding Box và dóng xuống viền Polyline
        /// </summary>
        public List<Point3d> CalculateLiftingPoints(Polyline poly)
        {
            List<Point3d> pts = new List<Point3d>();
            Extents3d bounds = poly.GeometricExtents;

            Point3d bbTL = new Point3d(bounds.MinPoint.X, bounds.MaxPoint.Y, 0);
            Point3d bbTR = new Point3d(bounds.MaxPoint.X, bounds.MaxPoint.Y, 0);
            Point3d bbBL = new Point3d(bounds.MinPoint.X, bounds.MinPoint.Y, 0);
            Point3d bbBR = new Point3d(bounds.MaxPoint.X, bounds.MinPoint.Y, 0);

            pts.Add(GetClosestVertex(poly, bbTL));
            pts.Add(GetClosestVertex(poly, bbTR));
            pts.Add(GetClosestVertex(poly, bbBL));
            pts.Add(GetClosestVertex(poly, bbBR));

            return pts.Distinct(new Point3dEqualityComparer()).ToList();
        }

        /// <summary>
        /// Sắp xếp các điểm Lifting Lugs theo thứ tự P/S
        /// </summary>
        public List<Point3d> SortLiftingPoints(List<Point3d> pts, Point3d cogPt, string classification)
        {
            var sorted = pts.OrderBy(p => Math.Atan2(p.Y - cogPt.Y, p.X - cogPt.X)).ToList();
            if (classification.Contains("P")) 
            {
                sorted.Reverse(); 
                Point3d startPt = sorted.OrderBy(p => p.X - p.Y).First(); 
                int idx = sorted.IndexOf(startPt);
                return sorted.Skip(idx).Concat(sorted.Take(idx)).ToList();
            }
            else
            {
                Point3d startPt = sorted.OrderBy(p => p.X + p.Y).First(); 
                int idx = sorted.IndexOf(startPt);
                return sorted.Skip(idx).Concat(sorted.Take(idx)).ToList();
            }
        }

        /// <summary>
        /// Tịnh tiến Balloon theo hướng chéo vào trọng tâm để không đè lên viền
        /// </summary>
        private Point3d GetBalloonSafeCenter(Polyline poly, Point3d cornerPt, Point3d cogPt, Transaction tr)
        {
            // TRẢ LẠI LOGIC ĐƯỜNG CHÉO (DIAGONAL) VÀ TĂNG KHOẢNG CÁCH OFFSET
            Vector3d inDirX = cornerPt.X > cogPt.X ? -Vector3d.XAxis : Vector3d.XAxis;
            Vector3d inDirY = cornerPt.Y > cogPt.Y ? -Vector3d.YAxis : Vector3d.YAxis;
            Vector3d diagVec = (inDirX + inDirY).GetNormal();
            
            Point3d finalPt = cornerPt;
            
            for (int step = 0; step < 6; step++) 
            {
                // Tăng khoảng cách gốc lên 3000, giãn cách mỗi bước lên 1000
                finalPt = cornerPt + diagVec * (3000.0 + step * 1000.0);
                if (IsPointInsidePolyline(poly.ObjectId, finalPt, tr)) return finalPt;
            }
            return cornerPt + diagVec * 3000.0; 
        }

        /// <summary>
        /// Tìm đỉnh gần nhất của Polyline so với một điểm cho trước
        /// </summary>
        public Point3d GetClosestVertex(Polyline poly, Point3d target)
        {
            Point3d closest = poly.GetPoint3dAt(0);
            double minDist = target.DistanceTo(closest);
            for (int i = 1; i < poly.NumberOfVertices; i++)
            {
                Point3d pt = poly.GetPoint3dAt(i);
                double dist = target.DistanceTo(pt);
                if (dist < minDist) { minDist = dist; closest = pt; }
            }
            return closest;
        }
    }
}