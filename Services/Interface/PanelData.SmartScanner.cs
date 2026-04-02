using System;
using System.Collections.Generic;
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
        // MODULE: SMART SCANNER - QUẢN LÝ DỮ LIỆU PANEL TRÊN RAM
        // ====================================================================

        /// <summary>
        /// THAY ĐỔI QUAN TRỌNG: Chuyển thành static để tất cả các instance của AutoCadService 
        /// (từ Tab Interface và Tab Fitting) đều dùng chung một bộ nhớ RAM.
        /// </summary>
        public static List<PanelNode> ExtractedPanelNodes { get; set; } = new List<PanelNode>();

        public void SmartAutoNamePanels(string deckNumber, bool addLiftingLugs = true, int guidingType = 2)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // BƯỚC 1: CHỌN ĐƯỜNG CENTERLINE
            PromptNestedEntityOptions nestOpt = new PromptNestedEntityOptions("\n[Ship Plugin] Step 1: Select CENTERLINE (Aft -> Forward): ");
            PromptNestedEntityResult nestRes = ed.GetNestedEntity(nestOpt);
            if (nestRes.Status != PromptStatus.OK) return;

            Point3d clStartWcs, clEndWcs;
            Vector3d clVectorWcs;
            Line wcsClLine; 

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                Entity ent = tr.GetObject(nestRes.ObjectId, OpenMode.ForRead) as Entity;
                Line clLine = ent as Line;
                if (clLine == null) { Application.ShowAlertDialog("Selected object is not a Line!"); return; }

                clStartWcs = clLine.StartPoint.TransformBy(nestRes.Transform);
                clEndWcs = clLine.EndPoint.TransformBy(nestRes.Transform);
                clVectorWcs = clEndWcs - clStartWcs;
                wcsClLine = new Line(clStartWcs, clEndWcs);

                tr.Commit();
            }

            // BƯỚC 2: CHỌN VÙNG QUÉT PANEL
            PromptSelectionOptions selOpt = new PromptSelectionOptions();
            selOpt.MessageForAdding = "\n[Ship Plugin] Step 2: Window select the deck area: ";
            
            TypedValue[] filter = new TypedValue[] { 
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "INSERT"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            };
            PromptSelectionResult selRes = ed.GetSelection(selOpt, new SelectionFilter(filter));
            if (selRes.Status != PromptStatus.OK) return;

            // LÀM TRỐNG KHO DỮ LIỆU STATIC TRƯỚC KHI NẠP MỚI
            ExtractedPanelNodes.Clear(); 
            List<PanelData> validPanels = new List<PanelData>();
            Dictionary<PanelData, Polyline> proxyMap = new Dictionary<PanelData, Polyline>(); 

            int skippedCount = 0, layerFixedCount = 0, autoClosedCount = 0, errorCount = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                EnsureCOGBlockExists(db);

                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                    if (guidingType > 0) EnsureGuidingLayerExists(db, tr);

                    foreach (SelectedObject selObj in selRes.Value)
                    {
                        Entity topEnt = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                        Matrix3d topTransform = Matrix3d.Identity;
                        if (topEnt is BlockReference topBlk) topTransform = topBlk.BlockTransform;

                        Stack<Tuple<ObjectId, Matrix3d>> stack = new Stack<Tuple<ObjectId, Matrix3d>>();
                        stack.Push(new Tuple<ObjectId, Matrix3d>(selObj.ObjectId, topTransform)); 

                        while(stack.Count > 0)
                        {
                            var current = stack.Pop();
                            Entity ent = tr.GetObject(current.Item1, OpenMode.ForRead) as Entity;

                            if (ent is BlockReference blk)
                            {
                                BlockTableRecord btr = tr.GetObject(blk.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                                foreach (ObjectId childId in btr)
                                {
                                    Entity childEnt = tr.GetObject(childId, OpenMode.ForRead) as Entity;
                                    if (childEnt is BlockReference cb) stack.Push(new Tuple<ObjectId, Matrix3d>(childId, current.Item2 * cb.BlockTransform));
                                    else if (childEnt is Polyline) stack.Push(new Tuple<ObjectId, Matrix3d>(childId, current.Item2));
                                }
                            }
                            else if (ent is Polyline childPoly)
                            {
                                Polyline wcsPoly = childPoly.Clone() as Polyline;
                                if (current.Item2 != Matrix3d.Identity) wcsPoly.TransformBy(current.Item2);

                                double area = wcsPoly.Area;
                                if (area < 50000000.0) { skippedCount++; continue; }

                                childPoly.UpgradeOpen();
                                if (!childPoly.Closed)
                                {
                                    if (childPoly.StartPoint.DistanceTo(childPoly.EndPoint) <= 50.0) { childPoly.Closed = true; wcsPoly.Closed = true; autoClosedCount++; }
                                    else { childPoly.ColorIndex = 1; errorCount++; continue; }
                                }

                                if (childPoly.Layer != "0") 
                                { 
                                    childPoly.Layer = "0"; 
                                    childPoly.ColorIndex = 256; 
                                    layerFixedCount++; 
                                }

                                Extents3d bounds = wcsPoly.GeometricExtents;
                                Point3d cogPt = GetPolylineCentroid(wcsPoly); 

                                Point3dCollection intersections = new Point3dCollection();
                                wcsPoly.IntersectWith(wcsClLine, Intersect.OnBothOperands, intersections, IntPtr.Zero, IntPtr.Zero);

                                PanelData pData = new PanelData {
                                    PolyId = childPoly.ObjectId, 
                                    MinX = bounds.MinPoint.X, MaxX = bounds.MaxPoint.X,
                                    Area = area, CogPoint = cogPt,
                                    IntersectsCenterline = intersections.Count > 0
                                };
                                
                                validPanels.Add(pData);
                                proxyMap.Add(pData, wcsPoly); 
                            }
                        }
                    }

                    if (validPanels.Count == 0) return;

                    // BƯỚC 3: PHÂN NHÓM VÀ TÍNH TOÁN TÊN PANEL
                    validPanels = CalculateAndAssignPanelNames(validPanels, clStartWcs, clVectorWcs, clEndWcs);

                    foreach (var panel in validPanels)
                    {
                        Polyline wcsPoly = proxyMap[panel]; 
                        
                        // TÊN PANEL GỌN GÀNG (VD: 6D-01P)
                        string finalName = deckNumber + panel.GroupNumber.ToString("00") + panel.Classification;
                        
                        List<Point2d> polyPts = new List<Point2d>();
                        for (int i = 0; i < wcsPoly.NumberOfVertices; i++)
                        {
                            polyPts.Add(new Point2d(wcsPoly.GetPoint2dAt(i).X, wcsPoly.GetPoint2dAt(i).Y));
                        }
                        
                        // NẠP VÀO KHO DỮ LIỆU STATIC ĐỂ CÁC TAB KHÁC CÓ THỂ SỬ DỤNG
                        ExtractedPanelNodes.Add(new PanelNode {
                            Name = finalName,
                            Area = Math.Round(panel.Area / 1000000.0, 1),
                            WcsBounds = wcsPoly.GeometricExtents,
                            WcsPolygonPoints = polyPts 
                        });

                        // VẼ ĐỒ HỌA RA BẢN VẼ
                        CleanupOldLabelsWCS(tr, currentSpace, wcsPoly);
                        DrawCOGBlock(currentSpace, tr, db, panel);
                        DrawPanelLabels(currentSpace, tr, panel, deckNumber);

                        if (addLiftingLugs)
                        {
                            List<Point3d> liftingPoints = CalculateLiftingPoints(wcsPoly);
                            liftingPoints = SortLiftingPoints(liftingPoints, panel.CogPoint, panel.Classification);
                            DrawPanelGuidesAuto(currentSpace, tr, panel, liftingPoints, guidingType, clVectorWcs, clStartWcs, clEndWcs);
                            DrawBalloonsAndSymbolsWCS(currentSpace, tr, wcsPoly, liftingPoints, panel);
                        }
                    }
                    tr.Commit();
                }
            }
            Application.ShowAlertDialog($"[Smart Scanner] Done!\n- Extracted: {validPanels.Count} Panels to SHARED RAM.\n- Forced Layer 0: {layerFixedCount} objects.\n- Skipped (<50m2): {skippedCount} objects.");
        }

        // --- CÁC HÀM HỖ TRỢ HÌNH HỌC WCS ---

        public bool IsPointInsidePolylineWCS(Polyline poly, Point3d pt)
        {
            bool isInside = false; int n = poly.NumberOfVertices;
            for (int i = 0, j = n - 1; i < n; j = i++) {
                Point3d p1 = poly.GetPoint3dAt(i); Point3d p2 = poly.GetPoint3dAt(j);
                if (((p1.Y > pt.Y) != (p2.Y > pt.Y)) && (pt.X < (p2.X - p1.X) * (pt.Y - p1.Y) / (p2.Y - p1.Y) + p1.X)) isInside = !isInside;
            }
            return isInside;
        }

        public void CleanupOldLabelsWCS(Transaction tr, BlockTableRecord space, Polyline wcsPoly)
        {
            foreach (ObjectId id in space) {
                Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                if (ent == null) continue;

                if (ent.Layer == "Mechanical-AM_5" && ent is Polyline gPoly && gPoly.NumberOfVertices > 0) {
                    Point3d mid = new Point3d((gPoly.GetPoint3dAt(0).X + gPoly.GetPoint3dAt(gPoly.NumberOfVertices - 1).X) / 2, (gPoly.GetPoint3dAt(0).Y + gPoly.GetPoint3dAt(gPoly.NumberOfVertices - 1).Y) / 2, 0);
                    if (IsPointInsidePolylineWCS(wcsPoly, mid)) ent.Erase();
                    continue; 
                }

                if (ent.Layer != "0") continue;

                // [ĐÃ SỬA LỖI CS0103] TextVerticalMode.TextBase
                if (ent is DBText dbText && (dbText.TextString.Contains("m2") || dbText.TextString.Contains("%%u") || dbText.ColorIndex == 4)) {
                    Point3d txtPt = (dbText.HorizontalMode == TextHorizontalMode.TextLeft && dbText.VerticalMode == TextVerticalMode.TextBase) ? dbText.Position : dbText.AlignmentPoint;
                    if (IsPointInsidePolylineWCS(wcsPoly, txtPt)) ent.Erase();
                } else if (ent is BlockReference blk && blk.Name.ToUpper() == "COG") {
                    if (IsPointInsidePolylineWCS(wcsPoly, blk.Position)) ent.Erase();
                } else if (ent is Circle circ && circ.ColorIndex == 4) {
                    if (IsPointInsidePolylineWCS(wcsPoly, circ.Center)) ent.Erase();
                } else if (ent.ColorIndex == 7 && (ent is Hatch || ent is Polyline || ent is Circle)) {
                    Point3d chkPt = Point3d.Origin;
                    if (ent is Circle c) chkPt = c.Center;
                    if (chkPt != Point3d.Origin && IsPointInsidePolylineWCS(wcsPoly, chkPt)) ent.Erase();
                }
            }
        }

        public void DrawBalloonsAndSymbolsWCS(BlockTableRecord currentSpace, Transaction tr, Polyline wcsPoly, List<Point3d> liftingPoints, PanelData panel)
        {
            int lugIndex = 1;
            foreach (Point3d pt in liftingPoints) {
                Vector3d diagVec = ((pt.X > panel.CogPoint.X ? -Vector3d.XAxis : Vector3d.XAxis) + (pt.Y > panel.CogPoint.Y ? -Vector3d.YAxis : Vector3d.YAxis)).GetNormal();
                Point3d balloonCenter = pt;
                
                for (int step = 0; step < 6; step++) {
                    balloonCenter = pt + diagVec * (3000.0 + step * 1000.0);
                    if (IsPointInsidePolylineWCS(wcsPoly, balloonCenter)) break;
                }

                Circle balloon = new Circle(balloonCenter, Vector3d.ZAxis, 600.0) { ColorIndex = 4, Layer = "0" };
                currentSpace.AppendEntity(balloon); tr.AddNewlyCreatedDBObject(balloon, true);

                // [ĐÃ SỬA LỖI CS0117] TextVerticalMode.TextVerticalMid
                DBText lugNum = new DBText() { TextString = lugIndex.ToString(), Height = 750, ColorIndex = 4, Layer = "0", Position = balloonCenter, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid, AlignmentPoint = balloonCenter };
                currentSpace.AppendEntity(lugNum); tr.AddNewlyCreatedDBObject(lugNum, true);

                DrawForceSymbol(currentSpace, tr, balloonCenter, "T1");
                lugIndex++;
            }
        }
    }
}