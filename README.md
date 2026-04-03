# 🛠️ MACGREGOR FITTING TOOLS - HƯỚNG DẪN SỬ DỤNG

Chào mừng bạn đến với tài liệu hướng dẫn sử dụng **Fitting Tools**. Đây là bộ công cụ toàn diện được thiết kế để tự động hóa quy trình bóc tách bản vẽ Inventor, quản lý thư viện Fitting tập trung, xuất bảng thống kê vật tư (BOM) và đánh Balloon thông minh trên AutoCAD.

> **⚠️ Quy tắc "Single Source of Truth":** > Đối với thông số hình học và số lượng, **Bản vẽ CAD là Chân lý**. Không chỉnh sửa số lượng hay số Pos thủ công trên Excel. Nếu có thay đổi, hãy thao tác trên CAD (Scan lại, Sync Pos) và xuất lại bảng BOM.

Giao diện bộ công cụ được chia thành 4 bước chính ứng với luồng công việc tiêu chuẩn:

---

## 1. FITTING EXTRACTION (STEP 1)
Bước đầu tiên để đưa dữ liệu hình học từ mô hình 3D (Inventor) vào hệ thống CAD 2D.
* **Nút [Import .idw files]:** Cho phép chọn hàng loạt file bản vẽ Inventor (`.idw` hoặc `.dwg`). Tool sẽ tự động trích xuất hình học và các thuộc tính (Metadata) ra các file JSON và DWG trung gian.

## 2. FITTING FACTORY (STEP 2)
Xử lý dữ liệu thô vừa trích xuất và biến chúng thành các Block thông minh của AutoCAD.
* **Set Target BOM Type:** Chọn loại phân bổ BOM mục tiêu trước khi nạp. Bạn có thể chọn **Panel (Structure)** hoặc **Detail (Hull Matrix)**.
* **Nút [Import .json files]:** Đọc các file JSON, tự động map layer (Nét thấy, Nét đứt, Tâm), căn chỉnh gốc tọa độ, và đặc biệt là cấy các thẻ Attribute tàng hình (như `BOM_TYPE`, `PART_NUMBER`, `MASS`...) vào thẳng Block.

## 3. FITTING LIBRARY (STEP 3)
Khu vực tra cứu và quản lý thư viện tập trung.
* **Master Library (Kho Tổng):** Chứa toàn bộ Fitting chuẩn của công ty. Ở chế độ này, bạn không thể chỉnh sửa số Position (Project Pos).
* **Project Library (Kho Dự án):** Chứa Fitting dùng riêng cho một dự án cụ thể. Cho phép dùng tính năng **Auto-Assign Pos** để rải số thứ tự tự động (001, 002...) gom nhóm theo `Part ID`.
* **Nút [Insert to CAD]:** Chèn Fitting từ thư viện vào bản vẽ hiện tại. Hệ thống sẽ tự động cấy thêm thẻ Attribute tàng hình `POS_NUM` vào Block nếu nó chưa có.
* **Nút [Push Update]:** Cho phép bạn sửa hình học của một Fitting trên bản vẽ hiện tại, sau đó đẩy ngược bản cập nhật đó vào lại Thư viện để ghi đè hình dáng cũ.

## 4. BOM EXPORT & BALLOONING (STEP 4)
Khu vực xuất khối lượng và tự động hóa ghi chú. Mở cửa sổ **BOM EXPORT** để thao tác:
* **Scan & Count từ CAD:** Quét bản vẽ để đếm số lượng Fitting. Thuật toán X-Ray sẽ đệ quy vào tận cùng các lớp Block con để tìm Fitting, đồng thời áp dụng các logic tự động đo chiều dài dây cáp (Wire Rope).
* **Nút [Auto-Assign Positions]:** (Dành cho Panel BOM) Tự động rải số Pos theo từng vùng Panel.
* **Nút [Sync Pos to CAD]:** Bơm ngược các con số Position từ bảng BOM vào các thẻ Attribute `POS_NUM` đang tàng hình trên bản vẽ.
* **Nút [Place Smart Balloon]:** Công cụ đánh Balloon thủ công thông minh. Gõ lệnh 1 lần, tia X-Ray sẽ xuyên qua các Block cha để đọc mã `POS_NUM` bên trong Fitting. Hỗ trợ click liên tục, chống lỗi đâm xuyên viền và sử dụng Block `_TagCircle` chuẩn.
* **Nút [Mass Auto-Balloon]:** "Trùm cuối" đánh Balloon hàng loạt. Kỹ sư chỉ cần quét chọn vùng bản vẽ, hệ thống sẽ tự động lọc Fitting, loại bỏ trùng lặp, tính toán khoảng cách "từ tính" an toàn và xếp các Balloon thẳng tắp ở ngoài mép bản vẽ mà không bị đè lên nhau.

## 5. BLOCK UTILITIES
Các công cụ phụ trợ giúp Kỹ sư sửa đổi Block cực nhanh mà không cần Explode (phá khối), tránh nguy cơ làm mất dữ liệu Attribute:
* **Sync/Redefine Blocks:** Đồng bộ lại hình dáng Block từ Thư viện.
* **Smart Replace (Spatial):** Thay thế Block cũ bằng Block mới ngay tại tọa độ hiện tại.
* **Change Insertion Point:** Đổi gốc tọa độ Block.
* **Add / Extract Objects from Block:** Thêm hoặc bóc tách nét vẽ ra khỏi Block.