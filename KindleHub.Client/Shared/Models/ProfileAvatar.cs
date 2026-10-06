using System;
using Avalonia;
using Avalonia.Media;

namespace KindleHub.Client.Models;

/// <summary>Codec and renderer for the official client's KHAV1/KHAV2 avatar codes.</summary>
public static class ProfileAvatar
{
    public const int Side = 22;
    public const int CellCount = Side * Side;
    public static readonly string[] BackgroundColors =
        { "#ffffff", "#111827", "#2563eb", "#dc2626", "#16a34a", "#f59e0b", "#7c3aed", "#0891b2", "#db2777", "#0f766e", "#eab308", "#e5e7eb" };
    public static readonly string[] InkColors =
        { "", "#111111", "#ffffff", "#6b7280", "#dc2626", "#f59e0b", "#eab308", "#16a34a", "#0891b2", "#2563eb", "#7c3aed", "#db2777", "#92400e", "#f5c9a6", "#0f766e", "#d1d5db" };
    public static readonly string[] InkNames =
        { "Erase", "Black", "White", "Grey", "Red", "Orange", "Yellow", "Green", "Teal", "Blue", "Purple", "Pink", "Brown", "Skin", "Dark green", "Light grey" };
    public static readonly string[] BackgroundNames =
        { "White", "Ink", "Blue", "Red", "Green", "Orange", "Purple", "Teal", "Pink", "Dark teal", "Yellow", "Light grey" };

    public static bool TryDecode(string? code, out int background, out int[] cells)
    {
        background = 0;
        cells = new int[CellCount];
        if (string.IsNullOrWhiteSpace(code)) return false;
        var version2 = code.StartsWith("KHAV2:", StringComparison.Ordinal);
        var version1 = code.StartsWith("KHAV1:", StringComparison.Ordinal);
        if (!version1 && !version2) return false;
        var payload = code.Substring(6);
        var dot = payload.IndexOf('.');
        if (dot < 1 || !int.TryParse(payload[..dot], out background) || background < 0 || background >= BackgroundColors.Length)
            return false;
        var packed = payload[(dot + 1)..];
        if (version2)
        {
            try
            {
                var bytes = Convert.FromBase64String(packed);
                if (bytes.Length != CellCount / 2) return false;
                for (var i = 0; i < CellCount; i++) cells[i] = (i & 1) == 0 ? bytes[i / 2] >> 4 : bytes[i / 2] & 15;
                return true;
            }
            catch (FormatException) { return false; }
        }

        if (packed.Length != (CellCount + 3) / 4) return false;
        for (var i = 0; i < packed.Length; i++)
        {
            if (!int.TryParse(packed[i].ToString(), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var nibble)) return false;
            for (var bit = 0; bit < 4 && i * 4 + bit < CellCount; bit++) cells[i * 4 + bit] = (nibble >> bit) & 1;
        }
        return true;
    }

    public static string Encode(int background, int[] cells)
    {
        background = Math.Clamp(background, 0, BackgroundColors.Length - 1);
        if (cells is null || cells.Length != CellCount) throw new ArgumentException("Avatar grid must be 22 by 22.", nameof(cells));
        var bytes = new byte[CellCount / 2];
        for (var i = 0; i < CellCount; i++)
        {
            var ink = Math.Clamp(cells[i], 0, InkColors.Length - 1);
            if ((i & 1) == 0) bytes[i / 2] = (byte)(ink << 4);
            else bytes[i / 2] |= (byte)ink;
        }
        return $"KHAV2:{background}.{Convert.ToBase64String(bytes)}";
    }

    public static DrawingImage? Render(string? code)
    {
        if (!TryDecode(code, out var background, out var cells)) return null;
        var group = new DrawingGroup();
        group.Children.Add(Rect(BackgroundColors[background], 0, 0, Side, Side));
        var isV1 = code!.StartsWith("KHAV1:", StringComparison.Ordinal);
        var ink = BackgroundColors[background];
        var darkBackground = Luminance(ink) < 140;
        var legacyInk = darkBackground ? "#ffffff" : "#111111";
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            if (cell == 0) continue;
            var color = isV1 ? legacyInk : InkColors[cell];
            group.Children.Add(Rect(color, i % Side, i / Side, 1, 1));
        }
        return new DrawingImage(group);
    }

    private static GeometryDrawing Rect(string color, double x, double y, double width, double height) =>
        new() { Brush = new SolidColorBrush(Color.Parse(color)), Geometry = new RectangleGeometry(new Rect(x, y, width, height)) };

    private static double Luminance(string hex)
    {
        var color = Color.Parse(hex);
        return color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
    }
}
