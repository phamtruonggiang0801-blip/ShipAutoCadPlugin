using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: PANEL COG (Center of Gravity)
        // ====================================================================

        /// <summary>
        /// Hàm chèn Block COG vào bản vẽ tại vị trí trọng tâm của Panel
        /// </summary>
        public void DrawCOGBlock(BlockTableRecord currentSpace, Transaction tr, Database db, PanelData panel)
        {
            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
            if (bt.Has("COG"))
            {
                BlockReference cogRef = new BlockReference(panel.CogPoint, bt["COG"]);
                cogRef.Layer = "0";
                currentSpace.AppendEntity(cogRef);
                tr.AddNewlyCreatedDBObject(cogRef, true);
            }
        }

        /// <summary>
        /// Thuật toán tính trọng tâm của Polyline. Ưu tiên dùng Region, dự phòng dùng Bounding Box.
        /// </summary>
        private Point3d GetPolylineCentroid(Polyline poly)
        {
            try
            {
                using (DBObjectCollection objs = new DBObjectCollection())
                {
                    objs.Add(poly);
                    using (DBObjectCollection regions = Region.CreateFromCurves(objs))
                    {
                        if (regions.Count > 0)
                        {
                            Region reg = (Region)regions[0];
                            Point3d origin = Point3d.Origin;
                            Vector3d xAxis = Vector3d.XAxis;
                            Vector3d yAxis = Vector3d.YAxis;
                            Point2d cent2d = reg.AreaProperties(ref origin, ref xAxis, ref yAxis).Centroid;
                            return new Point3d(cent2d.X, cent2d.Y, 0);
                        }
                    }
                }
            }
            catch { }
            
            // Fallback: Tính trung điểm của Bounding Box nếu tạo Region thất bại
            Extents3d bounds = poly.GeometricExtents;
            return new Point3d((bounds.MinPoint.X + bounds.MaxPoint.X) / 2, (bounds.MinPoint.Y + bounds.MaxPoint.Y) / 2, 0);
        }

        /// <summary>
        /// Đảm bảo Block "COG" luôn tồn tại trong bản vẽ (Copy từ thư viện hoặc tự vẽ tay)
        /// </summary>
        private void EnsureCOGBlockExists(Database destDb)
        {
            using (Transaction tr = destDb.TransactionManager.StartTransaction())
            {
                if (((BlockTable)tr.GetObject(destDb.BlockTableId, OpenMode.ForRead)).Has("COG")) 
                { 
                    tr.Commit(); 
                    return; 
                }
                tr.Commit();
            }

            string sourceFile = @"C:\CustomTools\Symbol.dwg";
            if (!System.IO.File.Exists(sourceFile)) 
            { 
                CreateFallbackCOGBlock(destDb); 
                return; 
            }

            try
            {
                using (Database sourceDb = new Database(false, true))
                {
                    sourceDb.ReadDwgFile(sourceFile, FileOpenMode.OpenForReadAndAllShare, true, "");
                    ObjectId sourceBlockId = ObjectId.Null;
                    using (Transaction tr = sourceDb.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                        if (bt.Has("COG")) sourceBlockId = bt["COG"];
                        tr.Commit();
                    }
                    if (sourceBlockId != ObjectId.Null)
                    {
                        ObjectIdCollection ids = new ObjectIdCollection { sourceBlockId };
                        destDb.WblockCloneObjects(ids, destDb.BlockTableId, new IdMapping(), DuplicateRecordCloning.Ignore, false);
                    }
                    else CreateFallbackCOGBlock(destDb);
                }
            }
            catch { CreateFallbackCOGBlock(destDb); }
        }

        /// <summary>
        /// Tự động vẽ Block COG nếu không tìm thấy file thư viện
        /// </summary>
        private void CreateFallbackCOGBlock(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForWrite) as BlockTable;
                if (!bt.Has("COG"))
                {
                    BlockTableRecord btr = new BlockTableRecord { Name = "COG" };
                    bt.Add(btr); 
                    tr.AddNewlyCreatedDBObject(btr, true);
                    
                    Circle c = new Circle(Point3d.Origin, Vector3d.ZAxis, 150) { ColorIndex = 2 }; 
                    btr.AppendEntity(c); 
                    tr.AddNewlyCreatedDBObject(c, true);
                    
                    Line l1 = new Line(new Point3d(-250, 0, 0), new Point3d(250, 0, 0)) { ColorIndex = 2 }; 
                    btr.AppendEntity(l1); 
                    tr.AddNewlyCreatedDBObject(l1, true);
                    
                    Line l2 = new Line(new Point3d(0, -250, 0), new Point3d(0, 250, 0)) { ColorIndex = 2 }; 
                    btr.AppendEntity(l2); 
                    tr.AddNewlyCreatedDBObject(l2, true);
                }
                tr.Commit();
            }
        }
    }
}