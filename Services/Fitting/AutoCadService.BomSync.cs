using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ShipAutoCadPlugin.Models;
using Autodesk.AutoCAD.DatabaseServices;

namespace ShipAutoCadPlugin.Services
{
    public partial class AutoCadService
    {
        // ====================================================================
        // MODULE: BOM SYNCHRONIZATION (Đồng bộ dữ liệu RAM và File JSON)
        // ====================================================================

        /// <summary>
        /// [STRUCTURE MODE] Phương thức chính để đồng bộ Fitting trực tiếp vào Panel.
        /// Sử dụng trực tiếp kho lưu trữ 'static' ExtractedPanelNodes.
        /// </summary>
        public void SyncFittingsToProjectPanels(List<BomHarvestRecord> scanResults)
        {
            var projectPanels = ExtractedPanelNodes;

            if (projectPanels == null || !projectPanels.Any() || scanResults == null || !scanResults.Any())
            {
                return;
            }

            var groupedFittings = scanResults.GroupBy(r => r.PanelName);

            foreach (var group in groupedFittings)
            {
                var targetPanel = projectPanels.FirstOrDefault(p => 
                    string.Equals(p.Name, group.Key, StringComparison.OrdinalIgnoreCase));

                if (targetPanel != null)
                {
                    targetPanel.AssociatedFittings = group.ToList();
                }
            }
        }

        /// <summary>
        /// [INTERFACE MODE] Đồng bộ Fitting vào các Detail, sau đó nhân số lượng 
        /// và tổng hợp ngược lại cho Panel cha để hiển thị lên Dashboard.
        /// </summary>
        public void SyncFittingsToDetails(List<BomHarvestRecord> scanResults)
        {
            var projectPanels = ExtractedPanelNodes;

            if (projectPanels == null || !projectPanels.Any() || scanResults == null || !scanResults.Any())
            {
                return;
            }

            // 1. Tạo Map dữ liệu Fitting theo tên Detail (VD: "Detail 1" -> Danh sách Fitting của nó)
            var detailFittingsMap = scanResults.GroupBy(r => r.PanelName)
                                               .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // 2. Duyệt qua từng Panel trong kho RAM
            foreach (var panel in projectPanels)
            {
                var aggregatedFittings = new List<BomHarvestRecord>();

                if (panel.Children != null && panel.Children.Count > 0)
                {
                    // 3. Duyệt qua các Detail con của Panel này
                    foreach (DetailNode detail in panel.Children)
                    {
                        if (detailFittingsMap.TryGetValue(detail.Name, out var fittingsForDetail))
                        {
                            // 4. Nhân bản danh sách Fitting và tính toán lại Quantity
                            foreach (var fit in fittingsForDetail)
                            {
                                aggregatedFittings.Add(new BomHarvestRecord
                                {
                                    PanelName = panel.Name, 
                                    VaultName = fit.VaultName,
                                    PartId = fit.PartId,
                                    Description = fit.Description,
                                    XClass = fit.XClass,
                                    Position = fit.Position, 
                                    
                                    // [BẢO TOÀN DỮ LIỆU MỚI]: Giữ lại toàn bộ thông tin chuẩn bị cho Export & Balloon
                                    ProjectPosNum = fit.ProjectPosNum, 
                                    UoM = fit.UoM,                     
                                    ParentBlockName = fit.ParentBlockName, 
                                    InstanceIds = fit.InstanceIds,     
                                    
                                    Quantity = fit.Quantity * detail.Qty 
                                });
                            }
                        }
                    }
                }

                // 5. Gom nhóm (Merge) các Fitting trùng nhau
                // [LƯU Ý QUAN TRỌNG]: Nhóm theo ParentBlockName và UoM để Accessory không bị gộp sai
                var mergedFittings = aggregatedFittings
                    .GroupBy(f => new { f.VaultName, f.ParentBlockName, f.UoM })
                    .Select(g => new BomHarvestRecord
                    {
                        PanelName = panel.Name,
                        VaultName = g.Key.VaultName,
                        PartId = g.First().PartId,
                        Description = g.First().Description,
                        XClass = g.First().XClass,
                        Position = g.First().Position,
                        
                        // Kế thừa các thuộc tính của nhóm
                        ProjectPosNum = g.First().ProjectPosNum,
                        UoM = g.Key.UoM,
                        ParentBlockName = g.Key.ParentBlockName,
                        Quantity = g.Sum(f => f.Quantity), 
                        
                        // Gộp tất cả ObjectId lại thành một túi to để Ballooning dễ tìm
                        InstanceIds = g.SelectMany(f => f.InstanceIds ?? new List<ObjectId>()).Distinct().ToList() 
                    }).OrderBy(f => f.Position).ToList();

                // 6. Gán danh sách đã tổng hợp vào Panel để UI hiển thị
                panel.AssociatedFittings = mergedFittings;
            }
        }

        /// <summary>
        /// Phương thức hỗ trợ đồng bộ cho một danh sách Panel cụ thể (Dùng cho xử lý File)
        /// </summary>
        public void SyncFittingsToSpecificList(List<PanelNode> targetList, List<BomHarvestRecord> scanResults)
        {
            if (targetList == null || scanResults == null) return;

            var groupedFittings = scanResults.GroupBy(r => r.PanelName);
            foreach (var group in groupedFittings)
            {
                var targetPanel = targetList.FirstOrDefault(p => 
                    string.Equals(p.Name, group.Key, StringComparison.OrdinalIgnoreCase));

                if (targetPanel != null)
                {
                    targetPanel.AssociatedFittings = group.ToList();
                }
            }
        }

        /// <summary>
        /// Đồng bộ trực tiếp vào File JSON dự án.
        /// Giúp lưu trữ dữ liệu bền vững ngay cả khi đóng AutoCAD.
        /// </summary>
        public bool SyncFittingsToProjectFile(string projectJsonPath, List<BomHarvestRecord> scanResults)
        {
            if (!File.Exists(projectJsonPath)) return false;

            try
            {
                string json = File.ReadAllText(projectJsonPath);
                var projectPanels = JsonConvert.DeserializeObject<List<PanelNode>>(json) ?? new List<PanelNode>();

                SyncFittingsToSpecificList(projectPanels, scanResults);

                string newJson = JsonConvert.SerializeObject(projectPanels, Formatting.Indented);
                File.WriteAllText(projectJsonPath, newJson);

                return true;
            }
            catch (Exception ex)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog("Error during JSON file sync: " + ex.Message);
                return false;
            }
        }
    }
}