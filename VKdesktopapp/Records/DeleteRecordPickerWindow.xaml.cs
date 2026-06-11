using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using CRMRSDesktopApp.Models;

namespace CRMRSDesktopApp.Records;

public partial class DeleteRecordPickerWindow : Window
{
    public sealed class FinanceChoice : INotifyPropertyChanged
    {
        public VehicleSearchItem Record { get; init; } = null!;

        public string Financer =>
            string.IsNullOrWhiteSpace(Record.Financer) ? "(no finance)" : Record.Financer;

        public string BranchLine
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Record.BranchName))
                    parts.Add(Record.BranchName);
                if (!string.IsNullOrWhiteSpace(Record.BranchFromExcel))
                    parts.Add($"Branch: {Record.BranchFromExcel}");
                return parts.Count > 0 ? string.Join("   •   ", parts) : "—";
            }
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<FinanceChoice> _choices;

    public List<VehicleSearchItem> SelectedRecords { get; private set; } = new();

    public DeleteRecordPickerWindow(string vehicleNo, IEnumerable<VehicleSearchItem> copies)
    {
        InitializeComponent();

        _choices = new ObservableCollection<FinanceChoice>(
            copies.Select(r => new FinanceChoice { Record = r }));
        icFinances.ItemsSource = _choices;

        txtPrompt.Text =
            $"\"{vehicleNo}\" exists in {_choices.Count} finances. " +
            "Tick the finance(s) to delete this vehicle from — every other " +
            "finance and the rest of your search results stay untouched.";
    }

    private void chkAll_Checked(object sender, RoutedEventArgs e)
    {
        foreach (var c in _choices) c.IsChecked = true;
    }

    private void chkAll_Unchecked(object sender, RoutedEventArgs e)
    {
        foreach (var c in _choices) c.IsChecked = false;
    }

    private void btnDelete_Click(object sender, RoutedEventArgs e)
    {
        var picked = _choices.Where(c => c.IsChecked).Select(c => c.Record).ToList();
        if (picked.Count == 0)
        {
            MessageBox.Show("Select at least one finance to delete from.",
                "Delete Vehicle Record", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Permanently delete this vehicle from {picked.Count} finance(s)?\n" +
            "This cannot be undone.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        SelectedRecords = picked;
        DialogResult = true;
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
