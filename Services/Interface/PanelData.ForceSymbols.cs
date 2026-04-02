using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: FORCE SYMBOLS (T1, T2, T3)
        // ====================================================================

        // Tọa độ Offset tương đối so với Tâm của Balloon (Chỉnh ở đây nếu cần to/nhỏ/xa/gần)
        private const double SYMBOL_OFFSET_X = 800.0;
        private const double SYMBOL_OFFSET_Y = -800.0;
        private const double SYMBOL_RADIUS = 250.0;

        /// <summary>
        /// Hàm vẽ Ký hiệu Force (T1=Tam giác, T2=Vuông, T3=Tròn)
        /// </summary>
        public void DrawForceSymbol(BlockTableRecord space, Transaction tr, Point3d balloonCenter, string type)
        {
            // Xác định tâm của Symbol
            Point3d symCenter = new Point3d(balloonCenter.X + SYMBOL_OFFSET_X, balloonCenter.Y + SYMBOL_OFFSET_Y, balloonCenter.Z);

            ObjectId boundaryId = ObjectId.Null;

            if (type.ToUpper() == "T3") // T3: Tròn
            {
                Circle circ = new Circle(symCenter, Vector3d.ZAxis, SYMBOL_RADIUS);
                circ.ColorIndex = 7; // Trắng (in ra đen)
                circ.Layer = "0";
                boundaryId = space.AppendEntity(circ);
                tr.AddNewlyCreatedDBObject(circ, true);
            }
            else // T1 (Tam giác) hoặc T2 (Vuông)
            {
                Polyline poly = new Polyline();
                poly.SetDatabaseDefaults();
                poly.ColorIndex = 7;
                poly.Layer = "0";
                poly.Closed = true;

                if (type.ToUpper() == "T1") // T1: Tam giác đều nội tiếp
                {
                    double h = SYMBOL_RADIUS;
                    // Tọa độ 3 đỉnh tam giác đều
                    poly.AddVertexAt(0, new Point2d(symCenter.X, symCenter.Y + h), 0, 0, 0);
                    poly.AddVertexAt(1, new Point2d(symCenter.X - h * 0.866, symCenter.Y - h * 0.5), 0, 0, 0);
                    poly.AddVertexAt(2, new Point2d(symCenter.X + h * 0.866, symCenter.Y - h * 0.5), 0, 0, 0);
                }
                else if (type.ToUpper() == "T2") // T2: Hình vuông
                {
                    double r = SYMBOL_RADIUS * 0.85; // Cạnh nhỏ lại 1 chút cho cân đối với T3
                    poly.AddVertexAt(0, new Point2d(symCenter.X - r, symCenter.Y + r), 0, 0, 0);
                    poly.AddVertexAt(1, new Point2d(symCenter.X + r, symCenter.Y + r), 0, 0, 0);
                    poly.AddVertexAt(2, new Point2d(symCenter.X + r, symCenter.Y - r), 0, 0, 0);
                    poly.AddVertexAt(3, new Point2d(symCenter.X - r, symCenter.Y - r), 0, 0, 0);
                }

                boundaryId = space.AppendEntity(poly);
                tr.AddNewlyCreatedDBObject(poly, true);
            }

            // Đổ Solid Hatch màu trắng (in ra đen đặc)
            if (boundaryId != ObjectId.Null)
            {
                Hatch hatch = new Hatch();
                space.AppendEntity(hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);

                hatch.SetDatabaseDefaults();
                hatch.ColorIndex = 7;
                hatch.Layer = "0";
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");

                ObjectIdCollection ids = new ObjectIdCollection();
                ids.Add(boundaryId);
                hatch.AppendLoop(HatchLoopTypes.Default, ids);
                hatch.EvaluateHatch(true);
            }
        }

        /// <summary>
        /// Hàm dọn rác cục bộ: Xóa Symbol cũ chính xác tại 1 điểm Balloon mà không đụng hàng xóm
        /// </summary>
        public void RemoveOldForceSymbol(BlockTableRecord space, Transaction tr, Point3d balloonCenter)
        {
            // Tọa độ kỳ vọng của Symbol cũ
            Point3d expectedSymCenter = new Point3d(balloonCenter.X + SYMBOL_OFFSET_X, balloonCenter.Y + SYMBOL_OFFSET_Y, balloonCenter.Z);
            double searchRadius = SYMBOL_RADIUS * 2.0; // Bán kính tìm kiếm hẹp (gấp đôi bán kính Symbol)

            foreach (ObjectId id in space)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                // Chỉ tìm các đối tượng màu 7 ở layer 0 (Đặc điểm nhận diện Symbol)
                if (ent.ColorIndex == 7 && ent.Layer == "0")
                {
                    bool isNear = false;
                    
                    if (ent is Circle circ)
                    {
                        if (circ.Center.DistanceTo(expectedSymCenter) < searchRadius) isNear = true;
                    }
                    else if (ent is Hatch hatch || ent is Polyline)
                    {
                        try 
                        {
                            Point3d extCenter = GetExtentsCenter(ent.GeometricExtents);
                            if (extCenter.DistanceTo(expectedSymCenter) < searchRadius) isNear = true;
                        } 
                        catch { }
                    }

                    if (isNear)
                    {
                        ent.UpgradeOpen();
                        ent.Erase();
                    }
                }
            }
        }

        // Hàm tiện ích nội bộ hỗ trợ tính trọng tâm hình học Extents
        private Point3d GetExtentsCenter(Extents3d ext)
        {
            return new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5
            );
        }
    }
}