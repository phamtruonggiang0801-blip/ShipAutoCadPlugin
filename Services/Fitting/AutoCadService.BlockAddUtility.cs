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
        // MODULE: ADD TO BLOCK (Chèn nét vẽ rời rạc vào trong Block bằng Ma trận)
        // ====================================================================
        public void AddEntitiesToBlock()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. Yêu cầu chọn Block đích
            PromptEntityOptions peoBlock = new PromptEntityOptions("\nSelect the target Block: ");
            peoBlock.SetRejectMessage("\nObject must be a Block Reference.");
            peoBlock.AddAllowedClass(typeof(BlockReference), true);
            PromptEntityResult perBlock = ed.GetEntity(peoBlock);

            if (perBlock.Status != PromptStatus.OK) return;

            // 2. Yêu cầu chọn các đối tượng rời rạc muốn nạp vào Block
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect objects to add into the Block: ";
            PromptSelectionResult psr = ed.GetSelection(pso);

            if (psr.Status != PromptStatus.OK || psr.Value.Count == 0) return;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        BlockReference blkRef = tr.GetObject(perBlock.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (blkRef == null) return;

                        // Lấy BlockTableRecord (Ruột của Block) để ghi thêm dữ liệu
                        BlockTableRecord btr = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForWrite) as BlockTableRecord;

                        // Lấy Ma trận chuyển đổi của Block hiện tại và tạo Ma trận nghịch đảo
                        Matrix3d blockTransform = blkRef.BlockTransform;
                        Matrix3d inverseTransform = blockTransform.Inverse();

                        foreach (SelectedObject selObj in psr.Value)
                        {
                            Entity sourceEnt = tr.GetObject(selObj.ObjectId, OpenMode.ForWrite) as Entity;
                            if (sourceEnt == null) continue;

                            // Clone đối tượng để đưa vào Block
                            Entity clonedEnt = sourceEnt.Clone() as Entity;

                            // [MA THUẬT MA TRẬN]: Dịch chuyển đối tượng bằng Ma trận nghịch đảo
                            // Điều này đảm bảo khi đưa vào Block, nó vẫn giữ đúng vị trí trên màn hình
                            clonedEnt.TransformBy(inverseTransform);

                            // Nạp vào ruột Block
                            btr.AppendEntity(clonedEnt);
                            tr.AddNewlyCreatedDBObject(clonedEnt, true);

                            // Xóa đối tượng gốc ở bên ngoài Model Space
                            sourceEnt.Erase();
                        }

                        // Buộc Block cập nhật đồ họa ngay lập tức trên màn hình
                        blkRef.UpgradeOpen();
                        blkRef.RecordGraphicsModified(true);

                        tr.Commit();
                        ed.WriteMessage($"\nSuccessfully added {psr.Value.Count} object(s) to Block '{btr.Name}'.");
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage("\nError adding to block: " + ex.Message);
                    }
                }
            }
        }
    }
}