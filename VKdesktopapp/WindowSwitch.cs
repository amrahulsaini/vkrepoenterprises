using System.Windows;
using VRASDesktopApp.Records;

namespace VRASDesktopApp;

// Borderless / maximized WPF windows from the same process don't reliably
// raise to the front when their taskbar thumbnail is clicked. The robust
// workaround: keep only one big window non-minimized at a time. When a window
// is activated it minimizes the others, so every taskbar click becomes a
// guaranteed "restore from minimized" — which always works.
internal static class WindowSwitch
{
    public static void MinimizeOthers(Window active)
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (ReferenceEquals(w, active)) continue;
            if (w is MainWindow or RecordsEditorWindow or RecordValidatorAndUploaderWindow)
            {
                if (w.WindowState != WindowState.Minimized)
                    w.WindowState = WindowState.Minimized;
            }
        }
    }
}
