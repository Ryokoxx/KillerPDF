using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
        private static readonly FontFamily _uiFallback   = new("Segoe UI, Microsoft JhengHei UI, Nirmala UI");
        private static readonly FontFamily _monoFallback = new("Consolas");
        private static readonly FontFamily _iconFallback = new("Segoe MDL2 Assets");

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
            => new() { Color = Colors.Black, BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity };

        // The default quick-color palette, shared by the annotate bars and the color picker's swatch row
        // (the "UserSwatches" setting seeds from this). One source so the two can't drift.
        public static readonly Color[] DefaultSwatches =
        [
            Color.FromRgb(0xE0, 0x3C, 0x3C), Color.FromRgb(0xE8, 0x7A, 0x1E), Color.FromRgb(0xF2, 0xC0, 0x1E),
            Color.FromRgb(0x2E, 0xA5, 0x4C), Color.FromRgb(0x2E, 0x86, 0xDE), Color.FromRgb(0x8E, 0x5B, 0xD6),
            Color.FromRgb(0xE0, 0x4A, 0x9A), Colors.Black, Colors.White
        ];

        // ---- control factories -------------------------------------------------------------------

        // Themed checkbox: rounded box with an accent check mark when checked. Replaces the per-dialog
        // StyleCheckBox/ThemedCheckTemplate copies so every checkbox in the app is identical.
        public static CheckBox CheckBox(string label) => new()
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
            var row = new FrameworkElementFactory(typeof(StackPanel)) { Name = "root" };
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
            // Disabled state: dim the whole control (box + label) so it's obviously inactive.
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4) { TargetName = "root" });
            ct.Triggers.Add(disabled);
            return ct;
        }

        // Themed radio button with a clean horizontal layout (ring + accent dot + label), built from the
        // theme brushes. Unlike the settings-panel ThemeRadio (a full-width vertical row), this lays out
        // tightly for inline/horizontal use.
        public static RadioButton Radio(string text) => new()
        {
            Content                  = text,
            Foreground               = Brush("TextPrimary"),
            FontFamily               = UiFont,
            FontSize                 = 12,
            Cursor                   = Cursors.Hand,
            VerticalContentAlignment = VerticalAlignment.Center,
            Template                 = RadioTemplate()
        };

        private static ControlTemplate RadioTemplate()
        {
            var sp = new FrameworkElementFactory(typeof(StackPanel)) { Name = "root" };
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            sp.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            var ring = new FrameworkElementFactory(typeof(Border)) { Name = "ring" };
            ring.SetValue(Border.WidthProperty, 15.0);
            ring.SetValue(Border.HeightProperty, 15.0);
            ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(7.5));
            ring.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            ring.SetValue(Border.BorderBrushProperty, Brush("TextDim"));
            ring.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
            ring.SetValue(Border.MarginProperty, new Thickness(0, 1, 7, 0));   // +1 top settles it against the text optical center

            var dot = new FrameworkElementFactory(typeof(Border)) { Name = "dot" };
            dot.SetValue(Border.WidthProperty, 7.0);
            dot.SetValue(Border.HeightProperty, 7.0);
            dot.SetValue(Border.CornerRadiusProperty, new CornerRadius(3.5));
            dot.SetValue(Border.BackgroundProperty, Brush("RadioAccent"));
            dot.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            dot.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
            dot.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            ring.AppendChild(dot);

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            sp.AppendChild(ring);
            sp.AppendChild(cp);

            var ct = new ControlTemplate(typeof(RadioButton)) { VisualTree = sp };
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible) { TargetName = "dot" });
            on.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("RadioAccent")) { TargetName = "ring" });
            ct.Triggers.Add(on);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4) { TargetName = "root" });
            ct.Triggers.Add(off);
            return ct;
        }

        // Themed single-line input, fully self-contained (templated from the theme brushes) so it renders
        // correctly in ANY window without depending on a window-scoped XAML style. Kills the OS-default
        // white box / blue focus + selection chrome.
        public static TextBox Field(double width = double.NaN)
        {
            var tb = new TextBox
            {
                FontFamily         = UiFont,
                FontSize           = 12,
                Background         = Brush("BgCanvas"),
                Foreground         = Brush("TextPrimary"),
                BorderBrush        = Brush("BorderDim"),
                BorderThickness    = new Thickness(1),
                Padding            = new Thickness(6, 4, 6, 4),
                CaretBrush         = Brush("TextPrimary"),
                SelectionBrush     = Brush("AccentDim"),
                SelectionTextBrush = Brush("TextPrimary"),
                Template           = FieldTemplate()
            };
            if (!double.IsNaN(width)) tb.Width = width;
            return tb;
        }

        private static ControlTemplate FieldTemplate()
        {
            var b = new FrameworkElementFactory(typeof(Border));
            b.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            b.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            b.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            b.SetValue(Border.CornerRadiusProperty, RadControl);
            var sv = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
            sv.SetValue(Control.PaddingProperty, new Thickness(0));
            b.AppendChild(sv);
            return new ControlTemplate(typeof(TextBox)) { VisualTree = b };
        }

        // A dialog section heading (e.g. "ROTATE", "PAGE NUMBERS").
        public static TextBlock SectionHeader(string text) => new()
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
        public static TextBlock GroupLabel(string text) => new()
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
