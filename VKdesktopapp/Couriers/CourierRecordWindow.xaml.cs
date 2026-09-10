using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace CRMRSDesktopApp.Couriers;

/// <summary>
/// The full courier record, opened from a row click. Replaces the old inline
/// row-details expander that forced a long drag to the right.
/// </summary>
public partial class CourierRecordWindow : Window
{
    private readonly List<(string Label, string Value, bool Wide)> _fields;

    public CourierRecordWindow(string headline, string subline,
                               List<(string Label, string Value, bool Wide)> fields)
    {
        InitializeComponent();
        _fields = fields;
        txtHeadline.Text = headline;
        txtSubline.Text  = subline;
        BuildGrid();
    }

    /// Narrow fields pair up two per row; a wide one takes the full width and
    /// closes off whatever half-row was open.
    private void BuildGrid()
    {
        int row = 0, col = 0;
        void NewRow()
        {
            gridFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        NewRow();

        foreach (var (label, value, wide) in _fields)
        {
            if (wide && col != 0) { row++; col = 0; NewRow(); }

            var head = new Border { Style = (Style)FindResource("TblHead") };
            head.Child = new TextBlock { Text = label, Style = (Style)FindResource("TblLabel") };
            Grid.SetRow(head, row);
            Grid.SetColumn(head, col);
            gridFields.Children.Add(head);

            var body = new Border { Style = (Style)FindResource("TblBody") };
            body.Child = new TextBox { Text = value, Style = (Style)FindResource("TblValue") };
            Grid.SetRow(body, row);
            Grid.SetColumn(body, col + 1);
            if (wide) Grid.SetColumnSpan(body, 3);
            gridFields.Children.Add(body);

            if (wide) { row++; col = 0; NewRow(); }
            else if (col == 0) col = 2;
            else { row++; col = 0; NewRow(); }
        }
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (var (label, value, _) in _fields)
            sb.Append(label).Append('\t').AppendLine((value ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '));
        try { Clipboard.SetText(sb.ToString()); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not copy:\n{ex.Message}", "Copy",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
