using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace KillerPDF
{
    public partial class MainWindow
    {
        // ============================================================
        // PDFium text-selection engine
        //
        // Chrome-style character selection needs: char-index-at-a-point, per-char boxes, the selection
        // rectangles for a char range (for glyph-level highlighting), and range->text. PdfPig (used for
        // the old box/word marquee) exposes none of these conveniently; PDFium's FPDFText_* API is built
        // for exactly this and reads object-stream PDFs (same reason links moved to PDFium). We reuse the
        // cached document handle from Links.cs (_linkPdfiumDoc via EnsureLinkPdfiumDoc) and cache one text
        // page for the active document page on top of it. The page + text-page handles are children of the
        // cached doc, so CloseLinkPdfiumDoc() also closes them (see the CloseTextPage() call there).
        //
        // Coordinates: PDFium text coords are PDF points, origin bottom-left, Y up. Canvas coords are the
        // page's render-dim space (the same space _renderDims and the link overlays use), origin top-left,
        // Y down. CanvasToPdfPoint / PdfRectToCanvasRect below convert between them.
        // ============================================================

        // THREADING: raw externs suffixed Raw; only the wrappers below may be called. Every wrapper
        // holds PdfiumLock (FileOperations.cs) - the same lock Docnet's renders use - because a
        // UI-thread text pass racing a background render inside single-threaded PDFium corrupts the
        // native heap (0xc0000374). Same rule the link externs follow.
        [DllImport("pdfium.dll", EntryPoint = "FPDFText_LoadPage", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr FPDFText_LoadPageRaw(IntPtr page);
        private static IntPtr FPDFText_LoadPage(IntPtr page)
        { lock (PdfiumLock) return FPDFText_LoadPageRaw(page); }

        [DllImport("pdfium.dll", EntryPoint = "FPDFText_ClosePage", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FPDFText_ClosePageRaw(IntPtr textPage);
        private static void FPDFText_ClosePage(IntPtr textPage)
        { lock (PdfiumLock) FPDFText_ClosePageRaw(textPage); }

        [DllImport("pdfium.dll", EntryPoint = "FPDFText_CountChars", CallingConvention = CallingConvention.Cdecl)]
        private static extern int FPDFText_CountCharsRaw(IntPtr textPage);
        private static int FPDFText_CountChars(IntPtr textPage)
        { lock (PdfiumLock) return FPDFText_CountCharsRaw(textPage); }

        [DllImport("pdfium.dll", EntryPoint = "FPDFText_GetCharIndexAtPos", CallingConvention = CallingConvention.Cdecl)]
        private static extern int FPDFText_GetCharIndexAtPosRaw(IntPtr textPage, double x, double y, double xTolerance, double yTolerance);
        private static int FPDFText_GetCharIndexAtPos(IntPtr textPage, double x, double y, double xTolerance, double yTolerance)
        { lock (PdfiumLock) return FPDFText_GetCharIndexAtPosRaw(textPage, x, y, xTolerance, yTolerance); }

        [DllImport("pdfium.dll", EntryPoint = "FPDFText_GetUnicode", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint FPDFText_GetUnicodeRaw(IntPtr textPage, int index);
        private static uint FPDFText_GetUnicode(IntPtr textPage, int index)
        { lock (PdfiumLock) return FPDFText_GetUnicodeRaw(textPage, index); }

        [DllImport("pdfium.dll", EntryPoint = "FPDFText_CountRects", CallingConvention = CallingConvention.Cdecl)]
        private static extern int FPDFText_CountRectsRaw(IntPtr textPage, int startIndex, int count);
        private static int FPDFText_CountRects(IntPtr textPage, int startIndex, int count)
        { lock (PdfiumLock) return FPDFText_CountRectsRaw(textPage, startIndex, count); }

        [DllImport("pdfium.dll", EntryPoint = "FPDFText_GetRect", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool FPDFText_GetRectRaw(IntPtr textPage, int rectIndex, out double left, out double top, out double right, out double bottom);
        private static bool FPDFText_GetRect(IntPtr textPage, int rectIndex, out double left, out double top, out double right, out double bottom)
        { lock (PdfiumLock) return FPDFText_GetRectRaw(textPage, rectIndex, out left, out top, out right, out bottom); }

        [DllImport("pdfium.dll", EntryPoint = "FPDFText_GetText", CallingConvention = CallingConvention.Cdecl)]
        private static extern int FPDFText_GetTextRaw(IntPtr textPage, int startIndex, int count, [Out] ushort[] result);
        private static int FPDFText_GetText(IntPtr textPage, int startIndex, int count, [Out] ushort[] result)
        { lock (PdfiumLock) return FPDFText_GetTextRaw(textPage, startIndex, count, result); }

        // Cached text pages, keyed by page index. A cross-page selection needs the anchor page, the focus
        // page and any intermediates live at once, so this is a small multi-entry cache (a single-entry
        // cache would thrash open/close on every mouse-move of a cross-page drag). All handles are children
        // of _linkPdfiumDoc, so CloseLinkPdfiumDoc() calls CloseTextPage(). UI thread only.
        private readonly Dictionary<int, (IntPtr page, IntPtr textPage)> _textPages = [];
        private const int MaxCachedTextPages = 16;

        /// <summary>
        /// Loads (and caches) the PDFium text page for pageIndex, reusing the shared document handle.
        /// Returns IntPtr.Zero if unavailable. The cache resets when the working file changes.
        /// </summary>
        private IntPtr EnsureTextPage(int pageIndex)
        {
            if (_textPagesFile != _currentFile) CloseTextPage();
            if (_textPages.TryGetValue(pageIndex, out var cached)) return cached.textPage;

            IntPtr doc = EnsureLinkPdfiumDoc();
            if (doc == IntPtr.Zero) return IntPtr.Zero;

            // Bound the cache: a very long cross-page drag touches many pages for its copy text, but only
            // the pages of the CURRENT selection span need to stay warm. Drop everything outside it; if the
            // span itself is wider than the cap (a 50-page drag), also drop its interior pages - only the
            // endpoints are hit on every mouse-move, interiors reload on demand for copy/highlight.
            if (_textPages.Count >= MaxCachedTextPages)
            {
                foreach (var idx in _textPages.Keys.Where(i => i < _selStart.page || i > _selEnd.page).ToList())
                    CloseCachedTextPage(idx);
                if (_textPages.Count >= MaxCachedTextPages)
                    foreach (var idx in _textPages.Keys
                                 .Where(i => i != _selStart.page && i != _selEnd.page && i != pageIndex).ToList())
                        CloseCachedTextPage(idx);
            }

            IntPtr page = FPDF_LoadPage(doc, pageIndex);
            if (page == IntPtr.Zero) return IntPtr.Zero;
            IntPtr textPage = FPDFText_LoadPage(page);
            if (textPage == IntPtr.Zero) { FPDF_ClosePage(page); return IntPtr.Zero; }

            _textPages[pageIndex] = (page, textPage);
            _textPagesFile = _currentFile;
            return textPage;
        }
        private string? _textPagesFile;   // the working file the cache was loaded from

        // Closes one cached entry's text page + backing page handle.
        private void CloseCachedTextPage(int pageIndex)
        {
            if (!_textPages.TryGetValue(pageIndex, out var h)) return;
            try { FPDFText_ClosePage(h.textPage); } catch { }
            try { FPDF_ClosePage(h.page); } catch { }
            _textPages.Remove(pageIndex);
        }

        /// <summary>Closes every cached text page + backing page handle. Called on doc change and from
        /// CloseLinkPdfiumDoc() (the text pages are children of the doc they were loaded from).</summary>
        private void CloseTextPage()
        {
            foreach (var idx in _textPages.Keys.ToList()) CloseCachedTextPage(idx);
            _textPagesFile = null;
        }

        // --- coordinate conversion (canvas render-dim space <-> PDF points) ---

        // PDF page size in points for a cached text page, or A4 fallback.
        private (double w, double h) TextPagePdfSize(int pageIndex)
        {
            if (!_textPages.TryGetValue(pageIndex, out var h) || h.page == IntPtr.Zero) return (595.28, 841.89);
            double w = FPDF_GetPageWidth(h.page);
            double hh = FPDF_GetPageHeight(h.page);
            if (w <= 0) w = 595.28;
            if (hh <= 0) hh = 841.89;
            return (w, hh);
        }

        // Canvas point (render-dim units) -> PDF point (points, origin bottom-left).
        private bool CanvasToPdfPoint(int pageIndex, Point canvas, out double pdfX, out double pdfY)
        {
            pdfX = pdfY = 0;
            if (!_renderDims.TryGetValue(pageIndex, out var rd) || rd.w <= 0 || rd.h <= 0) return false;
            var (pw, ph) = TextPagePdfSize(pageIndex);
            pdfX = canvas.X * (pw / rd.w);
            pdfY = ph - canvas.Y * (ph / rd.h);   // flip Y
            return true;
        }

        // PDF rect (left/top/right/bottom in points, Y up) -> canvas Rect (render-dim units, Y down).
        private Rect PdfRectToCanvasRect(int pageIndex, double left, double top, double right, double bottom)
        {
            if (!_renderDims.TryGetValue(pageIndex, out var rd)) return Rect.Empty;
            var (pw, ph) = TextPagePdfSize(pageIndex);
            double x = left  / pw * rd.w;
            double y = (ph - top) / ph * rd.h;
            double w = (right - left) / pw * rd.w;
            double h = (top - bottom) / ph * rd.h;
            return new Rect(x, y, Math.Max(0, w), Math.Max(0, h));
        }

        // --- query helpers (operate on the cached text page for pageIndex) ---

        // Char index under a canvas point, or -1. Tolerance is a few PDF points so a near-miss still hits.
        private int TextCharAtCanvasPoint(int pageIndex, Point canvas, double tolPts = 4.0)
        {
            IntPtr tp = EnsureTextPage(pageIndex);
            if (tp == IntPtr.Zero) return -1;
            if (!CanvasToPdfPoint(pageIndex, canvas, out double px, out double py)) return -1;
            int idx = FPDFText_GetCharIndexAtPos(tp, px, py, tolPts, tolPts);
            return idx < 0 ? -1 : idx;
        }

        // Expands a char index to the word around it as a [start, endExclusive) range, using whitespace
        // as the boundary. Returns (start, count); count 0 if the index isn't on a word char.
        private (int start, int count) TextWordRangeAt(int pageIndex, int charIndex)
        {
            IntPtr tp = EnsureTextPage(pageIndex);
            if (tp == IntPtr.Zero || charIndex < 0) return (charIndex, 0);
            int total = FPDFText_CountChars(tp);
            if (charIndex >= total) return (charIndex, 0);

            static bool IsWordChar(uint u) => u != 0 && !char.IsWhiteSpace((char)u);
            if (!IsWordChar(FPDFText_GetUnicode(tp, charIndex))) return (charIndex, 0);

            int start = charIndex;
            while (start > 0 && IsWordChar(FPDFText_GetUnicode(tp, start - 1))) start--;
            int end = charIndex;
            while (end + 1 < total && IsWordChar(FPDFText_GetUnicode(tp, end + 1))) end++;
            return (start, end - start + 1);
        }

        // Selection rectangles (canvas render-dim space) for a char range - one per visual line run,
        // with a little vertical breathing room like a real highlighter stroke: the pen overshoots the
        // glyphs above and below, never sideways. Proportional so it scales with the text size, small
        // enough that consecutive lines don't visibly double up where translucent fills overlap. Clamped
        // to the page. Both consumers (drag-selection rects, committed highlight annotations) want it.
        private List<Rect> TextRangeRects(int pageIndex, int start, int count)
        {
            var rects = new List<Rect>();
            IntPtr tp = EnsureTextPage(pageIndex);
            if (tp == IntPtr.Zero || count <= 0) return rects;
            _renderDims.TryGetValue(pageIndex, out var rd);
            int n = FPDFText_CountRects(tp, start, count);
            for (int i = 0; i < n; i++)
                if (FPDFText_GetRect(tp, i, out double l, out double t, out double r, out double b))
                {
                    var rect = PdfRectToCanvasRect(pageIndex, l, t, r, b);
                    if (rect.Width < 0.5 || rect.Height < 0.5) continue;
                    double pad = rect.Height * 0.14;   // ponytail: tuned by eye; make it a named const if it grows more callers
                    double top = Math.Max(0, rect.Y - pad);
                    double bottom = rd.h > 0 ? Math.Min(rd.h, rect.Y + rect.Height + pad) : rect.Y + rect.Height + pad;
                    rects.Add(new Rect(rect.X, top, rect.Width, bottom - top));
                }
            return rects;
        }

        // Unicode text for a char range.
        private string TextRangeString(int pageIndex, int start, int count)
        {
            IntPtr tp = EnsureTextPage(pageIndex);
            if (tp == IntPtr.Zero || count <= 0) return string.Empty;
            var buf = new ushort[count + 1];                 // +1 for PDFium's null terminator
            int written = FPDFText_GetText(tp, start, count, buf);
            if (written <= 1) return string.Empty;
            var chars = new char[written - 1];               // drop the terminator
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)buf[i];
            return new string(chars);
        }

        // --- text selection state, word + flowing drag selection, and highlight rendering ---
        //
        // The selection is a document-order SPAN from (_selStart.page, _selStart.ch) to (_selEnd.page,
        // _selEnd.ch), both inclusive. A single-page selection is the special case where both pages match;
        // a cross-page drag spans them: the start page's tail + every intermediate page in full + the end
        // page's head. The drag anchor keeps its own (page, char) so the selection can extend in either
        // direction (and back) across pages.

        private readonly List<Rectangle> _textSelRects = [];   // currently rendered highlight rects (all pages)
        private (int page, int ch) _selStart = (-1, -1);       // span start, document order, inclusive
        private (int page, int ch) _selEnd   = (-1, -1);       // span end, document order, inclusive
        private bool _textDragging;                            // a flowing text drag is in progress
        private (int page, int ch) _textAnchor = (-1, -1);     // where the flowing drag started
        // Where the drag began, in SCROLLER (screen-ish) space, for the click-vs-drag test. Canvas units
        // are render-dim scaled (a 4-canvas-unit threshold is ~1 physical px zoomed out, ~10 zoomed in),
        // so the threshold must be measured in a space that tracks the physical gesture.
        private Point _textDragStartScreen;
        private System.Windows.Threading.DispatcherTimer? _textScrollTimer;   // edge auto-scroll during a drag
        private double _textScrollVelocity;                    // px per tick, signed (up = negative)

        private bool HasTextSelection => _selStart.page >= 0;

        // Per-page char sub-range of the current span: the start page gets its tail, the end page its head,
        // intermediates the whole page, and a single-page span just its own range. count 0 = page not in span.
        private (int start, int count) PageSubRange(int pageIndex)
        {
            if (!HasTextSelection || pageIndex < _selStart.page || pageIndex > _selEnd.page) return (0, 0);
            int from = pageIndex == _selStart.page ? _selStart.ch : 0;
            int to;                                            // inclusive
            if (pageIndex == _selEnd.page) to = _selEnd.ch;
            else
            {
                IntPtr tp = EnsureTextPage(pageIndex);
                if (tp == IntPtr.Zero) return (0, 0);
                to = FPDFText_CountChars(tp) - 1;
            }
            return to < from ? (0, 0) : (from, to - from + 1);
        }

        // Full text of the current span: per-page runs joined with newlines at page breaks.
        private string TextSpanString()
        {
            if (!HasTextSelection) return string.Empty;
            if (_selStart.page == _selEnd.page)
            {
                var (s, c) = PageSubRange(_selStart.page);
                return TextRangeString(_selStart.page, s, c);
            }
            var sb = new System.Text.StringBuilder();
            for (int p = _selStart.page; p <= _selEnd.page; p++)
            {
                var (s, c) = PageSubRange(p);
                if (c <= 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(TextRangeString(p, s, c));
            }
            return sb.ToString();
        }


        /// <summary>
        /// Double-click entry: selects the word under a canvas point on pageIndex. Returns false if there's
        /// no selectable PDF text there (the caller then falls back to its existing double-click behaviour).
        /// Highlights the word and copies it (the app's convention that a selection copies), and leaves it in
        /// _selectedText so Ctrl+C works too.
        /// </summary>
        private bool SelectWordAt(int pageIndex, Point canvasPos)
        {
            int ch = TextCharAtCanvasPoint(pageIndex, canvasPos);
            if (ch < 0) return false;
            var (start, count) = TextWordRangeAt(pageIndex, ch);
            if (count <= 0) return false;

            RenderTextSelection(pageIndex, start, count);
            var text = TextRangeString(pageIndex, start, count);
            _selectedText = text;
            if (!string.IsNullOrEmpty(text))
            {
                TrySetClipboard(text);
                SetStatus($"Copied: {text}");
            }
            return true;
        }

        // Records a single-page selection range, then (re)draws its highlight (double-click word, drag seed).
        private void RenderTextSelection(int pageIndex, int start, int count)
        {
            _selStart = (pageIndex, start);
            _selEnd   = (pageIndex, start + count - 1);
            DrawTextSelectionHighlight();
        }

        // Records the span between the drag anchor and a focus point (either direction, any page), then
        // (re)draws it. Normalises to document order: (page, char) tuple order decides which end is first.
        private void RenderTextSpan((int page, int ch) focus)
        {
            bool anchorFirst = _textAnchor.page < focus.page
                            || (_textAnchor.page == focus.page && _textAnchor.ch <= focus.ch);
            _selStart = anchorFirst ? _textAnchor : focus;
            _selEnd   = anchorFirst ? focus : _textAnchor;
            DrawTextSelectionHighlight();
        }

        // Draws one translucent rect per visual line run, per span page that has a live tile. Targets each
        // page's OWN canvas (VisibleCanvasForPage), not _activeCanvas - which async tile renders can re-point
        // to a neighbour page mid-gesture. Pages without a live tile (evicted / off-screen in continuous
        // view) are skipped and repainted by RepaintTextSelection when their tile streams back in.
        private void DrawTextSelectionHighlight()
        {
            RemoveTextSelectionRects();
            if (!HasTextSelection) return;
            var fill = AccentBrush(70);
            for (int p = _selStart.page; p <= _selEnd.page; p++)
            {
                // Live tiles only: CanvasForPage's primary-canvas fallback would draw this page's rects
                // onto whatever page the primary shows. The seed-draw at drag start always has a live
                // tile (it was just clicked), so nothing is lost by the null-return here.
                var canvas = VisibleCanvasForPage(p);
                if (canvas is null) continue;
                var (s, c) = PageSubRange(p);
                if (c <= 0) continue;
                foreach (var r in TextRangeRects(p, s, c))
                {
                    var rect = new Rectangle { Width = r.Width, Height = r.Height, Fill = fill, IsHitTestVisible = false };
                    Canvas.SetLeft(rect, r.X);
                    Canvas.SetTop(rect, r.Y);
                    canvas.Children.Add(rect);
                    _textSelRects.Add(rect);
                }
            }
        }

        // Re-draws the selection highlight after a page repaint. RenderAllAnnotations clears the page canvas's
        // children (which would wipe the highlight while _selectedText stayed live), so it calls this at the
        // end. No-op unless the repainted page is inside the current span.
        private void RepaintTextSelection(int pageIndex)
        {
            if (HasTextSelection && pageIndex >= _selStart.page && pageIndex <= _selEnd.page)
                DrawTextSelectionHighlight();
        }

        // Removes just the rendered rects (keeps the selection range so it can be redrawn).
        private void RemoveTextSelectionRects()
        {
            foreach (var r in _textSelRects)
                (r.Parent as Canvas)?.Children.Remove(r);
            _textSelRects.Clear();
        }

        // Clears the selection entirely: drops the rects AND forgets the range. Called from ClearTextSelection
        // (single click / tool switch / starting a new selection).
        private void ClearTextSelectionHighlight()
        {
            RemoveTextSelectionRects();
            _selStart = (-1, -1); _selEnd = (-1, -1);
        }

        // Cancels an in-progress flowing drag and releases the mouse capture. Safe no-op if none is active.
        // Called from ClearTextSelection (tool switch) and FinishStuckGesture (lost mouse-up / deactivation).
        private void CancelTextDrag()
        {
            StopTextAutoScroll();
            if (!_textDragging) return;
            _textDragging = false;
            (_gestureCanvas ?? _activeCanvas)?.ReleaseMouseCapture();
        }

        // Mouse-down: begin a flowing text selection if the point is on the page's text. Returns false when
        // there's no char there (the caller then falls back to the box marquee). Anchors at that char.
        private bool TextBeginDrag(int pageIndex, Point canvasPos)
        {
            int ch = TextCharAtCanvasPoint(pageIndex, canvasPos, 6.0);
            if (ch < 0) return false;
            _textDragging    = true;
            _textAnchor      = (pageIndex, ch);
            _textDragStartScreen = System.Windows.Input.Mouse.GetPosition(PagePreviewPanel);
            RenderTextSelection(pageIndex, ch, 1);         // seed the highlight with the anchor char
            _selectedText = TextRangeString(pageIndex, ch, 1);
            return true;
        }

        // Mouse-move (and auto-scroll ticks): extend the selection from the anchor to the char under the
        // pointer - on WHICHEVER page the pointer is over. The mouse is captured by the anchor page's
        // canvas, so event coordinates are useless for other pages; instead the pointer is resolved
        // directly against every live page tile (Mouse.GetPosition per tile).
        private void TextExtendDrag()
        {
            if (!_textDragging) return;
            var hit = TextFocusUnderPointer();
            if (hit.page >= 0)
            {
                RenderTextSpan(hit);
                _selectedText = TextSpanString();
            }
            UpdateTextAutoScroll();
        }

        // Resolves the pointer to (page, char) across every live page tile. Returns page -1 when the
        // pointer is over no tile, or over a tile but not near text (both keep the current selection,
        // matching the single-page behaviour for margins and gaps).
        private (int page, int ch) TextFocusUnderPointer()
        {
            foreach (var kv in _pages)
            {
                int pageIdx = kv.Key; var canvas = kv.Value;
                if (canvas.ActualWidth <= 0) continue;
                var pos = System.Windows.Input.Mouse.GetPosition(canvas);
                if (pos.X < 0 || pos.Y < 0 || pos.X > canvas.ActualWidth || pos.Y > canvas.ActualHeight) continue;
                int ch = TextCharAtCanvasPoint(pageIdx, pos, 10.0);
                return ch >= 0 ? (pageIdx, ch) : (-1, -1);
            }
            return (-1, -1);
        }

        // --- edge auto-scroll: dragging a selection to the viewport's top/bottom edge scrolls the view ---

        private void UpdateTextAutoScroll()
        {
            const double zone = 28;    // px from the viewport edge that starts scrolling
            var pos = System.Windows.Input.Mouse.GetPosition(PagePreviewPanel);
            double h = PagePreviewPanel.ViewportHeight;
            if (h <= 0) { StopTextAutoScroll(); return; }

            if (pos.Y < zone)          _textScrollVelocity = -Math.Min(40, (zone - pos.Y) * 1.2);
            else if (pos.Y > h - zone) _textScrollVelocity =  Math.Min(40, (pos.Y - (h - zone)) * 1.2);
            else { StopTextAutoScroll(); return; }

            if (_textScrollTimer is null)
            {
                _textScrollTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(30) };
                _textScrollTimer.Tick += (_, _) =>
                {
                    if (!_textDragging) { StopTextAutoScroll(); return; }
                    PagePreviewPanel.ScrollToVerticalOffset(PagePreviewPanel.VerticalOffset + _textScrollVelocity);
                    TextExtendDrag();   // the pointer sits still while content scrolls under it - re-resolve
                };
            }
            _textScrollTimer.Start();
        }

        private void StopTextAutoScroll()
        {
            _textScrollTimer?.Stop();
            _textScrollVelocity = 0;
        }

        // Mouse-up: finish the flowing selection - copy it (the app's selection-copies convention) and leave
        // it in _selectedText so Ctrl+C works. The highlight stays until the next click / tool switch.
        private void TextEndDrag()
        {
            _textDragging = false;
            StopTextAutoScroll();
            var text = _selectedText;
            if (text is null || text.Length == 0) return;
            TrySetClipboard(text);
            int n = text.Length;
            SetStatus($"Copied {n} character{(n == 1 ? "" : "s")}");
        }

        // Highlight-tool release over text: lay a Fill highlight annotation over each selected text-run rect
        // (one per visual line) in the current highlight colour, grouped as a single undo - across every page
        // the span touches. Then clears the transient blue selection highlight so only the yellow highlight
        // annotations remain. Undo removes each annotation from its OWN page (see the AnnotationGroup case).
        private void CommitTextHighlight()
        {
            if (!HasTextSelection) { ClearTextSelection(); return; }
            int firstPage = _selStart.page, lastPage = _selEnd.page;

            var group = new List<PageAnnotation>();
            for (int p = firstPage; p <= lastPage; p++)
            {
                var (s, c) = PageSubRange(p);
                if (c <= 0) continue;
                if (!_annotations.TryGetValue(p, out var list)) { list = []; _annotations[p] = list; }
                foreach (var r in TextRangeRects(p, s, c))
                {
                    if (r.Width < 1 || r.Height < 1) continue;
                    var ha = new HighlightAnnotation { PageIndex = p, Bounds = r, Style = HighlightStyle.Fill };
                    ha.SetColor(_highlightColor);
                    list.Add(ha);
                    group.Add(ha);
                }
            }

            bool wasDirty = _isDirty;
            ClearTextSelection();   // drop the transient selection highlight + reset range/drag
            if (group.Count == 0) return;
            PushUndo(new UndoEntry(UndoKind.AnnotationGroup, firstPage, WasDirty: wasDirty, AnnotGroup: group));
            MarkDirty();
            // Only repaint pages with a live tile: RenderAllAnnotations falls back to the PRIMARY canvas
            // for an unbuilt page and would stomp the visible page (same guard as the StampBatch undo).
            // Pages without a tile pick their new highlights up when their tile streams in.
            for (int p = firstPage; p <= lastPage; p++)
                if (_pages.ContainsKey(p)) RenderAllAnnotations(p);
            SetStatus($"Highlighted {group.Count} line{(group.Count == 1 ? "" : "s")}");
        }
    }
}
