using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: BLOCK REDEFINE (Sync/Redefine Blocks từ bản vẽ đang mở)
        // ====================================================================

        public void RedefineBlocksFromLibrary()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database destDb = doc.Database;
            Editor ed = doc.Editor;

            // 1. Quét chọn các Block cần cập nhật ở bản vẽ hiện tại
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect Blocks to Sync/Redefine: ";
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

            // 2. Lấy danh sách các bản vẽ ĐANG MỞ (Ngoại trừ bản vẽ hiện tại)
            DocumentCollection docs = Application.DocumentManager;
            List<Document> availableDocs = new List<Document>();
            
            foreach (Document d in docs)
            {
                if (d != doc) availableDocs.Add(d);
            }

            if (availableDocs.Count == 0)
            {
                System.Windows.MessageBox.Show("No other drawings are currently open.\nPlease open the source drawing in another tab first.", "Notice", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // =================================================================
            // [UX FIX]: Nén danh sách bản vẽ vào thẳng Dynamic Input Prompt
            // =================================================================
            StringBuilder promptBuilder = new StringBuilder("\nEnter source drawing number: ");
            for (int i = 0; i < availableDocs.Count; i++)
            {
                promptBuilder.Append($"[{i + 1}: {Path.GetFileName(availableDocs[i].Name)}]  ");
            }

            PromptIntegerOptions pio = new PromptIntegerOptions(promptBuilder.ToString());
            pio.LowerLimit = 1;
            pio.UpperLimit = availableDocs.Count;
            PromptIntegerResult pir = ed.GetInteger(pio);

            if (pir.Status != PromptStatus.OK) return;

            // Lấy Database của bản vẽ được chọn làm Nguồn (Source)
            Document sourceDoc = availableDocs[pir.Value - 1];
            Database sourceDb = sourceDoc.Database;

            int updatedCount = 0;

            // =================================================================
            // [PERFORMANCE & CRASH FIX]: Khóa kép và Bọc Transaction cho Clone
            // =================================================================
            using (DocumentLock destLock = doc.LockDocument()) // Khóa bản vẽ đích
            using (DocumentLock srcLock = sourceDoc.LockDocument()) // CRITICAL: Khóa bản vẽ nguồn chống Fatal Error
            {
                try
                {
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
                                ed.WriteMessage($"\n[Warning] Block '{bName}' not found in source drawing.");
                            }
                        }
                        srcTr.Commit(); 
                    }

                    if (sourceBlockIds.Count > 0)
                    {
                        // CRITICAL: WblockCloneObjects PHẢI NẰM TRONG TRANSACTION CỦA DEST DB ĐỂ TỐI ƯU TỐC ĐỘ!
                        using (Transaction destTr = destDb.TransactionManager.StartTransaction())
                        {
                            IdMapping mapping = new IdMapping();
                            destDb.WblockCloneObjects(sourceBlockIds, destDb.BlockTableId, mapping, DuplicateRecordCloning.Replace, false);
                            
                            destTr.Commit(); // Commit 1 lần duy nhất thay vì commit nhỏ lẻ ngầm
                        }
                        
                        ed.Regen();
                        System.Windows.MessageBox.Show($"Successfully synced/redefined {updatedCount} block definition(s) from '{Path.GetFileName(sourceDoc.Name)}'!", "Sync Complete", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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