using System;
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

            // ==========================================
            // BƯỚC 1: CLICK 1 - "X-RAY" CHỌN NESTED BLOCK
            // ==========================================
            PromptNestedEntityOptions pneo = new PromptNestedEntityOptions("\nSelect Fitting to balloon: ");
            PromptNestedEntityResult pner = ed.GetNestedEntity(pneo);

            if (pner.Status != PromptStatus.OK) return;

            Point3d arrowHeadPoint = pner.PickedPoint;
            
            ObjectId[] containers = pner.GetContainers();

            if (containers == null || containers.Length == 0)
            {
                ed.WriteMessage("\nSelected entity is not inside a Block!");
                return;
            }

            string posNumber = "";
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
                            posNumber = attRef.TextString;
                            foundPos = true;
                            break; 
                        }
                    }
                    if (foundPos) break; 
                }

                if (!foundPos || string.IsNullOrWhiteSpace(posNumber))
                {
                    ed.WriteMessage("\nFitting does not have a POS_NUM assigned or it is empty. Sync BOM first!");
                    return;
                }

                // ==========================================
                // BƯỚC 2: CLICK 2 - ĐẶT BALLOON
                // ==========================================
                PromptPointOptions ppo = new PromptPointOptions($"\nPlace balloon for Pos [{posNumber}]: ");
                ppo.UseBasePoint = true;
                ppo.BasePoint = arrowHeadPoint; 

                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK) return;

                Point3d balloonPoint = ppr.Value;

                // ==========================================
                // BƯỚC 3: VẼ MULTILEADER CHUẨN DESIGNER (CIRCLE BLOCK)
                // ==========================================
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                using (MLeader mleader = new MLeader())
                {
                    mleader.SetDatabaseDefaults();

                    // 1. Ép các thuộc tính hiển thị
                    mleader.Scale = 25.0;                   // Overall scale: 25
                    mleader.ArrowSize = 3.0;                // Arrowhead Size: 3
                    mleader.EnableDogleg = true;            // Horizontal Landing: Yes
                    
                    // [BÍ QUYẾT FIX ĐÂM XUYÊN]: Đặt 0.001 thay vì 0.0 để AutoCAD có Vector tính giao cắt viền tròn
                    mleader.DoglegLength = 0.001;             

                    // 2. Chuyển Layer sang Mechanical-AM_5
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (lt.Has("Mechanical-AM_5"))
                    {
                        mleader.Layer = "Mechanical-AM_5";
                    }

                    // 3. Vẽ Leader Line TRƯỚC
                    int leaderIndex = mleader.AddLeader();
                    int leaderLineIndex = mleader.AddLeaderLine(leaderIndex);
                    mleader.AddFirstVertex(leaderLineIndex, arrowHeadPoint);
                    mleader.AddLastVertex(leaderLineIndex, balloonPoint);

                    // Chỉ định hướng Dogleg rõ ràng (Trái hoặc Phải) để AutoCAD biết đường tính toán
                    Vector3d doglegDir = (balloonPoint.X > arrowHeadPoint.X) ? Vector3d.XAxis : -Vector3d.XAxis;
                    mleader.SetDogleg(leaderIndex, doglegDir);

                    // 4. Khởi tạo Block Vòng tròn SAU để nó tự động Offset lùi ra khỏi Leader Line
                    bool useCircleBlock = bt.Has("_TagCircle");
                    if (useCircleBlock)
                    {
                        mleader.ContentType = ContentType.BlockContent;
                        mleader.BlockContentId = bt["_TagCircle"];
                        mleader.BlockConnectionType = BlockConnectionType.ConnectExtents;
                        mleader.BlockPosition = balloonPoint;
                    }
                    else
                    {
                        // Phương án dự phòng nếu bản vẽ mất file block _TagCircle
                        mleader.ContentType = ContentType.MTextContent;
                        MText mText = new MText();
                        mText.SetDatabaseDefaults();
                        mText.Contents = posNumber;
                        mText.TextHeight = 2.5;
                        mleader.MText = mText;
                        mleader.EnableFrameText = true;
                        mleader.TextLocation = balloonPoint;
                    }

                    // 5. Lưu vào Database TRƯỚC KHI xử lý Attribute
                    btr.AppendEntity(mleader);
                    tr.AddNewlyCreatedDBObject(mleader, true);

                    // 6. Bơm số Pos vào Attribute
                    if (useCircleBlock)
                    {
                        BlockTableRecord circleBtr = (BlockTableRecord)tr.GetObject(bt["_TagCircle"], OpenMode.ForRead);
                        ObjectId attDefId = ObjectId.Null;
                        
                        foreach (ObjectId id in circleBtr)
                        {
                            if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(AttributeDefinition))))
                            {
                                attDefId = id;
                                break;
                            }
                        }

                        if (attDefId != ObjectId.Null)
                        {
                            AttributeDefinition attDef = (AttributeDefinition)tr.GetObject(attDefId, OpenMode.ForRead);
                            AttributeReference attRef = new AttributeReference();
                            
                            attRef.SetAttributeFromBlock(attDef, Matrix3d.Identity); 
                            attRef.TextString = posNumber;
                            
                            mleader.SetBlockAttribute(attDefId, attRef);
                        }
                    }
                }
                tr.Commit();
            }
        }
    }
}