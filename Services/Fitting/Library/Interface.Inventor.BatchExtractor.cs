using System;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json; 
using InventorApp = Inventor.Application; 
using Inventor;
using System.Collections.Generic;
using System.Text.RegularExpressions; // Thêm thư viện Regex để xử lý chuỗi Mass

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: INVENTOR PUPPET MASTER (Đã nâng cấp tính năng lấy tọa độ View & iProperties)
        // ====================================================================

        public void BatchProcessInventorFiles(string[] filePaths)
        {
            InventorApp invApp = null;
            bool isInventorCreatedByUs = false;

            try
            {
                // 1. KẾT NỐI INVENTOR AN TOÀN
                try { invApp = (InventorApp)Marshal.GetActiveObject("Inventor.Application"); }
                catch
                {
                    Type invType = Type.GetTypeFromProgID("Inventor.Application");
                    invApp = (InventorApp)Activator.CreateInstance(invType);
                    isInventorCreatedByUs = true;
                }

                if (isInventorCreatedByUs) { invApp.Visible = false; }
                try { invApp.SilentOperation = true; } catch { }

                int successCount = 0;
                Autodesk.AutoCAD.ApplicationServices.Document cadDoc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

                foreach (string filePath in filePaths)
                {
                    try
                    {
                        Inventor.Document doc = invApp.Documents.Open(filePath, false);
                        if (!(doc is DrawingDocument drawingDoc)) { doc.Close(true); continue; }

                        Inventor.Document modelDoc = GetReferencedModel(drawingDoc);
                        if (modelDoc != null)
                        {
                            // Đọc các iProperties cơ bản
                            string partNumber = GetiProperty(modelDoc, "Design Tracking Properties", "Part Number");
                            string desc = GetiProperty(modelDoc, "Design Tracking Properties", "Description");
                            string rev = GetiProperty(modelDoc, "Inventor Summary Information", "Revision Number");
                            string material = GetiProperty(modelDoc, "Design Tracking Properties", "Material");

                            // ========================================================
                            // [CẬP NHẬT MỚI]: LẤY MASS VÀ LÀM TRÒN SỐ (Nội dung 1)
                            // ========================================================
                            string rawMass = GetiProperty(modelDoc, "Design Tracking Properties", "Mass");
                            string mass = FormatAndRoundMass(rawMass);

                            // Lấy thêm Title và Designer cho PLM
                            string title = GetiProperty(modelDoc, "Inventor Summary Information", "Title");
                            
                            // Ưu tiên lấy Designer, nếu trống thì lấy Author làm Originator
                            string designer = GetiProperty(modelDoc, "Design Tracking Properties", "Designer");
                            if (string.IsNullOrWhiteSpace(designer))
                            {
                                designer = GetiProperty(modelDoc, "Inventor Summary Information", "Author");
                            }

                            // Lấy thông tin Hình chiếu và Tỷ lệ
                            var viewsList = new List<object>();
                            int viewIndex = 1;

                            foreach (Sheet sheet in drawingDoc.Sheets)
                            {
                                double baseScaleFactor = 1.0;
                                if (sheet.DrawingViews.Count > 0)
                                {
                                    baseScaleFactor = 1.0 / sheet.DrawingViews[1].Scale; 
                                }

                                foreach (DrawingView view in sheet.DrawingViews)
                                {
                                    double cx_sheet = view.Center.X * 10.0;
                                    double cy_sheet = view.Center.Y * 10.0;
                                    double w_sheet = view.Width * 10.0;
                                    double h_sheet = view.Height * 10.0;

                                    viewsList.Add(new {
                                        Name = "View_" + viewIndex,
                                        CenterX = cx_sheet * baseScaleFactor,
                                        CenterY = cy_sheet * baseScaleFactor,
                                        Width = w_sheet * baseScaleFactor,
                                        Height = h_sheet * baseScaleFactor
                                    });
                                    viewIndex++;
                                }
                            }

                            string dir = System.IO.Path.GetDirectoryName(filePath);
                            string baseName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                            string jsonPath = System.IO.Path.Combine(dir, baseName + ".json");
                            string dwgPath = System.IO.Path.Combine(dir, baseName + ".dwg");

                            if (System.IO.File.Exists(dwgPath)) System.IO.File.Delete(dwgPath);

                            // Đóng gói toàn bộ metadata vào JSON
                            var metadata = new {
                                PartNumber = partNumber, 
                                Description = desc,
                                Revision = rev, 
                                Mass = mass, 
                                Material = material,
                                Designer = designer, 
                                Title = title,       
                                Views = viewsList 
                            };
                            System.IO.File.WriteAllText(jsonPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));

                            drawingDoc.SaveAs(dwgPath, true);
                            successCount++;
                        }
                        doc.Close(true);
                    }
                    catch (Exception ex)
                    {
                        cadDoc?.Editor?.WriteMessage($"\n[Fitting Extractor] Lỗi xử lý file {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }

                System.Windows.MessageBox.Show($"Export complete {successCount}/{filePaths.Length} files.", "Batch Fitting Extraction", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex) { System.Windows.MessageBox.Show("Lỗi: " + ex.Message, "System Error"); }
            finally
            {
                if (invApp != null)
                {
                    try { invApp.SilentOperation = false; } catch { }
                    if (isInventorCreatedByUs) invApp.Quit();
                    Marshal.ReleaseComObject(invApp); 
                }
            }
        }

        // =========================================================
        // HELPER: CÁC HÀM HỖ TRỢ TRÍCH XUẤT
        // =========================================================
        private Inventor.Document GetReferencedModel(DrawingDocument drawingDoc)
        {
            foreach (Sheet sheet in drawingDoc.Sheets)
            {
                foreach (DrawingView view in sheet.DrawingViews)
                {
                    if (view.ReferencedDocumentDescriptor != null && view.ReferencedDocumentDescriptor.ReferencedDocument != null)
                    {
                        return view.ReferencedDocumentDescriptor.ReferencedDocument as Inventor.Document;
                    }
                }
            }
            return null;
        }

        private string GetiProperty(Inventor.Document doc, string setName, string propName)
        {
            try
            {
                PropertySet propSet = doc.PropertySets[setName];
                Property prop = propSet[propName];
                return prop.Value != null ? prop.Value.ToString() : "";
            }
            catch { return ""; }
        }

        // =========================================================
        // HELPER: XỬ LÝ CHUỖI KHỐI LƯỢNG (Làm tròn số lượng)
        // =========================================================
        private string FormatAndRoundMass(string rawMass)
        {
            if (string.IsNullOrWhiteSpace(rawMass)) return "";

            try
            {
                // Dùng Regex để tách số và chữ. VD: "24.532 kg" -> Nhóm 1: "24.532", Nhóm 2: "kg"
                Match match = Regex.Match(rawMass.Trim(), @"^([\d\.,]+)\s*(.*)$");
                if (match.Success)
                {
                    string numberPart = match.Groups[1].Value.Replace(",", "."); // Đảm bảo dấu thập phân là dấu chấm
                    string unitPart = match.Groups[2].Value.Trim();

                    if (double.TryParse(numberPart, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedValue))
                    {
                        // Làm tròn không lấy số thập phân
                        double roundedValue = Math.Round(parsedValue, 0);
                        
                        // Ghép lại với đơn vị gốc nếu có
                        return string.IsNullOrEmpty(unitPart) ? $"{roundedValue}" : $"{roundedValue} {unitPart}";
                    }
                }
                return rawMass; // Nếu không parse được thì trả về chuỗi gốc
            }
            catch
            {
                return rawMass; 
            }
        }
    }
}