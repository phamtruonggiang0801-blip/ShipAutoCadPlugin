using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShipAutoCadPlugin.Services
{
    public class BalloonCommands
    {
        [CommandMethod("ADD_BALLOON")]
        public void AutoBalloonNestedCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            while (true)
            {
                PromptNestedEntityOptions pneo = new PromptNestedEntityOptions("\nSelect Fitting to balloon (or press ESC to exit): ");
                PromptNestedEntityResult pner = ed.GetNestedEntity(pneo);

                if (pner.Status != PromptStatus.OK) break;

                Point3d arrowHeadPoint = pner.PickedPoint;
                ObjectId[] containers = pner.GetContainers();

                if (containers == null || containers.Length == 0)
                {
                    ed.WriteMessage("\nSelected entity is not inside a Block! Try again.");
                    continue;
                }

                string rawPosNumber = "";
                bool foundPos = false;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId containerId in containers)
                    {
                        BlockReference blkRef = tr.GetObject(containerId, OpenMode.ForRead) as BlockReference;
                        if (blkRef == null || blkRef.AttributeCollection == null) continue;

                        foreach (ObjectId attId in blkRef.AttributeCollection)
                        {
                            AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                            if (attRef != null && attRef.Tag.Equals("POS_NUM", StringComparison.OrdinalIgnoreCase))
                            {
                                rawPosNumber = attRef.TextString;
                                foundPos = true;
                                break; 
                            }
                        }
                        if (foundPos) break; 
                    }

                    if (!foundPos || string.IsNullOrWhiteSpace(rawPosNumber))
                    {
                        ed.WriteMessage("\nFitting does not have a POS_NUM assigned or it is empty. Sync BOM first!");
                        continue;
                    }

                    // [TÍNH NĂNG MỚI]: Tách chuỗi nếu có nhiều Balloon (Ví dụ: "001,002,003")
                    string[] posNumbers = rawPosNumber.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (posNumbers.Length == 0) continue;

                    PromptPointOptions ppo = new PromptPointOptions($"\nPlace balloon for Pos [{rawPosNumber}] (or press ESC to exit): ");
                    ppo.UseBasePoint = true;
                    ppo.BasePoint = arrowHeadPoint; 

                    PromptPointResult ppr = ed.GetPoint(ppo);
                    if (ppr.Status != PromptStatus.OK) break;

                    Point3d balloonPoint = ppr.Value;

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    double mleaderScale = 25.0; // Tỷ lệ chuẩn
                    Vector3d doglegDir = (balloonPoint.X > arrowHeadPoint.X) ? Vector3d.XAxis : -Vector3d.XAxis;
                    bool useCircleBlock = bt.Has("_TagCircle");

                    // 1. VẼ QUẢ BÓNG CHÍNH VÀ ĐƯỜNG LEADER
                    using (MLeader mleader = new MLeader())
                    {
                        mleader.SetDatabaseDefaults();
                        mleader.Scale = mleaderScale;                   
                        mleader.ArrowSize = 3.0;                
                        mleader.EnableDogleg = true;            
                        mleader.DoglegLength = 0.001;             

                        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        if (lt.Has("Mechanical-AM_5")) mleader.Layer = "Mechanical-AM_5";

                        int leaderIndex = mleader.AddLeader();
                        int leaderLineIndex = mleader.AddLeaderLine(leaderIndex);
                        mleader.AddFirstVertex(leaderLineIndex, arrowHeadPoint);
                        mleader.AddLastVertex(leaderLineIndex, balloonPoint);
                        mleader.SetDogleg(leaderIndex, doglegDir);

                        if (useCircleBlock)
                        {
                            mleader.ContentType = ContentType.BlockContent;
                            mleader.BlockContentId = bt["_TagCircle"];
                            mleader.BlockConnectionType = BlockConnectionType.ConnectExtents;
                            mleader.BlockPosition = balloonPoint;
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
                            mleader.TextLocation = balloonPoint;
                        }

                        btr.AppendEntity(mleader);
                        tr.AddNewlyCreatedDBObject(mleader, true);

                        if (useCircleBlock)
                        {
                            SetBlockAttribute(tr, bt["_TagCircle"], mleader, posNumbers[0]);
                        }
                    }

                    // 2. VẼ CÁC QUẢ BÓNG CHÙM (STACKED BALLOONS) NỐI TIẾP NHAU
                    if (useCircleBlock && posNumbers.Length > 1)
                    {
                        // Khoảng cách giữa các tâm vòng tròn (đường kính khoảng 12-15 đơn vị tùy scale Block gốc)
                        double circleSpacing = 14.0 * mleaderScale; 
                        
                        for (int i = 1; i < posNumbers.Length; i++)
                        {
                            // Tịnh tiến tọa độ sang Trái hoặc Phải
                            Point3d nextPt = balloonPoint + doglegDir * (circleSpacing * i);
                            
                            using (BlockReference stackedBlk = new BlockReference(nextPt, bt["_TagCircle"]))
                            {
                                stackedBlk.ScaleFactors = new Scale3d(mleaderScale);
                                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                                if (lt.Has("Mechanical-AM_5")) stackedBlk.Layer = "Mechanical-AM_5";

                                btr.AppendEntity(stackedBlk);
                                tr.AddNewlyCreatedDBObject(stackedBlk, true);

                                // Điền số cho các quả bóng phụ
                                InjectAttributeToBlock(tr, stackedBlk, posNumbers[i]);
                            }
                        }
                    }

                    tr.Commit();
                }
            }
        }

        // --- Helper cho MLeader ---
        private void SetBlockAttribute(Transaction tr, ObjectId blockId, MLeader leader, string value)
        {
            BlockTableRecord circleBtr = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
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
                attRef.TextString = value;
                leader.SetBlockAttribute(attDefId, attRef);
            }
        }

        // --- Helper cho Standalone Block (Stacked) ---
        private void InjectAttributeToBlock(Transaction tr, BlockReference blkRef, string value)
        {
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(AttributeDefinition))))
                {
                    AttributeDefinition attDef = (AttributeDefinition)tr.GetObject(id, OpenMode.ForRead);
                    using (AttributeReference attRef = new AttributeReference())
                    {
                        attRef.SetAttributeFromBlock(attDef, blkRef.BlockTransform);
                        attRef.TextString = value;
                        blkRef.AttributeCollection.AppendAttribute(attRef);
                        tr.AddNewlyCreatedDBObject(attRef, true);
                    }
                }
            }
        }
    }
}