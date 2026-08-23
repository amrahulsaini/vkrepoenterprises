using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public static class ProfileGate
{
    public static async Task<bool> EnsureAsync(Window owner, string mode)
    {
        if (App.ProfileUser != null) { Stamp(owner); return true; }

        bool required;
        try { required = await DesktopApiClient.ProfileLoginRequiredAsync(); }
        catch { required = false; }
        if (!required) return true;

        if (!owner.IsVisible) owner.Show();

        var w = new ProfileLoginWindow(mode) { Owner = owner };
        var res = w.ShowDialog();

        if (res != true && w.NeedsFingerprint)
        {
            var f = new FingerprintLoginWindow(mode) { Owner = owner };
            if (f.ShowDialog() != true) return false;
            App.ProfileUser = new App.ProfileSession { Name = f.SignedInName, Mobile = f.SignedInMobile, Role = f.Role, Modules = f.Modules };
            Stamp(owner);
            return true;
        }

        if (res != true) return false;

        App.ProfileUser = new App.ProfileSession { Name = w.SignedInName, Mobile = w.SignedInMobile, Role = w.Role, Modules = w.Modules };
        Stamp(owner);
        return true;
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
