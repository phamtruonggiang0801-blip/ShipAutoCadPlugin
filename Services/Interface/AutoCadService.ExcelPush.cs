using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using ShipAutoCadPlugin.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // =========================================================
        // MODULE: EXCEL PUSH (Đẩy dữ liệu từ CAD/RAM lên Excel)
        // =========================================================

        /// <summary>
        /// HÀM 1: ĐẨY LÊN EXCEL (SAFE PUSH - HỖ TRỢ GLOBAL BUMP CHO TAB 1 & 2)
        /// </summary>
        public void ExportToVaultExcel(List<SheetRowData> data, string excelPath)
        {
            try
            {
                dynamic excelApp;
                try { excelApp = System.Runtime.InteropServices.Marshal.GetActiveObject("Excel.Application"); }
                catch { Type excelType = Type.GetTypeFromProgID("Excel.Application"); excelApp = Activator.CreateInstance(excelType); }
                
                excelApp.Visible = true; 
                dynamic workbooks = excelApp.Workbooks;
                dynamic workbook = null;
                string fileName = System.IO.Path.GetFileName(excelPath);

                foreach (dynamic wb in workbooks) {
                    if (wb.Name == fileName || wb.FullName == excelPath) { workbook = wb; break; }
                }

                if (workbook == null) {
                    if (System.IO.File.Exists(excelPath)) workbook = workbooks.Open(excelPath);
                    else workbook = workbooks.Add();
                }

                // --- XỬ LÝ TAB 1: DRAWING DATABASE ---
                dynamic sheet1 = workbook.Sheets[1];
                sheet1.Name = "Drawing Database";
                sheet1.Cells[1, 1] = "Sheet No"; sheet1.Cells[1, 2] = "Sheet Content"; sheet1.Cells[1, 3] = "Rev";
                sheet1.Cells[1, 4] = "Date"; sheet1.Cells[1, 5] = "Amendment Desc";
                sheet1.Range["A1", "E1"].Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.DarkBlue);
                sheet1.Range["A1", "E1"].Font.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.White);
                sheet1.Range["A1", "E1"].Font.Bold = true;

                int maxRow1 = sheet1.UsedRange.Rows.Count;
                foreach (var item in data)
                {
                    int writeRowIndex = -1;
                    for (int r = 2; r <= maxRow1; r++) {
                        if (Convert.ToString(sheet1.Cells[r, 1].Value) == item.SheetNo) { writeRowIndex = r; break; }
                    }
                    if (writeRowIndex == -1) { maxRow1++; writeRowIndex = maxRow1; }

                    sheet1.Cells[writeRowIndex, 1] = item.SheetNo ?? "";
                    sheet1.Cells[writeRowIndex, 2] = item.Content ?? "";
                    sheet1.Cells[writeRowIndex, 3] = item.Rev ?? "";
                    sheet1.Cells[writeRowIndex, 4] = item.Date ?? "";
                    sheet1.Cells[writeRowIndex, 5] = item.AmendmentDescription ?? "";
                }
                sheet1.Columns.AutoFit();

                // --- XỬ LÝ TAB 2: REVISION HISTORY (SAFE MERGE & GLOBAL BUMP) ---
                dynamic sheet2;
                try { sheet2 = workbook.Sheets[2]; }
                catch { sheet2 = workbook.Sheets.Add(After: workbook.Sheets[1]); }
                sheet2.Name = "Revision History";

                sheet2.Cells[1, 1] = "Sheet No"; sheet2.Cells[1, 2] = "Rev"; sheet2.Cells[1, 3] = "Date"; sheet2.Cells[1, 4] = "Amendment Desc";
                sheet2.Range["A1", "D1"].Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.DarkRed);
                sheet2.Range["A1", "D1"].Font.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.White);
                sheet2.Range["A1", "D1"].Font.Bold = true;

                // 1. Đọc TOÀN BỘ Lịch sử hiện có trên Excel
                List<ExcelRevHistory> allHistory = new List<ExcelRevHistory>();
                int maxRow2 = sheet2.UsedRange.Rows.Count;
                for (int r = 2; r <= maxRow2; r++)
                {
                    string sNo = Convert.ToString(sheet2.Cells[r, 1].Value);
                    if (!string.IsNullOrEmpty(sNo))
                    {
                        dynamic rawDate = sheet2.Cells[r, 3].Value;
                        string dateStr = "";
                        if (rawDate != null)
                        {
                            if (rawDate is DateTime dt) dateStr = dt.ToString("yyyy/MM/dd");
                            else if (DateTime.TryParse(rawDate.ToString(), out DateTime parsedDt)) dateStr = parsedDt.ToString("yyyy/MM/dd");
                            else dateStr = rawDate.ToString();
                        }
                        allHistory.Add(new ExcelRevHistory { 
                            SheetNo = sNo, 
                            Rev = Convert.ToString(sheet2.Cells[r, 2].Value), 
                            Date = dateStr, 
                            Description = Convert.ToString(sheet2.Cells[r, 4].Value) 
                        });
                    }
                }

                // 2. Cập nhật dữ liệu từ CAD và DataGrid vào danh sách
                foreach (var item in data)
                {
                    if (item.A1BlockId != Autodesk.AutoCAD.DatabaseServices.ObjectId.Null)
                    {
                        // 2.1: Quét các Block Amendment đang có thực tế trên CAD
                        var cadHistory = GetRevisionHistory(item.A1BlockId);
                        cadHistory.Reverse(); 
                        
                        foreach (var ch in cadHistory) {
                            var existingRev = allHistory.FirstOrDefault(h => h.SheetNo == item.SheetNo && h.Rev.ToUpper() == ch.Rev.ToUpper());
                            if (existingRev != null)
                            {
                                existingRev.Date = ch.Date;
                                existingRev.Description = ch.Description;
                            }
                            else
                            {
                                allHistory.Add(new ExcelRevHistory { SheetNo = item.SheetNo, Rev = ch.Rev, Date = ch.Date, Description = ch.Description });
                            }
                        }

                        // 2.2: Ép dữ liệu hiện tại trên DataGrid vào (Cực kỳ quan trọng cho Global Bump có Description trống)
                        if (!string.IsNullOrEmpty(item.Rev))
                        {
                            var currentGridRev = allHistory.FirstOrDefault(h => h.SheetNo == item.SheetNo && h.Rev.ToUpper() == item.Rev.ToUpper());
                            if (currentGridRev != null)
                            {
                                currentGridRev.Date = item.Date;
                                currentGridRev.Description = item.AmendmentDescription;
                            }
                            else
                            {
                                // Nếu là Global Bump, CAD chưa có block này, ta vẫn ép thêm vào Excel
                                allHistory.Add(new ExcelRevHistory { SheetNo = item.SheetNo, Rev = item.Rev, Date = item.Date, Description = item.AmendmentDescription });
                            }
                        }
                    }
                }

                // 3. Xóa vùng data cũ và ghi list đã Merge
                if (maxRow2 > 1) sheet2.Range["A2:D" + maxRow2].ClearContents();
                
                int writeRow = 2;
                foreach (var h in allHistory)
                {
                    sheet2.Cells[writeRow, 1] = h.SheetNo ?? "";
                    sheet2.Cells[writeRow, 2] = h.Rev ?? "";
                    sheet2.Cells[writeRow, 3] = h.Date ?? "";
                    sheet2.Cells[writeRow, 4] = h.Description ?? "";
                    writeRow++;
                }
                sheet2.Columns.AutoFit();

                // === LƯU FILE ===
                bool isSaved = false;
                try
                {
                    if (!workbook.ReadOnly)
                    {
                        if (System.IO.File.Exists(excelPath)) workbook.Save();
                        else workbook.SaveAs(excelPath);
                        isSaved = true;
                    }
                }
                catch { }

                if (isSaved) Application.ShowAlertDialog("Safe sync complete: Current data SAVED to Excel!\nHistory from other engineers remains unaffected.");
                else Application.ShowAlertDialog("Data pushed to Excel!\n\nNOTE: This file is Read-Only. Please Check-out in Vault and manually Save (Ctrl + S).");

                System.Runtime.InteropServices.Marshal.ReleaseComObject(sheet2);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(sheet1);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(workbooks);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp);
            }
            catch (Exception ex)
            {
                Application.ShowAlertDialog("Excel connection error: " + ex.Message);
            }
        }

        /// <summary>
        /// HÀM 2: ĐẨY DỮ LIỆU PANEL TỪ RAM LÊN EXCEL (TAB 3)
        /// [Lưu ý: Đã sửa this.ExtractedPanelNodes thành AutoCadService.ExtractedPanelNodes]
        /// </summary>
        public void ExportPanelDataToExcel(string excelPath)
        {
            if (AutoCadService.ExtractedPanelNodes == null || AutoCadService.ExtractedPanelNodes.Count == 0)
            {
                Application.ShowAlertDialog("No Panel Data found in memory! Please run 'Scan & Audit' or 'Scan & Name' first.");
                return;
            }

            try
            {
                dynamic excelApp;
                try { excelApp = System.Runtime.InteropServices.Marshal.GetActiveObject("Excel.Application"); }
                catch { Type excelType = Type.GetTypeFromProgID("Excel.Application"); excelApp = Activator.CreateInstance(excelType); }
                
                excelApp.Visible = true; 
                dynamic workbooks = excelApp.Workbooks;
                dynamic workbook = null;
                string fileName = System.IO.Path.GetFileName(excelPath);

                foreach (dynamic wb in workbooks) {
                    if (wb.Name == fileName || wb.FullName == excelPath) { workbook = wb; break; }
                }

                if (workbook == null) {
                    if (System.IO.File.Exists(excelPath)) workbook = workbooks.Open(excelPath);
                    else workbook = workbooks.Add();
                }

                // --- XỬ LÝ TAB 3: PANEL ENGINEERING ---
                dynamic sheet3;
                try { sheet3 = workbook.Sheets[3]; }
                catch { 
                    int count = workbook.Sheets.Count;
                    sheet3 = workbook.Sheets.Add(After: workbook.Sheets[count]); 
                }
                sheet3.Name = "Panel Engineering";

                // Xóa data cũ
                sheet3.Cells.Clear();

                // Tạo Header
                sheet3.Cells[1, 1] = "Panel Name";
                sheet3.Cells[1, 2] = "Area (m2)";
                sheet3.Cells[1, 3] = "Detail Name";
                sheet3.Cells[1, 4] = "Quantity (Qty)";
                
                // Tô màu xanh lá (Dark Green) để phân biệt với Tab 1 và Tab 2
                sheet3.Range["A1", "D1"].Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.DarkGreen);
                sheet3.Range["A1", "D1"].Font.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.White);
                sheet3.Range["A1", "D1"].Font.Bold = true;

                int writeRow = 2;
                foreach (var panel in AutoCadService.ExtractedPanelNodes)
                {
                    if (panel.Children != null && panel.Children.Count > 0)
                    {
                        foreach (DetailNode child in panel.Children)
                        {
                            sheet3.Cells[writeRow, 1] = panel.Name ?? "";
                            sheet3.Cells[writeRow, 2] = panel.Area;
                            sheet3.Cells[writeRow, 3] = child.Name ?? "";
                            sheet3.Cells[writeRow, 4] = child.Qty;
                            writeRow++;
                        }
                    }
                    else
                    {
                        // Panel trống, không có Detail nào
                        sheet3.Cells[writeRow, 1] = panel.Name ?? "";
                        sheet3.Cells[writeRow, 2] = panel.Area;
                        sheet3.Cells[writeRow, 3] = "-";
                        sheet3.Cells[writeRow, 4] = 0;
                        writeRow++;
                    }
                }

                sheet3.Columns.AutoFit();

                // === LƯU FILE ===
                bool isSaved = false;
                try
                {
                    if (!workbook.ReadOnly)
                    {
                        if (System.IO.File.Exists(excelPath)) workbook.Save();
                        else workbook.SaveAs(excelPath);
                        isSaved = true;
                    }
                }
                catch { }

                if (isSaved) Application.ShowAlertDialog("Panel Data successfully pushed to Tab 3 (Panel Engineering)!");
                else Application.ShowAlertDialog("Panel Data pushed to Tab 3!\n\nNOTE: This file is Read-Only. Please manually Save (Ctrl + S) in Excel.");

                System.Runtime.InteropServices.Marshal.ReleaseComObject(sheet3);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(workbooks);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp);
            }
            catch (Exception ex)
            {
                Application.ShowAlertDialog("Excel connection error: " + ex.Message);
            }
        }
    }
}