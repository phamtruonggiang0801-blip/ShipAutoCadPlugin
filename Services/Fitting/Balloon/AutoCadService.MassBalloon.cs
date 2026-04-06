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
        private class DiscoveredFitting
        {
            public string PosNum { get; set; }
            public Point3d ArrowPoint { get; set; }
        }

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
                    double mleaderScale = 25.0; 
                    List<ObjectId> selectedIds = new List<ObjectId>(psr.Value.GetObjectIds());
                    
                    List<DiscoveredFitting> foundFittings = new List<DiscoveredFitting>();
                    HashSet<string> balloonedPos = new HashSet<string>();

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

                    double minX = double.MaxValue;
                    double maxX = double.MinValue;

                    foreach (var f in foundFittings)
                    {
                        if (f.ArrowPoint.X < minX) minX = f.ArrowPoint.X;
                        if (f.ArrowPoint.X > maxX) maxX = f.ArrowPoint.X;
                    }

                    double margin = 20.0 * mleaderScale;   
                    double slotSpacing = 12.0 * mleaderScale; 
                    
                    double leftBoundary = minX - margin;
                    double rightBoundary = maxX + margin;

                    List<Point3d> occupiedSlots = new List<Point3d>();
                    BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    foreach (var f in foundFittings)
                    {
                        double distLeft = Math.Abs(f.ArrowPoint.X - leftBoundary);
                        double distRight = Math.Abs(rightBoundary - f.ArrowPoint.X);
                        
                        double targetX = (distLeft < distRight) ? leftBoundary : rightBoundary;
                        double targetY = f.ArrowPoint.Y;

                        Point3d candidate = new Point3d(targetX, targetY, 0);

                        int attempt = 1;
                        while (IsSlotOccupied(candidate, occupiedSlots, slotSpacing))
                        {
                            double offset = slotSpacing * ((attempt + 1) / 2);
                            if (attempt % 2 != 0) offset = -offset; 
                            candidate = new Point3d(targetX, targetY + offset, 0);
                            attempt++;
                        }

                        DrawMagneticMLeader(tr, currentSpace, db, f.ArrowPoint, candidate, f.PosNum, mleaderScale);
                        occupiedSlots.Add(candidate);
                    }

                    tr.Commit();
                    ed.WriteMessage($"\nMass Ballooning complete! Placed {foundFittings.Count} smart balloon clusters.");
                }
            }
        }

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

            if (foundPos && !string.IsNullOrWhiteSpace(posNum) && !balloonedPos.Contains(posNum))
            {
                Point3d arrowPoint = Point3d.Origin.TransformBy(currentTransform);
                foundFittings.Add(new DiscoveredFitting { PosNum = posNum, ArrowPoint = arrowPoint });
                balloonedPos.Add(posNum);
            }

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

        private bool IsSlotOccupied(Point3d pt, List<Point3d> occupied, double minDistance)
        {
            foreach (var occ in occupied)
            {
                if (pt.DistanceTo(occ) < minDistance) return true;
            }
            return false;
        }

        // =========================================================
        // LÕI VẼ M-LEADER ĐƯỢC NÂNG CẤP HỖ TRỢ CHÙM BÓNG
        // =========================================================
        private void DrawMagneticMLeader(Transaction tr, BlockTableRecord btrSpace, Database db, 
                                         Point3d arrowPt, Point3d balloonPt, string rawPosNum, double scale)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            string[] posNumbers = rawPosNum.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (posNumbers.Length == 0) return;

            Vector3d doglegDir = (balloonPt.X > arrowPt.X) ? Vector3d.XAxis : -Vector3d.XAxis;
            bool useCircleBlock = bt.Has("_TagCircle");

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
                mleader.SetDogleg(leaderIndex, doglegDir);

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
                    mText.Contents = posNumbers[0];
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
                        attRef.TextString = posNumbers[0];
                        mleader.SetBlockAttribute(attDefId, attRef);
                    }
                }
            }

            // VẼ CÁC QUẢ BÓNG NỐI TIẾP
            if (useCircleBlock && posNumbers.Length > 1)
            {
                double circleSpacing = 14.0 * scale; 
                for (int i = 1; i < posNumbers.Length; i++)
                {
                    Point3d nextPt = balloonPt + doglegDir * (circleSpacing * i);
                    using (BlockReference stackedBlk = new BlockReference(nextPt, bt["_TagCircle"]))
                    {
                        stackedBlk.ScaleFactors = new Scale3d(scale);
                        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        if (lt.Has("Mechanical-AM_5")) stackedBlk.Layer = "Mechanical-AM_5";

                        btrSpace.AppendEntity(stackedBlk);
                        tr.AddNewlyCreatedDBObject(stackedBlk, true);

                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(stackedBlk.BlockTableRecord, OpenMode.ForRead);
                        foreach (ObjectId id in btr)
                        {
                            if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(AttributeDefinition))))
                            {
                                AttributeDefinition attDef = (AttributeDefinition)tr.GetObject(id, OpenMode.ForRead);
                                using (AttributeReference attRef = new AttributeReference())
                                {
                                    attRef.SetAttributeFromBlock(attDef, stackedBlk.BlockTransform);
                                    attRef.TextString = posNumbers[i];
                                    stackedBlk.AttributeCollection.AppendAttribute(attRef);
                                    tr.AddNewlyCreatedDBObject(attRef, true);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}