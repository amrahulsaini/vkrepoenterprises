using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public static class ProfileGate
{
    public static bool ChangeAgencyRequested { get; private set; }

    public static async Task<bool> EnsureAsync(Window owner, string mode, bool standalone = false)
    {
        ChangeAgencyRequested = false;

        if (App.ProfileUser != null) { Stamp(owner); return true; }

        bool required;
        try { required = await DesktopApiClient.ProfileLoginRequiredAsync(); }
        catch { required = false; }
        if (!required) return true;

        if (!standalone && !owner.IsVisible) owner.Show();

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
