using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: QA/QC STAMP (Đóng dấu tự động & Diệt hàng giả)
        // ====================================================================

        public void GenerateQaStamp()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        // 1. Đảm bảo Layer Defpoints tồn tại
                        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        if (!lt.Has("Defpoints"))
                        {
                            lt.UpgradeOpen();
                            LayerTableRecord ltr = new LayerTableRecord();
                            ltr.Name = "Defpoints";
                            ltr.IsPlottable = false; // Bản chất của Layer này là không in
                            lt.Add(ltr);
                            tr.AddNewlyCreatedDBObject(ltr, true);
                        }

                        // 2. Tạo Text đơn giản tại 0,0,0
                        MText stampText = new MText();
                        stampText.SetDatabaseDefaults();
                        stampText.Location = Point3d.Origin; // Tọa độ (0,0,0)
                        stampText.Layer = "Defpoints"; // Gán thẳng vào layer không in
                        stampText.TextHeight = 5.0; // Chiều cao chữ vừa phải
                        stampText.Attachment = AttachmentPoint.BottomLeft;
                        stampText.ColorIndex = 3; // Màu xanh lá cây (Green) cho dễ nhìn trên CAD
                        
                        stampText.Contents = "CheckList Passed";

                        // 3. Chèn vào không gian hiện tại của CAD
                        BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        currentSpace.AppendEntity(stampText);
                        tr.AddNewlyCreatedDBObject(stampText, true);

                        tr.Commit();
                    }
                    catch (Exception ex)
                    {
                        Application.ShowAlertDialog("Error generating QA Stamp: " + ex.Message);
                        tr.Abort();
                    }
                }
            }
        }

        // HÀM SÁT THỦ: Tự động lùng sục và xóa sổ Text "CheckList Passed" sai phép
        public void PurgeFakeQaStamps()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                        bool hasDeleted = false;

                        // Quét tất cả các đối tượng trong bản vẽ
                        foreach (ObjectId id in currentSpace)
                        {
                            Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            
                            // Nếu nằm trên layer Defpoints
                            if (ent != null && ent.Layer.Equals("Defpoints", StringComparison.OrdinalIgnoreCase))
                            {
                                // Và là MText hoặc DBText chứa nội dung "CheckList Passed"
                                if (ent is MText mtext && mtext.Text.Contains("CheckList Passed"))
                                {
                                    ent.UpgradeOpen();
                                    ent.Erase(); // Tiêu diệt
                                    hasDeleted = true;
                                }
                                else if (ent is DBText dbtext && dbtext.TextString.Contains("CheckList Passed"))
                                {
                                    ent.UpgradeOpen();
                                    ent.Erase(); // Tiêu diệt
                                    hasDeleted = true;
                                }
                            }
                        }

                        if (hasDeleted)
                        {
                            // In log ra command line cho Kỹ sư giật mình chơi (không hiện Popup để tránh phiền)
                            doc.Editor.WriteMessage("\n[QA System] Detected and purged fake/invalid QA Stamp(s)!");
                        }

                        tr.Commit();
                    }
                    catch
                    {
                        tr.Abort();
                    }
                }
            }
        }
    }
}