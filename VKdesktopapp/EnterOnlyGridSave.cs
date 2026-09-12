using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CRMRSDesktopApp;

internal static class EnterOnlyGridSave
{
    private sealed class GridState
    {
        public int AllowedCellEditEndings;
        public DispatcherTimer? ResetTimer;
    }

    private sealed class StatusWatch
    {
    }

    private static readonly ConditionalWeakTable<DataGrid, GridState> States = new();
    private static readonly ConditionalWeakTable<TextBlock, StatusWatch> StatusWatches = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnGridPreviewKeyDown),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            DataGrid.CellEditEndingEvent,
            new DataGridCellEditEndingEventHandler(OnCellEditEnding),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(ComboBox),
            Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(OnGridComboSelectionChanged),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(TextBox),
            UIElement.LostFocusEvent,
            new RoutedEventHandler(OnGridBillingRemarkLostFocus),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnGridLoaded),
            handledEventsToo: true);
    }

    private static bool IsTargetGrid(DataGrid grid)
    {
        var owner = FindAncestor<FrameworkElement>(grid);
        var name = owner?.GetType().FullName ?? "";
        return name == "CRMRSDesktopApp.Couriers.CouriersPage"
            || name == "CRMRSDesktopApp.Accounts.AccountsPage"
            || name == "CRMRSDesktopApp.Billing.ViewAllDetailsWindow";
    }

    private static void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || !IsTargetGrid(grid)) return;
        var root = FindAncestor<FrameworkElement>(grid);
        if (root == null) return;
        var status = FindNamedDescendant<TextBlock>(root, "txtStatus");
        if (status == null || StatusWatches.TryGetValue(status, out _)) return;

        StatusWatches.Add(status, new StatusWatch());
        status.TextChanged += Status_TextChanged;
    }

    private static void Status_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBlock status) return;
        var text = status.Text ?? "";
        if (!text.StartsWith("Saved", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("remark saved", StringComparison.OrdinalIgnoreCase)) return;

        status.Text = "Saved";
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        var expected = status.Text;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (status.Text == expected) status.Text = "";
        };
        timer.Start();
    }

    private static void OnGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not DependencyObject source) return;
        var grid = FindAncestor<DataGrid>(source);
        if (grid == null || !IsTargetGrid(grid)) return;

        // Billing Remark already has a dedicated Enter handler. Let it handle its own save.
        if (source is TextBox tb && IsBindingPath(tb, TextBox.TextProperty, "BillingRemark")
            && IsViewAll(grid)) return;

        if (source is not TextBox && source is not ComboBox) return;

        UpdateEditorSource(source);

        var state = States.GetOrCreateValue(grid);
        state.AllowedCellEditEndings = 2;
        state.ResetTimer?.Stop();
        state.ResetTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        state.ResetTimer.Tick += (_, _) =>
        {
            state.ResetTimer?.Stop();
            state.AllowedCellEditEndings = 0;
        };
        state.ResetTimer.Start();

        e.Handled = true;
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private static void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (sender is not DataGrid grid || !IsTargetGrid(grid)) return;
        var state = States.GetOrCreateValue(grid);

        if (state.AllowedCellEditEndings > 0)
        {
            state.AllowedCellEditEndings--;
            return;
        }

        // Existing page-level CellEditEnding handlers perform the actual API save.
        // Block them for mouse/focus commits so Enter is the only save trigger.
        e.Handled = true;
    }

    private static void OnGridComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        var grid = FindAncestor<DataGrid>(combo);
        if (grid == null || !IsTargetGrid(grid)) return;

        // Keep the binding/model updated, but suppress page-level SelectionChanged
        // handlers such as Couriers/Accounts Pick_Changed, which previously saved immediately.
        e.Handled = true;
    }

    private static void OnGridBillingRemarkLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        var grid = FindAncestor<DataGrid>(box);
        if (grid == null || !IsViewAll(grid)) return;
        if (!IsBindingPath(box, TextBox.TextProperty, "BillingRemark")) return;

        // Billing Remark must also wait for Enter; prevent the old LostFocus save path.
        e.Handled = true;
    }

    private static bool IsViewAll(DataGrid grid)
    {
        var owner = FindAncestor<FrameworkElement>(grid);
        return owner?.GetType().FullName == "CRMRSDesktopApp.Billing.ViewAllDetailsWindow";
    }

    private static void UpdateEditorSource(DependencyObject source)
    {
        if (source is TextBox textBox)
        {
            BindingOperations.GetBindingExpressionBase(textBox, TextBox.TextProperty)?.UpdateSource();
            return;
        }

        if (source is ComboBox comboBox)
        {
            BindingOperations.GetBindingExpressionBase(comboBox, Selector.SelectedValueProperty)?.UpdateSource();
            BindingOperations.GetBindingExpressionBase(comboBox, Selector.SelectedItemProperty)?.UpdateSource();
        }
    }

    private static bool IsBindingPath(DependencyObject element, DependencyProperty property, string expected)
    {
        var binding = BindingOperations.GetBindingBase(element, property);
        return binding is Binding b
            && string.Equals(NormalizePath(b.Path?.Path), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var clean = path.Trim();
        var dot = clean.LastIndexOf('.');
        return dot >= 0 ? clean[(dot + 1)..] : clean;
    }

    private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private static T? FindNamedDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T fe && fe.Name == name) return fe;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindNamedDescendant<T>(VisualTreeHelper.GetChild(root, i), name);
            if (found != null) return found;
        }
        return null;
    }
}
