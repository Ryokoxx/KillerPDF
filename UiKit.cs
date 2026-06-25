using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KillerPDF
{
    // The single design-language kit for code-built UI. Tokens (fonts, radii, shadows) resolve from
    // App.xaml's resource dictionary, so XAML and code share ONE source and cannot drift. The control
    // factories (checkbox, field, labels, button rows, grain) give every dialog and tool the same look
    // without re-implementing chrome. New tools should build from UiKit + UiButtons + DialogChrome only.
    internal static class UiKit
    {
        // ---- token + theme accessors -------------------------------------------------------------
        public static FontFamily UiFont   => Res("UiFont",   _uiFallback);
        public static FontFamily MonoFont => Res("MonoFont", _monoFallback);
        public static FontFamily IconFont => Res("IconFont", _iconFallback);
        private static readonly FontFamily _uiFallback   = new FontFamily("Segoe UI, Microsoft JhengHei UI, Nirmala UI");
        private static readonly FontFamily _monoFallback = new FontFamily("Consolas");
        private static readonly FontFamily _iconFallback = new FontFamily("Segoe MDL2 Assets");

        public static CornerRadius RadControl => Rad("RadControl", 3);
        public static CornerRadius RadCard    => Rad("RadCard", 6);
        public static CornerRadius RadWindow  => Rad("RadWindow", 7);

        // Fresh shadow instances (cheap) matching App.xaml's Shadow* resources, for code that builds Effects.
        public static DropShadowEffect ShadowText()   => Shadow(3,  1, 0.6);
        public static DropShadowEffect ShadowIcon()   => Shadow(4,  1, 0.9);
        public static DropShadowEffect ShadowBar()    => Shadow(6,  3, 0.38);
        public static DropShadowEffect ShadowDialog() => Shadow(18, 3, 0.6);

        // Active-theme brush by key, with a safe fallback so the kit never throws before the theme loads.
        public static Brush Brush(string key, Brush? fallback = null)
            => Application.Current?.TryFindResource(key) as Brush ?? fallback ?? Brushes.Gray;

        private static T Res<T>(string key, T fallback) where T : class
            => Application.Current?.TryFindResource(key) as T ?? fallback;
        private static CornerRadius Rad(string key, double fb)
            => Application.Current?.TryFindResource(key) is CornerRadius c ? c : new CornerRadius(fb);
        private static DropShadowEffect Shadow(double blur, double depth, double opacity)
            => new DropShadowEffect { Color = Colors.Black, BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity };

        // ---- control factories -------------------------------------------------------------------

        // Themed checkbox: rounded box with an accent check mark when checked. Replaces the per-dialog
        // StyleCheckBox/ThemedCheckTemplate copies so every checkbox in the app is identical.
        public static CheckBox CheckBox(string label) => new CheckBox
        {
            Content                  = label,
            Foreground               = Brush("TextPrimary"),
            FontFamily               = UiFont,
            FontSize                 = 12,
            Cursor                   = Cursors.Hand,
            VerticalContentAlignment = VerticalAlignment.Center,
            Template                 = CheckTemplate()
        };

        private static ControlTemplate CheckTemplate()
        {
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var box = new FrameworkElementFactory(typeof(Border));
            box.SetValue(Border.WidthProperty, 16.0);
            box.SetValue(Border.HeightProperty, 16.0);
            box.SetValue(Border.CornerRadiusProperty, RadControl);
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            box.SetValue(Border.BorderBrushProperty, Brush("BorderDim"));
            box.SetValue(Border.BackgroundProperty, Brush("BgCanvas"));
            box.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
            box.SetValue(Border.MarginProperty, new Thickness(0, 0, 8, 0));

            var check = new FrameworkElementFactory(typeof(TextBlock)) { Name = "chk" };
            check.SetValue(TextBlock.TextProperty, "");   // Segoe MDL2 CheckMark
            check.SetValue(TextBlock.FontFamilyProperty, IconFont);
            check.SetValue(TextBlock.FontSizeProperty, 14.0);
            check.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            check.SetValue(TextBlock.ForegroundProperty, Brush("RadioAccent"));
            check.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(check);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            row.AppendChild(box);
            row.AppendChild(content);

            var ct = new ControlTemplate(typeof(CheckBox)) { VisualTree = row };
            var trig = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            trig.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible) { TargetName = "chk" });
            ct.Triggers.Add(trig);
            return ct;
        }

        // Themed single-line input. Reuses the XAML FormFieldTextBox style (themed selection/caret) so
        // code-built dialogs match the chrome defined in MainWindow.xaml.
        public static TextBox Field(double width = double.NaN)
        {
            var tb = new TextBox { FontFamily = UiFont, FontSize = 12 };
            if (Application.Current?.TryFindResource("FormFieldTextBox") is Style s) tb.Style = s;
            if (!double.IsNaN(width)) tb.Width = width;
            return tb;
        }

        // A dialog section heading (e.g. "ROTATE", "PAGE NUMBERS").
        public static TextBlock SectionHeader(string text) => new TextBlock
        {
            Text       = text,
            FontFamily = MonoFont,
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextPrimary"),
            Margin     = new Thickness(0, 0, 0, 6),
            Effect     = ShadowText()
        };

        // A small secondary label sitting above/beside a field.
        public static TextBlock GroupLabel(string text) => new TextBlock
        {
            Text       = text,
            FontFamily = UiFont,
            FontSize   = 11,
            Foreground = Brush("TextSecondary"),
            Margin     = new Thickness(0, 0, 0, 2)
        };

        // Right-aligned row of dialog buttons with a consistent 8px gap. Pass buttons left-to-right.
        public static StackPanel ButtonRow(params Button[] buttons)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i > 0) buttons[i].Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(buttons[i]);
            }
            return row;
        }

        // A flat text "link" (e.g. "Reset all") with an accent hover, for low-emphasis dialog actions.
        public static TextBlock LinkLabel(string text, Action onClick)
        {
            var link = new TextBlock
            {
                Text       = text,
                FontFamily = UiFont,
                FontSize   = 12,
                Foreground = Brush("TextSecondary"),
                Cursor     = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            link.MouseEnter += (_, _2) => link.Foreground = Brush("Accent");
            link.MouseLeave += (_, _2) => link.Foreground = Brush("TextSecondary");
            link.MouseLeftButtonUp += (_, _2) => onClick();
            return link;
        }
    }
}
