using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VRASDesktopApp.Data;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Records;

public partial class MappingExplorerWindow : Window
{
    public MappingDetails MappingDetails;
    public ObservableCollection<Mapping> Mappings = new();

    private readonly MappingRepository _repo = new();

    public MappingExplorerWindow(MappingDetails mappingDetails)
    {
        InitializeComponent();
        MappingDetails         = mappingDetails;
        gvColumns.ItemsSource  = MappingDetails.ColumnTypes.OrderBy(d => d.ColumnTypeId);
        gvMappings.ItemsSource = Mappings;
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void gvColumns_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Mappings.Clear();
        if (gvColumns.SelectedItem is not ColumnType columnType) return;
        foreach (var item in MappingDetails.Mappings.Where(m => m.ColumnTypeId == columnType.ColumnTypeId))
            Mappings.Add(item);
    }

    // ─────────────────────────────────────────────────────
    //  Delete mapping (alias)
    // ─────────────────────────────────────────────────────

    private async void btnMappingDelete_Click(object sender, RoutedEventArgs e)
    {
        if (gvMappings.SelectedItem is not Mapping mapping) return;
        if (MessageBox.Show("Are you sure to delete this mapping?", "Delete confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No) return;
        try
        {
            await _repo.DeleteMappingAsync(mapping.MappingId);
            Mappings.Remove(mapping);
            MappingDetails.Mappings.Remove(mapping);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to delete mapping: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─────────────────────────────────────────────────────
    //  Add column type (left panel)
    // ─────────────────────────────────────────────────────

    private void btnAddColumnType_Click(object sender, RoutedEventArgs e)
    {
        brdNewColumnType.Visibility = Visibility.Visible;
        txtNewColumnType.Text       = string.Empty;
        txtNewColumnType.Focus();
    }

    private void btnCancelColumnType_Click(object sender, RoutedEventArgs e)
    {
        brdNewColumnType.Visibility = Visibility.Collapsed;
        txtNewColumnType.Text       = string.Empty;
    }

    private void txtNewColumnType_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SaveColumnTypeAsync();
        else if (e.Key == Key.Escape) btnCancelColumnType_Click(sender, new RoutedEventArgs());
    }

    private void btnSaveColumnType_Click(object sender, RoutedEventArgs e)
        => SaveColumnTypeAsync();

    private async void SaveColumnTypeAsync()
    {
        var name = txtNewColumnType.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) return;

        try
        {
            var newType = await _repo.CreateColumnTypeAsync(name);
            MappingDetails.ColumnTypes.Add(newType);
            // Refresh the DataGrid (it's bound to a sorted query, not ObservableCollection)
            gvColumns.ItemsSource = MappingDetails.ColumnTypes.OrderBy(d => d.ColumnTypeId).ToList();
            brdNewColumnType.Visibility = Visibility.Collapsed;
            txtNewColumnType.Text       = string.Empty;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to create column type: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─────────────────────────────────────────────────────
    //  Add alias (right panel)
    // ─────────────────────────────────────────────────────

    private void btnAddAlias_Click(object sender, RoutedEventArgs e)
    {
        if (gvColumns.SelectedItem is not ColumnType)
        {
            MessageBox.Show("Select a column type on the left first.", "Select Column",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        brdNewAlias.Visibility = Visibility.Visible;
        txtNewAlias.Text       = string.Empty;
        txtNewAlias.Focus();
    }

    private void btnCancelAlias_Click(object sender, RoutedEventArgs e)
    {
        brdNewAlias.Visibility = Visibility.Collapsed;
        txtNewAlias.Text       = string.Empty;
    }

    private void txtNewAlias_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SaveAliasAsync();
        else if (e.Key == Key.Escape) btnCancelAlias_Click(sender, new RoutedEventArgs());
    }

    private void btnSaveAlias_Click(object sender, RoutedEventArgs e)
        => SaveAliasAsync();

    private async void SaveAliasAsync()
    {
        if (gvColumns.SelectedItem is not ColumnType columnType) return;
        var raw = txtNewAlias.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(raw)) return;

        try
        {
            var newMapping = await _repo.CreateMappingAsync(columnType.ColumnTypeId, raw);
            MappingDetails.Mappings.Add(newMapping);
            Mappings.Add(newMapping);
            brdNewAlias.Visibility = Visibility.Collapsed;
            txtNewAlias.Text       = string.Empty;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to create alias: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
