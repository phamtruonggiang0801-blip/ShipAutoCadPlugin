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
            string helpMsg = "MACGREGOR CAD TOOLS - QUICK GUIDE\n\n" +
                             "• INTERFACE:\n" +
                             "  - Sync panels and details with Vault Excel.\n" +
                             "  - Generate detail labels and track sheet revisions.\n\n" +
                             "• FITTING TOOLS:\n" +
                             "  - Extract and generate Block Fittings from Inventor.\n" +
                             "  - Scan drawings, Auto-Assign Pos, and export BOM matrix.\n\n" +
                             "• DRAWING TOOLS:\n" +
                             "  - Interactive drawing checklist before Vault Release.\n" +
                             "  - Check items are saved invisibly into the DWG file.\n" +
                             "  - Sign & Approve to lock data and stamp the drawing.";
                             
            MessageBox.Show(helpMsg, "Help & Documentation", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}