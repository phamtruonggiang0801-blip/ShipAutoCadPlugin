using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ShipAutoCadPlugin.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // =========================================================
        // MODULE: EXCEL PULL (Kéo dữ liệu từ Excel về CAD/RAM)
        // =========================================================

        /// <summary>
        /// HÀM 1: KÉO TỪ EXCEL VỀ (TRẢ VỀ CẢ TAB 1 VÀ TAB 2)
        /// </summary>
        public List<SheetRowData> ImportFromVaultExcel(string excelPath, out List<ExcelRevHistory> importedHistory)
        {
            List<SheetRowData> importedList = new List<SheetRowData>();
            importedHistory = new List<ExcelRevHistory>(); 
            try
            {
                dynamic excelApp;
                try { excelApp = System.Runtime.InteropServices.Marshal.GetActiveObject("Excel.Application"); }
                catch { Type excelType = Type.GetTypeFromProgID("Excel.Application"); excelApp = Activator.CreateInstance(excelType); }
                
                dynamic workbooks = excelApp.Workbooks;
                dynamic workbook = null;
                string fileName = System.IO.Path.GetFileName(excelPath);

                foreach (dynamic wb in workbooks) {
                    if (wb.Name == fileName || wb.FullName == excelPath) { workbook = wb; break; }
                }

                if (workbook == null) workbook = workbooks.Open(excelPath, Type.Missing, true); // Ép ReadOnly

                // ĐỌC TAB 1
                dynamic sheet1 = workbook.Sheets[1];
                dynamic range1 = sheet1.UsedRange;
                int rowCount1 = range1.Rows.Count;
                
                for (int i = 2; i <= rowCount1; i++)
                {
                    string sheetNo = Convert.ToString(range1.Cells[i, 1].Value);
                    if (string.IsNullOrEmpty(sheetNo)) continue;

                    string content = Convert.ToString(range1.Cells[i, 2].Value);
                    string rev = Convert.ToString(range1.Cells[i, 3].Value);
                    string amend = Convert.ToString(range1.Cells[i, 5].Value);

                    dynamic rawDate = range1.Cells[i, 4].Value;
                    string dateStr = "";
                    if (rawDate != null)
                    {
                        if (rawDate is DateTime dt) dateStr = dt.ToString("yyyy/MM/dd");
                        else if (DateTime.TryParse(rawDate.ToString(), out DateTime parsedDt)) dateStr = parsedDt.ToString("yyyy/MM/dd");
                        else dateStr = rawDate.ToString();
                    }

                    int num = 0;
                    int.TryParse(sheetNo.ToUpper().Replace("SHEET", "").Trim(), out num);

                    importedList.Add(new SheetRowData { SheetNo = sheetNo, Content = content, Rev = rev, Date = dateStr, AmendmentDescription = amend, RawNumericSheetNo = num });
                }

                // ĐỌC TAB 2
                try
                {
                    dynamic sheet2 = workbook.Sheets[2];
                    dynamic range2 = sheet2.UsedRange;
                    int rowCount2 = range2.Rows.Count;
                    
                    for (int i = 2; i <= rowCount2; i++)
                    {
                        string sNo = Convert.ToString(range2.Cells[i, 1].Value);
                        if (!string.IsNullOrEmpty(sNo))
                        {
                            dynamic rawDate = range2.Cells[i, 3].Value;
                            string dateStr = "";
                            if (rawDate != null)
                            {
                                if (rawDate is DateTime dt) dateStr = dt.ToString("yyyy/MM/dd");
                                else if (DateTime.TryParse(rawDate.ToString(), out DateTime parsedDt)) dateStr = parsedDt.ToString("yyyy/MM/dd");
                                else dateStr = rawDate.ToString();
                            }
                            importedHistory.Add(new ExcelRevHistory { 
                                SheetNo = sNo, 
                                Rev = Convert.ToString(range2.Cells[i, 2].Value), 
                                Date = dateStr, 
                                Description = Convert.ToString(range2.Cells[i, 4].Value) 
                            });
                        }
                    }
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(sheet2);
                }
                catch { }

                System.Runtime.InteropServices.Marshal.ReleaseComObject(sheet1);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(workbooks);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp);
            }
            catch (Exception ex)
            {
                Application.ShowAlertDialog("Excel read error: " + ex.Message);
            }
            return importedList;
        }

        /// <summary>
        /// HÀM 2: AUTO-GENERATE (TỰ ĐỘNG CHÈN BLOCK TỪ TAB 2 EXCEL XUỐNG CAD)
        /// </summary>
        public int SyncHistoryBlocksFromExcel(List<ExcelRevHistory> excelHistories, List<SheetRowData> currentGridData)
        {
            if (excelHistories == null || excelHistories.Count == 0) return 0;
            
            int addedCount = 0;
            int updatedCount = 0;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                    
                    List<BlockReference> allBlocks = new List<BlockReference>();
                    foreach (ObjectId objId in currentSpace) {
                        BlockReference blk = tr.GetObject(objId, OpenMode.ForRead) as BlockReference;
                        if (blk != null) allBlocks.Add(blk);
                    }

                    foreach (var gridItem in currentGridData)
                    {
                        if (gridItem.A1BlockId == ObjectId.Null) continue;
                        var a1Block = tr.GetObject(gridItem.A1BlockId, OpenMode.ForRead) as BlockReference;
                        if (a1Block == null) continue;

                        var targetHistories = excelHistories.Where(x => x.SheetNo == gridItem.SheetNo).ToList();
                        if (!targetHistories.Any()) continue;

                        var cadHistories = GetRevisionHistory(gridItem.A1BlockId);

                        double blockScale = 1.0;
                        BlockReference casHeadBlock = allBlocks.FirstOrDefault(b => GetEffectiveName(tr, b).ToUpper() == "CAS_HEAD" && IsInsideExtents(b.GeometricExtents, a1Block.GeometricExtents));
                        if (casHeadBlock != null) blockScale = GetBlockScale(tr, casHeadBlock);

                        foreach (var exHist in targetHistories)
                        {
                            // -----------------------------------------------------------------
                            // CHỐT CHẶN: NẾU EXCEL CÓ DÒNG NÀY NHƯNG DESCRIPTION TRỐNG
                            // THÌ BỎ QUA HOÀN TOÀN, KHÔNG TẠO BLOCK TRÊN MẶT BẢN VẼ!
                            // -----------------------------------------------------------------
                            if (string.IsNullOrWhiteSpace(exHist.Description))
                            {
                                continue; 
                            }

                            var existingCad = cadHistories.FirstOrDefault(c => c.Rev.ToUpper() == exHist.Rev.ToUpper());
                            if (existingCad != null)
                            {
                                BlockReference blk = tr.GetObject(existingCad.BlockId, OpenMode.ForWrite) as BlockReference;
                                foreach (ObjectId attId in blk.AttributeCollection)
                                {
                                    AttributeReference att = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                                    if (att.Tag.ToUpper() == "DATE" && att.TextString != exHist.Date) { att.TextString = exHist.Date ?? ""; updatedCount++; }
                                    else if (att.Tag.ToUpper() == "AMENDMENT" && att.TextString != exHist.Description) { att.TextString = exHist.Description ?? ""; updatedCount++; }
                                }
                            }
                            else
                            {
                                Point3d insertPoint = CalculateStackedInsertionPoint(tr, allBlocks, a1Block.Position, blockScale);
                                ObjectId newId = InsertNewAmendmentBlock(db, tr, currentSpace, insertPoint, blockScale, exHist.Rev, exHist.Date, exHist.Description);
                                
                                if (newId != ObjectId.Null) {
                                    BlockReference newBlk = tr.GetObject(newId, OpenMode.ForRead) as BlockReference;
                                    allBlocks.Add(newBlk); 
                                    addedCount++;
                                }
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            if (addedCount > 0 || updatedCount > 0) doc.Editor.Regen();
            return addedCount + updatedCount;
        }
    }
}