using System;
using System.Collections.Generic;
using System.IO;
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
        // MODULE 1: SHEET CONTENT MANAGEMENT (Đồng bộ khung A1 & Bảng DataGrid)
        // ====================================================================

        public List<SheetRowData> SyncAndAddSheetContent(List<SheetRowData> currentDataList)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            int updateCount = 0;
            int addCount = 0;
            int recreateCount = 0; // Đếm số block được khôi phục hoặc chèn từ Excel

            PromptSelectionOptions opts = new PromptSelectionOptions();
            opts.MessageForAdding = "\n[Ship Plugin] Select A1 Blocks (or press ESC to ONLY SAVE GRID DATA): ";
            TypedValue[] filter = new TypedValue[] { 
                new TypedValue((int)DxfCode.Start, "INSERT"),
                new TypedValue((int)DxfCode.BlockName, "A1") 
            };
            PromptSelectionResult res = ed.GetSelection(opts, new SelectionFilter(filter));

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                    List<BlockReference> allBlocks = new List<BlockReference>();
                    
                    foreach (ObjectId objId in currentSpace)
                    {
                        BlockReference blk = tr.GetObject(objId, OpenMode.ForRead) as BlockReference;
                        if (blk != null) allBlocks.Add(blk);
                    }

                    // ======================================================
                    // PHẦN A: XỬ LÝ DỮ LIỆU ĐÃ CÓ TRÊN DATAGRID
                    // ======================================================
                    foreach (var item in currentDataList)
                    {
                        // 1. Kiểm tra Block SheetContent: Còn sống hay đã bị xóa?
                        bool isContentValid = item.SheetContentBlockId != ObjectId.Null && !item.SheetContentBlockId.IsErased;

                        if (isContentValid)
                        {
                            try {
                                BlockReference blk = tr.GetObject(item.SheetContentBlockId, OpenMode.ForWrite) as BlockReference;
                                if (blk != null && blk.AttributeCollection != null)
                                {
                                    foreach (ObjectId attId in blk.AttributeCollection)
                                    {
                                        AttributeReference att = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                                        if (att != null)
                                        {
                                            if (att.Tag.ToUpper() == "SHEETNUM" && att.TextString != item.SheetNo) { att.TextString = item.SheetNo; updateCount++; }
                                            else if (att.Tag.ToUpper() == "CONTENT" && att.TextString != item.Content) { att.TextString = item.Content; updateCount++; }
                                        }
                                    }
                                }
                            } catch { }
                        }
                        else if (item.A1BlockId != ObjectId.Null && !item.A1BlockId.IsErased)
                        {
                            // TỰ ĐỘNG CHÈN LẠI (Auto-Heal)
                            BlockReference a1Block = tr.GetObject(item.A1BlockId, OpenMode.ForRead) as BlockReference;
                            BlockReference casHeadBlock = allBlocks.FirstOrDefault(b => GetEffectiveName(tr, b).ToUpper() == "CAS_HEAD" && IsInsideExtents(b.GeometricExtents, a1Block.GeometricExtents));
                            
                            double blockScale = casHeadBlock != null ? GetBlockScale(tr, casHeadBlock) : 1.0;
                            
                            ObjectId newBlockId = CreateOrUpdateSheetContentBlock(db, tr, currentSpace, allBlocks, a1Block.Position, blockScale, item.Content, item.SheetNo);
                            item.SheetContentBlockId = newBlockId; 
                            recreateCount++;
                        }

                        // 2. Cập nhật Block Amendment (Lịch sử) 
                        if (item.AmendmentBlockId != ObjectId.Null && !item.AmendmentBlockId.IsErased)
                        {
                            try {
                                BlockReference blk = tr.GetObject(item.AmendmentBlockId, OpenMode.ForWrite) as BlockReference;
                                if (blk != null && blk.AttributeCollection != null)
                                {
                                    foreach (ObjectId attId in blk.AttributeCollection)
                                    {
                                        AttributeReference att = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                                        if (att != null)
                                        {
                                            if (att.Tag.ToUpper() == "REV" && att.TextString != item.Rev) { att.TextString = item.Rev; updateCount++; }
                                            else if (att.Tag.ToUpper() == "DATE" && att.TextString != item.Date) { att.TextString = item.Date; updateCount++; }
                                            else if (att.Tag.ToUpper() == "AMENDMENT" && att.TextString != item.AmendmentDescription) { att.TextString = item.AmendmentDescription; updateCount++; }
                                        }
                                    }
                                }
                            } catch { }
                        }
                    }

                    // ======================================================
                    // PHẦN B: QUÉT CHỌN ĐỂ THÊM MỚI HOẶC BƠM DATA EXCEL XUỐNG CAD
                    // ======================================================
                    if (res.Status == PromptStatus.OK)
                    {
                        foreach (SelectedObject selObj in res.Value)
                        {
                            BlockReference a1Block = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                            if (a1Block == null || GetEffectiveName(tr, a1Block).ToUpper() != "A1") continue;

                            BlockReference casHeadBlock = allBlocks.FirstOrDefault(b => 
                                GetEffectiveName(tr, b).ToUpper() == "CAS_HEAD" && IsInsideExtents(b.GeometricExtents, a1Block.GeometricExtents));

                            if (casHeadBlock != null)
                            {
                                string sheetNo = "SHEET";
                                string arasSheetNo = GetAttributeValue(tr, casHeadBlock, "ARAS_DOCSHEETNO");
                                int rawNumericSheetNo = 0;
                                if (int.TryParse(arasSheetNo, out rawNumericSheetNo)) sheetNo = "SHEET " + rawNumericSheetNo.ToString("D2");

                                double blockScale = GetBlockScale(tr, casHeadBlock);

                                var existingItem = currentDataList.FirstOrDefault(x => x.A1BlockId == a1Block.ObjectId || x.SheetNo == sheetNo);

                                if (existingItem != null)
                                {
                                    if (existingItem.A1BlockId == ObjectId.Null || existingItem.SheetContentBlockId == ObjectId.Null || existingItem.SheetContentBlockId.IsErased)
                                    {
                                        existingItem.A1BlockId = a1Block.ObjectId; 

                                        ObjectId savedBlockId = CreateOrUpdateSheetContentBlock(db, tr, currentSpace, allBlocks, a1Block.Position, blockScale, existingItem.Content, existingItem.SheetNo);
                                        existingItem.SheetContentBlockId = savedBlockId;
                                        recreateCount++;
                                    }
                                }
                                else
                                {
                                    string contentString = GetDetailListString(tr, allBlocks, a1Block.GeometricExtents);
                                    if (string.IsNullOrEmpty(contentString)) contentString = "<No Details>";
                                    
                                    ObjectId savedBlockId = CreateOrUpdateSheetContentBlock(db, tr, currentSpace, allBlocks, a1Block.Position, blockScale, contentString, sheetNo);

                                    currentDataList.Add(new SheetRowData { 
                                        SheetNo = sheetNo, 
                                        Content = contentString, 
                                        RawNumericSheetNo = rawNumericSheetNo,
                                        SheetContentBlockId = savedBlockId,
                                        A1BlockId = a1Block.ObjectId
                                    });
                                    addCount++;
                                }
                            }
                        }
                    }
                    tr.Commit();
                }
            }

            if (updateCount > 0 || addCount > 0 || recreateCount > 0)
            {
                doc.Editor.Regen();
                Application.ShowAlertDialog($"SYNC SUCCESSFUL!\n- Updated: {updateCount} items.\n- Restored/Inserted from Excel: {recreateCount} blocks.\n- Added new drawings: {addCount} items.");
            }
            else if (res.Status == PromptStatus.OK)
            {
                ed.WriteMessage("\n[Ship Plugin] Everything is up-to-date. No data was changed or added.");
            }

            return currentDataList;
        }

        public ObjectId CreateOrUpdateSheetContentBlock(Database db, Transaction tr, BlockTableRecord space, List<BlockReference> allBlocks, Point3d a1Pos, double scale, string content, string sheetNum)
        {
            string blockName = "SheetContent";
            string blockPath = @"C:\CustomTools\Symbol.dwg";
            Point3d insertPt = new Point3d(a1Pos.X, a1Pos.Y - (scale * 20), a1Pos.Z);

            double tolerance = 0.0001;
            var existingBlock = allBlocks.FirstOrDefault(b => GetEffectiveName(tr, b).ToUpper() == blockName.ToUpper() && 
                                Math.Abs(b.Position.X - insertPt.X) < tolerance && Math.Abs(b.Position.Y - insertPt.Y) < tolerance);
            
            if (existingBlock != null)
            {
                existingBlock.UpgradeOpen();
                existingBlock.Erase();
            }

            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
            ObjectId btrId = ObjectId.Null;

            if (bt.Has(blockName))
            {
                btrId = bt[blockName];
            }
            else
            {
                if (!File.Exists(blockPath))
                {
                    Application.ShowAlertDialog($"Error: Library file not found at {blockPath}");
                    return ObjectId.Null;
                }

                try
                {
                    using (Database extDb = new Database(false, true))
                    {
                        extDb.ReadDwgFile(blockPath, FileOpenMode.OpenForReadAndAllShare, true, "");
                        
                        ObjectId sourceBtrId = ObjectId.Null;
                        using (Transaction extTr = extDb.TransactionManager.StartTransaction())
                        {
                            BlockTable extBt = extTr.GetObject(extDb.BlockTableId, OpenMode.ForRead) as BlockTable;
                            if (extBt.Has(blockName))
                            {
                                sourceBtrId = extBt[blockName];
                            }
                            extTr.Commit();
                        }

                        if (sourceBtrId != ObjectId.Null)
                        {
                            ObjectIdCollection ids = new ObjectIdCollection();
                            ids.Add(sourceBtrId);
                            IdMapping mapping = new IdMapping();
                            db.WblockCloneObjects(ids, db.BlockTableId, mapping, DuplicateRecordCloning.Replace, false);
                            btrId = mapping[sourceBtrId].Value;
                        }
                        else
                        {
                            Application.ShowAlertDialog($"Error: No block named '{blockName}' found in Symbol.dwg!");
                            return ObjectId.Null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Application.ShowAlertDialog($"Error loading block {blockName} from {blockPath}: {ex.Message}");
                    return ObjectId.Null;
                }
            }

            BlockReference newBlk = new BlockReference(insertPt, btrId);
            newBlk.ScaleFactors = new Scale3d(scale);
            space.AppendEntity(newBlk);
            tr.AddNewlyCreatedDBObject(newBlk, true);

            BlockTableRecord btrDef = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
            if (btrDef.HasAttributeDefinitions)
            {
                foreach (ObjectId objId in btrDef)
                {
                    AttributeDefinition attDef = tr.GetObject(objId, OpenMode.ForRead) as AttributeDefinition;
                    if (attDef != null && !attDef.Constant)
                    {
                        AttributeReference attRef = new AttributeReference();
                        attRef.SetAttributeFromBlock(attDef, newBlk.BlockTransform);
                        
                        if (attRef.Tag.ToUpper() == "CONTENT") attRef.TextString = content;
                        else if (attRef.Tag.ToUpper() == "SHEETNUM") attRef.TextString = sheetNum;

                        newBlk.AttributeCollection.AppendAttribute(attRef);
                        tr.AddNewlyCreatedDBObject(attRef, true);
                    }
                }
            }

            return newBlk.ObjectId;
        }
    }
}