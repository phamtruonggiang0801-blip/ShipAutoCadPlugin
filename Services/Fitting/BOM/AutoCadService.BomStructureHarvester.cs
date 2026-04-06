using System;
using System.Collections.Generic;
using System.Linq;
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
        // MODULE: BOM STRUCTURE HARVESTER (Thu hoạch Panel BOM qua Master Catalog)
        // ====================================================================

        public List<BomHarvestRecord> HarvestStructureBom()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<BomHarvestRecord> rawRecords = new List<BomHarvestRecord>();

            // 1. TẢI TỪ ĐIỂN QUY TẮC (MASTER CATALOG) LÊN
            List<CatalogItem> masterCatalog = GetMasterCatalogItems();

            using (DocumentLock docLock = doc.LockDocument())
            {
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelect Panel Block References to analyze: ";
                TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
                PromptSelectionResult psr = ed.GetSelection(pso, new SelectionFilter(filter));

                if (psr.Status != PromptStatus.OK) return rawRecords;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject selObj in psr.Value)
                    {
                        BlockReference panelRef = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (panelRef == null) continue;

                        string panelName = panelRef.IsDynamicBlock ? 
                            ((BlockTableRecord)tr.GetObject(panelRef.DynamicBlockTableRecord, OpenMode.ForRead)).Name : 
                            panelRef.Name;
                            
                        panelName = CleanPanelName(panelName);
                        ed.WriteMessage($"\n>>> PROCESSING PANEL: {panelName}");

                        BlockTableRecord btr = tr.GetObject(panelRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                        
                        // 2. CHẠY ĐỘNG CƠ X-RAY ĐỂ QUÉT BLOCK & GEOMETRY THEO LUẬT (RULES)
                        ExtractStructureItemsRecursive(tr, btr, panelRef.ScaleFactors.X, panelName, masterCatalog, rawRecords);
                    }
                    tr.Commit();
                }
            }

            // 3. GỘP CÁC KẾT QUẢ BỊ LẺ THÀNH TỔNG SỐ LƯỢNG / TỔNG CHIỀU DÀI
            // Lưu ý: Hàm ConsolidateRawResults đã được dùng chung với BomDetailHarvester
            return ConsolidateRawResults(rawRecords);
        }

        // --- ĐỘNG CƠ ĐỆ QUY QUÉT BLOCK VÀ NÉT VẼ TỰ DO ---
        private void ExtractStructureItemsRecursive(Transaction tr, BlockTableRecord btr, double currentScale, string panelName, List<CatalogItem> catalog, List<BomHarvestRecord> results)
        {
            // Lọc trước các quy tắc nhận diện nét vẽ hình học
            var geoRules = catalog.Where(c => c.EntityType != "Block" && !string.IsNullOrEmpty(c.TriggerLayer)).ToList();

            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                // A. XỬ LÝ BLOCK (FITTING CHÍNH & ACCESSORY)
                if (ent is BlockReference blkRef)
                {
                    if (blkRef.Layer == "Mechanical-AM_7") continue; // Giữ lại bộ lọc an toàn của legacy

                    string blkName = GetEffectiveName(tr, blkRef);
                    if (blkName.IndexOf("CAS-", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string bomType = GetAttributeValue(tr, blkRef, "BOM_TYPE").ToUpper();
                        
                        // Chỉ lấy đồ của Panel
                        if (bomType != "DETAIL" && bomType != "HULL")
                        {
                            string vaultName = CleanVaultName(blkName);
                            
                            // Lấy Metadata từ Master Catalog
                            var mainCatItem = catalog.FirstOrDefault(c => c.PartNumber == vaultName || (c.BlockName != null && c.BlockName.Split(';').Contains(blkName, StringComparer.OrdinalIgnoreCase)));

                            // 1. Thêm Fitting Chính
                            results.Add(new BomHarvestRecord {
                                PanelName = panelName,
                                VaultName = vaultName,
                                ParentBlockName = blkName,
                                Quantity = 1,
                                UoM = mainCatItem?.UoM ?? "pcs",
                                PartId = vaultName,
                                
                                // [CẬP NHẬT MỚI]: Map dữ liệu chuẩn và Khai báo đây là Mẹ
                                Description = mainCatItem != null && !string.IsNullOrEmpty(mainCatItem.Description) ? mainCatItem.Description : "Harvested from CAD",
                                XClass = mainCatItem?.Title ?? "",
                                ProjectPosNum = mainCatItem?.ProjectPosNum ?? "",
                                IsAccessory = false,
                                ParentPartId = "",
                                
                                InstanceIds = new List<ObjectId> { blkRef.ObjectId }
                            });

                            // 2. Thêm Phụ kiện ảo (Accessory) từ Master Catalog
                            if (mainCatItem != null && mainCatItem.Accessories != null)
                            {
                                foreach (var acc in mainCatItem.Accessories)
                                {
                                    var accCatItem = catalog.FirstOrDefault(c => c.PartNumber == acc.PartId);
                                    
                                    results.Add(new BomHarvestRecord {
                                        PanelName = panelName,
                                        VaultName = acc.PartId,
                                        ParentBlockName = blkName,
                                        Quantity = acc.Quantity, // Số lượng phụ kiện
                                        UoM = accCatItem?.UoM ?? "pcs",
                                        PartId = acc.PartId,
                                        
                                        // [CẬP NHẬT MỚI]: Map dữ liệu Phụ kiện và gắn dây rốn về Mẹ
                                        Description = accCatItem != null && !string.IsNullOrEmpty(accCatItem.Description) ? accCatItem.Description : "Accessory (Auto-Generated)",
                                        XClass = accCatItem?.Title ?? "Accessory",
                                        ProjectPosNum = accCatItem?.ProjectPosNum ?? "",
                                        IsAccessory = true,
                                        ParentPartId = vaultName,
                                        
                                        InstanceIds = new List<ObjectId> { blkRef.ObjectId }
                                    });
                                }
                            }
                        }
                    }

                    // Chui sâu đệ quy vào các Block con
                    if (!blkRef.IsDynamicBlock)
                    {
                        double nextScale = Math.Abs(blkRef.ScaleFactors.X) * currentScale;
                        BlockTableRecord nestedBtr = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                        ExtractStructureItemsRecursive(tr, nestedBtr, nextScale, panelName, catalog, results);
                    }
                }
                // B. XỬ LÝ NÉT VẼ TỰ DO DỰA THEO RULE (GEOMETRIC VIRTUAL BOM)
                else if (geoRules.Count > 0 && (ent is Polyline || ent is Line || ent is Arc || ent is Circle))
                {
                    string entType = ent.GetType().Name;
                    string entLayer = ent.Layer;

                    foreach (var rule in geoRules)
                    {
                        if (rule.EntityType == entType && rule.TriggerLayer == entLayer)
                        {
                            double qty = 1.0;
                            if (rule.UoM == "m")
                            {
                                // Đo chiều dài và nhân với tỷ lệ Scale của Block Mẹ
                                if (ent is Polyline pl) qty = (pl.Length * currentScale) / 1000.0;
                                else if (ent is Line ln) qty = (ln.Length * currentScale) / 1000.0;
                                else if (ent is Arc arc) qty = (arc.Length * currentScale) / 1000.0;
                            }

                            results.Add(new BomHarvestRecord {
                                PanelName = panelName,
                                VaultName = rule.PartNumber,
                                ParentBlockName = "Linear Item",
                                Quantity = (int)Math.Ceiling(qty), // Sẽ được nhóm và cộng dồn ở hàm Consolidate
                                UoM = rule.UoM,
                                PartId = rule.PartNumber,
                                
                                // [CẬP NHẬT MỚI]: Map dữ liệu cho Virtual Item
                                Description = !string.IsNullOrEmpty(rule.Description) ? rule.Description : "Linear/Virtual Item",
                                XClass = rule.Title ?? "",
                                ProjectPosNum = rule.ProjectPosNum ?? "",
                                IsAccessory = false,
                                ParentPartId = "",
                                
                                InstanceIds = new List<ObjectId> { ent.ObjectId }
                            });
                            break; // Khớp 1 Rule thì thoát vòng lặp check Rule
                        }
                    }
                }
            }
        }

        // --- CÁC HÀM HELPER CHUẨN HÓA CHUỖI ---
        private string CleanPanelName(string fullName)
        {
            string clean = fullName.Trim();
            if (clean.StartsWith("New ", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(4).Trim();
            if (clean.StartsWith("T.", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(2).Trim();
            if (clean.EndsWith("_Assy", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - 5);
            return clean;
        }

        private string CleanVaultName(string fullName)
        {
            var match = System.Text.RegularExpressions.Regex.Match(fullName, @"CAS-\d{7}");
            return match.Success ? match.Value : fullName;
        }
    }
}