using System;
using System.Runtime.InteropServices;
using Inventor; 
using ShipAutoCadPlugin.UI; 
using System.Windows; 

namespace ShipAutoCadPlugin
{
    [Guid("D8B6C7A2-1234-4B56-8A90-123456789ABC")]
    [ComVisible(true)] 
    public class StandardAddInServer : ApplicationAddInServer
    {
        private Inventor.Application _invApp;
        private ButtonDefinition _btnOpenPalette;

        public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
        {
            // BỌC TOÀN BỘ HÀM BẰNG TRY-CATCH ĐỂ BẮT LỖI TẬN GỐC
            try
            {
                _invApp = addInSiteObject.Application;

                ControlDefinitions controlDefs = _invApp.CommandManager.ControlDefinitions;
                
                // Khởi tạo Nút bấm
                _btnOpenPalette = controlDefs.AddButtonDefinition(
                    "MacGregor\nTools", 
                    "MacGregor_OpenPalette_Cmd", 
                    CommandTypesEnum.kShapeEditCmdType, 
                    "{D8B6C7A2-1234-4B56-8A90-123456789ABC}", 
                    "Open MacGregor CAD Tools", 
                    "Opens the main dashboard for Fitting and QA/QC");

                _btnOpenPalette.OnExecute += BtnOpenPalette_OnExecute;

                Ribbon drawingRibbon = _invApp.UserInterfaceManager.Ribbons["Drawing"];
                RibbonTab macGregorTab = null;
                
                foreach (RibbonTab tab in drawingRibbon.RibbonTabs)
                {
                    if (tab.InternalName == "id_TabMacGregor") { macGregorTab = tab; break; }
                }
                if (macGregorTab == null)
                {
                    macGregorTab = drawingRibbon.RibbonTabs.Add("MacGregor", "id_TabMacGregor", "{D8B6C7A2-1234-4B56-8A90-123456789ABC}");
                }

                RibbonPanel automationPanel = null;
                foreach (RibbonPanel panel in macGregorTab.RibbonPanels)
                {
                    if (panel.InternalName == "id_PanelAutomation") { automationPanel = panel; break; }
                }
                if (automationPanel == null)
                {
                    automationPanel = macGregorTab.RibbonPanels.Add("Automation", "id_PanelAutomation", "{D8B6C7A2-1234-4B56-8A90-123456789ABC}");
                }

                try 
                { 
                    automationPanel.CommandControls.AddButton(_btnOpenPalette, true); 
                } 
                catch { }
            }
            catch (Exception ex)
            {
                // NẾU CÓ LỖI CHẾT NGẦM, NÓ SẼ BẬT HỘP THOẠI NÀY LÊN
                MessageBox.Show($"CRITICAL ERROR LOADING ADD-IN:\n\n{ex.Message}\n\n{ex.StackTrace}", "Inventor Add-In Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenPalette_OnExecute(NameValueMap Context)
        {
            try
            {
                Window hostWindow = new Window
                {
                    Title = "MacGregor CAD Tools (Inventor Mode)",
                    Content = new MainPalette(), 
                    Width = 450,
                    Height = 800,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true
                };

                hostWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening UI: {ex.Message}", "UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Deactivate()
        {
            try
            {
                if (_btnOpenPalette != null)
                {
                    _btnOpenPalette.OnExecute -= BtnOpenPalette_OnExecute;
                    _btnOpenPalette.Delete();
                    _btnOpenPalette = null;
                }
                if (_invApp != null)
                {
                    Marshal.ReleaseComObject(_invApp);
                    _invApp = null;
                }
                GC.Collect();
            }
            catch { }
        }

        public void ExecuteCommand(int commandID) { }
        
        public object Automation => null;
    }
}