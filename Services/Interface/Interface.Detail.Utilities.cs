using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System.Text.RegularExpressions;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: GENERAL CAD UTILITIES (Các hàm hỗ trợ CAD dùng chung)
        // ====================================================================

        public bool DeleteBlock(ObjectId blockId)
        {
            if (blockId == ObjectId.Null) return false;
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = doc.TransactionManager.StartTransaction())
                    {
                        DBObject obj = tr.GetObject(blockId, OpenMode.ForWrite);
                        if (obj != null && !obj.IsErased) obj.Erase();
                        tr.Commit();
                    }
                }
                doc.Editor.Regen(); 
                return true;
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog("Error deleting Block: " + ex.Message);
                return false;
            }
        }

        public void ZoomToExtentsWithBuffer(Editor ed, Extents3d extents, double bufferFactor)
        {
            try
            {
                double width = extents.MaxPoint.X - extents.MinPoint.X;
                double height = extents.MaxPoint.Y - extents.MinPoint.Y;
                
                double addX = (width * bufferFactor) / 2.0;
                double addY = (height * bufferFactor) / 2.0;

                extents.AddExtents(new Extents3d(
                    new Point3d(extents.MinPoint.X - addX, extents.MinPoint.Y - addY, 0),
                    new Point3d(extents.MaxPoint.X + addX, extents.MaxPoint.Y + addY, 0)));

                ViewTableRecord view = ed.GetCurrentView();
                view.CenterPoint = new Point2d(
                    (extents.MaxPoint.X + extents.MinPoint.X) / 2.0,
                    (extents.MaxPoint.Y + extents.MinPoint.Y) / 2.0);
                view.Height = extents.MaxPoint.Y - extents.MinPoint.Y;
                view.Width = extents.MaxPoint.X - extents.MinPoint.X;
                
                ed.SetCurrentView(view);
            }
            catch (Exception) {}
        }

        public void EnsureLayerExists(Transaction tr, Database db, string layerName)
        {
            LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3); 
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        public string GetEffectiveName(Transaction tr, BlockReference blk)
        {
            return blk.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(blk.DynamicBlockTableRecord, OpenMode.ForRead)).Name : blk.Name;
        }

        public bool IsInsideExtents(Extents3d inner, Extents3d outer)
        {
            return inner.MinPoint.X >= outer.MinPoint.X && inner.MinPoint.Y >= outer.MinPoint.Y &&
                   inner.MaxPoint.X <= outer.MaxPoint.X && inner.MaxPoint.Y <= outer.MaxPoint.Y;
        }

        public string GetAttributeValue(Transaction tr, BlockReference blk, string tag)
        {
            if (blk.AttributeCollection != null)
            {
                foreach (ObjectId attId in blk.AttributeCollection)
                {
                    AttributeReference att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (att != null && att.Tag.ToUpper() == tag.ToUpper()) return att.TextString;
                }
            }
            return "";
        }

        public double GetBlockScale(Transaction tr, BlockReference casHead)
        {
            string scaleText = GetAttributeValue(tr, casHead, "GEN-TITLE-SCA{5.42}");
            if (scaleText.Contains(":"))
            {
                string numStr = scaleText.Substring(scaleText.IndexOf(":") + 1).Trim();
                if (double.TryParse(numStr, out double scale)) return scale;
            }
            return 1.0;
        }

        /// <summary>
        /// Trích xuất ký tự Detail từ tên Block bằng Regex (Dùng chung cho cả Plan và Detail)
        /// </summary>
        public string ExtractDetailId(string input)
        {
            // Bắt cả chữ "Det." VÀ "Detail ", bỏ qua khoảng trắng, lấy phần Số + Chữ phía sau.
            Match match = Regex.Match(input, @"(?i)(?:Det\.|Detail\s+)\s*(\d+[a-zA-Z]*)");
            if (match.Success) return match.Groups[1].Value;
            return string.Empty;
        }

        // ====================================================================
        // MODULE: X-RAY & GEOMETRY (Thuật toán hình học và bóc tách Block lồng)
        // ====================================================================

        /// <summary>
        /// Thuật toán X-Ray: Đệ quy chui vào các Block con, lấy ra các Entity kèm theo Ma trận tọa độ thực (WCS)
        /// </summary>
        public void ExtractEntitiesFromNestedBlock(Transaction tr, ObjectId blockId, Matrix3d currentMatrix, List<Tuple<Entity, Matrix3d>> outputList)
        {
            BlockReference blkRef = tr.GetObject(blockId, OpenMode.ForRead) as BlockReference;
            if (blkRef == null) return;

            BlockTableRecord btr = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;

            foreach (ObjectId entId in btr)
            {
                Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ent is BlockReference nestedBlk)
                {
                    // Nếu gặp Block con -> Nhân dồn ma trận và đệ quy chui tiếp vào trong
                    Matrix3d newMatrix = currentMatrix * nestedBlk.BlockTransform;
                    ExtractEntitiesFromNestedBlock(tr, nestedBlk.ObjectId, newMatrix, outputList);
                }
                else
                {
                    // Nếu là đối tượng thường (Line, Polyline, Text) -> Bỏ vào danh sách cùng Ma trận để xử lý sau
                    outputList.Add(new Tuple<Entity, Matrix3d>(ent, currentMatrix));
                }
            }
        }

        /// <summary>
        /// Phép giao cắt Bounding Box (AABB Intersection)
        /// </summary>
        public bool IsBoundsIntersecting(Extents3d a, Extents3d b)
        {
            // Nếu Bounding Box A nằm hoàn toàn bên ngoài Bounding Box B ở bất kỳ trục nào thì trả về false
            if (a.MaxPoint.X < b.MinPoint.X || a.MinPoint.X > b.MaxPoint.X ||
                a.MaxPoint.Y < b.MinPoint.Y || a.MinPoint.Y > b.MaxPoint.Y)
            {
                return false;
            }
            return true; // Ngược lại là có chạm/cắt nhau
        }
    }
}