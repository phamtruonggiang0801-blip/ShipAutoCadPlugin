using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Microsoft.Win32;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: BLOCK REDEFINE (Sync/Redefine Blocks từ thư viện ngoài)
        // ====================================================================

        public void RedefineBlocksFromLibrary()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database destDb = doc.Database;
            Editor ed = doc.Editor;

            // 1. Quét chọn các Block cần cập nhật
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nStep 1: Select Blocks to Sync/Redefine: ";
            TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
            PromptSelectionResult psr = ed.GetSelection(pso, new SelectionFilter(filter));
            
            if (psr.Status != PromptStatus.OK) return;

            // Lấy danh sách tên Block duy nhất
            HashSet<string> blockNamesToSync = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = destDb.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in psr.Value)
                {
                    BlockReference blkRef = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (blkRef != null)
                    {
                        string name = GetEffectiveName(tr, blkRef);
                        blockNamesToSync.Add(name);
                    }
                }
                tr.Commit();
            }

            if (blockNamesToSync.Count == 0) return;

            // 2. Chọn file bản vẽ thư viện (Không cần bắt người dùng mở file lên như VBA)
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "AutoCAD Drawing (*.dwg)|*.dwg";
            ofd.Title = "Step 2: Select Source Library Drawing (.dwg)";
            if (ofd.ShowDialog() != true) return;

            int updatedCount = 0;

            // 3. Tiến hành chép đè định nghĩa Block (Bao gồm cả Block con nhờ DuplicateRecordCloning.Replace)
            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Database sourceDb = new Database(false, true))
                {
                    try
                    {
                        sourceDb.ReadDwgFile(ofd.FileName, FileShare.Read, true, "");
                        ObjectIdCollection sourceBlockIds = new ObjectIdCollection();

                        using (Transaction srcTr = sourceDb.TransactionManager.StartTransaction())
                        {
                            BlockTable srcBt = (BlockTable)srcTr.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                            foreach (string bName in blockNamesToSync)
                            {
                                if (srcBt.Has(bName))
                                {
                                    sourceBlockIds.Add(srcBt[bName]);
                                    updatedCount++;
                                }
                                else
                                {
                                    ed.WriteMessage($"\n[Warning] Block '{bName}' not found in source library.");
                                }
                            }
                            srcTr.Commit();
                        }

                        // Ma thuật xảy ra ở đây: AutoCAD API tự lo toàn bộ vòng lặp đệ quy!
                        if (sourceBlockIds.Count > 0)
                        {
                            IdMapping mapping = new IdMapping();
                            destDb.WblockCloneObjects(sourceBlockIds, destDb.BlockTableId, mapping, DuplicateRecordCloning.Replace, false);
                            
                            ed.Regen();
                            System.Windows.MessageBox.Show($"Successfully synced/redefined {updatedCount} block definition(s)!", "Sync Complete", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error syncing blocks: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }
            }
        }
    }
}