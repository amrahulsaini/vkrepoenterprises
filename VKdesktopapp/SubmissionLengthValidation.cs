using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Runtime.CompilerServices;

namespace CRMRSDesktopApp;

internal static class SubmissionLengthValidation
{
    private sealed record Rule(string Label, int Max);

    // Limits mirror the VARCHAR sizes used by repo_submissions in the tenant schema.
    // TEXT columns are intentionally omitted because they do not have a VARCHAR limit.
    private static readonly IReadOnlyDictionary<string, Rule> Rules =
        new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase)
        {
            ["FinanceName"]          = new("Finance", 255),
            ["BranchName"]           = new("Branch", 255),
            ["LoanNo"]               = new("Loan No", 128),
            ["CustomerName"]         = new("Customer Name", 255),
            ["VehicleNo"]            = new("Vehicle No", 64),
            ["Model"]                = new("Model / Maker", 255),
            ["ChassisNo"]            = new("Chassis No", 128),
            ["EngineNo"]             = new("Engine No", 128),
            ["AgentName"]            = new("Agent Name", 255),
            ["ParkingYardName"]      = new("Parking Yard Name", 255),
            ["ParkingYardMobile"]    = new("Parking Yard Mobile", 64),
            ["LoadDetails"]          = new("Load Details", 512),
            ["AddlChargesNotes"]     = new("Additional Charges Notes", 512),
            ["ConfirmationByName"]   = new("Confirmation By Name", 255),
            ["ConfirmationByMobile"] = new("Confirmation By Mobile", 64),
            ["ExecutiveName"]        = new("Executive Name", 255),
            ["CollectionUpdate"]     = new("Collection Update", 512),
            ["Remark"]               = new("Remark", 512),
            ["BillingRemark"]        = new("Billing Remark", 512),
            ["AccountsRemark"]       = new("Accounts Remark", 512),
            ["PodNumber"]            = new("POD Number", 128),
            ["InvoiceNo"]            = new("Invoice No", 64),
            ["UtrNo"]                = new("UTR No", 64),
        };

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            UIElement.PreviewLostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnPreviewKeyboardFocusChanged),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(TextBox),
            UIElement.LostFocusEvent,
            new RoutedEventHandler(OnTextBoxLostFocus),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(ButtonBase),
            ButtonBase.ClickEvent,
            new RoutedEventHandler(OnButtonClick),
            handledEventsToo: true);
    }

    private static void OnPreviewKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box || !ReferenceEquals(e.OldFocus, box)) return;

        if (!TryGetError(GetBoundProperty(box), box.Text, out var message)) return;

        e.Handled = true;
        ShowError(message);
        box.SelectAll();
    }

    private static void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;

        if (!TryGetError(GetBoundProperty(box), box.Text, out var message)) return;

        // Prevent module-specific LostFocus save handlers from sending invalid data.
        e.Handled = true;
        ShowError(message);
        box.Focus();
        box.SelectAll();
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox box) return;

        if (!TryGetError(GetBoundProperty(box), box.Text, out var message)) return;

        e.Handled = true;
        ShowError(message);
        box.Focus();
        box.SelectAll();
    }

    private static string? GetBoundProperty(DataGridColumn column)
    {
        return column is DataGridBoundColumn bound && bound.Binding is Binding binding
            ? NormalizePath(binding.Path?.Path)
            : null;
    }

    private static string? GetBoundProperty(TextBox box)
    {
        var binding = BindingOperations.GetBindingBase(box, TextBox.TextProperty);
        return binding is Binding b ? NormalizePath(b.Path?.Path) : null;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var clean = path.Trim();
        var dot = clean.LastIndexOf('.');
        return dot >= 0 ? clean[(dot + 1)..] : clean;
    }

    private static bool TryGetError(string? property, string? value, out string message)
    {
        message = "";
        if (property == null || !Rules.TryGetValue(property, out var rule)) return false;

        var length = value?.Length ?? 0;
        if (length <= rule.Max) return false;

        message = $"{rule.Label} exceeds the maximum allowed length.\n\n" +
                  $"Maximum allowed: {rule.Max} characters\n" +
                  $"You entered: {length} characters\n\n" +
                  "Please shorten the value and try again.";
        return true;
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(
            message,
            "Input Too Long",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
