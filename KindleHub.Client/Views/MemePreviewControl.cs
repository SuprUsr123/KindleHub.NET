using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client.Views;

/// <summary>Vector rendition of the official client's ten 480×480 meme scenes.</summary>
public sealed class MemePreviewControl : Control
{
    private const double CanvasSize = 480;
    private static readonly Pen Ink = new(Brushes.Black, 5);
    private static readonly Pen FineInk = new(Brushes.Black, 3);
    private static readonly Pen SelectionPen = new(Brushes.DodgerBlue, 2);
    private CommunityViewModel.MemeCaptionSlot? _dragCaption;
    private bool _resizingCaption;
    private Point _pointerStart;
    private double _startLeft;
    private double _startTop;
    private double _startWidth;
    private double _startHeight;
    private CommunityViewModel? _viewModel;

    public MemePreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;
            _viewModel.MemeCaptions.CollectionChanged -= CaptionsChanged;
        }

        _viewModel = DataContext as CommunityViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelPropertyChanged;
            _viewModel.MemeCaptions.CollectionChanged += CaptionsChanged;
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.White, null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        if (_viewModel is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var scaleX = Bounds.Width / CanvasSize;
        var scaleY = Bounds.Height / CanvasSize;
        using (context.PushTransform(Matrix.CreateScale(scaleX, scaleY)))
        {
            if (_viewModel.MemePicture is { } picture)
                DrawPicture(context, picture);
            else
                DrawScene(context, _viewModel.SelectedMemeScene.Id);
            foreach (var caption in _viewModel.MemeCaptions)
                DrawCaption(context, caption);
            if (_viewModel.SelectedMemeCaption is { } selected && _viewModel.MemeCaptions.Contains(selected))
                DrawCaptionSelection(context, selected);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_viewModel is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var point = ToCanvasPoint(e.GetPosition(this));
        foreach (var caption in _viewModel.MemeCaptions.Reverse())
        {
            var rect = new Rect(caption.Left, caption.Top, caption.Width, caption.Height);
            if (!rect.Contains(point)) continue;

            _viewModel.SelectedMemeCaption = caption;
            _dragCaption = caption;
            _resizingCaption = point.X >= rect.Right - 18 && point.Y >= rect.Bottom - 18;
            _pointerStart = point;
            _startLeft = caption.Left;
            _startTop = caption.Top;
            _startWidth = caption.Width;
            _startHeight = caption.Height;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        _viewModel.SelectedMemeCaption = null;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragCaption is not { } caption) return;
        var point = ToCanvasPoint(e.GetPosition(this));
        var delta = point - _pointerStart;
        if (_resizingCaption)
            caption.ResizeTo(_startWidth + delta.X, _startHeight + delta.Y);
        else
            caption.MoveTo(_startLeft + delta.X, _startTop + delta.Y);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragCaption is null) return;
        _dragCaption = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private Point ToCanvasPoint(Point point) => new(point.X * CanvasSize / Bounds.Width, point.Y * CanvasSize / Bounds.Height);

    private static void DrawCaptionSelection(DrawingContext dc, CommunityViewModel.MemeCaptionSlot caption)
    {
        var rect = new Rect(caption.Left, caption.Top, caption.Width, caption.Height);
        dc.DrawRectangle(null, SelectionPen, rect);
        dc.DrawRectangle(Brushes.DodgerBlue, new Pen(Brushes.White, 1),
            new Rect(rect.Right - 8, rect.Bottom - 8, 12, 12));
    }

    private static void DrawPicture(DrawingContext dc, Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var scale = Math.Min(CanvasSize / width, CanvasSize / height);
        var drawWidth = width * scale;
        var drawHeight = height * scale;
        var destination = new Rect((CanvasSize - drawWidth) / 2, (CanvasSize - drawHeight) / 2, drawWidth, drawHeight);
        dc.DrawImage(bitmap, new Rect(0, 0, width, height), destination);
    }

    private static void DrawScene(DrawingContext dc, string id)
    {
        switch (id)
        {
            case "two": DrawTwoDoors(dc); break;
            case "brain": DrawBrain(dc); break;
            case "fine": DrawFine(dc); break;
            case "point": DrawPointing(dc); break;
            case "drake": DrawDrake(dc); break;
            case "brain4": DrawFourBrains(dc); break;
            case "two_btn": DrawTwoButtons(dc); break;
            case "panik": DrawPanikKalm(dc); break;
            case "sign": DrawSign(dc); break;
        }
    }

    private static void DrawTwoDoors(DrawingContext dc)
    {
        Rect(dc, 60, 150, 140, 230, Ink);
        Rect(dc, 280, 150, 140, 230, Ink);
        Ellipse(dc, 180, 270, 9, 9, Brushes.Black);
        Ellipse(dc, 300, 270, 9, 9, Brushes.Black);
        Ellipse(dc, 240, 300, 20, 20, Ink);
        Line(dc, 240, 320, 240, 380);
        Line(dc, 240, 335, 210, 360);
        Line(dc, 240, 335, 270, 360);
        Line(dc, 240, 380, 216, 415);
        Line(dc, 240, 380, 264, 415);
    }

    private static void DrawBrain(DrawingContext dc)
    {
        Ellipse(dc, 240, 250, 110, 110, Ink);
        for (var i = 0; i < 5; i++)
            Curve(dc, new Point(150, 190 + i * 30), new Point(200, 170 + i * 30),
                new Point(280, 215 + i * 30), new Point(330, 190 + i * 30), FineInk);
        Line(dc, 240, 140, 240, 360, Ink);
    }

    private static void DrawFine(DrawingContext dc)
    {
        Line(dc, 80, 340, 400, 340, Ink);
        Line(dc, 110, 340, 110, 420, Ink);
        Line(dc, 370, 340, 370, 420, Ink);
        Rect(dc, 210, 300, 60, 40, Ink);
        Curve(dc, new Point(280, 320), new Point(300, 300), new Point(300, 340), new Point(280, 340), FineInk);
        for (var i = 0; i < 6; i++)
        {
            var x = 90 + i * 62;
            Curve(dc, new Point(x, 420), new Point(x - 18, 380), new Point(x + 20, 370), new Point(x, 330), FineInk);
            Curve(dc, new Point(x, 330), new Point(x + 22, 368), new Point(x + 18, 392), new Point(x, 420), FineInk);
        }
    }

    private static void DrawPointing(DrawingContext dc)
    {
        Ellipse(dc, 170, 230, 40, 40, Ink);
        Line(dc, 170, 270, 170, 370);
        Line(dc, 170, 295, 330, 250);
        Line(dc, 170, 295, 130, 350);
        Line(dc, 170, 370, 140, 430);
        Line(dc, 170, 370, 205, 430);
        Line(dc, 330, 250, 360, 240, FineInk);
    }

    private static void DrawDrake(DrawingContext dc)
    {
        Line(dc, 0, 240, 480, 240);
        Line(dc, 70, 80, 150, 160, new Pen(Brushes.Black, 6));
        Line(dc, 150, 80, 70, 160, new Pen(Brushes.Black, 6));
        Line(dc, 70, 340, 105, 378, new Pen(Brushes.Black, 6));
        Line(dc, 105, 378, 160, 310, new Pen(Brushes.Black, 6));
    }

    private static void DrawFourBrains(DrawingContext dc)
    {
        for (var i = 0; i < 4; i++)
        {
            var y = 62 + i * 120;
            var radius = 16 + i * 11;
            Line(dc, 10, i * 120 + 2, 470, i * 120 + 2, new Pen(Brushes.Black, 4));
            Ellipse(dc, 350, y, 44, 44, new Pen(Brushes.Black, 4));
            Ellipse(dc, 350, y - 6, radius, radius, new Pen(Brushes.Black, 3));
            for (var eye = 0; eye < i + 1; eye++)
                Ellipse(dc, 350 - radius * 0.5 + eye * (radius / Math.Max(1, i)), y - 6, Math.Max(4, radius * 0.45), Math.Max(4, radius * 0.45), null, FineInk);
        }
        Line(dc, 232, 4, 232, 476, new Pen(Brushes.Black, 4));
    }

    private static void DrawTwoButtons(DrawingContext dc)
    {
        Rect(dc, 36, 92, 178, 88, Ink);
        Rect(dc, 258, 92, 178, 88, Ink);
        Ellipse(dc, 240, 270, 34, 34, Ink);
        Line(dc, 240, 304, 240, 382);
        Line(dc, 240, 322, 180, 200);
        Line(dc, 240, 322, 300, 352);
        Ellipse(dc, 206, 252, 6, 6, null, FineInk);
        Ellipse(dc, 276, 258, 5, 5, null, FineInk);
    }

    private static void DrawPanikKalm(DrawingContext dc)
    {
        var moods = new[] { (Y: 8d, Mouth: 13d), (Y: 170d, Mouth: 0d), (Y: 332d, Mouth: 20d) };
        foreach (var mood in moods)
        {
            var y = mood.Y;
            Line(dc, 8, y - 2, 472, y - 2, new Pen(Brushes.Black, 4));
            Ellipse(dc, 370, y + 72, 52, 52, new Pen(Brushes.Black, 4));
            var eyes = mood.Mouth == 0 ? 4 : mood.Mouth < 16 ? 11 : 16;
            Ellipse(dc, 350, y + 58, eyes, eyes, new Pen(Brushes.Black, 3));
            Ellipse(dc, 392, y + 58, eyes, eyes, new Pen(Brushes.Black, 3));
            if (mood.Mouth == 0)
                Line(dc, 350, y + 96, 392, y + 96, new Pen(Brushes.Black, 3));
            else
                Ellipse(dc, 371, y + 92, mood.Mouth, Math.Max(3, mood.Mouth / 2), new Pen(Brushes.Black, 3));
        }
    }

    private static void DrawSign(DrawingContext dc)
    {
        Rect(dc, 50, 240, 380, 130, Ink);
        Line(dc, 50, 370, 30, 470);
        Line(dc, 430, 370, 450, 470);
        Ellipse(dc, 240, 168, 44, 44, Ink);
        Line(dc, 196, 206, 160, 240);
        Line(dc, 284, 206, 320, 240);
        Ellipse(dc, 224, 160, 5, 5, null, FineInk);
        Ellipse(dc, 258, 160, 5, 5, null, FineInk);
        Line(dc, 220, 190, 262, 190, FineInk);
    }

    private static void DrawCaption(DrawingContext dc, CommunityViewModel.MemeCaptionSlot caption)
    {
        if (string.IsNullOrWhiteSpace(caption.Text)) return;
        var typeface = new Typeface("Inter", FontStyle.Normal, FontWeight.Black, FontStretch.Normal);
        FormattedText? text = null;
        var maxWidth = Math.Max(1, caption.Width - 20);
        for (var fontSize = 48d; fontSize >= 18; fontSize -= 2)
        {
            text = new FormattedText(caption.Text.ToUpperInvariant(), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, fontSize, Brushes.White)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = maxWidth,
                MaxLineCount = 4,
                Trimming = TextTrimming.WordEllipsis,
                LineHeight = fontSize * 1.02
            };
            if (text.Height <= caption.Height - 14 && text.Width <= maxWidth) break;
        }
        if (text is null) return;
        var y = caption.At == "bottom" ? caption.Top + caption.Height - text.Height - 7
            : caption.At == "middle" ? caption.Top + (caption.Height - text.Height) / 2
            : caption.Top + 7;
        var glyphs = text.BuildGeometry(new Point(caption.Left, y));
        if (glyphs is not null)
            dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 3), glyphs);
    }

    private static void Rect(DrawingContext dc, double x, double y, double width, double height, Pen pen) =>
        dc.DrawRectangle(null, pen, new Rect(x, y, width, height));

    private static void Ellipse(DrawingContext dc, double x, double y, double radiusX, double radiusY, IBrush fill) =>
        dc.DrawEllipse(fill, null, new Point(x, y), radiusX, radiusY);

    private static void Ellipse(DrawingContext dc, double x, double y, double radiusX, double radiusY, Pen? outline, Pen? circlePen = null)
    {
        if (circlePen is not null) outline = circlePen;
        dc.DrawEllipse(null, outline, new Point(x, y), radiusX, radiusY);
    }

    private static void Line(DrawingContext dc, double x1, double y1, double x2, double y2, Pen? pen = null) =>
        dc.DrawLine(pen ?? Ink, new Point(x1, y1), new Point(x2, y2));

    private static void Curve(DrawingContext dc, Point start, Point control1, Point control2, Point end, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(start, false);
            path.CubicBezierTo(control1, control2, end, true);
            path.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CommunityViewModel.SelectedMemeScene) or nameof(CommunityViewModel.MemeCaptions) or
            nameof(CommunityViewModel.MemePicture) or nameof(CommunityViewModel.SelectedMemeCaption))
            InvalidateVisual();
    }

    private void CaptionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
