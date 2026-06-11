using System.Windows;
using System.Windows.Controls;

namespace CRMRSDesktopApp.Material
{
    public class MaterialButton : Button
    {
        static MaterialButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MaterialButton), new FrameworkPropertyMetadata(typeof(MaterialButton)));
        }
    }
}
