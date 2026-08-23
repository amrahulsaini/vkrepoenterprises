using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public static class ProfileGate
{
    /// Set when the person asked to sign in to a different agency instead of
    /// signing in as a profile. Only the startup gate offers that.
    public static bool ChangeAgencyRequested { get; private set; }

    /// standalone: this is the gate the app opens with, so there is no window
    /// behind it to fall back to. It centres on the screen, takes a taskbar
    /// entry of its own, and carries the agency identity and "Change agency"
    /// that would otherwise live on the window behind.
    public static async Task<bool> EnsureAsync(Window owner, string mode, bool standalone = false)
    {
        ChangeAgencyRequested = false;

        if (App.ProfileUser != null) { Stamp(owner); return true; }

        bool required;
        try { required = await DesktopApiClient.ProfileLoginRequiredAsync(); }
        catch { required = false; }
        if (!required) return true;

        if (!standalone && !owner.IsVisible) owner.Show();

        // Backing out of the fingerprint scan returns to the mobile step rather
        // than dropping the person out of the app, so they can still try the
        // password or a different number.
        while (true)
        {
            var w = new ProfileLoginWindow(mode, standalone) { Owner = owner };
            Detach(w, standalone);
            var res = w.ShowDialog();

            if (res == true)
            {
                App.ProfileUser = new App.ProfileSession { Name = w.SignedInName, Mobile = w.SignedInMobile, Role = w.Role, Modules = w.Modules };
                Stamp(owner);
                return true;
            }

            if (w.ChangeAgencyRequested) { ChangeAgencyRequested = true; return false; }

            if (!w.NeedsFingerprint && !w.ChoseFingerprint) return false;

            var f = new FingerprintLoginWindow(mode, w.EnteredMobile) { Owner = owner };
            Detach(f, standalone);
            if (f.ShowDialog() != true) continue;

            App.ProfileUser = new App.ProfileSession { Name = f.SignedInName, Mobile = f.SignedInMobile, Role = f.Role, Modules = f.Modules };
            Stamp(owner);
            return true;
        }
    }

    /// A modal window centres on its owner and hides from the taskbar, which
    /// only makes sense while that owner is on screen.
    private static void Detach(Window w, bool standalone)
    {
        if (!standalone) return;
        w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowInTaskbar = true;
    }

    public static void Stamp(Window owner)
    {
        if (owner.FindName("lblProfile") is not TextBlock tb) return;
        var u = App.ProfileUser;
        if (u == null) { tb.Visibility = Visibility.Collapsed; return; }
        tb.Text = string.IsNullOrWhiteSpace(u.Name) ? u.Mobile : u.Name + "  ·  " + u.Mobile;
        tb.Visibility = Visibility.Visible;
        tb.IsHitTestVisible = true;
        tb.Cursor = System.Windows.Input.Cursors.Hand;
        tb.ToolTip = "Sign out of this profile";

        if (tb.Tag as string == "wired") return;
        tb.Tag = "wired";
        tb.MouseLeftButtonUp += (_, __) =>
        {
            var ask = MessageBox.Show(
                "Sign out of this profile? This screen will close and the next person can sign in.",
                "Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;
            App.ProfileUser = null;
            owner.Close();
        };
    }
}
