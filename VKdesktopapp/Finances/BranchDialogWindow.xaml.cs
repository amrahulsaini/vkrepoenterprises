using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CRMRSDesktopApp.Data;
using CRMRSDesktopApp.Models;

namespace CRMRSDesktopApp.Finances;

public partial class BranchDialogWindow : Window
{
    public ObservableCollection<Branch> BranchesFiltered = new();
    public List<Branch> Branches { get; set; }
    public Branch? SelectedBranch { get; set; }

    public BranchDialogWindow(List<Branch> branches)
    {
        InitializeComponent();
        Branches = branches;
        dgList.ItemsSource = BranchesFiltered;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var branch in Branches)
        {
            BranchesFiltered.Add(branch);
        }
        txtTerm.Focus();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void txtTerm_TextChanged(object sender, TextChangedEventArgs e) => RebuildFiltered();

    private void RebuildFiltered()
    {
        lblTerm_Placeholder.Visibility = txtTerm.Text.Length <= 0 ? Visibility.Visible : Visibility.Collapsed;
        BranchesFiltered.Clear();
        var term = txtTerm.Text;
        var list = Branches
            .Where(b => MatchesAllWords(b.BranchName, term))
            .OrderBy(b => b.BranchName)
            .ToList();
        foreach (var item in list)
        {
            BranchesFiltered.Add(item);
        }
    }

    private async void btnRefresh_Click(object sender, RoutedEventArgs e)
    {
        btnRefresh.IsEnabled = false;
        var prevContent = btnRefresh.Content;
        btnRefresh.Content = "⟳ …";
        try
        {
            var dtos = await DesktopApiClient.GetAllBranchesAsync();
            Branches = dtos.Select(d => new Branch
            {
                BranchId       = d.Id.ToString(),
                BranchName     = d.Name,
                HeadOfficeName = d.FinanceName,
                BranchCode     = "",
                Address        = d.Address
            }).ToList();
            RebuildFiltered();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to refresh branches: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnRefresh.Content = prevContent;
            btnRefresh.IsEnabled = true;
        }
    }

    private static bool MatchesAllWords(string? target, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (string.IsNullOrEmpty(target))     return false;
        foreach (var w in query.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
            if (target.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var count = dgList.Items.Count;
        if (count <= 0)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            if (dgList.SelectedIndex == -1)
            {
                dgList.SelectedIndex = 0;
            }
            if (dgList.SelectedIndex != 0)
            {
                dgList.SelectedIndex--;
            }
            dgList.ScrollIntoView(dgList.SelectedItem);
        }
        else if (e.Key == Key.Down)
        {
            if (dgList.SelectedIndex == -1)
            {
                dgList.SelectedIndex = count;
            }
            if (dgList.SelectedIndex != count)
            {
                dgList.SelectedIndex++;
            }
            dgList.ScrollIntoView(dgList.SelectedItem);
        }
        else if (e.Key == Key.Enter)
        {
            if (dgList.SelectedItem is Branch selectedBranch)
            {
                SelectedBranch = selectedBranch;
                DialogResult = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}
