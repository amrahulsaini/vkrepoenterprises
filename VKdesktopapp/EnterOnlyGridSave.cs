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
            new RoutedEventHandler(OnGridTextBoxLostFocus),
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

        // Billing ViewAll has a dedicated direct-save handler for Billing Remark.
        if (source is TextBox tb
            && IsBindingPath(tb, TextBox.TextProperty, "BillingRemark")
            && IsViewAll(grid))
            return;

        // Focus can move onto the cell/grid while a text edit is still pending.
        // Locate that editor instead of silently ignoring Enter from a cell or its child.
        var editor = source as TextBox ?? (DependencyObject?)FindAncestor<TextBox>(source)
            ?? source as ComboBox ?? FindAncestor<ComboBox>(source) ?? FindEditingControl(grid);
        if (editor == null) return;
        UpdateEditorSource(editor);
        if (Validation.GetHasError(editor)) { e.Handled = true; return; }

        e.Handled = true;
        Commit(grid);

        // Leave the editor immediately so there is no lingering caret/focus border.
        grid.Dispatcher.BeginInvoke(new Action(() => grid.Focus()),
            DispatcherPriority.Input);
    }

    private static void OnGridComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        var grid = FindAncestor<DataGrid>(combo);
        if (grid == null || !IsTargetGrid(grid)) return;

        if (!combo.IsLoaded || FindAncestor<DataGridCell>(combo)?.IsEditing != true || e.AddedItems.Count == 0) return;
        UpdateEditorSource(combo);
        grid.Dispatcher.BeginInvoke(new Action(() => Commit(grid)), DispatcherPriority.Background);
    }

    internal static bool AllowCommit(DataGrid grid, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return false;
        if (States.GetOrCreateValue(grid).AllowedCellEditEndings > 0) return true;
        // Keep text in its editor until explicitly submitted with Enter (Escape cancels).
        e.Cancel = true;
        return false;
    }

    private static void Commit(DataGrid grid)
    {
        var state = States.GetOrCreateValue(grid);
        state.AllowedCellEditEndings++;
        try
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
        }
        finally { state.AllowedCellEditEndings--; }
    }

    private static void OnGridTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        var grid = FindAncestor<DataGrid>(box);
        if (grid == null || !IsTargetGrid(grid)) return;

        // Specifically suppress the old Billing Remark LostFocus save path.
        if (IsViewAll(grid) && IsBindingPath(box, TextBox.TextProperty, "BillingRemark"))
            e.Handled = true;
    }

    private static DependencyObject? FindEditingControl(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if ((child is TextBox || child is ComboBox) && FindAncestor<DataGridCell>(child)?.IsEditing == true)
                return child;
            var found = FindEditingControl(child);
            if (found != null) return found;
        }
        return null;
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
