using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ShipAutoCadPlugin.UI;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        [CommandMethod("BLOCK_RENAME_CLONE")]
        public void InteractiveBlockRenameClone()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. CHỌN BLOCK TRÊN CAD
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect a Block to Rename or Clone: ");
            peo.SetRejectMessage("\nPlease select a Block Reference only.");
            peo.AddAllowedClass(typeof(BlockReference), true);
            
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            string originalName = "";
            ObjectId blockRefId = per.ObjectId;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockReference blkRef = tr.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                if (blkRef == null) return;

                // Lấy tên thật (Hỗ trợ nhỡ có dính block động)
                originalName = blkRef.IsDynamicBlock ? 
                    ((BlockTableRecord)tr.GetObject(blkRef.DynamicBlockTableRecord, OpenMode.ForRead)).Name : 
                    blkRef.Name;

                // 2. HIỂN THỊ GIAO DIỆN
                RenameBlockWindow ui = new RenameBlockWindow(originalName);
                bool? result = Application.ShowModalWindow(ui);

                if (result != true)
                {
                    tr.Commit();
                    return; 
                }

                string newName = ui.NewBlockName;
                bool isClone = ui.IsCreateNewMode;

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                
                if (bt.Has(newName))
                {
                    Application.ShowAlertDialog($"A block named '{newName}' already exists in this drawing!");
                    return;
                }

                // 3. THỰC THI LOGIC
                try
                {
                    if (isClone)
                    {
                        // ----------------------------------------------------
                        // HÀNH ĐỘNG 1: CLONE (TẠO MỚI & THAY THẾ)
                        // ----------------------------------------------------
                        bt.UpgradeOpen();
                        BlockTableRecord oldBtr = (BlockTableRecord)tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead);

                        // Tạo ruột Block mới
                        BlockTableRecord newBtr = new BlockTableRecord();
                        newBtr.Name = newName;
                        newBtr.Origin = oldBtr.Origin;
                        bt.Add(newBtr);
                        tr.AddNewlyCreatedDBObject(newBtr, true);

                        // Copy toàn bộ nét vẽ từ Block cũ sang Block mới (Bỏ qua Attribute)
                        ObjectIdCollection ids = new ObjectIdCollection();
                        foreach (ObjectId id in oldBtr) { ids.Add(id); }
                        
                        if (ids.Count > 0)
                        {
                            IdMapping mapping = new IdMapping();
                            db.DeepCloneObjects(ids, newBtr.ObjectId, mapping, false);
                        }

                        // Cập nhật/Thêm MText
                        UpdateOrAddInternalMText(tr, db, newBtr, newName);

                        // Đặt Block Reference mới ra bản vẽ thay thế vị trí cũ
                        BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        BlockReference newBlkRef = new BlockReference(blkRef.Position, newBtr.ObjectId)
                        {
                            ScaleFactors = blkRef.ScaleFactors,
                            Rotation = blkRef.Rotation,
                            Layer = blkRef.Layer,
                            Color = blkRef.Color
                        };

                        currentSpace.AppendEntity(newBlkRef);
                        tr.AddNewlyCreatedDBObject(newBlkRef, true);

                        // Xóa Block Reference cũ
                        blkRef.UpgradeOpen();
                        blkRef.Erase();

                        ed.WriteMessage($"\nSuccess: Cloned and replaced as '{newName}'.");
                    }
                    else
                    {
                        // ----------------------------------------------------
                        // HÀNH ĐỘNG 2: RENAME (ĐỔI TÊN ĐỊNH NGHĨA GỐC)
                        // ----------------------------------------------------
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForWrite);
                        btr.Name = newName;

                        // Cập nhật/Thêm MText
                        UpdateOrAddInternalMText(tr, db, btr, newName);

                        ed.WriteMessage($"\nSuccess: Definition renamed to '{newName}'. All instances updated.");
                    }

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    Application.ShowAlertDialog("Error processing block: " + ex.Message);
                    tr.Abort();
                }
            }
        }

        // ====================================================================
        // GIA VỊ BÍ MẬT: CẬP NHẬT/THÊM MỚI MTEXT VÀO BLOCK DEFINITION
        // ====================================================================
        private void UpdateOrAddInternalMText(Transaction tr, Database db, BlockTableRecord btr, string newBlockName)
        {
            bool textFound = false;

            // 1. Quét tìm chữ cứng (DBText / MText) bên trong Block
            foreach (ObjectId entId in btr)
            {
                Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                
                if (ent is DBText dbText)
                {
                    dbText.UpgradeOpen();
                    dbText.TextString = newBlockName;
                    textFound = true;
                }
                else if (ent is MText mText)
                {
                    mText.UpgradeOpen();
                    mText.Contents = newBlockName;
                    textFound = true;
                }
                // Bỏ qua AttributeDefinition theo yêu cầu
            }

            // 2. Nếu không có chữ cứng nào, đẻ ra 1 MText mới
            if (!textFound)
            {
                // Kiểm tra và tạo Layer "Mechanical-AM_9" nếu chưa có
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                string reqLayer = "Mechanical-AM_9";

                if (!lt.Has(reqLayer))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord newLtr = new LayerTableRecord
                    {
                        Name = reqLayer,
                        Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7) // Màu trắng/đen chuẩn
                    };
                    lt.Add(newLtr);
                    tr.AddNewlyCreatedDBObject(newLtr, true);
                }

                // Cài đặt thông số chuẩn công ty (Tọa độ: 0, -15, 0 | Cao chữ: 10)
                MText newMText = new MText();
                newMText.SetDatabaseDefaults();
                newMText.Location = new Point3d(0, -15, 0); 
                newMText.TextHeight = 10;
                newMText.Contents = newBlockName;
                newMText.Layer = reqLayer;
                newMText.Attachment = AttachmentPoint.BottomLeft;

                btr.UpgradeOpen();
                btr.AppendEntity(newMText);
                tr.AddNewlyCreatedDBObject(newMText, true);
            }
        }
    }
}