using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using KillerPDF.Services;

namespace KillerPDF
{
    public partial class MainWindow
    {
        // ============================================================
        // Selection
        // ============================================================

        // Resolve the active theme's "SelectionAccent" color: a per-theme color picked to stay
        // readable on the white PDF page (Accent is white in several themes, and AccentBorder is a
        // pale cream that washes out on white). Falls back to brand green.
        private Color AccentColor()
            => TryFindResource("SelectionAccent") is SolidColorBrush b ? b.Color : Color.FromRgb(30, 165, 76);
        private SolidColorBrush AccentBrush(byte alpha = 255)
        {
            var c = AccentColor();
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }
        // A darker shade of the accent, used for a cover's selection chrome and its in-edit outline so a
        // cover reads as distinct from the lighter accent on the text box stacked over it.
        private SolidColorBrush DarkerAccentBrush(byte alpha = 255)
        {
            var c = AccentColor();
            return new SolidColorBrush(Color.FromArgb(alpha, (byte)(c.R * 0.6), (byte)(c.G * 0.6), (byte)(c.B * 0.6)));
        }

        // Select-all on the current page, via the same PDFium text engine as the flowing selection: the
        // whole page becomes one text selection (per-line highlight on the right tile, Ctrl+C-able),
        // instead of the old PdfPig full-document reopen + a full-canvas box on the primary canvas
        // (which was the wrong tile in grid/continuous view).
        private void SelectAllText()
        {
            if (_currentFile is null) return;
            // Prefer the page the live selection starts on, so dragging on one page then hitting Ctrl+A
            // widens THAT page rather than whichever page the viewport happens to centre on (the current
            // page is viewport-driven in continuous/grid view). Only when that page is still on screen -
            // otherwise a stale selection would silently copy a page the user scrolled away from.
            int pageIdx = HasTextSelection && VisibleCanvasForPage(_selStart.page) is not null
                ? _selStart.page
                : PageList.SelectedIndex;
            if (pageIdx < 0) return;

            IntPtr tp = EnsureTextPage(pageIdx);
            int total = tp == IntPtr.Zero ? 0 : FPDFText_CountChars(tp);
            string text = total > 0 ? TextRangeString(pageIdx, 0, total) : string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus(Loc("Str_St_NoTextOnPage"));
                return;
            }
            ClearTextSelection();
            RenderTextSelection(pageIdx, 0, total);
            _selectedText = text;
            TrySetClipboard(text);
            SetStatus($"Selected all text - copied to clipboard");
        }

        private void CopySelectedText()
        {
            if (!string.IsNullOrEmpty(_selectedText))
            {
                Clipboard.SetText(_selectedText);
                SetStatus($"Copied to clipboard");
            }
            else
            {
                SetStatus(Loc("Str_St_NoTextSelected"));
            }
        }

        private void ClearTextSelection()
        {
            if (_selectRect is not null)
            {
                // Remove from the rect's ACTUAL parent. Since the cross-page marquee rework the
                // selection box lives on the window-level MarqueeLayer, not the page canvas, so
                // removing from _activeCanvas was a silent no-op that orphaned the box on the
                // layer until app restart (#121).
                (_selectRect.Parent as Canvas)?.Children.Remove(_selectRect);
                _selectRect = null;
            }
            CancelTextDrag();                // cancel any in-progress flowing drag + free its capture
            ClearTextSelectionHighlight();   // and drop the word/char highlight rects + forget the range
            _selectedText = null;
        }

    }
}
