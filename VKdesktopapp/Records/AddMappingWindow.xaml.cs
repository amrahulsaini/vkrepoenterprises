using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CRMRSDesktopApp.Data;
using CRMRSDesktopApp.Models;

namespace CRMRSDesktopApp.Records;

public partial class AddMappingWindow : Window
{
    public ObservableCollection<ColumnType> ColumnTypesFiltered = new();
    public string ColumnNameToMapped;
    public Mapping MappedColumn = new();
    public List<ColumnType> ColumnTypes { get; set; }

    private readonly MappingRepository _repo = new();

    public AddMappingWindow(List<ColumnType> columnTypes, string columnNameToMapped)
    {
        InitializeComponent();
        ColumnTypes        = columnTypes;
        ColumnNameToMapped = columnNameToMapped;
        dgList.ItemsSource = ColumnTypesFiltered;
        lblColumnToMap.Text = columnNameToMapped;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var item in ColumnTypes.OrderBy(f => f.ColumnTypeId))
            ColumnTypesFiltered.Add(item);
        txtTerm.Focus();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void txtTerm_TextChanged(object sender, TextChangedEventArgs e)
    {
        lblTerm_Placeholder.Visibility = txtTerm.Text.Length <= 0 ? Visibility.Visible : Visibility.Collapsed;
        ColumnTypesFiltered.Clear();
        var term = txtTerm.Text;
        foreach (var item in ColumnTypes
            .Where(f => MatchesAllWords(f.ColumnTypeName, term))
            .OrderBy(f => f.ColumnTypeId))
            ColumnTypesFiltered.Add(item);
    }

    private static bool MatchesAllWords(string? target, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (string.IsNullOrEmpty(target))     return false;
        foreach (var w in query.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
            if (target.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var count = dgList.Items.Count;
        if (count <= 0) return;

        if (e.Key == Key.Up)
        {
            if (dgList.SelectedIndex == -1) dgList.SelectedIndex = 0;
            if (dgList.SelectedIndex != 0)  dgList.SelectedIndex--;
            dgList.ScrollIntoView(dgList.SelectedItem);
        }
        else if (e.Key == Key.Down)
        {
            if (dgList.SelectedIndex == -1) dgList.SelectedIndex = count;
            if (dgList.SelectedIndex != count) dgList.SelectedIndex++;
            dgList.ScrollIntoView(dgList.SelectedItem);
        }
        else if (e.Key == Key.Enter)
        {
            if (dgList.SelectedItem is ColumnType ct &&
                MessageBox.Show("Are you sure to map this column?", "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                CreateMapping(ct);
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private async void CreateMapping(ColumnType columnType)
    {
        try
        {
            MappedColumn = await _repo.CreateMappingAsync(columnType.ColumnTypeId, lblColumnToMap.Text);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to create mapping: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
