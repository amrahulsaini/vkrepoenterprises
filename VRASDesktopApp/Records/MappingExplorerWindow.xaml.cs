using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Records;

public partial class MappingExplorerWindow : Window
{
    public MappingDetails MappingDetails;
    public ObservableCollection<Mapping> Mappings = new();

    public MappingExplorerWindow(MappingDetails mappingDetails)
    {
        InitializeComponent();
        MappingDetails = mappingDetails;
        gvColumns.ItemsSource = MappingDetails.ColumnTypes.OrderBy(d => d.ColumnTypeId);
        gvMappings.ItemsSource = Mappings;
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void gvColumns_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Mappings.Clear();
        if (gvColumns.SelectedItem is not ColumnType columnType)
        {
            return;
        }

        var list = MappingDetails.Mappings.Where(m => m.ColumnTypeId == columnType.ColumnTypeId);
        foreach (var item in list)
        {
            Mappings.Add(item);
        }
    }

    private async void btnMappingDelete_Click(object sender, RoutedEventArgs e)
    {
        if (gvMappings.SelectedItem is not Mapping mapping)
        {
            return;
        }

        try
        {
            if (MessageBox.Show("Are you sure to delete this record?", "Delete confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.No)
            {
                (await App.HttpClient.PostAsync(App.ApiBaseUrl + "api/Mapping/UnMap?MappingId=" + mapping.MappingId, null)).EnsureSuccessStatusCode();
                Mappings.Remove(mapping);
                MappingDetails.Mappings.Remove(mapping);
            }
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show("Http Request Exception: " + ex.Message);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Exception: " + ex.Message);
        }
    }
}
