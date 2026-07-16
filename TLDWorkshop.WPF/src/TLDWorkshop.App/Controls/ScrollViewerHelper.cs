using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TLDWorkshop.App.Controls;

/// <summary>
/// 滚动辅助 attached property。
///
/// 彻底修复所有页面的鼠标滚轮 + 触屏滑动问题。
/// 滚轮用 PreviewMouseWheel（已工作），触屏用 PreviewTouchMove 手动跟踪手指移动。
/// </summary>
public static class ScrollViewerHelper
{
    public static readonly DependencyProperty EnableWheelScrollProperty =
        DependencyProperty.RegisterAttached(
            "EnableWheelScroll",
            typeof(bool),
            typeof(ScrollViewerHelper),
            new PropertyMetadata(false, OnEnableWheelScrollChanged));

    public static bool GetEnableWheelScroll(DependencyObject obj)
        => (bool)obj.GetValue(EnableWheelScrollProperty);

    public static void SetEnableWheelScroll(DependencyObject obj, bool value)
        => obj.SetValue(EnableWheelScrollProperty, value);

    // 每个 ScrollViewer 的触屏跟踪状态
    private static readonly ConditionalWeakTable<ScrollViewer, TouchTracker> _trackers = new();

    private sealed class TouchTracker
    {
        public Point? LastPosition;
        public int? LastTouchId;
    }

    private static void OnEnableWheelScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        if ((bool)e.NewValue)
        {
            sv.Loaded += OnScrollViewerLoaded;
            ForceScrollProperties(sv);
            sv.PreviewMouseWheel += HandlePreviewMouseWheel;
            // 触屏支持
            sv.PreviewTouchDown += HandlePreviewTouchDown;
            sv.PreviewTouchMove += HandlePreviewTouchMove;
            sv.PreviewTouchUp += HandlePreviewTouchUp;
        }
        else
        {
            sv.Loaded -= OnScrollViewerLoaded;
            sv.PreviewMouseWheel -= HandlePreviewMouseWheel;
            sv.PreviewTouchDown -= HandlePreviewTouchDown;
            sv.PreviewTouchMove -= HandlePreviewTouchMove;
            sv.PreviewTouchUp -= HandlePreviewTouchUp;
        }
    }

    private static void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer sv)
            ForceScrollProperties(sv);
    }

    private static void ForceScrollProperties(ScrollViewer sv)
    {
        sv.CanContentScroll = false;
        sv.PanningMode = PanningMode.VerticalOnly;
        sv.IsManipulationEnabled = true;
    }

    /// <summary>
    /// 递归遍历可视化树，给所有 ScrollViewer 强制设置触屏 + 滚轮支持。
    /// 用 ConditionalWeakTable 跟踪已处理的 ScrollViewer，避免内存泄漏（ScrollViewer 可被 GC 回收时自动移除）。
    /// </summary>
    private static readonly ConditionalWeakTable<ScrollViewer, object> _processed = new();

    public static void ApplyToAllScrollableViewers(DependencyObject root)
    {
        ApplyRecursive(root);
    }

    private static void ApplyRecursive(DependencyObject root)
    {
        if (root is ScrollViewer sv)
        {
            // 只处理未处理过的 ScrollViewer（性能优化）
            if (!_processed.TryGetValue(sv, out _))
            {
                ForceScrollProperties(sv);
                if (!GetEnableWheelScroll(sv))
                    SetEnableWheelScroll(sv, true);
                _processed.AddOrUpdate(sv, new object());
            }
            else
            {
                // 已处理过的也强制刷新属性（WPF-UI 主题可能重置）
                ForceScrollProperties(sv);
            }
        }

        // 只遍历 VisualTree（比 LogicalTree 更准确，且更快）
        if (root is Visual)
        {
            var n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
                ApplyRecursive(VisualTreeHelper.GetChild(root, i));
        }
    }

    /// <summary>PreviewMouseWheel 隧道事件处理。</summary>
    private static void HandlePreviewMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.ScrollableHeight <= 0) return;

        // Bug 修复：ComboBox 下拉展开时，不要滚动页面 ScrollViewer
        // 检查鼠标位置是否在 ComboBox 的 Popup 上
        if (IsMouseOverComboBoxPopup(sv, e))
        {
            // 让 ComboBox 自己的 Popup ScrollViewer 处理
            return;
        }

        var delta = e.Delta > 0 ? 48 : (e.Delta < 0 ? -48 : 0);
        sv.ScrollToVerticalOffset(sv.VerticalOffset - delta);
        e.Handled = true;
    }

    /// <summary>检查鼠标是否在 ComboBox 下拉 Popup 上。</summary>
    private static bool IsMouseOverComboBoxPopup(ScrollViewer sv, MouseWheelEventArgs e)
    {
        try
        {
            // ComboBox 的 Popup 在独立的可视化树里，不在 sv 的子树里
            // 检查 OriginalSource 是否是 Popup 的子元素
            var source = e.OriginalSource as DependencyObject;
            if (source == null) return false;

            // 向上找，如果遇到 ComboBoxItem 或 Popup，说明在下拉里
            var current = source;
            while (current != null)
            {
                if (current is System.Windows.Controls.Primitives.Popup ||
                    current is ComboBoxItem)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
        }
        catch { }
        return false;
    }

    /// <summary>PreviewTouchDown：记录起始位置。</summary>
    private static void HandlePreviewTouchDown(object? sender, TouchEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        var tracker = _trackers.GetOrCreateValue(sv);
        tracker.LastTouchId = e.TouchDevice.Id;
        tracker.LastPosition = e.GetTouchPoint(sv).Position;
    }

    /// <summary>PreviewTouchMove：根据手指移动距离手动滚动。</summary>
    private static void HandlePreviewTouchMove(object? sender, TouchEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.ScrollableHeight <= 0) return;

        var tracker = _trackers.GetOrCreateValue(sv);
        if (tracker.LastTouchId != e.TouchDevice.Id) return;
        if (tracker.LastPosition is not Point last) return;

        var current = e.GetTouchPoint(sv).Position;
        var dy = current.Y - last.Y;

        if (Math.Abs(dy) < 1) return;

        // 手指向下 → 内容向下滚（VerticalOffset 减小）
        sv.ScrollToVerticalOffset(sv.VerticalOffset - dy);
        tracker.LastPosition = current;
        // 标记 Handled 阻止 PanningMode 重复处理
        e.Handled = true;
    }

    /// <summary>PreviewTouchUp：清空跟踪状态。</summary>
    private static void HandlePreviewTouchUp(object? sender, TouchEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        var tracker = _trackers.GetOrCreateValue(sv);
        if (tracker.LastTouchId == e.TouchDevice.Id)
        {
            tracker.LastPosition = null;
            tracker.LastTouchId = null;
        }
    }
}
