using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ShipAutoCadPlugin.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: DETAIL BOM HARVESTER (Quét Fitting bằng Bounding Box Khung A1)
        // ====================================================================

        public List<BomHarvestRecord> HarvestInterfaceBom()
        {
            List<BomHarvestRecord> rawResults = new List<BomHarvestRecord>();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. CHỈ QUÉT CHỌN KHUNG BẢN VẼ (Lọc Dynamic Block an toàn)
            PromptSelectionOptions selOpt = new PromptSelectionOptions();
            selOpt.MessageForAdding = "\n[Hull Matrix] Select A1 Frames to scan: ";
            TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") };
            PromptSelectionResult selRes = ed.GetSelection(selOpt, new SelectionFilter(filter));
            
            if (selRes.Status != PromptStatus.OK) return rawResults;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                    
                    // Lấy trước TẤT CẢ các Block trên mặt bằng để đỡ phải quét lại nhiều lần
                    List<BlockReference> allSpaceBlocks = new List<BlockReference>();
                    foreach (ObjectId id in currentSpace)
                    {
                        if (tr.GetObject(id, OpenMode.ForRead) is BlockReference b) allSpaceBlocks.Add(b);
                    }

                    int a1Counter = 1;

                    // 2. DUYỆT QUA TỪNG KHUNG A1 VÀ TẠO BOUNDING BOX
                    foreach (SelectedObject selObj in selRes.Value)
                    {
                        BlockReference a1Blk = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (a1Blk == null || !a1Blk.Bounds.HasValue) continue;

                        string a1Name = GetEffectiveName(tr, a1Blk);
                        // Bỏ qua nếu người dùng lỡ quét nhầm Block không phải A1
                        if (!a1Name.Equals("A1", StringComparison.OrdinalIgnoreCase)) continue;

                        Extents3d a1Ext = a1Blk.GeometricExtents;
                        
                        // Tìm Tên của Detail. Có thể đọc Attribute "VIEW_NAME" hoặc "TITLE". Nếu không có, tự đánh số.
                        string detailName = GetAttributeValue(tr, a1Blk, "VIEW_NAME");
                        if (string.IsNullOrEmpty(detailName)) detailName = GetAttributeValue(tr, a1Blk, "TITLE");
                        if (string.IsNullOrEmpty(detailName)) detailName = $"Detail {a1Counter}";

                        // 3. TÌM CÁC BLOCK RƠI VÀO BÊN TRONG KHUNG A1 NÀY
                        foreach (BlockReference innerBlk in allSpaceBlocks)
                        {
                            if (innerBlk.ObjectId == a1Blk.ObjectId) continue;

                            // Spatial Check: Tọa độ chèn (Position) của Block có nằm trong hộp Bounding Box của A1 không?
                            if (IsPointInsideExtents(innerBlk.Position, a1Ext))
                            {
                                ExtractFittingsFromBlock(tr, innerBlk, detailName, 1, rawResults);
                            }
                        }
                        a1Counter++;
                    }
                    tr.Commit();
                }
            }
            return rawResults;
        }

        // Đệ quy X-Ray bóc tách Fitting
        private void ExtractFittingsFromBlock(Transaction tr, BlockReference blkRef, string detailName, int multiplier, List<BomHarvestRecord> results)
        {
            string blkName = GetEffectiveName(tr, blkRef);

            // Bỏ qua các Block Khung hoặc rác
            if (blkName.Equals("A1", StringComparison.OrdinalIgnoreCase) ||
                blkName.Equals("CAS_HEAD", StringComparison.OrdinalIgnoreCase)) return;

            // Nếu là Fitting (CAS-)
            if (blkName.IndexOf("CAS-", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string vaultName = ExtractVaultName(blkName);
                
                results.Add(new BomHarvestRecord
                {
                    PanelName = detailName, 
                    VaultName = vaultName,
                    ParentBlockName = blkName,
                    XClass = "N/A", 
                    Quantity = multiplier,
                    PartId = vaultName,
                    Description = "Hull Fitting", // Tạm thời ghi, sẽ bị VLOOKUP ghi đè
                    
                    // [TÍNH NĂNG MỚI]: Bỏ ObjectId của Block này vào "Túi nhớ" để lát nữa bơm Attribute
                    InstanceIds = new List<ObjectId> { blkRef.ObjectId } 
                });

                // Xử lý bơm cáp tự động (Wire Rope) theo màu Layer
                if (vaultName == "CAS-0066727" || vaultName == "CAS-0066773")
                {
                    bool isRed = blkRef.Layer.IndexOf("Mechanical-AM_7", StringComparison.OrdinalIgnoreCase) >= 0;
                    string ropeVault = isRed ? "400304446" : "400304448";
                    string ropeDesc = isRed ? "Wire Rope (Red)" : "Wire Rope (Yellow)";

                    results.Add(new BomHarvestRecord
                    {
                        PanelName = detailName,
                        VaultName = ropeVault,
                        ParentBlockName = blkName,
                        XClass = "N/A",
                        Quantity = multiplier,
                        PartId = ropeVault,
                        Description = ropeDesc,
                        
                        // [TÍNH NĂNG MỚI]: Bỏ ObjectId của Block này vào "Túi nhớ"
                        InstanceIds = new List<ObjectId> { blkRef.ObjectId } 
                    });
                }
            }

            // Chui sâu vào Block lồng (Assembly) - Chỉ chui nếu không phải Dynamic Block để tránh lỗi đệ quy vòng lặp
            if (!blkRef.IsDynamicBlock)
            {
                BlockTableRecord btr = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                foreach (ObjectId childId in btr)
                {
                    if (tr.GetObject(childId, OpenMode.ForRead) is BlockReference childBlk)
                    {
                        ExtractFittingsFromBlock(tr, childBlk, detailName, multiplier * 1, results);
                    }
                }
            }
        }

        // Helper: Kiểm tra Tọa độ Điểm có nằm trong Extents (Bounding Box) không
        private bool IsPointInsideExtents(Point3d pt, Extents3d ext)
        {
            return pt.X >= ext.MinPoint.X && pt.X <= ext.MaxPoint.X &&
                   pt.Y >= ext.MinPoint.Y && pt.Y <= ext.MaxPoint.Y;
        }

        private string ExtractVaultName(string blockName)
        {
            Match match = Regex.Match(blockName, @"(?i)CAS-\d{7}");
            if (match.Success) return match.Value.ToUpper();
            
            if (blockName.ToUpper().StartsWith("CAS-"))
                return blockName.Length >= 11 ? blockName.Substring(0, 11).ToUpper() : blockName.ToUpper();
            
            return "N/A";
        }
    }
}