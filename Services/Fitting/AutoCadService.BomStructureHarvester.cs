using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ShipAutoCadPlugin.Models;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: BOM STRUCTURE HARVESTER (Thu hoạch Panel BOM)
        // ====================================================================

        public List<BomHarvestRecord> HarvestStructureBom()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<BomHarvestRecord> allRecords = new List<BomHarvestRecord>();

            using (DocumentLock docLock = doc.LockDocument())
            {
                // 1. Yêu cầu chọn các Block Panel
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelect Panel Block References to analyze: ";
                TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
                SelectionFilter selFilter = new SelectionFilter(filter);
                PromptSelectionResult psr = ed.GetSelection(pso, selFilter);

                if (psr.Status != PromptStatus.OK) return allRecords;

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

                        // ==========================================================
                        // [NÂNG CẤP LỘ TRÌNH 2]: Đổi Dictionary từ Int sang List<ObjectId>
                        // ==========================================================
                        Dictionary<string, List<ObjectId>> localInstances = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);

                        BlockTableRecord btr = tr.GetObject(panelRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                        
                        // 2. Đệ quy đếm các Block con và gom ObjectId
                        ScanNestedBlocks(btr, tr, localInstances, panelRef.ObjectId);

                        // 3. APPLY BUSINESS LOGIC (Wire Rope, Thimble, Clamp)
                        ApplyStructureLogic(btr, tr, localInstances, panelRef.ScaleFactors.X);

                        // 4. Map dữ liệu vào Model
                        foreach (var kvp in localInstances)
                        {
                            allRecords.Add(new BomHarvestRecord
                            {
                                PanelName = panelName,
                                VaultName = kvp.Key, 
                                PartId = kvp.Key,    
                                Quantity = kvp.Value.Count,   // [UPDATE]: Số lượng = độ dài của danh sách ID
                                ParentBlockName = panelRef.Name,
                                XClass = "N/A",      
                                Description = "Harvested from CAD",
                                InstanceIds = kvp.Value       // [CHÍ TỬ]: Nhét cái túi ID này vào Record để chốc nữa Sync
                            });
                        }
                    }
                    tr.Commit();
                }
            }
            return allRecords;
        }

        // --- ĐỆ QUY ĐẾM BLOCK VÀ NHẶT OBJECT ID ---
        private void ScanNestedBlocks(BlockTableRecord btr, Transaction tr, Dictionary<string, List<ObjectId>> instances, ObjectId parentBlockId)
        {
            foreach (ObjectId id in btr)
            {
                BlockReference nestedRef = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (nestedRef != null && nestedRef.Layer != "Mechanical-AM_7")
                {
                    string nestedName = nestedRef.IsDynamicBlock ? 
                        ((BlockTableRecord)tr.GetObject(nestedRef.DynamicBlockTableRecord, OpenMode.ForRead)).Name : 
                        nestedRef.Name;

                    if (nestedName.ToUpper().Contains("CAS"))
                    {
                        string bomType = GetAttributeValue(tr, nestedRef, "BOM_TYPE").ToUpper();

                        // Bộ lọc BOM_TYPE
                        if (bomType != "DETAIL" && bomType != "HULL")
                        {
                            string cleanName = CleanVaultName(nestedName);
                            
                            // Nhét ID của cái Block con này vào giỏ
                            if (!instances.ContainsKey(cleanName))
                            {
                                instances.Add(cleanName, new List<ObjectId>());
                            }
                            instances[cleanName].Add(nestedRef.ObjectId);
                        }
                    }

                    // Tiếp tục đào sâu
                    BlockTableRecord nestedBtr = tr.GetObject(nestedRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    ScanNestedBlocks(nestedBtr, tr, instances, nestedRef.ObjectId);
                }
            }
        }

        // --- APPLY MACGREGOR'S TRIGGERS LOGIC ---
        private void ApplyStructureLogic(BlockTableRecord btr, Transaction tr, Dictionary<string, List<ObjectId>> instances, double parentScale)
        {
            // Lấy độ dài mảng (Số lượng) của các Trigger
            int c1 = instances.ContainsKey(BomTriggers.CAS_150_DEDUCT_1) ? instances[BomTriggers.CAS_150_DEDUCT_1].Count : 0;
            int c2 = instances.ContainsKey(BomTriggers.CAS_150_DEDUCT_2) ? instances[BomTriggers.CAS_150_DEDUCT_2].Count : 0;
            int c3 = instances.ContainsKey(BomTriggers.CAS_NO_DEDUCT) ? instances[BomTriggers.CAS_NO_DEDUCT].Count : 0;

            int triggerForAssembly = c1 + c2;
            int triggerForComponents = c1 + c2 + c3;

            // Kích hoạt Assembly và Component ảo (Mấy món này không có Block thực tế nên List ObjectId rỗng)
            if (triggerForAssembly > 0)
            {
                AddVirtualInstances(instances, BomTriggers.WIRE_ROPE_ASSY_PART_ID, 1 * triggerForAssembly);
            }

            if (triggerForComponents > 0)
            {
                AddVirtualInstances(instances, BomTriggers.THIMBLE_PART_ID, 2 * triggerForComponents);
                AddVirtualInstances(instances, BomTriggers.CLAMP_PART_ID, 2 * triggerForComponents);

                double rawLengthMm = GetWireRopeLengthRecursive(btr, tr, parentScale);
                double deduction = 150.0 * (c1 + c2);
                double netLengthMm = rawLengthMm - deduction;
                
                if (netLengthMm > 0)
                {
                    int finalLengthM = (int)Math.Ceiling(netLengthMm / 1000.0);
                    AddVirtualInstances(instances, BomTriggers.WIRE_ROPE_PART_ID, finalLengthM);
                }
            }
        }

        // --- HÀM HELPER ADD HÀNG ẢO ---
        // (Nhét n cái ObjectId.Null vào mảng để lừa nó đếm ra n số lượng cho các vật tư ảo không có Block)
        private void AddVirtualInstances(Dictionary<string, List<ObjectId>> instances, string key, int qty)
        {
            if (!instances.ContainsKey(key))
            {
                instances.Add(key, new List<ObjectId>());
            }
            for (int i = 0; i < qty; i++)
            {
                instances[key].Add(ObjectId.Null); 
            }
        }

        // --- THUẬT TOÁN ĐO CHIỀU DÀI DÂY CÁP TỪ POLYLINE ---
        private double GetWireRopeLengthRecursive(BlockTableRecord btr, Transaction tr, double currentScale)
        {
            double totalLength = 0;

            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                
                if (ent is Polyline poly)
                {
                    double width = poly.ConstantWidth;
                    if (width == 0 && poly.NumberOfVertices > 0) width = poly.GetStartWidthAt(0);

                    if (Math.Abs(width - 3.0) < 0.1)
                    {
                        double itemLen = poly.Length;
                        if (itemLen >= 200 && !(itemLen >= 1350 && itemLen <= 1450))
                        {
                            totalLength += itemLen * currentScale;
                        }
                    }
                }
                else if (ent is BlockReference nestedRef && nestedRef.Layer != "Mechanical-AM_7")
                {
                    double nextScale = Math.Abs(nestedRef.ScaleFactors.X) * currentScale;
                    BlockTableRecord nestedBtr = tr.GetObject(nestedRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    totalLength += GetWireRopeLengthRecursive(nestedBtr, tr, nextScale);
                }
            }
            return totalLength;
        }

        // --- CÁC HÀM HELPER XỬ LÝ CHUỖI ---
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