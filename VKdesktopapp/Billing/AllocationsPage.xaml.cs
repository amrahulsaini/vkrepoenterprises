using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Billing;

public partial class AllocationsPage : Page
{
    private class MemberRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Mobile { get; set; } = "";
        public string Email { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool IsActive { get; set; }
        public List<int> FinanceIds { get; set; } = new();
        public int FinanceCount => FinanceIds.Count;
        public string ActiveText => IsActive ? "Yes" : "No";
    }

    private class FinanceCheck
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsChecked { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? OwnerName { get; set; }
        public string Display => IsEnabled ? Name : $"{Name}   (allocated to {OwnerName})";
    }

    private List<MemberRow> _members = new();
    private readonly ObservableCollection<FinanceCheck> _finances = new();
    private ICollectionView? _financesView;
    private long _editingId;
    private bool _suppressCheck;

    public AllocationsPage()
    {
        InitializeComponent();
        _financesView = CollectionViewSource.GetDefaultView(_finances);
        _financesView.Filter = FinanceFilter;
        lstFinances.ItemsSource = _financesView;
        Loaded += async (_, __) => { await LoadFinancesAsync(); await LoadMembersAsync(); ResetForm(); };
    }

    private bool FinanceFilter(object o)
    {
        var q = txtFinanceSearch?.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return o is FinanceCheck f && f.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void txtFinanceSearch_TextChanged(object sender, TextChangedEventArgs e)
        => _financesView?.Refresh();

    private void RecomputeAvailability()
    {
        var owners = new Dictionary<int, string>();
        foreach (var m in _members.Where(m => m.Id != _editingId))
            foreach (var fid in m.FinanceIds)
                owners[fid] = m.Name;
        foreach (var f in _finances)
        {
            if (owners.TryGetValue(f.Id, out var owner)) { f.IsEnabled = false; f.OwnerName = owner; }
            else { f.IsEnabled = true; f.OwnerName = null; }
        }
        lstFinances.Items.Refresh();
    }

    private void FinanceCheck_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressCheck) return;
        if (sender is not CheckBox cb || cb.DataContext is not FinanceCheck fc) return;
        var owner = _members.FirstOrDefault(m => m.Id != _editingId && m.FinanceIds.Contains(fc.Id));
        if (owner != null)
        {
            MessageBox.Show(
                $"\"{fc.Name}\" is already allocated to \"{owner.Name}\".\nEach finance can belong to only one member.",
                "Already allocated", MessageBoxButton.OK, MessageBoxImage.Warning);
            fc.IsChecked = false;
            cb.IsChecked = false;
        }
    }

    private async Task LoadFinancesAsync()
    {
        _finances.Clear();
        try
        {
            var list = await DesktopApiClient.GetFinancesAsync();
            foreach (var f in list.OrderBy(f => f.Name))
                _finances.Add(new FinanceCheck { Id = f.Id, Name = f.Name });
        }
        catch (Exception ex) { txtFormStatus.Foreground = System.Windows.Media.Brushes.Red; txtFormStatus.Text = "Finances: " + ex.Message; }
    }

    private async Task LoadMembersAsync()
    {
        try
        {
            var list = await DesktopApiClient.GetBillingMembersAsync();
            _members = list.Select(m => new MemberRow
            {
                Id = m.Id, Name = m.Name, Mobile = m.Mobile, Email = m.Email,
                Username = m.Username, Password = m.Password, IsActive = m.IsActive,
                FinanceIds = m.FinanceIds ?? new List<int>()
            }).ToList();
            lstMembers.ItemsSource = null;
            lstMembers.ItemsSource = _members;
            txtListStatus.Text = $"{_members.Count} member(s).";
        }
        catch (Exception ex) { txtListStatus.Text = "Could not load: " + ex.Message; }
    }

    private void ResetForm()
    {
        _editingId = 0;
        txtFormTitle.Text = "New Member";
        txtName.Text = txtMobile.Text = txtEmail.Text = txtUsername.Text = txtPassword.Text = "";
        chkActive.IsChecked = true;
        _suppressCheck = true;
        foreach (var f in _finances) f.IsChecked = false;
        _suppressCheck = false;
        RecomputeAvailability();
        btnDelete.Visibility = Visibility.Collapsed;
        txtFormStatus.Text = "";
        lstMembers.SelectedItem = null;
    }

    private void btnNew_Click(object sender, RoutedEventArgs e) => ResetForm();

    private void lstMembers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstMembers.SelectedItem is not MemberRow m) return;
        _editingId = m.Id;
        txtFormTitle.Text = "Edit Member";
        txtName.Text = m.Name; txtMobile.Text = m.Mobile; txtEmail.Text = m.Email;
        txtUsername.Text = m.Username; txtPassword.Text = m.Password;
        chkActive.IsChecked = m.IsActive;
        _suppressCheck = true;
        foreach (var f in _finances) f.IsChecked = m.FinanceIds.Contains(f.Id);
        _suppressCheck = false;
        RecomputeAvailability();
        btnDelete.Visibility = Visibility.Visible;
        txtFormStatus.Text = "";
    }

    private async void btnSave_Click(object sender, RoutedEventArgs e)
    {
        var name = txtName.Text.Trim();
        var username = txtUsername.Text.Trim();
        var password = txtPassword.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(username))
        {
            Warn("Member name and username are required."); return;
        }
        if (_editingId == 0 && string.IsNullOrWhiteSpace(password))
        {
            Warn("A password is required for a new member."); return;
        }

        var financeIds = _finances.Where(f => f.IsChecked).Select(f => f.Id).ToList();
        var dto = new
        {
            Id = _editingId,
            Name = name,
            Mobile = txtMobile.Text.Trim(),
            Email = txtEmail.Text.Trim(),
            Username = username,
            Password = string.IsNullOrWhiteSpace(password) ? (string?)null : password,
            IsActive = chkActive.IsChecked == true,
            FinanceIds = financeIds
        };

        try
        {
            btnSave.IsEnabled = false;
            if (_editingId == 0) await DesktopApiClient.CreateBillingMemberAsync(dto);
            else await DesktopApiClient.UpdateBillingMemberAsync(_editingId, dto);
            await LoadMembersAsync();
            ResetForm();
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Green;
            txtFormStatus.Text = "Saved.";
        }
        catch (Exception ex) { Warn("Save failed: " + ex.Message); }
        finally { btnSave.IsEnabled = true; }
    }

    private async void btnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId == 0) return;
        if (MessageBox.Show("Delete this member?", "Allocations", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            await DesktopApiClient.DeleteBillingMemberAsync(_editingId);
            await LoadMembersAsync();
            ResetForm();
        }
        catch (Exception ex) { Warn("Delete failed: " + ex.Message); }
    }

    private void Warn(string msg)
    {
        txtFormStatus.Foreground = System.Windows.Media.Brushes.Red;
        txtFormStatus.Text = msg;
    }

    private void btnBack_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
    }
}
