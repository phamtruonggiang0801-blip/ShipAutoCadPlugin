using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ShipAutoCadPlugin.Services
{
    // =========================================================
    // 1. CLASS LƯU TRỮ DỮ LIỆU PANEL TẠM THỜI
    // =========================================================
    public class PanelData
    {
        public ObjectId PolyId { get; set; }
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double Area { get; set; }
        public Point3d CogPoint { get; set; } 
        public int GroupNumber { get; set; }
        public string Classification { get; set; }
        public bool IntersectsCenterline { get; set; }
    }

    class Point3dEqualityComparer : IEqualityComparer<Point3d>
    {
        public bool Equals(Point3d p1, Point3d p2) => p1.DistanceTo(p2) < 1.0; 
        public int GetHashCode(Point3d p) => 0; 
    }

    // =========================================================
    // 2. LÕI XỬ LÝ AUTO PANEL NAMING & LIFTING LUGS & GUIDING
    // =========================================================
    public partial class AutoCadService
    {
        public void AutoNamePanels(string deckNumber, bool addLiftingLugs = true, int guidingType = 2)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptNestedEntityOptions nestOpt = new PromptNestedEntityOptions("\n[Ship Plugin] Step 1: Select CENTERLINE (Aft -> Forward): ");
            PromptNestedEntityResult nestRes = ed.GetNestedEntity(nestOpt);
            
            if (nestRes.Status != PromptStatus.OK) return;

            Line clLine;
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                Entity ent = tr.GetObject(nestRes.ObjectId, OpenMode.ForRead) as Entity;
                clLine = ent as Line;
                if (clLine == null)
                {
                    Application.ShowAlertDialog("Selected object is not a Line. Please try again!");
                    return;
                }
                tr.Commit();
            }

            Vector3d clVector = clLine.EndPoint - clLine.StartPoint;
            Point3d clStart = clLine.StartPoint;

            PromptSelectionOptions selOpt = new PromptSelectionOptions();
            selOpt.MessageForAdding = "\n[Ship Plugin] Step 2: Window select the deck area: ";
            TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") };
            PromptSelectionResult selRes = ed.GetSelection(selOpt, new SelectionFilter(filter));
            
            if (selRes.Status != PromptStatus.OK) return;

            List<PanelData> validPanels = new List<PanelData>();
            int skippedCount = 0, errorCount = 0, autoClosedCount = 0, layerFixedCount = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                EnsureCOGBlockExists(db);

                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                    if (guidingType > 0)
                    {
                        EnsureGuidingLayerExists(db, tr);
                    }

                    // 3. LỌC PANEL VÀ SMART LAYER FIX
                    foreach (SelectedObject selObj in selRes.Value)
                    {
                        Polyline poly = tr.GetObject(selObj.ObjectId, OpenMode.ForWrite) as Polyline;
                        if (poly == null) continue;

                        if (poly.Area < 50000000.0) 
                        {
                            skippedCount++; 
                            continue;
                        }

                        if (!poly.Closed)
                        {
                            if (poly.StartPoint.DistanceTo(poly.EndPoint) <= 10.0)
                            {
                                poly.Closed = true;
                                autoClosedCount++;
                            }
                            else
                            {
                                poly.ColorIndex = 1; 
                                errorCount++;
                                continue; 
                            }
                        }

                        if (poly.Layer != "0")
                        {
                            poly.Layer = "0";
                            poly.ColorIndex = 256; 
                            layerFixedCount++;
                        }

                        Extents3d bounds = poly.GeometricExtents;
                        Point3d cogPt = GetPolylineCentroid(poly);

                        Point3dCollection intersections = new Point3dCollection();
                        poly.IntersectWith(clLine, Intersect.OnBothOperands, intersections, IntPtr.Zero, IntPtr.Zero);

                        validPanels.Add(new PanelData
                        {
                            PolyId = poly.ObjectId,
                            MinX = bounds.MinPoint.X,
                            MaxX = bounds.MaxPoint.X,
                            Area = poly.Area,
                            CogPoint = cogPt,
                            IntersectsCenterline = intersections.Count > 0
                        });
                    }

                    if (validPanels.Count == 0) 
                    {
                        Application.ShowAlertDialog("No valid Panels found in the selected area!");
                        return;
                    }

                    // 4 & 5. MODULE PHÂN NHÓM VÀ MA TRẬN ĐÁNH TÊN
                    validPanels = CalculateAndAssignPanelNames(validPanels, clStart, clVector, clLine.EndPoint);

                    // 6. XUẤT ĐỒ HỌA RA BẢN VẼ
                    foreach (var panel in validPanels)
                    {
                        CleanupOldLabels(tr, currentSpace, panel);

                        // MODULE 1: Vẽ điểm COG
                        DrawCOGBlock(currentSpace, tr, db, panel);

                        // MODULE 2: Vẽ Tên Panel và Diện tích m2
                        DrawPanelLabels(currentSpace, tr, panel, deckNumber);

                        // MODULE ĐIỂM NÂNG HẠ VÀ GUIDING
                        if (addLiftingLugs)
                        {
                            Polyline polyObj = tr.GetObject(panel.PolyId, OpenMode.ForRead) as Polyline;
                            
                            List<Point3d> liftingPoints = CalculateLiftingPoints(polyObj);
                            liftingPoints = SortLiftingPoints(liftingPoints, panel.CogPoint, panel.Classification);

                            // MODULE 4: Vẽ Guide 2 hoặc Guide 3 (Mũi tên chỉ hướng)
                            DrawPanelGuidesAuto(currentSpace, tr, panel, liftingPoints, guidingType, clVector, clStart, clLine.EndPoint);

                            // === VẼ BALLOON SAU (TỊNH TIẾN ĐƯỜNG CHÉO SAFE PUSH) ===
                            // MODULE 3: Vẽ Balloon và Force Symbol (T1 mặc định)
                            DrawBalloonsAndSymbols(currentSpace, tr, polyObj, liftingPoints, panel);
                        }
                    }

                    tr.Commit();
                }
            }

            string finalMsg = $"Successfully processed {validPanels.Count} Panels.\n";
            if (skippedCount > 0) finalMsg += $"- Skipped {skippedCount} invalid objects.\n";
            if (layerFixedCount > 0) finalMsg += $"- Forced {layerFixedCount} Panels to Layer 0.\n";
            if (autoClosedCount > 0) finalMsg += $"- Auto-closed {autoClosedCount} slightly open Panels.\n";
            if (errorCount > 0) finalMsg += $"- Highlighted RED {errorCount} unclosed Panels.";
            Application.ShowAlertDialog(finalMsg);
        }

        // =========================================================
        // HÀM MỚI: THÊM THỦ CÔNG THÔNG MINH ĐỘC LẬP
        // =========================================================
        public void AddManualLiftingPoints()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptKeywordOptions pko = new PromptKeywordOptions("\nSelect object to add/edit [Balloon/Guide2/Guide3/UpdateSymbol] <Balloon>: ");
            pko.Keywords.Add("Balloon");
            pko.Keywords.Add("Guide2");
            pko.Keywords.Add("Guide3");
            pko.Keywords.Add("UpdateSymbol");
            pko.Keywords.Default = "Balloon";
            pko.AllowNone = true;

            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK) return;

            string choice = pkr.StringResult;

            // ====================================================================
            // XỬ LÝ NHÁNH UPDATE SYMBOL HÀNG LOẠT (BATCH UPDATE)
            // ====================================================================
            if (choice == "UpdateSymbol")
            {
                // BỎ TIỀN TỐ TRANG TRÍ CÓ NGOẶC VUÔNG, CHỈ DÙNG NGOẶC VUÔNG CHO KEYWORD
                PromptKeywordOptions pkoType = new PromptKeywordOptions("\nSelect new support type [T1/T2/T3] <T1>: ");
                pkoType.Keywords.Add("T1");
                pkoType.Keywords.Add("T2");
                pkoType.Keywords.Add("T3");
                pkoType.Keywords.Default = "T1"; // Đặt mặc định là T1 vì dùng nhiều nhất
                
                PromptResult pkrType = ed.GetKeywords(pkoType);
                if (pkrType.Status != PromptStatus.OK) return;
                string newSymType = pkrType.StringResult;

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nWindow/Crossing select Balloons to update symbol: ";
                
                // Bộ lọc: Chỉ bắt đúng cái vòng tròn của Balloon (Circle, Color 4)
                TypedValue[] filterBalloon = new TypedValue[] { 
                    new TypedValue((int)DxfCode.Start, "CIRCLE"), 
                    new TypedValue((int)DxfCode.Color, 4) 
                };
                PromptSelectionResult psr = ed.GetSelection(pso, new SelectionFilter(filterBalloon));
                if (psr.Status != PromptStatus.OK) return;

                int updatedCount = 0;
                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction trUpdate = db.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord currentSpace = trUpdate.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                        foreach (SelectedObject so in psr.Value)
                        {
                            Circle circ = trUpdate.GetObject(so.ObjectId, OpenMode.ForRead) as Circle;
                            if (circ != null)
                            {
                                RemoveOldForceSymbol(currentSpace, trUpdate, circ.Center); // Xóa râu cũ
                                DrawForceSymbol(currentSpace, trUpdate, circ.Center, newSymType); // Vẽ râu mới (T1/T2/T3)
                                updatedCount++;
                            }
                        }
                        trUpdate.Commit();
                    }
                }
                ed.WriteMessage($"\n>> [Success] Updated {updatedCount} symbols to {newSymType}.\n");
                return;
            }

            PromptEntityOptions peo = new PromptEntityOptions("\n[Ship Plugin] Select Panel boundary (Polyline): ");
            peo.SetRejectMessage("\nPlease select a Polyline (Panel boundary)!");
            peo.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult per = ed.GetEntity(peo);
            
            if (per.Status != PromptStatus.OK) return;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    Polyline poly = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Polyline;
                    BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                    Point3d cogPt = GetPolylineCentroid(poly);
                    string classification = "C"; 
                    
                    List<Point3d> existingLugPts = new List<Point3d>();
                    List<ObjectId> objsToDelete = new List<ObjectId>();

                    foreach (ObjectId id in currentSpace)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent.Layer != "0") continue;

                        if (ent is DBText txt)
                        {
                            Point3d txtPt = (txt.HorizontalMode == TextHorizontalMode.TextLeft && txt.VerticalMode == TextVerticalMode.TextBase) ? txt.Position : txt.AlignmentPoint;
                            if (txt.TextString.Contains("%%u") && IsPointInsidePolyline(poly.ObjectId, txtPt, tr))
                            {
                                if (txt.TextString.EndsWith("P")) classification = "P";
                                else if (txt.TextString.EndsWith("S")) classification = "S";
                            }
                            
                            // Cửa tử: Chỉ dọn dẹp Balloon nếu chọn luồng Balloon
                            if (choice == "Balloon" && txt.ColorIndex == 4 && IsPointInsidePolyline(poly.ObjectId, txtPt, tr))
                                objsToDelete.Add(id);
                        }
                        else if (ent is Circle circ && circ.ColorIndex == 4 && choice == "Balloon")
                        {
                            if (IsPointInsidePolyline(poly.ObjectId, circ.Center, tr))
                            {
                                existingLugPts.Add(circ.Center); 
                                objsToDelete.Add(id);
                            }
                        }
                    }

                    if (choice == "Guide2" || choice == "Guide3") EnsureGuidingLayerExists(db, tr);

                    List<Point3d> newPts = new List<Point3d>();

                    while (true)
                    {
                        PromptPointOptions ppo = new PromptPointOptions("\n[+] Click exact location to add (Press Enter/Right-click to FINISH): ");
                        ppo.AllowNone = true; 
                        
                        PromptPointResult ppr = ed.GetPoint(ppo);
                        if (ppr.Status == PromptStatus.None || ppr.Status == PromptStatus.Cancel) break; 
                        if (ppr.Status != PromptStatus.OK) break;

                        try 
                        {
                            Point3d clickPt = new Point3d(ppr.Value.X, ppr.Value.Y, poly.Elevation);

                            // Bắt buộc phải click bên trong Panel (Silent Ignore)
                            if (!IsPointInsidePolyline(poly.ObjectId, clickPt, tr))
                            {
                                continue; 
                            }

                            newPts.Add(clickPt);
                            ed.WriteMessage($"\n>> 1 point added (Total temp: {newPts.Count}). Continue clicking or press Enter!");
                        }
                        catch { }
                    }

                    if (newPts.Count == 0) 
                    {
                        ed.WriteMessage("\n[Ship Plugin] No points added. Command ended.");
                        tr.Abort(); 
                        return;
                    }

                    // XỬ LÝ 3 LUỒNG
                    if (choice == "Balloon") 
                    {
                        foreach (ObjectId id in objsToDelete) { Entity e = tr.GetObject(id, OpenMode.ForWrite) as Entity; e?.Erase(); }

                        List<Point3d> allPts = new List<Point3d>();
                        allPts.AddRange(existingLugPts);
                        allPts.AddRange(newPts);
                        allPts = SortLiftingPoints(allPts, cogPt, classification);

                        int lugIndex = 1;
                        foreach (Point3d pt in allPts)
                        {
                            Circle balloon = new Circle(pt, Vector3d.ZAxis, 600.0);
                            balloon.ColorIndex = 4;
                            balloon.Layer = "0";
                            currentSpace.AppendEntity(balloon);
                            tr.AddNewlyCreatedDBObject(balloon, true);

                            DBText lugNum = new DBText();
                            lugNum.TextString = lugIndex.ToString();
                            lugNum.Height = 750;
                            lugNum.ColorIndex = 4;
                            lugNum.Layer = "0";
                            lugNum.Position = pt;
                            lugNum.HorizontalMode = TextHorizontalMode.TextCenter;
                            lugNum.VerticalMode = TextVerticalMode.TextVerticalMid;
                            lugNum.AlignmentPoint = pt; 
                            
                            currentSpace.AppendEntity(lugNum);
                            tr.AddNewlyCreatedDBObject(lugNum, true);
                            DrawForceSymbol(currentSpace, tr, pt, "T1");
                            lugIndex++;
                        }
                    }
                    else 
                    {
                        foreach (Point3d clickPt in newPts)
                        {
                            // TÌM VÀ XÓA GUIDE CŨ TRONG BÁN KÍNH 2000mm NHƯNG PHẢI THUỘC PANEL NÀY
                            foreach (ObjectId id in currentSpace)
                            {
                                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                                if (ent != null && ent.Layer == "Mechanical-AM_5" && ent is Polyline ldrPoly)
                                {
                                    if (ldrPoly.NumberOfVertices > 0)
                                    {
                                        Point3d p1 = ldrPoly.GetPoint3dAt(0);
                                        Point3d p2 = ldrPoly.GetPoint3dAt(ldrPoly.NumberOfVertices - 1);
                                        Point3d midPt = new Point3d((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2, 0);

                                        if (p1.DistanceTo(clickPt) <= 2000.0 || p2.DistanceTo(clickPt) <= 2000.0)
                                        {
                                            // Kiểm tra xem nét Guide này có nằm trong Panel hiện tại không (Bảo vệ Panel hàng xóm)
                                            if (IsPointInsidePolyline(poly.ObjectId, midPt, tr))
                                            {
                                                ent.UpgradeOpen();
                                                ent.Erase();
                                            }
                                        }
                                    }
                                }
                            }

                            int gType = choice == "Guide2" ? 1 : 2;
                            // Dóng điểm click xuống viền để vẽ vuông góc
                            Point3d edgePt = GetClosestVertex(poly, clickPt);
                            DrawGuiding(currentSpace, tr, edgePt, cogPt, gType, classification);
                        }
                    }

                    tr.Commit();
                }
            }
        }
    }
}