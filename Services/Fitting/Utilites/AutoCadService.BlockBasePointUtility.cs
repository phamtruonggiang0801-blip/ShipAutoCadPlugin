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
        // MODULE: ĐỔI BASE POINT CHO BLOCK (Sử dụng Ma trận WCS/OCS)
        // ====================================================================

        public void ChangeBlockBasePoint()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                // 1. Chọn Block
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect block to change base point: ");
                peo.SetRejectMessage("\nPlease select a valid Block Reference.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                PromptEntityResult per = ed.GetEntity(peo);

                if (per.Status != PromptStatus.OK) return;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockReference blkRef = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (blkRef == null) return;

                    ObjectId btrId = blkRef.IsDynamicBlock ? blkRef.DynamicBlockTableRecord : blkRef.BlockTableRecord;

                    // 2. Chọn điểm Base Point mới (Tọa độ WCS)
                    PromptPointOptions ppo = new PromptPointOptions("\nPick the NEW base point: ");
                    ppo.UseBasePoint = true;
                    ppo.BasePoint = blkRef.Position;
                    PromptPointResult ppr = ed.GetPoint(ppo);

                    if (ppr.Status != PromptStatus.OK) return;
                    Point3d pNewWcs = ppr.Value;

                    // 3. TÍNH TOÁN MA TRẬN
                    // Lấy ma trận chuyển đổi của Block và tìm ma trận nghịch đảo
                    Matrix3d matOcs2Wcs = blkRef.BlockTransform;
                    Matrix3d matWcs2Ocs = matOcs2Wcs.Inverse();

                    // Chuyển điểm Pick (WCS) sang tọa độ Local của Block (OCS)
                    Point3d pNewOcs = pNewWcs.TransformBy(matWcs2Ocs);

                    // Vector dịch chuyển để dời các nét vẽ trong Block (đẩy về gốc tọa độ 0,0,0)
                    Vector3d vecMoveEntities = Point3d.Origin - pNewOcs;

                    BlockTableRecord btr = tr.GetObject(btrId, OpenMode.ForWrite) as BlockTableRecord;

                    // 4. Dịch chuyển ruột Block
                    foreach (ObjectId id in btr)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent != null) ent.TransformBy(Matrix3d.Displacement(vecMoveEntities));
                    }

                    // 5. Bù trừ vị trí cho TẤT CẢ các Block trên bản vẽ để chúng không bị nhảy hình
                    ObjectIdCollection refIds = btr.GetBlockReferenceIds(true, true);
                    foreach (ObjectId id in refIds)
                    {
                        BlockReference br = tr.GetObject(id, OpenMode.ForWrite) as BlockReference;
                        if (br != null)
                        {
                            // Tọa độ mới của điểm chèn chính là điểm pNewOcs nhân với Ma trận transform cũ
                            Point3d correctedPos = pNewOcs.TransformBy(br.BlockTransform);
                            br.Position = correctedPos;
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage($"\nSuccessfully changed base point for Block '{btr.Name}'.");
                }
                ed.Regen();
            }
        }
    }
}