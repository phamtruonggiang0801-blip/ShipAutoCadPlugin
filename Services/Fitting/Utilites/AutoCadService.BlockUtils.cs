using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: BLOCK UTILS (Các tiện ích khởi tạo và thuộc tính)
        // ====================================================================

        /// <summary>
        /// Kiểm tra và tạo Layer nếu chưa tồn tại.
        /// </summary>
        public void CheckAndCreateLayer(Database db, Transaction tr, string name, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord
                {
                    Name = name,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
                };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        /// <summary>
        /// Thêm định nghĩa Attribute vào Block Table Record.
        /// </summary>
        public void AddAttributeDef(BlockTableRecord btr, Transaction tr, string tag, string val, string prompt, bool inv)
        {
            AttributeDefinition att = new AttributeDefinition
            {
                Position = new Point3d(0, 0, 0),
                Tag = tag,
                TextString = val ?? "",
                Prompt = prompt,
                Invisible = inv,
                Height = 2.5
            };
            btr.AppendEntity(att);
            tr.AddNewlyCreatedDBObject(att, true);
        }

        /// <summary>
        /// Chèn Block Reference vào ModelSpace và gán các Attribute từ định nghĩa.
        /// </summary>
        public void InsertBlockReference(Database db, Transaction tr, ObjectId btrId, Point3d pos)
        {
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            BlockReference br = new BlockReference(pos, btrId);
            ms.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                if (ent is AttributeDefinition ad)
                {
                    AttributeReference ar = new AttributeReference();
                    ar.SetAttributeFromBlock(ad, br.BlockTransform);
                    br.AttributeCollection.AppendAttribute(ar);
                    tr.AddNewlyCreatedDBObject(ar, true);
                }
            }
        }
    }
}