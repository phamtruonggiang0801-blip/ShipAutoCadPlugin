using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: SMART BLOCK REPLACE (Thay thế qua Mapping Không gian)
        // ====================================================================

        public void SmartReplaceBlocks()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 1. Quét chọn Nguồn (Source) và Đích (Target)
                    PromptSelectionOptions psoSrc = new PromptSelectionOptions();
                    psoSrc.MessageForAdding = "\nStep 1: Select SOURCE Blocks (Old): ";
                    TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
                    PromptSelectionResult psrSrc = ed.GetSelection(psoSrc, new SelectionFilter(filter));
                    
                    if (psrSrc.Status != PromptStatus.OK) return;

                    PromptSelectionOptions psoTgt = new PromptSelectionOptions();
                    psoTgt.MessageForAdding = "\nStep 2: Select TARGET Blocks (New): ";
                    PromptSelectionResult psrTgt = ed.GetSelection(psoTgt, new SelectionFilter(filter));
                    
                    if (psrTgt.Status != PromptStatus.OK) return;

                    // Kiểm tra nghiêm ngặt số lượng
                    if (psrSrc.Value.Count != psrTgt.Value.Count)
                    {
                        System.Windows.MessageBox.Show($"Error: Quantity mismatch!\nSource: {psrSrc.Value.Count}\nTarget: {psrTgt.Value.Count}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }

                    List<BlockReference> srcBlocks = GetBlockReferences(tr, psrSrc.Value);
                    List<BlockReference> tgtBlocks = GetBlockReferences(tr, psrTgt.Value);

                    // 2. Sắp xếp Không gian (LINQ Spatial Sort) - Dùng Tâm Bounding Box
                    srcBlocks = SpatialSortByBoundingBox(srcBlocks);
                    tgtBlocks = SpatialSortByBoundingBox(tgtBlocks);

                    // 3. Xây dựng Bản đồ Mapping (Source Name -> Target ObjectId)
                    Dictionary<string, ObjectId> blockMap = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                    for (int i = 0; i < srcBlocks.Count; i++)
                    {
                        string srcName = GetEffectiveName(tr, srcBlocks[i]);
                        string tgtName = GetEffectiveName(tr, tgtBlocks[i]);

                        if (!srcName.Equals(tgtName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!blockMap.ContainsKey(srcName) && bt.Has(tgtName))
                            {
                                blockMap.Add(srcName, bt[tgtName]);
                            }
                        }
                    }

                    if (blockMap.Count == 0)
                    {
                        ed.WriteMessage("\n[Notice] No mapping rules created (all source and target names are identical).");
                        return;
                    }

                    // 4. Chọn Phạm vi (Scope)
                    PromptSelectionOptions psoScope = new PromptSelectionOptions();
                    psoScope.MessageForAdding = "\nStep 3: Select blocks to replace (Press ENTER for GLOBAL replacement): ";
                    PromptSelectionResult psrScope = ed.GetSelection(psoScope, new SelectionFilter(filter));

                    int replacedCount = 0;

                    // Bắt lỗi Error hoặc Cancel khi người dùng ấn Enter bỏ qua bước quét
                    if (psrScope.Status == PromptStatus.Error || psrScope.Status == PromptStatus.Cancel || psrScope.Status == PromptStatus.None)
                    {
                        // THAY THẾ TOÀN BẢN VẼ (GLOBAL)
                        ed.WriteMessage("\nExecuting GLOBAL replacement...");
                        foreach (ObjectId btrId in bt)
                        {
                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                            replacedCount += ReplaceBlocksInBTR(tr, btr, blockMap);
                        }
                    }
                    else if (psrScope.Status == PromptStatus.OK)
                    {
                        // THAY THẾ THEO VÙNG CHỌN (RECURSIVE SELECTION)
                        ed.WriteMessage("\nExecuting SELECTION replacement (Recursive)...");
                        List<BlockReference> scopeBlocks = GetBlockReferences(tr, psrScope.Value);
                        HashSet<ObjectId> processedDefs = new HashSet<ObjectId>();

                        foreach (var blkRef in scopeBlocks)
                        {
                            // Đổi Block cha
                            string currentName = GetEffectiveName(tr, blkRef);
                            if (blockMap.ContainsKey(currentName))
                            {
                                blkRef.UpgradeOpen();
                                blkRef.BlockTableRecord = blockMap[currentName]; 
                                replacedCount++;
                            }

                            // Chui sâu vào Block con
                            ObjectId defId = blkRef.IsDynamicBlock ? blkRef.DynamicBlockTableRecord : blkRef.BlockTableRecord;
                            replacedCount += RecursiveReplaceDef(tr, defId, blockMap, processedDefs);
                        }
                    }

                    tr.Commit();
                    ed.Regen();
                    System.Windows.MessageBox.Show($"Replacement Complete! Changed {replacedCount} block reference(s).", "Smart Replace", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
        }

        // --- HELPER: Lấy danh sách BlockReference từ SelectionSet ---
        private List<BlockReference> GetBlockReferences(Transaction tr, SelectionSet sSet)
        {
            List<BlockReference> list = new List<BlockReference>();
            foreach (SelectedObject selObj in sSet)
            {
                BlockReference br = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br != null) list.Add(br);
            }
            return list;
        }

        // ====================================================================
        // [NÂNG CẤP]: THUẬT TOÁN SẮP XẾP KHÔNG GIAN BẰNG TÂM HÌNH BAO
        // ====================================================================
        private List<BlockReference> SpatialSortByBoundingBox(List<BlockReference> blocks)
        {
            if (blocks == null || blocks.Count == 0) return new List<BlockReference>();

            // 1. Tính toán điểm tâm của hình bao (Bounding Box Center) cho mỗi Block
            var blockWithCenters = blocks.Select(b => 
            {
                Point3d centerPt = b.Position; // Default fallback
                if (b.Bounds.HasValue)
                {
                    Extents3d ext = b.Bounds.Value;
                    centerPt = new Point3d(
                        (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                        (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0
                    );
                }
                return new { Block = b, Center = centerPt };
            }).ToList();

            // 2. Sắp xếp sơ bộ theo chiều Y giảm dần (Từ trên xuống dưới)
            var sortedByY = blockWithCenters.OrderByDescending(item => item.Center.Y).ToList();
            
            List<BlockReference> finalSorted = new List<BlockReference>();
            
            // [FIX ERROR CS0825/CS1950]: Khởi tạo mảng ẩn danh (anonymous type array) rồi ToList()
            var currentGroup = new[] { sortedByY[0] }.ToList(); 
            
            // Dung sai để nhóm các block vào cùng một hàng (Row)
            double tolerance = 10.0; 
            double currentY = sortedByY[0].Center.Y;

            // 3. Phân nhóm theo hàng (Row) và sắp xếp trong từng hàng theo chiều X (Từ trái qua phải)
            for (int i = 1; i < sortedByY.Count; i++)
            {
                if (Math.Abs(currentY - sortedByY[i].Center.Y) <= tolerance)
                {
                    currentGroup.Add(sortedByY[i]);
                }
                else
                {
                    finalSorted.AddRange(currentGroup.OrderBy(item => item.Center.X).Select(item => item.Block));
                    currentGroup.Clear();
                    currentGroup.Add(sortedByY[i]);
                    currentY = sortedByY[i].Center.Y;
                }
            }
            if (currentGroup.Count > 0)
            {
                finalSorted.AddRange(currentGroup.OrderBy(item => item.Center.X).Select(item => item.Block));
            }
            
            return finalSorted;
        }

        // --- HELPER: Duyệt qua tất cả Block Reference trong 1 BTR ---
        private int ReplaceBlocksInBTR(Transaction tr, BlockTableRecord btr, Dictionary<string, ObjectId> blockMap)
        {
            int count = 0;
            btr.UpgradeOpen();
            foreach (ObjectId id in btr)
            {
                BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br != null)
                {
                    string name = GetEffectiveName(tr, br);
                    if (blockMap.ContainsKey(name))
                    {
                        br.UpgradeOpen();
                        br.BlockTableRecord = blockMap[name];
                        count++;
                    }
                }
            }
            return count;
        }

        // --- HELPER: Đệ quy thay thế trong Block con ---
        private int RecursiveReplaceDef(Transaction tr, ObjectId btrId, Dictionary<string, ObjectId> blockMap, HashSet<ObjectId> processed)
        {
            if (processed.Contains(btrId)) return 0;
            processed.Add(btrId);

            int count = 0;
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            
            foreach (ObjectId id in btr)
            {
                BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br != null)
                {
                    ObjectId childDefId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                    count += RecursiveReplaceDef(tr, childDefId, blockMap, processed);

                    string name = GetEffectiveName(tr, br);
                    if (blockMap.ContainsKey(name))
                    {
                        br.UpgradeOpen();
                        br.BlockTableRecord = blockMap[name];
                        count++;
                    }
                }
            }
            return count;
        }
    }
}