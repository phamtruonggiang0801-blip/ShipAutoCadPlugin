using System;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using ShipAutoCadPlugin.Models;
using Inventor; // Thư viện API của Inventor

namespace ShipAutoCadPlugin.Services
{
    public class InventorService
    {
        private const string QA_SET_NAME = "MACGREGOR_QA_SYSTEM";
        private const string QA_ATT_NAME = "CHECKLIST_DATA";

        // Hàm Helper để kết nối với phần mềm Inventor đang mở
        private Inventor.Application GetInventorApp()
        {
            try
            {
                return (Inventor.Application)Marshal.GetActiveObject("Inventor.Application");
            }
            catch
            {
                throw new Exception("Please ensure Autodesk Inventor is open and a document is active.");
            }
        }

        // ====================================================================
        // 1. HÀM LƯU DỮ LIỆU CHECKLIST VÀO BẢN VẼ INVENTOR (SAVE)
        // ====================================================================
        public bool SaveChecklistToInventor(ChecklistDocument checklistDoc)
        {
            try
            {
                var invApp = GetInventorApp();
                Document doc = invApp.ActiveDocument;
                if (doc == null) return false;

                string jsonString = JsonConvert.SerializeObject(checklistDoc);

                // 1. Tìm hoặc Tạo AttributeSet (Thư mục ẩn)
                AttributeSet qaSet;
                if (doc.AttributeSets.NameIsUsed[QA_SET_NAME])
                {
                    qaSet = doc.AttributeSets[QA_SET_NAME];
                }
                else
                {
                    qaSet = doc.AttributeSets.Add(QA_SET_NAME);
                }

                // 2. Tìm hoặc Ghi đè Attribute (File dữ liệu ẩn)
                if (qaSet.NameIsUsed[QA_ATT_NAME])
                {
                    qaSet[QA_ATT_NAME].Value = jsonString;
                }
                else
                {
                    // Inventor.ValueTypeEnum.kStringType là định dạng chuỗi
                    qaSet.Add(QA_ATT_NAME, ValueTypeEnum.kStringType, jsonString);
                }

                // Ép Inventor lưu trạng thái "Cần save file" (Dirty)
                doc.Dirty = true; 
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error saving QA Checklist to Inventor: " + ex.Message);
                return false;
            }
        }

        // ====================================================================
        // 2. HÀM ĐỌC DỮ LIỆU CHECKLIST TỪ BẢN VẼ INVENTOR (LOAD)
        // ====================================================================
        public ChecklistDocument LoadChecklistFromInventor()
        {
            try
            {
                var invApp = GetInventorApp();
                if (invApp.ActiveDocument == null) return null;
                Document doc = invApp.ActiveDocument;

                // Kiểm tra xem File này đã có Thư mục QA chưa
                if (!doc.AttributeSets.NameIsUsed[QA_SET_NAME]) return null;
                
                AttributeSet qaSet = doc.AttributeSets[QA_SET_NAME];
                
                // Kiểm tra xem có File dữ liệu không
                if (!qaSet.NameIsUsed[QA_ATT_NAME]) return null;

                // Lấy chuỗi JSON ra
                string jsonString = (string)qaSet[QA_ATT_NAME].Value;

                // Dịch ngược JSON thành Object
                return JsonConvert.DeserializeObject<ChecklistDocument>(jsonString);
            }
            catch
            {
                return null;
            }
        }

        // ====================================================================
        // 3. HÀM XÓA CHECKLIST KHỎI BẢN VẼ INVENTOR (RESET)
        // ====================================================================
        public bool DeleteChecklistFromInventor()
        {
            try
            {
                var invApp = GetInventorApp();
                if (invApp.ActiveDocument == null) return false;
                Document doc = invApp.ActiveDocument;

                if (doc.AttributeSets.NameIsUsed[QA_SET_NAME])
                {
                    doc.AttributeSets[QA_SET_NAME].Delete();
                    doc.Dirty = true;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ====================================================================
        // 4. HÀM ĐÓNG DẤU (STAMP) CHO INVENTOR
        // ====================================================================
        public void GenerateQaStampInventor()
        {
            try
            {
                var invApp = GetInventorApp();
                
                // Kiểm tra xem file hiện tại có phải là bản vẽ (DrawingDocument) không
                if (!(invApp.ActiveDocument is DrawingDocument drawDoc)) return;

                // 1. KHỞI TẠO LAYER "KHÔNG IN"
                Layer qaLayer = null;
                string layerName = "MACGREGOR_QA_STAMP";
                
                try 
                {
                    // Thử tìm layer xem đã có chưa
                    qaLayer = drawDoc.StylesManager.Layers[layerName];
                } 
                catch 
                {
                    // Nếu chưa có, copy từ layer đầu tiên ra thành layer mới
                    Layer baseLayer = drawDoc.StylesManager.Layers[1];
                    qaLayer = (Layer)baseLayer.Copy(layerName);
                    
                    // CỰC KỲ QUAN TRỌNG: Tắt thuộc tính In Ấn (Non-plotting)
                    qaLayer.Plot = false; 
                    
                    // Set màu xanh lá (Green) cho dễ nhìn trên màn hình
                    qaLayer.Color = invApp.TransientObjects.CreateColor(0, 128, 0); 
                }

                // 2. CHUẨN BỊ TỌA ĐỘ VÀ NỘI DUNG
                Sheet activeSheet = drawDoc.ActiveSheet;
                TransientGeometry tg = invApp.TransientGeometry;
                
                // Điểm chèn là 0,0 của tờ giấy
                Point2d position = tg.CreatePoint2d(0, 0); 
                string stampText = "CheckList Passed";

                // 3. CHÈN TEXT VÀO BẢN VẼ
                GeneralNote note = activeSheet.DrawingNotes.GeneralNotes.AddFitted(position, stampText);
                note.Layer = qaLayer;

                // Lưu trạng thái bản vẽ
                drawDoc.Dirty = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error generating QA Stamp in Inventor: " + ex.Message);
            }
        }

        // ====================================================================
        // 5. HÀM SÁT THỦ: TỰ ĐỘNG DIỆT CON DẤU GIẢ TRONG INVENTOR
        // ====================================================================
        public void PurgeFakeQaStampsInventor()
        {
            try
            {
                var invApp = GetInventorApp();
                if (!(invApp.ActiveDocument is DrawingDocument drawDoc)) return;

                bool hasDeleted = false;

                // Lặp qua tất cả các tờ giấy (Sheet) trong bản vẽ
                foreach (Sheet sheet in drawDoc.Sheets)
                {
                    // Khi xóa object trong collection, phải lặp ngược từ dưới lên (Reverse Loop)
                    for (int i = sheet.DrawingNotes.GeneralNotes.Count; i >= 1; i--)
                    {
                        GeneralNote note = sheet.DrawingNotes.GeneralNotes[i];
                        
                        // Kiểm tra nếu nội dung chứa chữ CheckList Passed
                        if (note.Text.Contains("CheckList Passed") || note.FormattedText.Contains("CheckList Passed"))
                        {
                            note.Delete();
                            hasDeleted = true;
                        }
                    }
                }

                if (hasDeleted)
                {
                    drawDoc.Dirty = true;
                }
            }
            catch
            {
                // Nếu lỗi thì âm thầm bỏ qua, không làm gián đoạn trải nghiệm của Kỹ sư
            }
        }
    }
}