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
        // MODULE: ENGINEERING AUDIT (Liên kết Panel - Detail bằng Đa Giác Chính Xác)
        // ====================================================================

        public List<PanelNode> ScanAndMapPanelDetails()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            List<PanelNode> targetPanels = new List<PanelNode>();

            bool useRam = false;
            // [ĐÃ SỬA CS0176] Dùng AutoCadService.ExtractedPanelNodes thay cho this.
            if (AutoCadService.ExtractedPanelNodes != null && AutoCadService.ExtractedPanelNodes.Count > 0)
            {
                PromptKeywordOptions pko = new PromptKeywordOptions($"\n[Audit] Found {AutoCadService.ExtractedPanelNodes.Count} highly accurate Panels in RAM. Use it or Scan manually? [Ram/Scan] <Ram>: ");
                pko.Keywords.Add("Ram");
                pko.Keywords.Add("Scan");
                pko.Keywords.Default = "Ram";
                PromptResult pkr = ed.GetKeywords(pko);
                if (pkr.Status == PromptStatus.Cancel) return null;
                useRam = (pkr.StringResult == "Ram");
            }

            ObjectId panelBlockId = ObjectId.Null;
            if (!useRam)
            {
                PromptEntityOptions opt1 = new PromptEntityOptions("\n[Audit] Select the Master Block containing Panel OuterLines: ");
                opt1.SetRejectMessage("\nYou must select a Block!");
                opt1.AddAllowedClass(typeof(BlockReference), true);
                PromptEntityResult res1 = ed.GetEntity(opt1);
                if (res1.Status != PromptStatus.OK) return null;
                panelBlockId = res1.ObjectId;
            }
            else
            {
                // [ĐÃ SỬA CS0176] Dùng AutoCadService.ExtractedPanelNodes
                // Clone dữ liệu từ RAM, mang theo cả Đa giác (Đã gọt bỏ Type và CalcForce)
                targetPanels = AutoCadService.ExtractedPanelNodes.Select(p => new PanelNode 
                { 
                    Name = p.Name, 
                    Area = p.Area, 
                    WcsBounds = p.WcsBounds,
                    WcsPolygonPoints = p.WcsPolygonPoints, 
                    Children = new System.Collections.ObjectModel.ObservableCollection<BaseEngNode>()
                }).ToList();
            }

            PromptEntityOptions opt2 = new PromptEntityOptions("\n[Audit] Select the Master Block containing Detail Plan Views: ");
            opt2.SetRejectMessage("\nYou must select a Block!");
            opt2.AddAllowedClass(typeof(BlockReference), true);
            PromptEntityResult res2 = ed.GetEntity(opt2);
            if (res2.Status != PromptStatus.OK) return null;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    BlockReference masterDetailBlk = tr.GetObject(res2.ObjectId, OpenMode.ForRead) as BlockReference;

                    // --- BƯỚC A: FALLBACK NẾU KHÔNG DÙNG RAM ---
                    if (!useRam)
                    {
                        BlockReference masterPanelBlk = tr.GetObject(panelBlockId, OpenMode.ForRead) as BlockReference;
                        List<Tuple<Entity, Matrix3d>> rawPanels = new List<Tuple<Entity, Matrix3d>>();
                        Stack<Tuple<ObjectId, Matrix3d>> stackP = new Stack<Tuple<ObjectId, Matrix3d>>();
                        stackP.Push(new Tuple<ObjectId, Matrix3d>(masterPanelBlk.ObjectId, masterPanelBlk.BlockTransform));

                        while(stackP.Count > 0)
                        {
                            var current = stackP.Pop();
                            Entity ent = tr.GetObject(current.Item1, OpenMode.ForRead) as Entity;
                            if (ent is BlockReference blk)
                            {
                                BlockTableRecord btr = tr.GetObject(blk.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                                foreach (ObjectId childId in btr)
                                {
                                    Entity childEnt = tr.GetObject(childId, OpenMode.ForRead) as Entity;
                                    if (childEnt is BlockReference cb) stackP.Push(new Tuple<ObjectId, Matrix3d>(childId, current.Item2 * cb.BlockTransform));
                                    else stackP.Push(new Tuple<ObjectId, Matrix3d>(childId, current.Item2));
                                }
                            }
                            else rawPanels.Add(new Tuple<Entity, Matrix3d>(ent, current.Item2));
                        }

                        var panelPolylines = rawPanels.Where(x => x.Item1 is Polyline).ToList();
                        var panelTexts = rawPanels.Where(x => x.Item1 is DBText || x.Item1 is MText).ToList();

                        foreach (var pLine in panelPolylines)
                        {
                            try
                            {
                                Extents3d bounds = pLine.Item1.GeometricExtents;
                                bounds.TransformBy(pLine.Item2);

                                // Lấy đỉnh Đa giác cho Fallback
                                List<Point2d> polyPts = new List<Point2d>();
                                Polyline poly = pLine.Item1 as Polyline;
                                for (int i = 0; i < poly.NumberOfVertices; i++)
                                {
                                    Point3d pt = poly.GetPoint3dAt(i).TransformBy(pLine.Item2);
                                    polyPts.Add(new Point2d(pt.X, pt.Y));
                                }

                                string panelName = "Unknown Panel";
                                foreach (var txt in panelTexts)
                                {
                                    Point3d txtPt = (txt.Item1 is DBText dbTxt) ? dbTxt.Position : ((MText)txt.Item1).Location;
                                    txtPt = txtPt.TransformBy(txt.Item2);
                                    if (txtPt.X >= bounds.MinPoint.X && txtPt.X <= bounds.MaxPoint.X && txtPt.Y >= bounds.MinPoint.Y && txtPt.Y <= bounds.MaxPoint.Y)
                                    {
                                        string rawText = (txt.Item1 is DBText d) ? d.TextString : ((MText)txt.Item1).Contents;
                                        if (!rawText.Contains("m2")) { panelName = rawText.Replace("%%u", "").Trim(); break; }
                                    }
                                }
                                targetPanels.Add(new PanelNode { Name = panelName, WcsBounds = bounds, WcsPolygonPoints = polyPts, Children = new System.Collections.ObjectModel.ObservableCollection<BaseEngNode>() });
                            }
                            catch { }
                        }
                    }

                    // --- BƯỚC B: BÓC TÁCH DETAIL (TIGHT BOX X-RAY) ---
                    List<DetailNode> detailList = new List<DetailNode>();
                    Stack<Tuple<ObjectId, Matrix3d>> stackD = new Stack<Tuple<ObjectId, Matrix3d>>();
                    stackD.Push(new Tuple<ObjectId, Matrix3d>(masterDetailBlk.ObjectId, Matrix3d.Identity)); 

                    while(stackD.Count > 0)
                    {
                        var current = stackD.Pop();
                        Entity ent = tr.GetObject(current.Item1, OpenMode.ForRead) as Entity;
                        Matrix3d parentMatrix = current.Item2;

                        if (ent is BlockReference blk)
                        {
                            string rawName = GetEffectiveName(tr, blk);
                            if (rawName.ToUpper().Contains("DET"))
                            {
                                Extents3d? tightBounds = null;
                                Stack<Tuple<ObjectId, Matrix3d>> innerStack = new Stack<Tuple<ObjectId, Matrix3d>>();
                                innerStack.Push(new Tuple<ObjectId, Matrix3d>(blk.ObjectId, parentMatrix));

                                while (innerStack.Count > 0)
                                {
                                    var currInner = innerStack.Pop();
                                    Entity innerEnt = tr.GetObject(currInner.Item1, OpenMode.ForRead) as Entity;
                                    if (innerEnt is BlockReference innerBlk)
                                    {
                                        BlockTableRecord innerBtr = tr.GetObject(innerBlk.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                                        foreach (ObjectId innerId in innerBtr) innerStack.Push(new Tuple<ObjectId, Matrix3d>(innerId, currInner.Item2 * innerBlk.BlockTransform));
                                    }
                                    else if (innerEnt is Polyline innerPoly && innerPoly.Closed && innerPoly.Layer.Equals("Mechanical-AM_7", StringComparison.OrdinalIgnoreCase))
                                    {
                                        Extents3d b = innerPoly.GeometricExtents;
                                        b.TransformBy(currInner.Item2);
                                        tightBounds = b;
                                        break; 
                                    }
                                }
                                try 
                                {
                                    string detailId = ExtractDetailId(rawName);
                                    string cleanName = !string.IsNullOrEmpty(detailId) ? "Detail " + detailId : rawName;
                                    Extents3d finalBounds = tightBounds ?? blk.GeometricExtents;
                                    if (!tightBounds.HasValue) finalBounds.TransformBy(parentMatrix);
                                    detailList.Add(new DetailNode { Name = cleanName, WcsBounds = finalBounds });
                                }
                                catch { }
                                continue; 
                            }
                            else
                            {
                                Matrix3d currentMatrix = parentMatrix * blk.BlockTransform;
                                BlockTableRecord btr = tr.GetObject(blk.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                                foreach (ObjectId childId in btr) stackD.Push(new Tuple<ObjectId, Matrix3d>(childId, currentMatrix));
                            }
                        }
                    }

                    // --- BƯỚC C: GIAO CẮT ĐA GIÁC (POLYGON INTERSECTION) ---
                    foreach (var panel in targetPanels)
                    {
                        panel.Children.Clear(); 
                        List<DetailNode> matchedDetails = new List<DetailNode>();

                        foreach (var detail in detailList)
                        {
                            if (IsDetailInsidePanel(detail.WcsBounds, panel))
                            {
                                matchedDetails.Add(detail);
                            }
                        }

                        // NHÓM DETAIL VÀ LƯU QTY ĐỘC LẬP
                        var groupedDetails = matchedDetails.GroupBy(d => d.Name).OrderBy(g => g.Key);
                        foreach (var group in groupedDetails)
                        {
                            int qty = group.Count();
                            panel.Children.Add(new DetailNode
                            {
                                Name = group.Key,    // Trả lại tên mộc mạc (VD: "Detail 2")
                                Qty = qty,           // Gán số lượng vào cột độc lập
                                WcsBounds = group.First().WcsBounds
                            });
                        }
                    }
                    tr.Commit();
                }
            }
            
            // [ĐÃ SỬA CS0176] Lưu lại kết quả vào kho chung
            AutoCadService.ExtractedPanelNodes = targetPanels;
            return targetPanels;
        }

        // ====================================================================
        // HỆ THỐNG TOÁN HỌC KHÔNG GIAN (SPATIAL MATH ENGINE)
        // ====================================================================

        public bool IsDetailInsidePanel(Extents3d detailBounds, PanelNode panel)
        {
            if (detailBounds.MaxPoint.X < panel.WcsBounds.MinPoint.X ||
                detailBounds.MinPoint.X > panel.WcsBounds.MaxPoint.X ||
                detailBounds.MaxPoint.Y < panel.WcsBounds.MinPoint.Y ||
                detailBounds.MinPoint.Y > panel.WcsBounds.MaxPoint.Y)
            {
                return false;
            }

            if (panel.WcsPolygonPoints == null || panel.WcsPolygonPoints.Count < 3) return true;

            Point2d p1 = new Point2d(detailBounds.MinPoint.X, detailBounds.MinPoint.Y); 
            Point2d p2 = new Point2d(detailBounds.MaxPoint.X, detailBounds.MinPoint.Y); 
            Point2d p3 = new Point2d(detailBounds.MaxPoint.X, detailBounds.MaxPoint.Y); 
            Point2d p4 = new Point2d(detailBounds.MinPoint.X, detailBounds.MaxPoint.Y); 
            Point2d center = new Point2d(
                (detailBounds.MinPoint.X + detailBounds.MaxPoint.X) / 2.0,
                (detailBounds.MinPoint.Y + detailBounds.MaxPoint.Y) / 2.0);

            if (IsPointInPolygonMath(p1, panel.WcsPolygonPoints) ||
                IsPointInPolygonMath(p2, panel.WcsPolygonPoints) ||
                IsPointInPolygonMath(p3, panel.WcsPolygonPoints) ||
                IsPointInPolygonMath(p4, panel.WcsPolygonPoints) ||
                IsPointInPolygonMath(center, panel.WcsPolygonPoints))
            {
                return true;
            }

            return false;
        }

        public bool IsPointInPolygonMath(Point2d pt, List<Point2d> polygon)
        {
            bool isInside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point2d p1 = polygon[i];
                Point2d p2 = polygon[j];
                if (((p1.Y > pt.Y) != (p2.Y > pt.Y)) &&
                    (pt.X < (p2.X - p1.X) * (pt.Y - p1.Y) / (p2.Y - p1.Y) + p1.X))
                {
                    isInside = !isInside;
                }
            }
            return isInside;
        }
    }
}