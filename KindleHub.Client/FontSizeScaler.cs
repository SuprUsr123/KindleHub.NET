using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace KindleHub.Client;

/// <summary>Scales text while preserving the layout's designed proportions.</summary>
internal static class FontSizeScaler
{
    private sealed class ControlSizes
    {
        public SizeState TextBlock { get; } = new();
        public SizeState Templated { get; } = new();
    }

    private sealed class SizeState
    {
        public double? Base { get; set; }
        public double Applied { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, ControlSizes> Sizes = new();
    private static readonly List<WeakReference<Window>> Windows = new();
    private static int _fontSizePx = 16;

    public static void Attach(Window window)
    {
        Windows.Add(new WeakReference<Window>(window));
        window.LayoutUpdated += (_, _) => Apply(window);
        Apply(window);
    }

    public static void SetFontSizePx(int fontSizePx)
    {
        _fontSizePx = fontSizePx is 13 or 16 or 19 or 22 ? fontSizePx : 16;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyAll);
            return;
        }
        ApplyAll();
    }

    private static void ApplyAll()
    {
        for (var i = Windows.Count - 1; i >= 0; i--)
        {
            if (Windows[i].TryGetTarget(out var window)) Apply(window);
            else Windows.RemoveAt(i);
        }
    }

    private static void Apply(Visual root)
    {
        var scale = _fontSizePx switch
        {
            13 => 0.92d,
            19 => 1.08d,
            22 => 1.16d,
            _ => 1d
        };
        Visit(root, scale);
    }

    private static void Visit(Visual visual, double scale)
    {
        if (visual is Control control)
        {
            var sizes = Sizes.GetOrCreateValue(control);
            // Template text inherits its size from the owning control. Scaling that
            // TextBlock again multiplies the size a second time, so only scale
            // TextBlocks that declare their own font-size value.
            if (control is TextBlock && control.IsSet(TextBlock.FontSizeProperty))
                Scale(control, TextBlock.FontSizeProperty, sizes.TextBlock, scale);
            if (control is TemplatedControl && control.IsSet(TemplatedControl.FontSizeProperty))
                Scale(control, TemplatedControl.FontSizeProperty, sizes.Templated, scale);
        }

        foreach (var child in visual.GetVisualChildren())
            Visit(child, scale);
    }

    private static void Scale(Control control, Avalonia.StyledProperty<double> property,
        SizeState state, double scale)
    {
        var current = control.GetValue(property);
        if (state.Base is null)
            state.Base = current;

        var target = state.Base.Value * scale;
        if (Math.Abs(current - target) > 0.001)
            control.SetCurrentValue(property, target);
        state.Applied = target;
    }
}
