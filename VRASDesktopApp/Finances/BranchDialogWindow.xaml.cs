using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Finances;

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

    private void MaterialQuickActionButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void txtTerm_TextChanged(object sender, TextChangedEventArgs e)
    {
        lblTerm_Placeholder.Visibility = txtTerm.Text.Length <= 0 ? Visibility.Visible : Visibility.Collapsed;
        BranchesFiltered.Clear();
        var list = Branches
            .Where(b => b.BranchName.ToLower().Contains(txtTerm.Text.Trim().ToLower()))
            .OrderBy(b => b.BranchName)
            .ToList();
        foreach (var item in list)
        {
            BranchesFiltered.Add(item);
        }
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
