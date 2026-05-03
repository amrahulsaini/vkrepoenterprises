using System.Windows;
using System.Windows.Controls;

namespace VRASDesktopApp.Material
{
    public class MaterialCardButton : Button
    {
        static MaterialCardButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MaterialCardButton), new FrameworkPropertyMetadata(typeof(MaterialCardButton)));
        }
    }
}
