using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace KillerPDF
{
    // Reusable title-bar chrome for KillerPDF's modal dialog windows (Print Preview, Transform,
    // Digital Signature, and any future ones). Builds the standard draggable bar: the "KillerPDF"
    // wordmark (Killer + green PDF) followed by a courier "- <suffix>" label, with the chrome-style
    // red close button on the right - the same look across every window. New windows just call
    // BuildTitleBar instead of re-implementing the wordmark/close button.
    internal static class DialogChrome
    {
        public const string CloseGlyph = ""; // Segoe MDL2 ChromeClose

        // Brush from the owner (then app) resources, with a safe fallback so the helper never throws.
        private static Brush Brush(Window? owner, string key, Brush fallback)
            => (owner?.TryFindResource(key) ?? Application.Current?.TryFindResource(key)) as Brush ?? fallback;

        // Builds the title bar.
        //   win       - the window being chromed (used for DragMove on the whole bar)
        //   owner      - supplies the themed brushes + the ChromeCloseButton style (pass the window's owner)
        //   fullTitle  - the complete title, e.g. "KillerPDF - Transform"; the "KillerPDF" part becomes the
        //                wordmark and the remainder (" - Transform") is rendered in the courier title font
        //   onClose    - invoked when the red close button is clicked (e.g. set a result then Close())
        public static Border BuildTitleBar(Window win, Window? owner, string? fullTitle, Action onClose)
        {
            // Transparent (not null) background so the WHOLE bar is hit-testable and acts as a drag handle.
            var bar = new Border { Background = Brushes.Transparent };
            bar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var segoe = UiKit.UiFont;

            // Build the wordmark row. A DropShadowEffect applied directly to text rasterizes it and
            // disables ClearType, which reads as blurry. So we LAYER it instead: a blurred black duplicate
            // sits behind a crisp, effect-free copy - soft shadow, sharp text. `shadow` paints the duplicate.
            StackPanel BuildWordmark(bool shadow)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Brush primary   = shadow ? Brushes.Black : Brush(owner, "TextPrimary", Brushes.White);
                Brush logo      = shadow ? Brushes.Black : Brush(owner, "AccentLogo", Brushes.LimeGreen);
                Brush secondary = shadow ? Brushes.Black : Brush(owner, "TextSecondary", Brushes.Gray);
                int kp = fullTitle?.IndexOf("KillerPDF", StringComparison.Ordinal) ?? -1;
                if (kp >= 0)
                {
                    sp.Children.Add(new TextBlock { Text = "Killer", FontFamily = segoe, FontWeight = FontWeights.Bold, FontSize = 15.5, Foreground = primary, VerticalAlignment = VerticalAlignment.Center });
                    sp.Children.Add(new TextBlock { Text = "PDF", FontFamily = segoe, FontWeight = FontWeights.Bold, FontSize = 15.5, Foreground = logo, VerticalAlignment = VerticalAlignment.Center });
                    string after = fullTitle![(kp + "KillerPDF".Length)..];
                    if (!string.IsNullOrEmpty(after))
                        sp.Children.Add(new TextBlock { Text = after, FontFamily = UiKit.MonoFont, FontSize = 14, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 1, 0, 0) });
                }
                else
                {
                    sp.Children.Add(new TextBlock { Text = fullTitle ?? "", FontFamily = UiKit.MonoFont, FontSize = 14, Foreground = primary, VerticalAlignment = VerticalAlignment.Center });
                }
                return sp;
            }

            var title = new Grid { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var shadowLayer = BuildWordmark(true);
            shadowLayer.Opacity = 0.5;
            shadowLayer.Effect = new BlurEffect { Radius = 2 };
            shadowLayer.RenderTransform = new TranslateTransform(0.7, 1.2);
            title.Children.Add(shadowLayer);
            title.Children.Add(BuildWordmark(false));
            Grid.SetColumn(title, 0);
            grid.Children.Add(title);

            // Full red rounded-corner close button (ChromeCloseButton), matching the main window chrome.
            var close = new Button { Content = CloseGlyph };
            if (owner?.TryFindResource("ChromeCloseButton") is Style chromeClose)
            {
                close.Style = chromeClose;
            }
            else
            {
                close.FontFamily = UiKit.IconFont;
                close.FontSize = 10;
                close.Width = 46; close.Height = 36;
                close.Foreground = Brush(owner, "DangerRed", Brushes.Red);
                close.Background = Brushes.Transparent;
                close.BorderThickness = new Thickness(0);
                close.Cursor = Cursors.Hand;
            }
            close.Click += (_, _2) => onClose();
            Grid.SetColumn(close, 1);
            grid.Children.Add(close);

            bar.Child = grid;
            return bar;
        }
    }
}
