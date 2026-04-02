using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: PLAN VIEW LABELS (Thuật toán Trí tuệ Nhân tạo Định vị Text)
        // ====================================================================

        public void GenerateDetailLabels()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") };
            SelectionFilter selFilter = new SelectionFilter(filter);

            PromptSelectionOptions opts = new PromptSelectionOptions();
            opts.MessageForAdding = "\n[Ship Plugin] Select Detail Blocks (or Master Block) to auto-generate labels: ";
            
            PromptSelectionResult res = ed.GetSelection(opts, selFilter);
            if (res.Status != PromptStatus.OK) return;

            int counter = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    EnsureLayerExists(tr, db, "Mechanical-AM_1"); 
                    BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                    foreach (SelectedObject selObj in res.Value)
                    {
                        BlockReference topBlkRef = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (topBlkRef == null) continue;

                        // BƯỚC 1: TÌM TẤT CẢ BLOCK DETAIL
                        List<Tuple<string, ObjectId, Matrix3d>> targetDetails = new List<Tuple<string, ObjectId, Matrix3d>>();
                        Stack<Tuple<ObjectId, Matrix3d>> stack = new Stack<Tuple<ObjectId, Matrix3d>>();
                        stack.Push(new Tuple<ObjectId, Matrix3d>(topBlkRef.ObjectId, topBlkRef.BlockTransform));

                        while (stack.Count > 0)
                        {
                            var current = stack.Pop();
                            BlockReference blk = tr.GetObject(current.Item1, OpenMode.ForRead) as BlockReference;
                            string bName = GetEffectiveName(tr, blk);
                            string detId = ExtractDetailId(bName);

                            if (!string.IsNullOrEmpty(detId))
                            {
                                targetDetails.Add(new Tuple<string, ObjectId, Matrix3d>(detId, current.Item1, current.Item2));
                            }
                            else
                            {
                                BlockTableRecord btr = tr.GetObject(blk.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                                foreach (ObjectId entId in btr)
                                {
                                    Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                                    if (ent is BlockReference childBlk)
                                    {
                                        stack.Push(new Tuple<ObjectId, Matrix3d>(childBlk.ObjectId, current.Item2 * childBlk.BlockTransform));
                                    }
                                }
                            }
                        }

                        if (targetDetails.Count == 0) continue;

                        // BƯỚC 2: TIỀN XỬ LÝ - THU THẬP VẬT CẢN (OBSTACLES)
                        List<Extents3d> obstacles = new List<Extents3d>();
                        
                        // 2.1 Thu thập Balloon, Guide và Symbol có sẵn trên bản vẽ
                        foreach (ObjectId id in currentSpace)
                        {
                            Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (ent == null) continue;

                            if (ent.Layer == "Mechanical-AM_5" || (ent is Circle && ent.ColorIndex == 4) || ent.ColorIndex == 7)
                            {
                                try { obstacles.Add(ent.GeometricExtents); } catch { }
                            }
                        }

                        // 2.2 Bóc tách khung AM_7 của tất cả Detail để làm vật cản (Tránh Text này đè lên Khung kia)
                        var detailDataList = new List<Tuple<string, ObjectId, Extents3d?, Matrix3d>>();
                        foreach (var detInfo in targetDetails)
                        {
                            List<Tuple<Entity, Matrix3d>> nestedEnts = new List<Tuple<Entity, Matrix3d>>();
                            ExtractEntitiesFromNestedBlock(tr, detInfo.Item2, detInfo.Item3, nestedEnts);

                            Extents3d? am7Bounds = null;
                            foreach (var item in nestedEnts)
                            {
                                if (item.Item1.Layer.Equals("Mechanical-AM_7", StringComparison.OrdinalIgnoreCase) && item.Item1 is Polyline poly && poly.Closed)
                                {
                                    Extents3d b = poly.GeometricExtents;
                                    b.TransformBy(item.Item2);
                                    am7Bounds = b;
                                    obstacles.Add(b); // Đưa khung AM_7 vào danh sách vật cản
                                    break; 
                                }
                            }
                            detailDataList.Add(new Tuple<string, ObjectId, Extents3d?, Matrix3d>(detInfo.Item1, detInfo.Item2, am7Bounds, detInfo.Item3));
                        }

                        // BƯỚC 3: THUẬT TOÁN TÌM CHỖ TRỐNG (COLLISION DETECTION)
                        double offset = 400.0;    // Dịch ra khỏi khung 400mm
                        double textW = 4500.0;    // Dự đoán chiều rộng của Text "Detail X"
                        double textH = 1000.0;    // Dự đoán chiều cao của Text

                        foreach (var data in detailDataList)
                        {
                            Point3d insertPt = new Point3d(data.Item4.CoordinateSystem3d.Origin.X, data.Item4.CoordinateSystem3d.Origin.Y + 1500, 0);
                            AttachmentPoint attachPt = AttachmentPoint.BottomLeft;
                            Extents3d finalTextBox = new Extents3d();

                            if (data.Item3.HasValue)
                            {
                                Extents3d am7 = data.Item3.Value;

                                // Tạo 4 Ứng viên (Candidates) ở 4 góc
                                // 1. Top-Right
                                Point3d pt1 = new Point3d(am7.MaxPoint.X + offset, am7.MaxPoint.Y + offset, 0);
                                Extents3d box1 = new Extents3d(pt1, new Point3d(pt1.X + textW, pt1.Y + textH, 0));
                                
                                // 2. Top-Left
                                Point3d pt2 = new Point3d(am7.MinPoint.X - offset, am7.MaxPoint.Y + offset, 0);
                                Extents3d box2 = new Extents3d(new Point3d(pt2.X - textW, pt2.Y, 0), new Point3d(pt2.X, pt2.Y + textH, 0));
                                
                                // 3. Bottom-Right
                                Point3d pt3 = new Point3d(am7.MaxPoint.X + offset, am7.MinPoint.Y - offset, 0);
                                Extents3d box3 = new Extents3d(new Point3d(pt3.X, pt3.Y - textH, 0), new Point3d(pt3.X + textW, pt3.Y, 0));
                                
                                // 4. Bottom-Left
                                Point3d pt4 = new Point3d(am7.MinPoint.X - offset, am7.MinPoint.Y - offset, 0);
                                Extents3d box4 = new Extents3d(new Point3d(pt4.X - textW, pt4.Y - textH, 0), new Point3d(pt4.X, pt4.Y, 0));

                                // Danh sách ứng viên theo thứ tự ưu tiên (Thẩm mỹ nhất -> Ít thẩm mỹ hơn)
                                var candidates = new[] {
                                    new { Pt = pt1, Attach = AttachmentPoint.BottomLeft, Box = box1 },
                                    new { Pt = pt2, Attach = AttachmentPoint.BottomRight, Box = box2 },
                                    new { Pt = pt3, Attach = AttachmentPoint.TopLeft, Box = box3 },
                                    new { Pt = pt4, Attach = AttachmentPoint.TopRight, Box = box4 }
                                };

                                int minScore = int.MaxValue;
                                var bestCandidate = candidates[0];

                                // Chấm điểm từng Ứng viên
                                foreach (var cand in candidates)
                                {
                                    int score = 0;
                                    foreach (var obs in obstacles)
                                    {
                                        // Bỏ qua nếu vật cản chính là khung AM_7 của Detail này
                                        if (obs.MinPoint == am7.MinPoint && obs.MaxPoint == am7.MaxPoint) continue;

                                        // Hàm AABB Intersection nội bộ siêu tốc
                                        if (!(cand.Box.MaxPoint.X < obs.MinPoint.X || cand.Box.MinPoint.X > obs.MaxPoint.X ||
                                              cand.Box.MaxPoint.Y < obs.MinPoint.Y || cand.Box.MinPoint.Y > obs.MaxPoint.Y))
                                        {
                                            score++; // Bị va chạm -> Bị phạt 1 điểm
                                        }
                                    }

                                    if (score < minScore)
                                    {
                                        minScore = score;
                                        bestCandidate = cand;
                                    }

                                    if (minScore == 0) break; // Điểm 0 (Không va ai) -> Hoàn hảo, bế luôn không cần check thêm!
                                }

                                insertPt = bestCandidate.Pt;
                                attachPt = bestCandidate.Attach;
                                finalTextBox = bestCandidate.Box;
                            }

                            // Đặt Text vào vị trí tốt nhất
                            MText mText = new MText();
                            mText.Contents = "Detail " + data.Item1;
                            mText.Location = insertPt;
                            mText.Attachment = attachPt; 
                            mText.TextHeight = 750; 
                            mText.Layer = "Mechanical-AM_1";
                            
                            currentSpace.AppendEntity(mText);
                            tr.AddNewlyCreatedDBObject(mText, true);

                            // CẬP NHẬT VẬT CẢN: Bổ sung chính cái Text vừa tạo vào list vật cản để các Text sau không đè lên nó!
                            if (data.Item3.HasValue) obstacles.Add(finalTextBox);
                            
                            counter++;
                        }
                    }

                    tr.Commit(); 
                }
            }

            if (counter > 0)
            {
                Application.ShowAlertDialog($"Done! AI successfully placed {counter} Detail label(s) with minimal collisions.");
            }
        }

        /// <summary>
        /// Cập nhật Text Detail có sẵn dựa trên khoảng cách (Proximity)
        /// </summary>
        public void UpdateDetailNameByProximity()
        {
            // ... (Phần code này giữ nguyên y hệt như cũ của bạn, không đụng chạm)
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptEntityOptions optSrc = new PromptEntityOptions("\n[Ship Plugin] Select SOURCE Block (Block containing child Details): ");
            optSrc.SetRejectMessage("\nYou must select a Block!");
            optSrc.AddAllowedClass(typeof(BlockReference), true);
            PromptEntityResult resSrc = ed.GetEntity(optSrc);
            if (resSrc.Status != PromptStatus.OK) return;

            PromptEntityOptions optTgt = new PromptEntityOptions("\n[Ship Plugin] Select TARGET Block (Block containing Text to update): ");
            optTgt.SetRejectMessage("\nYou must select a Block!");
            optTgt.AddAllowedClass(typeof(BlockReference), true);
            PromptEntityResult resTgt = ed.GetEntity(optTgt);
            if (resTgt.Status != PromptStatus.OK) return;

            if (resSrc.ObjectId == resTgt.ObjectId)
            {
                Application.ShowAlertDialog("Source and Target Blocks cannot be the same!");
                return;
            }

            int updateCounter = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    EnsureLayerExists(tr, db, "Mechanical-AM_1");

                    BlockReference sourceRef = tr.GetObject(resSrc.ObjectId, OpenMode.ForRead) as BlockReference;
                    BlockReference targetRef = tr.GetObject(resTgt.ObjectId, OpenMode.ForRead) as BlockReference;

                    BlockTableRecord sourceDef = tr.GetObject(sourceRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    BlockTableRecord targetDef = tr.GetObject(targetRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;

                    foreach (ObjectId srcId in sourceDef)
                    {
                        Entity srcEnt = tr.GetObject(srcId, OpenMode.ForRead) as Entity;
                        if (srcEnt is BlockReference nestedRef)
                        {
                            string nestedBlockName = GetEffectiveName(tr, nestedRef);
                            string detailId = ExtractDetailId(nestedBlockName);

                            if (!string.IsNullOrEmpty(detailId))
                            {
                                Point3d nestedWcsPt = nestedRef.Position.TransformBy(sourceRef.BlockTransform);
                                double minDistance = double.MaxValue;
                                ObjectId closestTextId = ObjectId.Null;

                                foreach (ObjectId tgtId in targetDef)
                                {
                                    Entity tgtEnt = tr.GetObject(tgtId, OpenMode.ForRead) as Entity;
                                    if (tgtEnt is MText || tgtEnt is DBText)
                                    {
                                        Point3d textLocalPt = (tgtEnt is MText mt) ? mt.Location : ((DBText)tgtEnt).Position;
                                        Point3d textWcsPt = textLocalPt.TransformBy(targetRef.BlockTransform);

                                        double dist = new Point2d(nestedWcsPt.X, nestedWcsPt.Y).GetDistanceTo(new Point2d(textWcsPt.X, textWcsPt.Y));

                                        if (dist < minDistance)
                                        {
                                            minDistance = dist;
                                            closestTextId = tgtId;
                                        }
                                    }
                                }

                                if (closestTextId != ObjectId.Null)
                                {
                                    Entity textToUpdate = tr.GetObject(closestTextId, OpenMode.ForWrite) as Entity;
                                    string newContent = "Detail " + detailId;

                                    if (textToUpdate is MText mTextUpd)
                                    {
                                        if (mTextUpd.Contents != newContent)
                                        {
                                            mTextUpd.Contents = newContent;
                                            mTextUpd.Layer = "Mechanical-AM_1";
                                            mTextUpd.TextHeight = 700;
                                            updateCounter++;
                                        }
                                    }
                                    else if (textToUpdate is DBText dbTextUpd)
                                    {
                                        if (dbTextUpd.TextString != newContent)
                                        {
                                            dbTextUpd.TextString = newContent;
                                            dbTextUpd.Layer = "Mechanical-AM_1";
                                            dbTextUpd.Height = 700;
                                            updateCounter++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    tr.Commit(); 
                }
            }

            if (updateCounter > 0)
            {
                ed.Regen(); 
                Application.ShowAlertDialog($"Done! Successfully updated {updateCounter} Text/MText object(s).");
            }
            else ed.WriteMessage("\n[Ship Plugin] No Text found that required updating.");
        }
    }
}