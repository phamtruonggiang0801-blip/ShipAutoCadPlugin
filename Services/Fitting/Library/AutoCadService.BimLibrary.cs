using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry; // Bổ sung để dùng Point3d

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: FITTING LIBRARY (Quản lý kho lưu trữ & Chèn Block ngoại vi)
        // ====================================================================

        private readonly string _libraryFolderPath = @"C:\Temp_BIM_Library";

        public Tuple<int, int> PublishToCentralLibrary(List<Tuple<ObjectId, CatalogItem>> itemsToPublish)
        {
            if (!Directory.Exists(_libraryFolderPath))
            {
                Directory.CreateDirectory(_libraryFolderPath);
            }

            Database db = HostApplicationServices.WorkingDatabase;
            List<CatalogItem> successItems = new List<CatalogItem>();

            foreach (var item in itemsToPublish)
            {
                ObjectId blockId = item.Item1;
                CatalogItem info = item.Item2;

                try
                {
                    using (Database exportDb = db.Wblock(blockId))
                    {
                        exportDb.Insunits = UnitsValue.Millimeters;
                        if (File.Exists(info.FilePath)) File.Delete(info.FilePath);
                        exportDb.SaveAs(info.FilePath, DwgVersion.Current);
                    }
                    successItems.Add(info);
                }
                catch (System.Exception ex)
                {
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[Library Error] Failed to export {info.BlockName}: {ex.Message}");
                }
            }

            if (successItems.Count > 0)
            {
                return UpdateMasterCatalog(successItems);
            }

            return new Tuple<int, int>(0, 0);
        }

        private Tuple<int, int> UpdateMasterCatalog(List<CatalogItem> newItems)
        {
            string catalogPath = Path.Combine(_libraryFolderPath, "MasterCatalog.json");
            return MergeItemsToJson(catalogPath, newItems);
        }

        public Tuple<int, int> AddItemsToProjectCatalog(string projectJsonPath, List<CatalogItem> itemsToAdd)
        {
            return MergeItemsToJson(projectJsonPath, itemsToAdd);
        }

        private Tuple<int, int> MergeItemsToJson(string jsonPath, List<CatalogItem> newItems)
        {
            List<CatalogItem> catalog = new List<CatalogItem>();
            int newCount = 0;
            int updatedCount = 0;

            if (File.Exists(jsonPath))
            {
                try
                {
                    string oldJson = File.ReadAllText(jsonPath);
                    catalog = JsonConvert.DeserializeObject<List<CatalogItem>>(oldJson) ?? new List<CatalogItem>();
                }
                catch { catalog = new List<CatalogItem>(); }
            }

            foreach (var newItem in newItems)
            {
                var existingItem = catalog.FirstOrDefault(x => x.BlockName == newItem.BlockName);
                
                if (existingItem == null)
                {
                    newCount++;
                    catalog.Add(newItem);
                }
                else
                {
                    if (existingItem.Revision != newItem.Revision) updatedCount++;
                    catalog.Remove(existingItem);
                    catalog.Add(newItem);
                }
            }

            string newJson = JsonConvert.SerializeObject(catalog, Formatting.Indented);
            File.WriteAllText(jsonPath, newJson);

            return new Tuple<int, int>(newCount, updatedCount);
        }

        // ====================================================================
        // HÀM HELPER LẤY DỮ LIỆU TỪ MASTER CATALOG CHO VLOOKUP (BOM)
        // ====================================================================
        public List<CatalogItem> GetMasterCatalogItems()
        {
            string catalogPath = Path.Combine(_libraryFolderPath, "MasterCatalog.json");
            if (!File.Exists(catalogPath))
            {
                return new List<CatalogItem>();
            }

            try
            {
                string json = File.ReadAllText(catalogPath);
                return JsonConvert.DeserializeObject<List<CatalogItem>>(json) ?? new List<CatalogItem>();
            }
            catch
            {
                return new List<CatalogItem>();
            }
        }

        // ====================================================================
        // HÀM INSERT: Cấy Attribute POS_NUM Tàng Hình & Chèn Block
        // ====================================================================
        public void InsertBlockFromLibrary(string dwgPath, string blockName)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            if (!File.Exists(dwgPath))
            {
                throw new FileNotFoundException($"Không tìm thấy file thư viện: {dwgPath}");
            }

            using (DocumentLock loc = doc.LockDocument())
            {
                ObjectId btrId = ObjectId.Null;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    
                    // 1. Tải định nghĩa Block vào bản vẽ (Nếu chưa có)
                    if (!bt.Has(blockName))
                    {
                        using (Database sideDb = new Database(false, true))
                        {
                            sideDb.ReadDwgFile(dwgPath, FileShare.Read, true, "");
                            sideDb.Insunits = db.Insunits; 
                            btrId = db.Insert(blockName, sideDb, true);
                        }
                    }
                    else
                    {
                        btrId = bt[blockName];
                    }

                    // 2. Mở định nghĩa Block (BlockTableRecord) để chỉnh sửa "Gen"
                    BlockTableRecord targetBtr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                    targetBtr.Units = db.Insunits; // Fix dứt điểm Scale 25.4

                    // ===================================================================
                    // [TÍNH NĂNG MỚI]: Kiểm tra và Cấy Attribute POS_NUM (Tàng hình)
                    // ===================================================================
                    bool hasPosNum = false;
                    foreach (ObjectId childId in targetBtr)
                    {
                        DBObject obj = tr.GetObject(childId, OpenMode.ForRead);
                        if (obj is AttributeDefinition attDef && attDef.Tag.Equals("POS_NUM", StringComparison.OrdinalIgnoreCase))
                        {
                            hasPosNum = true;
                            break;
                        }
                    }

                    // Nếu Block chưa có thẻ POS_NUM, C# sẽ tự động cấy vào khuôn đúc
                    if (!hasPosNum)
                    {
                        AttributeDefinition newAttDef = new AttributeDefinition();
                        newAttDef.Position = new Point3d(0, 0, 0); // Đặt ở gốc tọa độ
                        newAttDef.Prompt = "Position Number";
                        newAttDef.Tag = "POS_NUM";
                        newAttDef.TextString = ""; // Giá trị mặc định rỗng
                        newAttDef.Invisible = true; // [QUAN TRỌNG]: Tàng hình trên bản vẽ
                        newAttDef.Height = 2.5;

                        targetBtr.AppendEntity(newAttDef);
                        tr.AddNewlyCreatedDBObject(newAttDef, true);
                    }

                    tr.Commit();
                }

                // 3. Tạm dừng AutoCAD, đợi Kỹ sư click chọn tọa độ
                PromptPointOptions ppo = new PromptPointOptions($"\nSelect insertion point for '{blockName}' (or press ESC to skip): ");
                PromptPointResult ppr = ed.GetPoint(ppo);

                if (ppr.Status == PromptStatus.OK)
                {
                    // 4. Sử dụng hàm Helper ở BlockUtils để chèn Block 
                    // (Hàm Helper của bạn sẽ tự động đọc AttributeDefinition POS_NUM vừa cấy 
                    // và biến nó thành AttributeReference gắn vào thực thể Block trên màn hình)
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        InsertBlockReference(db, tr, btrId, ppr.Value);
                        tr.Commit();
                    }
                }
                else
                {
                    ed.WriteMessage($"\n[Canceled] Skipped inserting '{blockName}'.");
                }
            }
        }
    }
}