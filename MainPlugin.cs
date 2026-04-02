using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using ShipAutoCadPlugin.UI;

namespace ShipAutoCadPlugin
{
    public class MainPlugin : IExtensionApplication
    {
        static PaletteSet _paletteSet;
        static MainPalette _mainPalette; // Đã đổi tên ở đây

        // Hàm chạy khi Plugin được load vào AutoCAD
        public void Initialize()
        {
            // Khởi tạo các tài nguyên nếu cần thiết
        }

        // Hàm chạy khi Plugin bị unload
        public void Terminate()
        {
        }

        // Khai báo lệnh trong AutoCAD: gõ SHIPPROP để hiển thị
        [CommandMethod("SHIPPROP")]
        public void ShowShipPropertiesPalette()
        {
            if (_paletteSet == null)
            {
                // Tạo một PaletteSet mới
                _paletteSet = new PaletteSet("Ship Structure Properties", new System.Guid("A1B2C3D4-E5F6-7777-8888-9999AAAABBBB"));
                _paletteSet.Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton;
                _paletteSet.MinimumSize = new System.Drawing.Size(250, 300);

                // Khởi tạo UI (WPF UserControl) - Gọi đúng tên class mới
                _mainPalette = new MainPalette();

                // Đưa UI vào trong PaletteSet qua ElementHost
                System.Windows.Forms.Integration.ElementHost host = new System.Windows.Forms.Integration.ElementHost();
                host.AutoSize = true;
                host.Dock = System.Windows.Forms.DockStyle.Fill;
                host.Child = _mainPalette; // Truyền biến mới vào đây

                _paletteSet.Add("Properties", host);
            }

            // Hiển thị Palette
            _paletteSet.KeepFocus = false;
            _paletteSet.Visible = true;
        }
    }
}