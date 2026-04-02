using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ShipAutoCadPlugin.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE 2: AMENDMENT & REVISION MANAGEMENT (Quản lý Lịch sử sửa đổi)
        // ====================================================================

        /// <summary>
        /// Tạo mới một Block Amendment (Revision) dựa trên dữ liệu người dùng nhập
        /// </summary>
        public ObjectId CreateNewAmendmentBlock(SheetRowData data)
        {
            ObjectId newBlockId = ObjectId.Null;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    BlockReference a1Block = tr.GetObject(data.A1BlockId, OpenMode.ForRead) as BlockReference;
                    if (a1Block == null) return ObjectId.Null;

                    BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                    List<BlockReference> allBlocks = new List<BlockReference>();
                    foreach (ObjectId objId in currentSpace)
                    {
                        BlockReference blk = tr.GetObject(objId, OpenMode.ForRead) as BlockReference;
                        if (blk != null) allBlocks.Add(blk);
                    }

                    double blockScale = 1.0;
                    BlockReference casHeadBlock = allBlocks.FirstOrDefault(b => 
                        GetEffectiveName(tr, b).ToUpper() == "CAS_HEAD" && 
                        IsInsideExtents(b.GeometricExtents, a1Block.GeometricExtents));
                    
                    if (casHeadBlock != null) blockScale = GetBlockScale(tr, casHeadBlock);

                    // Tính toán tọa độ và chèn block mới
                    Point3d insertPoint = CalculateStackedInsertionPoint(tr, allBlocks, a1Block.Position, blockScale);
                    newBlockId = InsertNewAmendmentBlock(db, tr, currentSpace, insertPoint, blockScale, data.Rev, data.Date, data.AmendmentDescription);

                    tr.Commit();
                }
            }

            if (newBlockId != ObjectId.Null) doc.Editor.Regen();
            return newBlockId;
        }

        /// <summary>
        /// Lấy danh sách lịch sử Revision của một bản vẽ cụ thể (Dựa vào ID khung A1)
        /// </summary>
        public List<RevisionHistory> GetRevisionHistory(ObjectId a1BlockId)
        {
            List<RevisionHistory> historyList = new List<RevisionHistory>();
            if (a1BlockId == ObjectId.Null) return historyList;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                BlockReference a1Block = tr.GetObject(a1BlockId, OpenMode.ForRead) as BlockReference;
                if (a1Block == null) return historyList;

                Point3d a1Pos = a1Block.Position;
                double tolerance = 2.0; 

                BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                var tempList = new List<Tuple<double, RevisionHistory>>();

                foreach (ObjectId objId in currentSpace)
                {
                    BlockReference blk = tr.GetObject(objId, OpenMode.ForRead) as BlockReference;
                    if (blk != null && GetEffectiveName(tr, blk).ToUpper() == "SHEETAMENDMENT")
                    {
                        if (Math.Abs(blk.Position.X - a1Pos.X) < tolerance && blk.Position.Y <= a1Pos.Y + tolerance)
                        {
                            string hRev = GetAttributeValue(tr, blk, "REV");
                            string hDate = GetAttributeValue(tr, blk, "DATE");
                            string hDesc = GetAttributeValue(tr, blk, "AMENDMENT");

                            if (!string.IsNullOrEmpty(hRev))
                            {
                                tempList.Add(new Tuple<double, RevisionHistory>(
                                    blk.Position.Y, 
                                    new RevisionHistory { Rev = hRev, Date = hDate, Description = hDesc, BlockId = blk.ObjectId }
                                ));
                            }
                        }
                    }
                }
                tr.Commit();

                historyList = tempList.OrderByDescending(t => t.Item1).Select(t => t.Item2).ToList();
            }

            return historyList;
        }

        /// <summary>
        /// Thuật toán tìm vị trí trống để xếp chồng các Block Rev không bị đè lên nhau
        /// </summary>
        private Point3d CalculateStackedInsertionPoint(Transaction tr, List<BlockReference> allBlocks, Point3d a1Pos, double scale)
        {
            string blockName = "SheetAmendment";
            double tolerance = 2.0; 
            
            Point3d currentPt = new Point3d(a1Pos.X, a1Pos.Y - (scale * 35), a1Pos.Z);

            while (true)
            {
                bool isOccupied = allBlocks.Any(b => GetEffectiveName(tr, b).ToUpper() == blockName.ToUpper() &&
                                  Math.Abs(b.Position.X - currentPt.X) < tolerance && 
                                  Math.Abs(b.Position.Y - currentPt.Y) < tolerance);
                if (isOccupied)
                {
                    currentPt = new Point3d(currentPt.X, currentPt.Y - (scale * 15), currentPt.Z);
                }
                else
                {
                    break;
                }
            }
            return currentPt;
        }

        /// <summary>
        /// Clone Block Amendment từ file thư viện (Symbol.dwg) và bơm Text (Attributes) vào
        /// </summary>
        private ObjectId InsertNewAmendmentBlock(Database db, Transaction tr, BlockTableRecord space, Point3d insertPt, double scale, string rev, string date, string desc)
        {
            string blockName = "SheetAmendment";
            string blockPath = @"C:\CustomTools\Symbol.dwg";
            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
            ObjectId btrId = ObjectId.Null;

            if (bt.Has(blockName)) btrId = bt[blockName];
            else
            {
                if (!System.IO.File.Exists(blockPath)) return ObjectId.Null;
                using (Database extDb = new Database(false, true))
                {
                    extDb.ReadDwgFile(blockPath, FileOpenMode.OpenForReadAndAllShare, true, "");
                    ObjectId sourceBtrId = ObjectId.Null;
                    using (Transaction extTr = extDb.TransactionManager.StartTransaction())
                    {
                        BlockTable extBt = extTr.GetObject(extDb.BlockTableId, OpenMode.ForRead) as BlockTable;
                        if (extBt.Has(blockName)) sourceBtrId = extBt[blockName];
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
                    else return ObjectId.Null;
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
                        
                        if (attRef.Tag.ToUpper() == "REV") attRef.TextString = rev ?? "";
                        else if (attRef.Tag.ToUpper() == "DATE") attRef.TextString = date ?? "";
                        else if (attRef.Tag.ToUpper() == "AMENDMENT") attRef.TextString = desc ?? "";
                        
                        newBlk.AttributeCollection.AppendAttribute(attRef);
                        tr.AddNewlyCreatedDBObject(attRef, true);
                    }
                }
            }
            return newBlk.ObjectId;
        }

        /// <summary>
        /// Đọc các Block trên bản vẽ và gợi ý chữ cái Revision tiếp theo (VD: REV. B)
        /// </summary>
        public string GetHighestRevFromDrawing(Transaction tr, List<BlockReference> allBlocks)
        {
            List<string> revs = new List<string>();
            foreach (var blk in allBlocks)
            {
                if (GetEffectiveName(tr, blk).ToUpper() == "SHEETAMENDMENT")
                {
                    string rev = GetAttributeValue(tr, blk, "REV");
                    if (!string.IsNullOrEmpty(rev)) revs.Add(rev);
                }
            }

            if (revs.Count == 0) return "REV. A";

            char highestChar = 'A';
            foreach (var r in revs)
            {
                string rClean = r.ToUpper().Replace("REV.", "").Replace("REV", "").Trim();
                if (rClean.Length == 1 && rClean[0] >= 'A' && rClean[0] <= 'Z')
                {
                    if (rClean[0] > highestChar) highestChar = rClean[0];
                }
            }

            if (highestChar < 'Z')
            {
                char nextChar = (char)(highestChar + 1);
                return "REV. " + nextChar;
            }
            return "REV. " + highestChar;
        }
    }
}