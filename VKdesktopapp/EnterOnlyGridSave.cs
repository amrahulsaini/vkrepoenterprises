using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    private static readonly ConditionalWeakTable<DataGrid, GridState> States = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnGridPreviewKeyDown),
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

    }

    private static FrameworkElement? TargetOwner(DependencyObject? child)
    {
        while (child != null)
        {
            if (child is FrameworkElement fe)
            {
                var name = fe.GetType().FullName;
                if (name == "CRMRSDesktopApp.Couriers.CouriersPage"
                    || name == "CRMRSDesktopApp.Accounts.AccountsPage"
                    || name == "CRMRSDesktopApp.Billing.ViewAllDetailsWindow")
                    return fe;
            }
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private static bool IsTargetGrid(DataGrid grid) => TargetOwner(grid) != null;

    private static void OnGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not DependencyObject source) return;
        var grid = FindAncestor<DataGrid>(source);
        if (grid == null || !IsTargetGrid(grid)) return;

        // Billing Remark already has a dedicated Enter handler in ViewAllDetailsWindow.EnterSave.cs.
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

    private static void OnGridComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        var grid = FindAncestor<DataGrid>(combo);
        if (grid == null || !IsTargetGrid(grid)) return;

        // Binding still updates the row; suppress Couriers/Accounts Pick_Changed immediate saves.
        e.Handled = true;
    }

    private static void OnGridBillingRemarkLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        var grid = FindAncestor<DataGrid>(box);
        if (grid == null || !IsViewAll(grid)) return;
        if (!IsBindingPath(box, TextBox.TextProperty, "BillingRemark")) return;

        // Billing Remark must wait for Enter; prevent the old LostFocus save path.
        e.Handled = true;
    }

    private static bool IsViewAll(DataGrid grid)
        => TargetOwner(grid)?.GetType().FullName == "CRMRSDesktopApp.Billing.ViewAllDetailsWindow";

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

}
