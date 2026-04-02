using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: UTILITIES (Toán học Hình học & Dọn rác bản vẽ)
        // ====================================================================

        /// <summary>
        /// Dọn rác: Xóa toàn bộ Text, Balloon, Guide và Force Symbol cũ nằm bên trong Panel
        /// </summary>
        public void CleanupOldLabels(Transaction tr, BlockTableRecord space, PanelData panel)
        {
            foreach (ObjectId id in space)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                if (ent == null) continue;

                // 1. Dọn dẹp đường Guide (Layer Mechanical-AM_5)
                if (ent.Layer == "Mechanical-AM_5")
                {
                    if (ent is Leader ldr && ldr.NumVertices > 0)
                    {
                        Point3d p1 = ldr.VertexAt(0);
                        Point3d p2 = ldr.VertexAt(1);
                        Point3d mid = new Point3d((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2, 0);
                        if (IsPointInsidePolyline(panel.PolyId, mid, tr)) ent.Erase();
                    }
                    else if (ent is Polyline guidePoly && guidePoly.NumberOfVertices > 0)
                    {
                        Point3d p1 = guidePoly.GetPoint3dAt(0);
                        Point3d p2 = guidePoly.GetPoint3dAt(guidePoly.NumberOfVertices - 1);
                        Point3d mid = new Point3d((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2, 0);
                        if (IsPointInsidePolyline(panel.PolyId, mid, tr)) ent.Erase();
                    }
                    continue; 
                }

                // Chỉ quét tiếp các đối tượng ở Layer 0
                if (ent.Layer != "0") continue;

                // 2. Dọn dẹp Text m2 và Text Tên (chứa %%u)
                if (ent is DBText dbText)
                {
                    if (dbText.TextString.Contains("m2") || dbText.TextString.Contains("%%u") || dbText.ColorIndex == 4)
                    {
                        Point3d txtPt = (dbText.HorizontalMode == TextHorizontalMode.TextLeft && dbText.VerticalMode == TextVerticalMode.TextBase) ? dbText.Position : dbText.AlignmentPoint;
                        if (IsPointInsidePolyline(panel.PolyId, txtPt, tr)) ent.Erase();
                    }
                }
                else if (ent is MText mtext)
                {
                    if (mtext.Contents.Contains("m2") || mtext.Contents.Contains("\\L"))
                    {
                        if (IsPointInsidePolyline(panel.PolyId, mtext.Location, tr)) ent.Erase();
                    }
                }
                // 3. Dọn dẹp Block COG
                else if (ent is BlockReference blk)
                {
                    if (blk.Name.ToUpper() == "COG") 
                    {
                        if (IsPointInsidePolyline(panel.PolyId, blk.Position, tr)) ent.Erase();
                    }
                }
                // 4. Dọn dẹp vòng tròn Balloon cũ (Màu 4 - Cyan)
                else if (ent is Circle circ && circ.ColorIndex == 4) 
                {
                    if (IsPointInsidePolyline(panel.PolyId, circ.Center, tr)) ent.Erase();
                }
                // 5. Dọn dẹp Ký hiệu Force Symbol cũ (Màu 7 - Trắng/Đen)
                else if (ent.ColorIndex == 7 && (ent is Hatch || ent is Polyline || ent is Circle))
                {
                    Point3d chkPt = Point3d.Origin;
                    if (ent is Hatch h) { try { chkPt = GetExtentsCenter(h.GeometricExtents); } catch {} }
                    else if (ent is Circle c) chkPt = c.Center;
                    else if (ent is Polyline p) { try { chkPt = GetExtentsCenter(p.GeometricExtents); } catch {} }
                    
                    if (chkPt != Point3d.Origin && IsPointInsidePolyline(panel.PolyId, chkPt, tr)) ent.Erase();
                }
            }
        }

        /// <summary>
        /// Thuật toán Ray-Casting: Kiểm tra xem 1 điểm có nằm bên trong một Đa giác (Polyline) hay không
        /// </summary>
        public bool IsPointInsidePolyline(ObjectId polyId, Point3d pt, Transaction tr)
        {
            Polyline poly = tr.GetObject(polyId, OpenMode.ForRead) as Polyline;
            bool isInside = false;
            int n = poly.NumberOfVertices;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point3d p1 = poly.GetPoint3dAt(i);
                Point3d p2 = poly.GetPoint3dAt(j);

                if (((p1.Y > pt.Y) != (p2.Y > pt.Y)) &&
                    (pt.X < (p2.X - p1.X) * (pt.Y - p1.Y) / (p2.Y - p1.Y) + p1.X))
                {
                    isInside = !isInside;
                }
            }
            return isInside;
        }

        /// <summary>
        /// Toán học Vector: Tính khoảng cách ngắn nhất từ 1 điểm đến 1 đoạn thẳng
        /// </summary>
        public double DistanceToLine(Point3d pt, Point3d lineStart, Point3d lineEnd)
        {
            double A = pt.X - lineStart.X, B = pt.Y - lineStart.Y;
            double C = lineEnd.X - lineStart.X, D = lineEnd.Y - lineStart.Y;
            double dot = A * C + B * D, len_sq = C * C + D * D;
            double param = len_sq != 0 ? dot / len_sq : -1;

            double xx = param < 0 ? lineStart.X : (param > 1 ? lineEnd.X : lineStart.X + param * C);
            double yy = param < 0 ? lineStart.Y : (param > 1 ? lineEnd.Y : lineStart.Y + param * D);

            return Math.Sqrt(Math.Pow(pt.X - xx, 2) + Math.Pow(pt.Y - yy, 2));
        }
    }
}