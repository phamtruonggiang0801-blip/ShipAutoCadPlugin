using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: VIRTUAL BOM & GEOMETRIC FEATURE (Nhận diện vật tư phi hình học)
        // ====================================================================

        public CatalogItem PickGeometricFeatureFromCad()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // [CẬP NHẬT MỚI]: Chuyển sang GetSelection để cho phép quét chọn (Multi-select)
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect objects (Blocks, Lines, Polylines, Circles, Arcs) to define as a Fitting: ";
            
            PromptSelectionResult psr = ed.GetSelection(pso);
            
            if (psr.Status != PromptStatus.OK) 
            {
                return null;
            }

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    CatalogItem draftItem = new CatalogItem();
                    List<string> collectedBlockNames = new List<string>();
                    Entity firstValidEnt = null;

                    // Duyệt qua tất cả các đối tượng được chọn
                    ObjectId[] ids = psr.Value.GetObjectIds();
                    foreach (ObjectId id in ids)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        bool isValidObject = ent is Line || ent is Polyline || ent is Polyline2d || ent is Polyline3d || ent is Circle || ent is Arc || ent is BlockReference;
                        if (!isValidObject) continue; // Bỏ qua rác (Text, Hatch...)

                        // Lấy đối tượng hợp lệ đầu tiên làm "Đại diện" để đọc Layer/Color
                        if (firstValidEnt == null) firstValidEnt = ent;

                        // Nếu là Block, thu thập tên của nó
                        if (ent is BlockReference blk)
                        {
                            string blkName = blk.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(blk.DynamicBlockTableRecord, OpenMode.ForRead)).Name : blk.Name;
                            if (!collectedBlockNames.Contains(blkName))
                            {
                                collectedBlockNames.Add(blkName);
                            }
                        }
                    }

                    if (firstValidEnt == null)
                    {
                        Application.ShowAlertDialog("No valid objects selected.\nPlease select Blocks, Lines, Polylines, Circles, or Arcs.");
                        return null;
                    }

                    // 1. Đọc Layer / Color từ đối tượng Đại diện
                    draftItem.EntityType = collectedBlockNames.Count > 0 ? "Block" : firstValidEnt.GetType().Name;
                    draftItem.TriggerLayer = firstValidEnt.Layer;        

                    if (firstValidEnt.Color.IsByLayer)
                    {
                        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        if (lt.Has(firstValidEnt.Layer))
                        {
                            LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[firstValidEnt.Layer], OpenMode.ForRead);
                            draftItem.TriggerColor = $"ByLayer (Index: {ltr.Color.ColorIndex})";
                        }
                    }
                    else
                    {
                        draftItem.TriggerColor = $"Index: {firstValidEnt.ColorIndex}";
                    }

                    // 2. Xử lý logic gộp Block (Multi-View)
                    double width = 0;
                    if (collectedBlockNames.Count > 0)
                    {
                        // Ghép các tên Block bằng dấu chấm phẩy (VD: "CAS-123 - tv;CAS-123 - fv")
                        draftItem.BlockName = string.Join(";", collectedBlockNames);
                        draftItem.UoM = "pcs";
                        
                        // Tự động tìm chuỗi "CAS-XXXXXXX" từ Block đầu tiên
                        var match = System.Text.RegularExpressions.Regex.Match(collectedBlockNames[0], @"(?i)CAS-\d{7}");
                        if (match.Success) 
                        {
                            draftItem.PartNumber = match.Value.ToUpper();
                        }
                    }
                    else if (firstValidEnt is Polyline pline) 
                    {
                        try { width = pline.ConstantWidth; } 
                        catch { if (pline.NumberOfVertices > 0) width = pline.GetStartWidthAt(0); else width = 0; }
                        draftItem.UoM = "m"; 
                    }
                    else if (firstValidEnt is Polyline2d pline2d)
                    {
                        try { width = pline2d.DefaultStartWidth; } catch { width = 0; }
                        draftItem.UoM = "m";
                    }
                    else if (firstValidEnt is Line || firstValidEnt is Arc)
                    {
                        draftItem.UoM = "m"; 
                    }
                    else
                    {
                        draftItem.UoM = "pcs"; 
                    }

                    if (width > 0) draftItem.Description = $"[Width: {width}] ";
                    draftItem.BomType = "DETAIL"; 
                    if (string.IsNullOrEmpty(draftItem.PartNumber)) draftItem.PartNumber = "";
                    draftItem.Title = ""; 
                    draftItem.Mass = "0";

                    tr.Commit();
                    
                    return draftItem;
                }
            }
        }
    }
}