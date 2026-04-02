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
                            
                        // Cắt bỏ các tiền tố/hậu tố để lấy tên chuẩn (Ví dụ: "New 6D-01P" -> "6D-01P")
                        panelName = CleanPanelName(panelName);

                        ed.WriteMessage($"\n>>> PROCESSING PANEL: {panelName}");

                        // Dictionary đếm số lượng Fitting thô trong 1 Panel
                        Dictionary<string, int> localCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                        BlockTableRecord btr = tr.GetObject(panelRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                        
                        // 2. Đệ quy đếm các Block con bên trong Panel
                        ScanNestedBlocks(btr, tr, localCounts);

                        // 3. APPLY BUSINESS LOGIC (Wire Rope, Thimble, Clamp)
                        ApplyStructureLogic(btr, tr, localCounts, panelRef.ScaleFactors.X);

                        // 4. Map dữ liệu vào Model
                        foreach (var kvp in localCounts)
                        {
                            allRecords.Add(new BomHarvestRecord
                            {
                                PanelName = panelName,
                                VaultName = kvp.Key, // Đây là mã Block hoặc Part ID sinh ra từ logic
                                PartId = kvp.Key,    // Sẽ được hệ thống Pivot/Lookup phân giải tên thực tế sau
                                Quantity = kvp.Value,
                                ParentBlockName = panelRef.Name,
                                XClass = "N/A",      // Khởi tạo giá trị mặc định tránh lỗi Null
                                Description = "Harvested from CAD"
                            });
                        }
                    }
                    tr.Commit();
                }
            }
            return allRecords;
        }

        // --- ĐỆ QUY ĐẾM BLOCK (Có tích hợp Bộ lọc BOM_TYPE) ---
        private void ScanNestedBlocks(BlockTableRecord btr, Transaction tr, Dictionary<string, int> counts)
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
                        // ========================================================
                        // [BỘ LỌC THÔNG MINH BOM_TYPE]: Đọc thẻ tàng hình
                        // ========================================================
                        string bomType = GetAttributeValue(tr, nestedRef, "BOM_TYPE").ToUpper();

                        // Nếu vật tư này đã bị Leader đóng dấu là của DETAIL (hoặc HULL) lúc tạo thư viện
                        // Ta sẽ lập tức bỏ qua nó (Bởi vì nó sẽ được đếm ở mặt trận Interface)
                        if (bomType != "DETAIL" && bomType != "HULL")
                        {
                            string cleanName = CleanVaultName(nestedName);
                            if (counts.ContainsKey(cleanName)) counts[cleanName]++;
                            else counts.Add(cleanName, 1);
                        }
                    }

                    // Tiếp tục đào sâu
                    BlockTableRecord nestedBtr = tr.GetObject(nestedRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    ScanNestedBlocks(nestedBtr, tr, counts);
                }
            }
        }

        // --- APPLY MACGREGOR'S TRIGGERS LOGIC ---
        private void ApplyStructureLogic(BlockTableRecord btr, Transaction tr, Dictionary<string, int> counts, double parentScale)
        {
            // (Chưa có class BomTriggers trong context hiện tại nên tôi tạm giả lập các mã cứng để code biên dịch được)
            // LƯU Ý: Đảm bảo bạn có Class tĩnh BomTriggers.cs chứa các biến hằng số (const) này trong Project!
            string CAS_150_DEDUCT_1 = "CAS-0012345"; // Thay bằng PartID thực tế
            string CAS_150_DEDUCT_2 = "CAS-0012346"; // Thay bằng PartID thực tế
            string CAS_NO_DEDUCT = "CAS-0012347";    // Thay bằng PartID thực tế
            
            string WIRE_ROPE_ASSY_PART_ID = "WIRE-ASSY-001";
            string THIMBLE_PART_ID = "THIMBLE-001";
            string CLAMP_PART_ID = "CLAMP-001";
            string WIRE_ROPE_PART_ID = "WIRE-ROPE-001";

            int c1 = counts.ContainsKey(CAS_150_DEDUCT_1) ? counts[CAS_150_DEDUCT_1] : 0;
            int c2 = counts.ContainsKey(CAS_150_DEDUCT_2) ? counts[CAS_150_DEDUCT_2] : 0;
            int c3 = counts.ContainsKey(CAS_NO_DEDUCT) ? counts[CAS_NO_DEDUCT] : 0;

            int triggerForAssembly = c1 + c2;
            int triggerForComponents = c1 + c2 + c3;

            // A. Kích hoạt Wire Rope Assembly
            if (triggerForAssembly > 0)
            {
                AddOrUpdateCount(counts, WIRE_ROPE_ASSY_PART_ID, 1 * triggerForAssembly);
            }

            // B. Kích hoạt Thimble, Clamp & Wire Rope Calculation
            if (triggerForComponents > 0)
            {
                AddOrUpdateCount(counts, THIMBLE_PART_ID, 2 * triggerForComponents);
                AddOrUpdateCount(counts, CLAMP_PART_ID, 2 * triggerForComponents);

                // ĐO DÂY CÁP
                double rawLengthMm = GetWireRopeLengthRecursive(btr, tr, parentScale);
                
                // Khấu trừ 150mm cho c1 và c2
                double deduction = 150.0 * (c1 + c2);
                double netLengthMm = rawLengthMm - deduction;
                if (netLengthMm < 0) netLengthMm = 0;

                // Tính toán ra mét và làm tròn trần (Ceiling + 1)
                if (netLengthMm > 0)
                {
                    int finalLengthM = (int)Math.Ceiling(netLengthMm / 1000.0);
                    AddOrUpdateCount(counts, WIRE_ROPE_PART_ID, finalLengthM);
                }
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
                    // Lấy Width của Polyline
                    double width = poly.ConstantWidth;
                    if (width == 0 && poly.NumberOfVertices > 0) width = poly.GetStartWidthAt(0);

                    // Nếu Width xấp xỉ 3 -> Là dây cáp
                    if (Math.Abs(width - 3.0) < 0.1)
                    {
                        double itemLen = poly.Length;
                        
                        // Lọc bỏ đoạn rác (<200) hoặc đoạn thừa (1350-1450)
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
        private void AddOrUpdateCount(Dictionary<string, int> dict, string key, int qty)
        {
            if (dict.ContainsKey(key)) dict[key] += qty;
            else dict.Add(key, qty);
        }

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
            // Trích xuất mã CAS-xxxxxxx bằng Regex hoặc Substring
            var match = System.Text.RegularExpressions.Regex.Match(fullName, @"CAS-\d{7}");
            return match.Success ? match.Value : fullName;
        }
    }
}