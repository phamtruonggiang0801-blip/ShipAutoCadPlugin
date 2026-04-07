using System;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json;
using ShipAutoCadPlugin.Models;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // Tên của Thư mục ẩn và File ẩn bên trong bản vẽ DWG
        private const string QA_DICT_NAME = "MACGREGOR_QA_SYSTEM";
        private const string QA_XRECORD_NAME = "CHECKLIST_DATA";

        // ====================================================================
        // 1. HÀM LƯU DỮ LIỆU CHECKLIST VÀO BẢN VẼ (SAVE)
        // ====================================================================
        public bool SaveChecklistToDwg(ChecklistDocument checklistDoc)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            Database db = doc.Database;

            try
            {
                // Biến toàn bộ Object thành chuỗi JSON
                string jsonString = JsonConvert.SerializeObject(checklistDoc);

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        // Truy cập vào "Bộ não" của bản vẽ (Named Object Dictionary)
                        DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                        // 1. Kiểm tra và Tạo Thư mục ẩn (Dictionary)
                        DBDictionary qaDict;
                        if (nod.Contains(QA_DICT_NAME))
                        {
                            qaDict = (DBDictionary)tr.GetObject(nod.GetAt(QA_DICT_NAME), OpenMode.ForWrite);
                        }
                        else
                        {
                            qaDict = new DBDictionary();
                            nod.SetAt(QA_DICT_NAME, qaDict);
                            tr.AddNewlyCreatedDBObject(qaDict, true);
                        }

                        // 2. Tạo File dữ liệu ẩn (XRecord)
                        Xrecord xRec = new Xrecord();
                        
                        // [THUẬT TOÁN BĂM NHỎ - CHUNKING]: Cắt chuỗi JSON thành các đoạn 250 ký tự để chống Crash CAD
                        ResultBuffer rb = new ResultBuffer();
                        int chunkSize = 250;
                        for (int i = 0; i < jsonString.Length; i += chunkSize)
                        {
                            int length = Math.Min(chunkSize, jsonString.Length - i);
                            string chunk = jsonString.Substring(i, length);
                            
                            // DxfCode.Text (mã 1) là kiểu dữ liệu chuỗi trong AutoCAD
                            rb.Add(new TypedValue((int)DxfCode.Text, chunk)); 
                        }

                        xRec.Data = rb;

                        // 3. Ghi đè file ẩn vào Thư mục
                        if (qaDict.Contains(QA_XRECORD_NAME))
                        {
                            // Mở XRecord cũ lên để ghi đè
                            Xrecord oldXrec = (Xrecord)tr.GetObject(qaDict.GetAt(QA_XRECORD_NAME), OpenMode.ForWrite);
                            oldXrec.Data = rb;
                        }
                        else
                        {
                            // Tạo mới
                            qaDict.SetAt(QA_XRECORD_NAME, xRec);
                            tr.AddNewlyCreatedDBObject(xRec, true);
                        }

                        tr.Commit();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Application.ShowAlertDialog("Error saving QA Checklist to drawing: " + ex.Message);
                return false;
            }
        }

        // ====================================================================
        // 2. HÀM ĐỌC DỮ LIỆU CHECKLIST TỪ BẢN VẼ (LOAD)
        // ====================================================================
        public ChecklistDocument LoadChecklistFromDwg()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return null;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Truy cập vào NOD
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                // Nếu bản vẽ chưa từng có Checklist, trả về null để UI biết đường tạo mới
                if (!nod.Contains(QA_DICT_NAME))
                {
                    return null;
                }

                DBDictionary qaDict = (DBDictionary)tr.GetObject(nod.GetAt(QA_DICT_NAME), OpenMode.ForRead);
                
                if (!qaDict.Contains(QA_XRECORD_NAME))
                {
                    return null;
                }

                // Mở file ẩn XRecord lên đọc
                Xrecord xRec = (Xrecord)tr.GetObject(qaDict.GetAt(QA_XRECORD_NAME), OpenMode.ForRead);
                ResultBuffer rb = xRec.Data;

                if (rb == null) return null;

                // [THUẬT TOÁN GHÉP MẢNH]: Nối các mảnh 250 ký tự lại thành chuỗi JSON ban đầu
                StringBuilder jsonBuilder = new StringBuilder();
                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode == (short)DxfCode.Text)
                    {
                        jsonBuilder.Append(tv.Value.ToString());
                    }
                }

                string jsonString = jsonBuilder.ToString();

                // Dịch ngược JSON thành Object C#
                try
                {
                    ChecklistDocument checklistDoc = JsonConvert.DeserializeObject<ChecklistDocument>(jsonString);
                    return checklistDoc;
                }
                catch
                {
                    // Lỗi giải mã (Corrupted data)
                    return null; 
                }
            }
        }
        
        // ====================================================================
        // 3. HÀM XÓA CHECKLIST (RESET)
        // ====================================================================
        public bool DeleteChecklistFromDwg()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            Database db = doc.Database;

            try
            {
                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                        if (nod.Contains(QA_DICT_NAME))
                        {
                            nod.Remove(QA_DICT_NAME);
                        }
                        tr.Commit();
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}