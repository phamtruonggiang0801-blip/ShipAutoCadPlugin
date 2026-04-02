using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: EXTRACT FROM BLOCK (Chế độ Click liên tục)
        // ====================================================================
        public void ExtractEntitiesFromBlock()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            int extractCount = 0;

            // Vòng lặp cho phép người dùng click liên tục nhiều đối tượng
            while (true)
            {
                PromptNestedEntityOptions pneo = new PromptNestedEntityOptions("\nSelect nested object to extract [Press Enter/Esc to Finish]: ");
                pneo.AllowNone = true; // Cho phép ấn Enter để thoát
                
                PromptNestedEntityResult pner = ed.GetNestedEntity(pneo);

                // Nếu người dùng ấn Enter, Esc, hoặc Chuột phải -> Thoát vòng lặp
                if (pner.Status == PromptStatus.Cancel || pner.Status == PromptStatus.None)
                {
                    break;
                }

                if (pner.Status != PromptStatus.OK) continue;

                if (pner.GetContainers().Length == 0)
                {
                    ed.WriteMessage("\nThe selected object is not inside a Block.");
                    continue;
                }

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        try
                        {
                            Entity nestedEnt = tr.GetObject(pner.ObjectId, OpenMode.ForWrite) as Entity;
                            if (nestedEnt == null) continue;

                            // Clone và Transform bằng Ma trận thuận
                            Entity extractedEnt = nestedEnt.Clone() as Entity;
                            extractedEnt.TransformBy(pner.Transform);

                            // Đưa ra Model Space
                            BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                            currentSpace.AppendEntity(extractedEnt);
                            tr.AddNewlyCreatedDBObject(extractedEnt, true);

                            // Xóa gốc
                            nestedEnt.Erase();

                            // Cập nhật Block cha trên màn hình
                            ObjectId parentBlockId = pner.GetContainers()[0];
                            BlockReference parentBlock = tr.GetObject(parentBlockId, OpenMode.ForWrite) as BlockReference;
                            if (parentBlock != null)
                            {
                                parentBlock.RecordGraphicsModified(true);
                            }

                            tr.Commit();
                            extractCount++;
                            ed.WriteMessage($"\n>> Extracted {extractCount} object(s).");
                        }
                        catch (Exception ex)
                        {
                            ed.WriteMessage("\nError: " + ex.Message);
                        }
                    }
                }
            }

            if (extractCount > 0)
            {
                ed.WriteMessage($"\n[Complete] Total {extractCount} object(s) extracted.");
                ed.Regen();
            }
        }
    }
}