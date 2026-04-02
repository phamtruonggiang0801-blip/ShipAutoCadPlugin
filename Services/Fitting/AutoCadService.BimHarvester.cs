using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using Newtonsoft.Json;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // [CẬP NHẬT MỚI]: Thêm tham số targetBomType do Leader quyết định từ UI
        public void BatchImportBimFittings(string[] jsonPaths, string targetBomType)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            int totalViewsHarvested = 0;
            int totalNewFittings = 0;
            int totalUpdatedFittings = 0;

            try
            {
                using (doc.LockDocument())
                {
                    foreach (string jsonPath in jsonPaths)
                    {
                        string dwgPath = Path.ChangeExtension(jsonPath, ".dwg");
                        if (!File.Exists(dwgPath))
                        {
                            ed.WriteMessage($"\n[Warning] DWG not found for: {Path.GetFileName(jsonPath)}");
                            continue;
                        }

                        // Truyền quyết định của Leader xuống hàm xử lý
                        var stats = HarvestViewsByBoundingBox(dwgPath, jsonPath, targetBomType);
                        
                        totalViewsHarvested += stats.Item1;
                        totalNewFittings += stats.Item2;
                        totalUpdatedFittings += stats.Item3;
                    }

                    if (totalViewsHarvested > 0)
                    {
                        ed.Regen();
                        
                        string msg = $"Batch Import Completed!\n\n" +
                                     $"• Total Files Processed: {jsonPaths.Length}\n" +
                                     $"• Fittings Classified as: [{targetBomType.ToUpper()}]\n" + // Báo cáo loại BOM
                                     $"• Total Views Extracted: {totalViewsHarvested}\n" +
                                     $"• New Fittings Added: {totalNewFittings}\n" +
                                     $"• Revisions Updated: {totalUpdatedFittings}\n\n" +
                                     $"Library is successfully updated at C:\\Temp_BIM_Library.";
                                     
                        System.Windows.MessageBox.Show(msg, "Fitting Library Publisher", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[Critical Error] {ex.Message}");
            }
        }

        // [CẬP NHẬT MỚI]: Thêm tham số targetBomType
        private Tuple<int, int, int> HarvestViewsByBoundingBox(string dwgPath, string jsonPath, string targetBomType)
        {
            Database destDb = HostApplicationServices.WorkingDatabase;
            int viewCount = 0;
            int newCount = 0;
            int updatedCount = 0;

            string jsonContent = File.ReadAllText(jsonPath);
            FittingMetadata metadata = JsonConvert.DeserializeObject<FittingMetadata>(jsonContent);
            
            string baseFileName = Path.GetFileNameWithoutExtension(dwgPath);

            if (metadata.Views == null || !metadata.Views.Any()) return new Tuple<int, int, int>(0, 0, 0);

            List<Tuple<ObjectId, CatalogItem>> blocksToPublish = new List<Tuple<ObjectId, CatalogItem>>();

            using (Database sourceDb = new Database(false, true))
            {
                sourceDb.ReadDwgFile(dwgPath, FileShare.Read, true, "");

                using (Transaction destTr = destDb.TransactionManager.StartTransaction())
                using (Transaction srcTr = sourceDb.TransactionManager.StartTransaction())
                {
                    CheckAndCreateLayer(destDb, destTr, "Mechanical-AM_3", 6); 
                    CheckAndCreateLayer(destDb, destTr, "Mechanical-AM_7", 4); 
                    CheckAndCreateLayer(destDb, destTr, "Mechanical-AM_9", 7); 

                    BlockTable destBt = (BlockTable)destTr.GetObject(destDb.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord srcMs = (BlockTableRecord)srcTr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(sourceDb), OpenMode.ForRead);

                    foreach (var view in metadata.Views)
                    {
                        double tolerance = 50.0; 
                        double minX = view.CenterX - (view.Width / 2.0) - tolerance;
                        double maxX = view.CenterX + (view.Width / 2.0) + tolerance;
                        double minY = view.CenterY - (view.Height / 2.0) - tolerance;
                        double maxY = view.CenterY + (view.Height / 2.0) + tolerance;

                        ObjectIdCollection entsToClone = new ObjectIdCollection();

                        foreach (ObjectId entId in srcMs)
                        {
                            Entity ent = srcTr.GetObject(entId, OpenMode.ForRead) as Entity;
                            
                            // ========================================================
                            // STRICT WHITELIST (Điểm danh đích danh)
                            // ========================================================
                            if (ent == null) continue;

                            bool isAllowed = ent is Line || 
                                             ent is Arc || 
                                             ent is Circle || 
                                             ent is Polyline || 
                                             ent is Polyline2d || 
                                             ent is Polyline3d || 
                                             ent is Spline || 
                                             ent is Ellipse;

                            if (!isAllowed) 
                            {
                                continue;
                            }

                            try
                            {
                                Extents3d ext = ent.GeometricExtents;
                                Point3d centerPt = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2, 0);

                                if (centerPt.X >= minX && centerPt.X <= maxX && centerPt.Y >= minY && centerPt.Y <= maxY)
                                    entsToClone.Add(entId);
                            }
                            catch { }
                        }

                        if (entsToClone.Count == 0) continue;

                        string uniqueName = GenerateUniqueBlockName(destBt, $"{baseFileName}_{view.Name}");
                        BlockTableRecord newBtr = new BlockTableRecord { Name = uniqueName };
                        destBt.Add(newBtr);
                        destTr.AddNewlyCreatedDBObject(newBtr, true);

                        IdMapping mapping = new IdMapping();
                        sourceDb.WblockCloneObjects(entsToClone, newBtr.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);
                        
                        TransformEntitiesInBlock(newBtr, destTr, new Vector3d(-view.CenterX, -view.CenterY, 0));

                        CreateViewLabel(newBtr, destTr, uniqueName.ToUpper(), -(view.Height / 2.0) - 20.0);
                        
                        // [CẬP NHẬT MỚI]: Truyền targetBomType vào hàm tạo Attribute
                        InjectBimAttributes(newBtr, destTr, metadata, targetBomType);

                        string exportPath = Path.Combine(@"C:\Temp_BIM_Library", uniqueName + ".dwg");
                        
                        blocksToPublish.Add(new Tuple<ObjectId, CatalogItem>(newBtr.ObjectId, new CatalogItem {
                            BlockName = uniqueName, 
                            PartNumber = metadata.PartNumber,
                            Description = metadata.Description, 
                            Material = metadata.Material,
                            Mass = metadata.Mass, 
                            Revision = metadata.Revision, 
                            Designer = metadata.Designer, 
                            Title = metadata.Title,
                            BomType = targetBomType, // <--- Ghi nhận phân loại vào Database MasterCatalog
                            FilePath = exportPath
                        }));

                        viewCount++;
                    }
                    destTr.Commit();
                }
            }

            if (blocksToPublish.Any()) 
            {
                var libraryStats = PublishToCentralLibrary(blocksToPublish);
                newCount = libraryStats.Item1;
                updatedCount = libraryStats.Item2;
            }

            return new Tuple<int, int, int>(viewCount, newCount, updatedCount);
        }

        private string GenerateUniqueBlockName(BlockTable bt, string baseName)
        {
            int suffix = 1;
            string name = baseName;
            while (bt.Has(name)) name = $"{baseName}_{suffix++}";
            return name;
        }

        private void TransformEntitiesInBlock(BlockTableRecord btr, Transaction tr, Vector3d moveVec)
        {
            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                if (ent == null) continue;
                ent.TransformBy(Matrix3d.Displacement(moveVec));
                
                string ly = ent.Layer.ToUpper();
                if (ly.Contains("VISIBLE")) { ent.Layer = "0"; ent.Color = Color.FromColorIndex(ColorMethod.ByBlock, 0); }
                else if (ly.Contains("HIDDEN")) ent.Layer = "Mechanical-AM_3";
                else if (ly.Contains("CENTER")) ent.Layer = "Mechanical-AM_7";

                // ========================================================
                // [FIX ISSUE 3]: Ép Linetype và LineWeight về ByLayer chuẩn
                // ========================================================
                try 
                {
                    ent.Linetype = "ByLayer";
                    ent.LineWeight = LineWeight.ByLayer;
                } 
                catch { /* Bỏ qua nếu Entity không hỗ trợ đổi Linetype */ }
            }
        }

        private void CreateViewLabel(BlockTableRecord btr, Transaction tr, string text, double yPos)
        {
            DBText label = new DBText();
            label.SetDatabaseDefaults();
            Point3d pt = new Point3d(0, yPos, 0);
            label.Position = pt;
            label.Height = 10.0;
            label.TextString = text;
            label.Justify = AttachmentPoint.TopCenter;
            label.AlignmentPoint = pt;
            label.Layer = "Mechanical-AM_9";
            btr.AppendEntity(label);
            tr.AddNewlyCreatedDBObject(label, true);
        }

        // [CẬP NHẬT MỚI]: Thêm tham số bomType và lệnh tạo Attribute tàng hình
        private void InjectBimAttributes(BlockTableRecord btr, Transaction tr, FittingMetadata meta, string bomType)
        {
            AddAttributeDef(btr, tr, "PART_NUMBER", meta.PartNumber, "PN", true);
            AddAttributeDef(btr, tr, "DESCRIPTION", meta.Description, "DESC", true);
            AddAttributeDef(btr, tr, "MATERIAL", meta.Material, "MAT", true);
            AddAttributeDef(btr, tr, "MASS", meta.Mass, "MASS", true);
            AddAttributeDef(btr, tr, "REVISION", meta.Revision, "REV", true);
            
            AddAttributeDef(btr, tr, "DESIGNER", meta.Designer, "DESIGNER", true);
            AddAttributeDef(btr, tr, "TITLE", meta.Title, "TITLE", true);
            
            // Đóng dấu phân loại BOM_TYPE thẳng vào Block một cách tàng hình (Invisible = true)
            AddAttributeDef(btr, tr, "BOM_TYPE", bomType.ToUpper(), "BOM_TYPE", true);
        }
    }
}