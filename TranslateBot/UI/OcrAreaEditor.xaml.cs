using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TranslateBot.OCR;

namespace TranslateBot.UI
{
    // Dialog quản lý OCR areas + exclusion areas (Section 16 + 17).
    // Nhận reference OcrAreaManager + ExclusionManager từ MainWindow, thao tác trực
    // tiếp trên đó (không clone) → thay đổi có hiệu lực ngay khi dialog đóng.
    public partial class OcrAreaEditor : Window
    {
        private readonly OcrAreaManager _areaManager;
        private readonly ExclusionManager _exclusionManager;

        // Callback để mở RegionSelector từ MainWindow (vì RegionSelector cần
        // biết WindowHandle + chế độ capture hiện tại, thông tin nằm ở MainWindow).
        public Func<Action<int, int, int, int>, bool>? RequestRegionSelection { get; set; }

        // Gọi khi có thay đổi cần persist
        public Action? OnAreasChanged { get; set; }

        // ── Display items cho ListView binding ─────────────────────────
        private class AreaDisplayItem
        {
            public string Id { get; init; } = "";
            public string Name { get; init; } = "";
            public string TypeDisplay { get; init; } = "";
            public string SizeDisplay { get; init; } = "";
            public string EnabledDisplay { get; init; } = "";
        }

        private class ExclusionDisplayItem
        {
            public string Id { get; init; } = "";
            public string Name { get; init; } = "";
            public string SizeDisplay { get; init; } = "";
            public string EnabledDisplay { get; init; } = "";
        }

        public OcrAreaEditor(OcrAreaManager areaManager, ExclusionManager exclusionManager)
        {
            _areaManager = areaManager;
            _exclusionManager = exclusionManager;
            InitializeComponent();
            RefreshAreaList();
            RefreshExclusionList();
        }

        // ═══════════════════════════════════════════════════════════════════
        // OCR Areas Tab
        // ═══════════════════════════════════════════════════════════════════

        private void RefreshAreaList()
        {
            AreaListView.ItemsSource = _areaManager.Areas.Select(a => new AreaDisplayItem
            {
                Id = a.Id,
                Name = a.Name,
                TypeDisplay = a.Type.ToString(),
                SizeDisplay = $"{a.RegionWidth}×{a.RegionHeight}",
                EnabledDisplay = a.Enabled ? "Bật" : "Tắt"
            }).ToList();
        }

        private void AddAreaBtn_Click(object sender, RoutedEventArgs e)
        {
            // Yêu cầu MainWindow mở RegionSelector, trả kết quả qua callback
            bool opened = RequestRegionSelection?.Invoke((x, y, w, h) =>
            {
                int count = _areaManager.Areas.Count + 1;
                var area = new OcrArea
                {
                    Name = $"Dialogue {count}",
                    Type = OcrAreaType.Dialogue,
                    RegionX = x,
                    RegionY = y,
                    RegionWidth = w,
                    RegionHeight = h
                };
                _areaManager.Add(area);
                OnAreasChanged?.Invoke();
                Dispatcher.Invoke(RefreshAreaList);
            }) ?? false;

            if (!opened)
            {
                MessageBox.Show("Không thể mở RegionSelector.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RemoveAreaBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AreaListView.SelectedItem is AreaDisplayItem selected)
            {
                _areaManager.Remove(selected.Id);
                OnAreasChanged?.Invoke();
                RefreshAreaList();
            }
        }

        private void ToggleAreaBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AreaListView.SelectedItem is AreaDisplayItem selected)
            {
                var area = _areaManager.Areas.FirstOrDefault(a => a.Id == selected.Id);
                if (area != null)
                {
                    _areaManager.SetEnabled(area.Id, !area.Enabled);
                    OnAreasChanged?.Invoke();
                    RefreshAreaList();
                }
            }
        }

        private void ReselectAreaBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AreaListView.SelectedItem is not AreaDisplayItem selected) return;

            var area = _areaManager.Areas.FirstOrDefault(a => a.Id == selected.Id);
            if (area == null) return;

            RequestRegionSelection?.Invoke((x, y, w, h) =>
            {
                area.RegionX = x;
                area.RegionY = y;
                area.RegionWidth = w;
                area.RegionHeight = h;
                OnAreasChanged?.Invoke();
                Dispatcher.Invoke(RefreshAreaList);
            });
        }

        // ═══════════════════════════════════════════════════════════════════
        // Exclusion Areas Tab
        // ═══════════════════════════════════════════════════════════════════

        private void RefreshExclusionList()
        {
            ExclusionListView.ItemsSource = _exclusionManager.Exclusions.Select(ex => new ExclusionDisplayItem
            {
                Id = ex.Id,
                Name = ex.Name,
                SizeDisplay = $"{ex.RegionWidth}×{ex.RegionHeight}",
                EnabledDisplay = ex.Enabled ? "Bật" : "Tắt"
            }).ToList();
        }

        private void AddExclusionBtn_Click(object sender, RoutedEventArgs e)
        {
            RequestRegionSelection?.Invoke((x, y, w, h) =>
            {
                int count = _exclusionManager.Exclusions.Count + 1;
                var exclusion = new ExclusionArea
                {
                    Name = $"Exclusion {count}",
                    RegionX = x,
                    RegionY = y,
                    RegionWidth = w,
                    RegionHeight = h
                };
                _exclusionManager.Add(exclusion);
                OnAreasChanged?.Invoke();
                Dispatcher.Invoke(RefreshExclusionList);
            });
        }

        private void RemoveExclusionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ExclusionListView.SelectedItem is ExclusionDisplayItem selected)
            {
                _exclusionManager.Remove(selected.Id);
                OnAreasChanged?.Invoke();
                RefreshExclusionList();
            }
        }

        private void ToggleExclusionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ExclusionListView.SelectedItem is ExclusionDisplayItem selected)
            {
                var ex = _exclusionManager.Exclusions.FirstOrDefault(e => e.Id == selected.Id);
                if (ex != null)
                {
                    _exclusionManager.SetEnabled(ex.Id, !ex.Enabled);
                    OnAreasChanged?.Invoke();
                    RefreshExclusionList();
                }
            }
        }
    }
}
