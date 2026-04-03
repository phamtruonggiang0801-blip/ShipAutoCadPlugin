using System;
using System.Collections.Generic;
using System.Linq;
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
        // MODULE: DETAIL BOM HARVESTER (Quét Fitting & Virtual BOM từ Khung A1)
        // ====================================================================

        public List<BomHarvestRecord> HarvestInterfaceBom()
        {
            List<BomHarvestRecord> rawResults = new List<BomHarvestRecord>();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. TẢI TỪ ĐIỂN QUY TẮC (MASTER CATALOG) LÊN ĐỂ ĐỐI CHIẾU
            List<CatalogItem> masterCatalog = GetMasterCatalogItems();

            PromptSelectionOptions selOpt = new PromptSelectionOptions();
            selOpt.MessageForAdding = "\nSelect A1 Frames to scan: ";
            TypedValue[] filter = new TypedValue[] 
            { 
                new TypedValue((int)DxfCode.Start, "INSERT"),
                new TypedValue((int)DxfCode.BlockName, "`*U*,A1") 
            };
            PromptSelectionResult selRes = ed.GetSelection(selOpt, new SelectionFilter(filter));
            
            if (selRes.Status != PromptStatus.OK) return rawResults;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                    
                    List<BlockReference> allSpaceBlocks = new List<BlockReference>();
                    List<Entity> allSpaceGeometries = new List<Entity>();

                    foreach (ObjectId id in currentSpace)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent is BlockReference b) allSpaceBlocks.Add(b);
                        else if (ent is Polyline || ent is Line || ent is Circle || ent is Arc) allSpaceGeometries.Add(ent);
                    }

                    int a1Counter = 1;

                    // 2. DUYỆT QUA TỪNG KHUNG A1
                    foreach (SelectedObject selObj in selRes.Value)
                    {
                        BlockReference a1Blk = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (a1Blk == null || !a1Blk.Bounds.HasValue) continue;

                        string a1Name = GetEffectiveName(tr, a1Blk);
                        if (!a1Name.Equals("A1", StringComparison.OrdinalIgnoreCase)) continue;

                        Extents3d a1Ext = a1Blk.GeometricExtents;
                        
                        string detailName = GetAttributeValue(tr, a1Blk, "VIEW_NAME");
                        if (string.IsNullOrEmpty(detailName)) detailName = GetAttributeValue(tr, a1Blk, "TITLE");
                        if (string.IsNullOrEmpty(detailName)) detailName = $"Detail {a1Counter}";

                        // 3A. QUÉT CÁC BLOCK (VÀ BUNG PHỤ KIỆN SUB-BOM)
                        foreach (BlockReference innerBlk in allSpaceBlocks)
                        {
                            if (innerBlk.ObjectId == a1Blk.ObjectId) continue;

                            if (IsPointInsideExtents(innerBlk.Position, a1Ext))
                            {
                                ExtractFittingsFromBlock(tr, innerBlk, detailName, 1, masterCatalog, rawResults);
                            }
                        }

                        // 3B. QUÉT CÁC NÉT VẼ HÌNH HỌC (ĐỂ TÍNH CÁP/ỐNG - VIRTUAL BOM)
                        ExtractGeometricItems(tr, allSpaceGeometries, a1Ext, detailName, masterCatalog, rawResults);

                        a1Counter++;
                    }
                    tr.Commit();
                }
            }

            // 4. GỘP CÁC KẾT QUẢ TUYẾN TÍNH (Mét) BỊ LẺ THÀNH TỔNG CHIỀU DÀI
            return ConsolidateRawResults(rawResults);
        }

        // ====================================================================
        // NHỊP 1: BÓC TÁCH BLOCK VÀ PHỤ KIỆN (ACCESSORY)
        // ====================================================================
        private void ExtractFittingsFromBlock(Transaction tr, BlockReference blkRef, string detailName, int multiplier, List<CatalogItem> catalog, List<BomHarvestRecord> results)
        {
            string blkName = GetEffectiveName(tr, blkRef);

            if (blkName.Equals("A1", StringComparison.OrdinalIgnoreCase) || blkName.Equals("CAS_HEAD", StringComparison.OrdinalIgnoreCase)) return;

            if (blkName.IndexOf("CAS-", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string vaultName = ExtractVaultName(blkName);
                
                // Lấy thông tin Fitting Chính từ Master Catalog
                var mainCatItem = catalog.FirstOrDefault(c => c.PartNumber == vaultName || (c.BlockName != null && c.BlockName.Split(';').Contains(blkName, StringComparer.OrdinalIgnoreCase)));
                
                // Ghi nhận Fitting Chính
                results.Add(new BomHarvestRecord
                {
                    PanelName = detailName, 
                    VaultName = vaultName,
                    ParentBlockName = blkName,
                    Quantity = multiplier,
                    UoM = "pcs",
                    PartId = vaultName,
                    
                    // [CẬP NHẬT]: Map đúng Metadata thay vì Hardcode
                    Description = mainCatItem != null && !string.IsNullOrEmpty(mainCatItem.Description) ? mainCatItem.Description : "Hull Fitting",
                    XClass = mainCatItem?.Title ?? "",
                    ProjectPosNum = mainCatItem?.ProjectPosNum ?? "",
                    
                    IsAccessory = false,
                    ParentPartId = "", // Mẹ thì không có Parent
                    
                    InstanceIds = new List<ObjectId> { blkRef.ObjectId } 
                });

                // [CẬP NHẬT]: ĐỌC SUB-BOM VÀ TẠO LIÊN KẾT MẸ-CON
                if (mainCatItem != null && mainCatItem.Accessories != null && mainCatItem.Accessories.Count > 0)
                {
                    foreach (var acc in mainCatItem.Accessories)
                    {
                        var accCatItem = catalog.FirstOrDefault(c => c.PartNumber == acc.PartId);
                        
                        results.Add(new BomHarvestRecord
                        {
                            PanelName = detailName,
                            VaultName = acc.PartId, 
                            ParentBlockName = blkName,
                            Quantity = acc.Quantity * multiplier, 
                            UoM = accCatItem?.UoM ?? "pcs",
                            PartId = acc.PartId,
                            
                            // Map Metadata của Phụ kiện
                            Description = accCatItem != null && !string.IsNullOrEmpty(accCatItem.Description) ? accCatItem.Description : "Accessory",
                            XClass = accCatItem?.Title ?? "Accessory",
                            ProjectPosNum = accCatItem?.ProjectPosNum ?? "",
                            
                            // [QUAN TRỌNG]: Đánh dấu đây là Phụ kiện và neo vào Fitting Mẹ
                            IsAccessory = true,
                            ParentPartId = vaultName,
                            
                            InstanceIds = new List<ObjectId> { blkRef.ObjectId } 
                        });
                    }
                }
            }

            if (!blkRef.IsDynamicBlock)
            {
                BlockTableRecord btr = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                foreach (ObjectId childId in btr)
                {
                    if (tr.GetObject(childId, OpenMode.ForRead) is BlockReference childBlk)
                    {
                        ExtractFittingsFromBlock(tr, childBlk, detailName, multiplier * 1, catalog, results);
                    }
                }
            }
        }

        // ====================================================================
        // NHỊP 2: BÓC TÁCH NÉT VẼ HÌNH HỌC DỰA VÀO LUẬT (GEOMETRIC RULES)
        // ====================================================================
        private void ExtractGeometricItems(Transaction tr, List<Entity> geometries, Extents3d a1Ext, string detailName, List<CatalogItem> catalog, List<BomHarvestRecord> results)
        {
            var geoRules = catalog.Where(c => c.EntityType != "Block" && !string.IsNullOrEmpty(c.TriggerLayer)).ToList();
            if (geoRules.Count == 0) return;

            foreach (var ent in geometries)
            {
                Point3d centerPt = GetEntityCenter(ent);
                if (!IsPointInsideExtents(centerPt, a1Ext)) continue;

                string entType = ent.GetType().Name;
                string entLayer = ent.Layer;

                foreach (var rule in geoRules)
                {
                    if (rule.EntityType == entType && rule.TriggerLayer == entLayer)
                    {
                        double qty = 1.0;
                        if (rule.UoM == "m") 
                        {
                            if (ent is Polyline pl) qty = pl.Length / 1000.0;
                            else if (ent is Line ln) qty = ln.Length / 1000.0;
                            else if (ent is Arc arc) qty = arc.Length / 1000.0;
                        }

                        results.Add(new BomHarvestRecord
                        {
                            PanelName = detailName,
                            VaultName = rule.PartNumber,
                            ParentBlockName = "Linear Item",
                            Quantity = (int)Math.Ceiling(qty), 
                            UoM = rule.UoM,
                            PartId = rule.PartNumber,
                            
                            // [CẬP NHẬT]: Map Metadata
                            Description = string.IsNullOrEmpty(rule.Description) ? "Virtual Item" : rule.Description,
                            XClass = rule.Title ?? "",
                            ProjectPosNum = rule.ProjectPosNum ?? "",
                            
                            IsAccessory = false,
                            ParentPartId = "",
                            
                            InstanceIds = new List<ObjectId> { ent.ObjectId }
                        });
                        
                        break; 
                    }
                }
            }
        }

        // ====================================================================
        // HÀM HELPER
        // ====================================================================
        private bool IsPointInsideExtents(Point3d pt, Extents3d ext)
        {
            return pt.X >= ext.MinPoint.X && pt.X <= ext.MaxPoint.X &&
                   pt.Y >= ext.MinPoint.Y && pt.Y <= ext.MaxPoint.Y;
        }

        private Point3d GetEntityCenter(Entity ent)
        {
            try
            {
                if (ent.Bounds.HasValue)
                {
                    Extents3d ext = ent.Bounds.Value;
                    return new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2, 0);
                }
            }
            catch { }
            return Point3d.Origin;
        }

        private string ExtractVaultName(string blockName)
        {
            Match match = Regex.Match(blockName, @"(?i)CAS-\d{7}");
            if (match.Success) return match.Value.ToUpper();
            
            if (blockName.ToUpper().StartsWith("CAS-"))
                return blockName.Length >= 11 ? blockName.Substring(0, 11).ToUpper() : blockName.ToUpper();
            
            return "N/A";
        }

        // [CẬP NHẬT]: Gộp các đoạn cáp/ống rời rạc thành tổng độ dài (Bảo toàn Parent-Child Link)
        private List<BomHarvestRecord> ConsolidateRawResults(List<BomHarvestRecord> rawList)
        {
            var consolidated = rawList
                // Nhóm thêm theo IsAccessory và ParentPartId để Phụ kiện không bị lộn xộn
                .GroupBy(r => new { r.PanelName, r.VaultName, r.ParentBlockName, r.UoM, r.IsAccessory, r.ParentPartId })
                .Select(g => new BomHarvestRecord
                {
                    PanelName = g.Key.PanelName,
                    VaultName = g.Key.VaultName,
                    ParentBlockName = g.Key.ParentBlockName,
                    UoM = g.Key.UoM,
                    PartId = g.First().PartId,
                    Description = g.First().Description,
                    XClass = g.First().XClass,
                    ProjectPosNum = g.First().ProjectPosNum,
                    
                    IsAccessory = g.Key.IsAccessory,
                    ParentPartId = g.Key.ParentPartId,
                    
                    Quantity = g.Sum(r => r.Quantity),
                    InstanceIds = g.SelectMany(r => r.InstanceIds).ToList()
                }).ToList();

            return consolidated;
        }
    }
}