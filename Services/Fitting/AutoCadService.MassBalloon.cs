using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime; 
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // CLASS LƯU TRỮ TẠM THỜI (Bộ nhớ RAM của thuật toán 2 nhịp)
        // ====================================================================
        private class DiscoveredFitting
        {
            public string PosNum { get; set; }
            public Point3d ArrowPoint { get; set; }
        }

        // ====================================================================
        // MODULE: MASS AUTO-BALLOONING (Đánh Balloon tự động hàng loạt)
        // ====================================================================
        public void MassAutoBalloon()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelect Panel or Details blocks to mass-balloon: ";
                TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
                PromptSelectionResult psr = ed.GetSelection(pso, new SelectionFilter(filter));

                if (psr.Status != PromptStatus.OK) return;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    double mleaderScale = 25.0; // Tỷ lệ chuẩn của Designer
                    List<ObjectId> selectedIds = new List<ObjectId>(psr.Value.GetObjectIds());
                    
                    List<DiscoveredFitting> foundFittings = new List<DiscoveredFitting>();
                    HashSet<string> balloonedPos = new HashSet<string>();

                    // ==========================================================
                    // NHỊP 1: QUÉT RADAR ĐỂ TÌM TỌA ĐỘ VÀ LƯU VÀO RAM
                    // ==========================================================
                    foreach (ObjectId id in selectedIds)
                    {
                        BlockReference topBlk = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (topBlk != null)
                        {
                            DiscoverFittings(tr, topBlk, topBlk.BlockTransform, balloonedPos, foundFittings);
                        }
                    }

                    if (foundFittings.Count == 0)
                    {
                        ed.WriteMessage("\nNo valid fittings with POS_NUM found in selection.");
                        return;
                    }

                    // ==========================================================
                    // NHỊP 2: TÍNH TOÁN BỘ KHUNG ÔM SÁT FITTING VÀ PHÂN BỔ
                    // ==========================================================
                    double minX = double.MaxValue;
                    double maxX = double.MinValue;

                    // Lọc ra mép Trái/Phải cùng của CHÍNH CÁC FITTING
                    foreach (var f in foundFittings)
                    {
                        if (f.ArrowPoint.X < minX) minX = f.ArrowPoint.X;
                        if (f.ArrowPoint.X > maxX) maxX = f.ArrowPoint.X;
                    }

                    // Khoảng cách từ tính (Dynamic Margins) = Kích thước Scale x Đơn vị đệm
                    double margin = 20.0 * mleaderScale;   // Khoảng lùi ra hai bên mép (Ví dụ: 500 units)
                    double slotSpacing = 12.0 * mleaderScale; // Khoảng cách chống đè chiều dọc (Ví dụ: 300 units)
                    
                    double leftBoundary = minX - margin;
                    double rightBoundary = maxX + margin;

                    List<Point3d> occupiedSlots = new List<Point3d>();
                    BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    // Xếp chỗ và Vẽ Balloon
                    foreach (var f in foundFittings)
                    {
                        double distLeft = Math.Abs(f.ArrowPoint.X - leftBoundary);
                        double distRight = Math.Abs(rightBoundary - f.ArrowPoint.X);
                        
                        // Fitting nằm bên nào thì hút về biên bên đó
                        double targetX = (distLeft < distRight) ? leftBoundary : rightBoundary;
                        double targetY = f.ArrowPoint.Y;

                        Point3d candidate = new Point3d(targetX, targetY, 0);

                        // Chống đè Balloon cũ (Trượt dần lên/xuống)
                        int attempt = 1;
                        while (IsSlotOccupied(candidate, occupiedSlots, slotSpacing))
                        {
                            double offset = slotSpacing * ((attempt + 1) / 2);
                            if (attempt % 2 != 0) offset = -offset; 
                            candidate = new Point3d(targetX, targetY + offset, 0);
                            attempt++;
                        }

                        // Chốt tọa độ và bắn MLeader
                        DrawMagneticMLeader(tr, currentSpace, db, f.ArrowPoint, candidate, f.PosNum, mleaderScale);
                        occupiedSlots.Add(candidate);
                    }

                    tr.Commit();
                    ed.WriteMessage($"\nMass Ballooning complete! Placed {foundFittings.Count} smart balloons.");
                }
            }
        }

        // =========================================================
        // HÀM ĐỆ QUY LẤY DỮ LIỆU FITTING (NHỊP 1)
        // =========================================================
        private void DiscoverFittings(Transaction tr, BlockReference blk, Matrix3d currentTransform, 
                                      HashSet<string> balloonedPos, List<DiscoveredFitting> foundFittings)
        {
            string posNum = "";
            bool foundPos = false;
            
            if (blk.AttributeCollection != null)
            {
                foreach (ObjectId attId in blk.AttributeCollection)
                {
                    AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (attRef != null && attRef.Tag.Equals("POS_NUM", StringComparison.OrdinalIgnoreCase))
                    {
                        posNum = attRef.TextString;
                        foundPos = true;
                        break;
                    }
                }
            }

            // Lưu lại Fitting nếu chưa bị đánh dấu
            if (foundPos && !string.IsNullOrWhiteSpace(posNum) && !balloonedPos.Contains(posNum))
            {
                Point3d arrowPoint = Point3d.Origin.TransformBy(currentTransform);
                foundFittings.Add(new DiscoveredFitting { PosNum = posNum, ArrowPoint = arrowPoint });
                balloonedPos.Add(posNum);
            }

            // Moi tiếp các Block con (Hỗ trợ cả Dynamic Block)
            ObjectId btrId = blk.IsDynamicBlock ? blk.DynamicBlockTableRecord : blk.BlockTableRecord;
            BlockTableRecord btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
            
            foreach (ObjectId childId in btr)
            {
                BlockReference childBlk = tr.GetObject(childId, OpenMode.ForRead) as BlockReference;
                if (childBlk != null)
                {
                    Matrix3d nextTransform = currentTransform * childBlk.BlockTransform;
                    DiscoverFittings(tr, childBlk, nextTransform, balloonedPos, foundFittings);
                }
            }
        }

        // =========================================================
        // THUẬT TOÁN KIỂM TRA CHỒNG LẤN (SLOT OCCUPANCY)
        // =========================================================
        private bool IsSlotOccupied(Point3d pt, List<Point3d> occupied, double minDistance)
        {
            foreach (var occ in occupied)
            {
                if (pt.DistanceTo(occ) < minDistance) return true;
            }
            return false;
        }

        // =========================================================
        // LÕI VẼ M-LEADER CHUẨN DESIGNER (Đã truyền thêm tham số Scale)
        // =========================================================
        private void DrawMagneticMLeader(Transaction tr, BlockTableRecord btrSpace, Database db, 
                                         Point3d arrowPt, Point3d balloonPt, string posNum, double scale)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            using (MLeader mleader = new MLeader())
            {
                mleader.SetDatabaseDefaults();
                mleader.Scale = scale;
                mleader.ArrowSize = 3.0;
                mleader.EnableDogleg = true;
                mleader.DoglegLength = 0.001; 

                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (lt.Has("Mechanical-AM_5")) mleader.Layer = "Mechanical-AM_5";

                int leaderIndex = mleader.AddLeader();
                int leaderLineIndex = mleader.AddLeaderLine(leaderIndex);
                mleader.AddFirstVertex(leaderLineIndex, arrowPt);
                mleader.AddLastVertex(leaderLineIndex, balloonPt);

                Vector3d doglegDir = (balloonPt.X > arrowPt.X) ? Vector3d.XAxis : -Vector3d.XAxis;
                mleader.SetDogleg(leaderIndex, doglegDir);

                bool useCircleBlock = bt.Has("_TagCircle");
                if (useCircleBlock)
                {
                    mleader.ContentType = ContentType.BlockContent;
                    mleader.BlockContentId = bt["_TagCircle"];
                    mleader.BlockConnectionType = BlockConnectionType.ConnectExtents;
                    mleader.BlockPosition = balloonPt;
                }
                else
                {
                    mleader.ContentType = ContentType.MTextContent;
                    MText mText = new MText();
                    mText.SetDatabaseDefaults();
                    mText.Contents = posNum;
                    mText.TextHeight = 2.5;
                    mleader.MText = mText;
                    mleader.EnableFrameText = true;
                    mleader.TextLocation = balloonPt;
                }

                btrSpace.AppendEntity(mleader);
                tr.AddNewlyCreatedDBObject(mleader, true);

                if (useCircleBlock)
                {
                    BlockTableRecord circleBtr = (BlockTableRecord)tr.GetObject(bt["_TagCircle"], OpenMode.ForRead);
                    ObjectId attDefId = ObjectId.Null;
                    foreach (ObjectId id in circleBtr)
                    {
                        if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(AttributeDefinition))))
                        {
                            attDefId = id; break;
                        }
                    }
                    if (attDefId != ObjectId.Null)
                    {
                        AttributeDefinition attDef = (AttributeDefinition)tr.GetObject(attDefId, OpenMode.ForRead);
                        AttributeReference attRef = new AttributeReference();
                        attRef.SetAttributeFromBlock(attDef, Matrix3d.Identity);
                        attRef.TextString = posNum;
                        mleader.SetBlockAttribute(attDefId, attRef);
                    }
                }
            }
        }
    }
}