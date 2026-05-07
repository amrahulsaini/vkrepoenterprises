using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        MappingDetails      = mappingDetails;
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
}
