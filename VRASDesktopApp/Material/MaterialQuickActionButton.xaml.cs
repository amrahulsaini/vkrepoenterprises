using System.Windows;
using System.Windows.Controls;

namespace VRASDesktopApp.Material
{
    public class MaterialQuickActionButton : Button
    {
        public static readonly DependencyProperty FontIconProperty =
            DependencyProperty.Register(nameof(FontIcon), typeof(string), typeof(MaterialQuickActionButton));

        public static readonly DependencyProperty IsNavigationButtonProperty =
            DependencyProperty.Register(nameof(IsNavigationButton), typeof(bool), typeof(MaterialQuickActionButton));

        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(MaterialQuickActionButton));

        public static readonly DependencyProperty MouseOverBackgroundBrushProperty =
            DependencyProperty.Register(nameof(MouseOverBackgroundBrush), typeof(object), typeof(MaterialQuickActionButton));

        public static readonly DependencyProperty MouseOverBorderBrushProperty =
            DependencyProperty.Register(nameof(MouseOverBorderBrush), typeof(object), typeof(MaterialQuickActionButton));

        public string FontIcon
        {
            get => (string)GetValue(FontIconProperty);
            set => SetValue(FontIconProperty, value);
        }

        public bool IsNavigationButton
        {
            get => (bool)GetValue(IsNavigationButtonProperty);
            set => SetValue(IsNavigationButtonProperty, value);
        }

        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        public object MouseOverBackgroundBrush
        {
            get => GetValue(MouseOverBackgroundBrushProperty);
            set => SetValue(MouseOverBackgroundBrushProperty, value);
        }

        public object MouseOverBorderBrush
        {
            get => GetValue(MouseOverBorderBrushProperty);
            set => SetValue(MouseOverBorderBrushProperty, value);
        }

        static MaterialQuickActionButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MaterialQuickActionButton), new FrameworkPropertyMetadata(typeof(MaterialQuickActionButton)));
        }
    }
}
