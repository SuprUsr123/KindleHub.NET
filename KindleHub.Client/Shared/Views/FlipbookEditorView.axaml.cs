using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client.Views;

/// <summary>Standalone flipbook drawing surface. All state lives in
/// <see cref="FlipbookEditorViewModel"/>; this file only translates pointer
/// input into cell edits and repaints the bitmap / frame strip.</summary>
public partial class FlipbookEditorView : UserControl
{
    private FlipbookEditorViewModel? _vm;
    private bool _drawing;
    private int _lastCell = -1;

    public FlipbookEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as FlipbookEditorViewModel);
        Loaded += (_, _) => Attach(DataContext as FlipbookEditorViewModel);
        Attach(DataContext as FlipbookEditorViewModel);
    }

    private void Attach(FlipbookEditorViewModel? vm)
    {
        _vm = vm;
        if (vm == null) return;
        vm.View = this;
        RenderCanvas();
        RenderThumbnails();
    }

    // ── pointer input ────────────────────────────────────────────────────────
    private void CanvasHost_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = _vm;
        if (vm == null) return;
        var cell = vm.CellFromPoint(e.GetPosition(CanvasHost));
        if (cell < 0) return;
        vm.BeginStroke();
        _drawing = true;
        _lastCell = cell;
        vm.ApplyCell(cell, vm.IsErase ? 0 : 1);
        RenderCanvas();
        e.Pointer.Capture(CanvasHost);
    }

    private void CanvasHost_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_drawing) return;
        var vm = _vm;
        if (vm == null) return;
        var cell = vm.CellFromPoint(e.GetPosition(CanvasHost));
        if (cell < 0 || cell == _lastCell) return;
        _lastCell = cell;
        vm.ApplyCell(cell, vm.IsErase ? 0 : 1);
        RenderCanvas();
    }

    private void CanvasHost_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _drawing = false;
        _lastCell = -1;
        RenderCanvas();
        RenderThumbnails();
        try { e.Pointer.Capture(null); } catch { /* pointer already gone */ }
    }

    private void CanvasHost_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _drawing = false;
        _lastCell = -1;
    }

    // ── painting ─────────────────────────────────────────────────────────────
    internal void RenderCanvas()
    {
        var vm = _vm;
        if (vm == null) return;
        var side = vm.CanvasSide;
        CanvasHost.Width = side;
        CanvasHost.Height = side;
        CanvasImage.Width = side;
        CanvasImage.Height = side;
        CanvasImage.Source = vm.RenderCurrentFrame();
    }

    internal void RenderThumbnails()
    {
        var vm = _vm;
        if (vm == null) return;
        Thumbnails.Children.Clear();
        var current = vm.CurrentIndex;
        for (var i = 0; i < vm.FrameCount; i++)
        {
            var index = i;
            var cell = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(5),
                Background = Brushes.White,
                BorderThickness = new Thickness(index == current ? 2 : 1),
                BorderBrush = index == current
                    ? new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xED))
                    : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Child = new Image
                {
                    Width = 40,
                    Height = 40,
                    Stretch = Stretch.None,
                    Source = vm.RenderThumbnail(vm.GetFrame(index))
                }
            };
            cell.PointerPressed += (_, _) => vm.SelectFrame(index);
            Thumbnails.Children.Add(cell);
        }
    }

    internal void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }
}
