using System.Windows;
using System.Windows.Controls;

namespace ShipAutoCadPlugin.UI
{
    public partial class MainPalette : UserControl
    {
        public MainPalette()
        {
            InitializeComponent();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            string helpMsg = "SHIP STRUCTURE TOOLS - QUICK GUIDE\n\n" +
                             "• FITTING TOOLS:\n" +
                             "  - Import .idw: Extract geometry from Inventor to DWG/JSON.\n" +
                             "  - Import .json: Harvest JSON into independent Blocks.\n" +
                             "  - Change Base Point: Move block grip without moving the graphics.\n\n" +
                             "• PANEL AUTOMATION...\n";
            MessageBox.Show(helpMsg, "Help & Documentation", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}