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

            var segoe = new FontFamily("Segoe UI, Microsoft JhengHei UI, Nirmala UI");
            var title = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.6 }
            };

            int kp = fullTitle?.IndexOf("KillerPDF", StringComparison.Ordinal) ?? -1;
            if (kp >= 0)
            {
                title.Children.Add(new TextBlock { Text = "Killer", FontFamily = segoe, FontWeight = FontWeights.Bold, FontSize = 15.5, Foreground = Brush(owner, "TextPrimary", Brushes.White), VerticalAlignment = VerticalAlignment.Center });
                title.Children.Add(new TextBlock { Text = "PDF", FontFamily = segoe, FontWeight = FontWeights.Bold, FontSize = 15.5, Foreground = Brush(owner, "AccentLogo", Brushes.LimeGreen), VerticalAlignment = VerticalAlignment.Center });
                string after = fullTitle![(kp + "KillerPDF".Length)..];
                if (!string.IsNullOrEmpty(after))
                    title.Children.Add(new TextBlock { Text = after, FontFamily = new FontFamily("Consolas"), FontSize = 14, Foreground = Brush(owner, "TextSecondary", Brushes.Gray), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 1, 0, 0) });
            }
            else
            {
                title.Children.Add(new TextBlock { Text = fullTitle ?? "", FontFamily = new FontFamily("Consolas"), FontSize = 14, Foreground = Brush(owner, "TextPrimary", Brushes.White), VerticalAlignment = VerticalAlignment.Center });
            }
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
                close.FontFamily = new FontFamily("Segoe MDL2 Assets");
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
