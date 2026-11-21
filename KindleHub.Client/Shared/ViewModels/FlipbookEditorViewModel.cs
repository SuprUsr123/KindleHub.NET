using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using KindleHub.Core;

namespace KindleHub.Client.ViewModels;

/// <summary>Backing state for the standalone flipbook drawing window. Mirrors the
/// official web client's Flipbook app: a square pixel grid, pen/erase, onion
/// skinning, frame reordering, fps playback and the KHFLIP1 wire export — but with
/// no frame cap, so a drawing can be as long as the author wants.</summary>
public class FlipbookEditorViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the author picks "Send to chat"; carries the KHFLIP1 wire.</summary>
    public Action<string>? SendFlipbook { get; set; }
    public Action<string>? SaveToCommunity { get; set; }
    public bool CanSaveToCommunity => SaveToCommunity != null;

    public Action? CloseWindow { get; set; }
    public Views.FlipbookEditorView? View { get; set; }

    private const int BasePixels = 280;
    private static readonly double[] ZoomLevels = { 1.0, 1.5, 2.0, 3.0 };
    private static readonly int[] GridLevels = { 10, 14, 28 };

    private readonly List<int[]> _frames = new();
    private int _currentIndex;
    private int _gridSize = 10;
    private int _zoomIndex;
    private int _fps = 6;
    private string _flipbookName = "";
    private bool _isErase;
    private int _onionIndex; // 0 = off, 1 = prev, 2 = prev + next
    private string _statusText = "Tap the grid to draw. Frames are unlimited.";
    private bool _isPlaying;
    private int[]? _undoSnapshot;
    private int _undoIndex = -1;
    private DispatcherTimer? _timer;

    // ── state ────────────────────────────────────────────────────────────────
    public int FrameCount => _frames.Count;
    public int CurrentIndex => _currentIndex;
    public string FrameCountText => $"{_frames.Count} frame{(_frames.Count == 1 ? "" : "s")}";
    public string FrameIndexText => _frames.Count == 0 ? "0 / 0" : $"{_currentIndex + 1} / {_frames.Count}";
    public int GridSize => _gridSize;
    public string GridText => $"{_gridSize}×{_gridSize} grid";
    public double Zoom => ZoomLevels[_zoomIndex];
    public string ZoomText => $"{Math.Round(Zoom * 100)}%";
    public int Fps => _fps;
    public string FpsText => $"{_fps} fps";
    public bool IsErase => _isErase;
    public string OnionLabel => _onionIndex switch { 1 => "Onion: prev", 2 => "Onion: prev+next", _ => "Onion: off" };
    public string PlayLabel => _isPlaying ? "Stop" : "Play";
    public bool CanUndo => _undoSnapshot != null;
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Changed(); }
    }
    public string FlipbookName
    {
        get => _flipbookName;
        set { _flipbookName = value ?? ""; Changed(); }
    }

    /// <summary>Rendered cell edge in device pixels — the bitmap is drawn at this
    /// scale so zooming never blurs the pixels.</summary>
    public int CellPixels => Math.Max(2, (int)Math.Round(BasePixels * Zoom / _gridSize));
    public int CanvasSide => _gridSize * CellPixels;

    private int[] BlankFrame() => new int[_gridSize * _gridSize];
    private int Cells => _gridSize * _gridSize;

    /// <summary>Raw 0/1 cells of a frame, for the thumbnail strip.</summary>
    public int[]? GetFrame(int index)
        => index >= 0 && index < _frames.Count ? _frames[index] : null;

    // ── drawing surface ──────────────────────────────────────────────────────
    public int CellFromPoint(Point point)
    {
        var cell = CellPixels;
        if (cell <= 0) return -1;
        var col = (int)Math.Floor(point.X / cell);
        var row = (int)Math.Floor(point.Y / cell);
        if (col < 0 || row < 0 || col >= _gridSize || row >= _gridSize) return -1;
        return row * _gridSize + col;
    }

    /// <summary>Called once when a stroke starts, so Undo can roll the whole
    /// stroke back rather than a single pixel.</summary>
    public void BeginStroke()
    {
        if (_currentIndex < 0 || _currentIndex >= _frames.Count) return;
        _undoSnapshot = (int[])_frames[_currentIndex].Clone();
        _undoIndex = _currentIndex;
        Changed(nameof(CanUndo));
    }

    public void ApplyCell(int cell, int value)
    {
        if (_currentIndex < 0 || _currentIndex >= _frames.Count) return;
        if (cell < 0 || cell >= _frames[_currentIndex].Length) return;
        if (_frames[_currentIndex][cell] == value) return;
        _frames[_currentIndex][cell] = value;
    }

    public void Undo()
    {
        if (_undoSnapshot == null || _undoIndex < 0 || _undoIndex >= _frames.Count) return;
        _frames[_undoIndex] = _undoSnapshot;
        _undoSnapshot = null;
        StatusText = "Undone.";
        Refresh();
    }

    public void ClearFrame()
    {
        if (_currentIndex < 0 || _currentIndex >= _frames.Count) return;
        _undoSnapshot = (int[])_frames[_currentIndex].Clone();
        _undoIndex = _currentIndex;
        _frames[_currentIndex] = BlankFrame();
        StatusText = "Frame cleared.";
        Refresh();
    }

    // ── frame management ─────────────────────────────────────────────────────
    public void AddFrame()
    {
        _frames.Insert(_currentIndex + 1, BlankFrame());
        _currentIndex++;
        StatusText = $"Frame {_currentIndex + 1} added.";
        Refresh();
    }

    public void DuplicateFrame()
    {
        if (_currentIndex < 0 || _currentIndex >= _frames.Count) return;
        _frames.Insert(_currentIndex + 1, (int[])_frames[_currentIndex].Clone());
        _currentIndex++;
        StatusText = "Frame duplicated.";
        Refresh();
    }

    public void DeleteFrame()
    {
        if (_frames.Count <= 1)
        {
            StatusText = "A flipbook needs at least one frame.";
            return;
        }
        _frames.RemoveAt(_currentIndex);
        if (_currentIndex >= _frames.Count) _currentIndex = _frames.Count - 1;
        StatusText = "Frame deleted.";
        Refresh();
    }

    public void MoveFrameLeft()
    {
        if (_currentIndex <= 0) return;
        (_frames[_currentIndex - 1], _frames[_currentIndex]) = (_frames[_currentIndex], _frames[_currentIndex - 1]);
        _currentIndex--;
        Refresh();
    }

    public void MoveFrameRight()
    {
        if (_currentIndex >= _frames.Count - 1) return;
        (_frames[_currentIndex + 1], _frames[_currentIndex]) = (_frames[_currentIndex], _frames[_currentIndex + 1]);
        _currentIndex++;
        Refresh();
    }

    public void SelectFrame(int index)
    {
        Stop();
        if (index < 0 || index >= _frames.Count) return;
        _currentIndex = index;
        Refresh();
    }

    public void PrevFrame()
    {
        if (_currentIndex <= 0) return;
        Stop();
        _currentIndex--;
        Refresh();
    }

    public void NextFrame()
    {
        if (_currentIndex >= _frames.Count - 1) return;
        Stop();
        _currentIndex++;
        Refresh();
    }

    /// <summary>Switch grid resolution, scaling the drawing up or down the same
    /// way the official client does when the user picks Simple/Medium/Fine.</summary>
    public void SetGrid(int size)
    {
        if (size == _gridSize) return;
        if (size is not (10 or 14 or 28)) return;
        Stop();
        var oldSide = _gridSize;
        var resized = new List<int[]>(_frames.Count);
        foreach (var frame in _frames)
        {
            var next = new int[size * size];
            for (var row = 0; row < size; row++)
            {
                for (var col = 0; col < size; col++)
                {
                    var srcCol = Math.Min(oldSide - 1, (int)((long)col * oldSide / size));
                    var srcRow = Math.Min(oldSide - 1, (int)((long)row * oldSide / size));
                    next[row * size + col] = frame[srcRow * oldSide + srcCol];
                }
            }
            resized.Add(next);
        }
        _frames.Clear();
        _frames.AddRange(resized);
        _gridSize = size;
        _zoomIndex = 0;
        StatusText = $"Grid set to {size}×{size}.";
        Refresh();
    }

    public void SetZoom(int index)
    {
        _zoomIndex = Math.Max(0, Math.Min(ZoomLevels.Length - 1, index));
        Changed();
        Dispatcher.UIThread.Post(() => View?.RenderCanvas());
    }

    public void ZoomIn() => SetZoom(_zoomIndex + 1);
    public void ZoomOut() => SetZoom(_zoomIndex - 1);
    public void SetPen() { _isErase = false; Changed(); }
    public void SetEraser() { _isErase = true; Changed(); }
    public void CycleOnion() { _onionIndex = (_onionIndex + 1) % 3; Changed(); }
    public void DecFps() { _fps = Math.Max(2, _fps - 1); Changed(); if (_isPlaying) Play(); }
    public void IncFps() { _fps = Math.Min(12, _fps + 1); Changed(); if (_isPlaying) Play(); }

    // ── playback ─────────────────────────────────────────────────────────────
    public void Play()
    {
        if (_frames.Count < 2)
        {
            StatusText = "Add another frame to animate.";
            return;
        }
        Stop();
        _isPlaying = true;
        var idx = _currentIndex;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / _fps) };
        _timer.Tick += (_, _) =>
        {
            idx = (idx + 1) % _frames.Count;
            _currentIndex = idx;
            Changed(nameof(FrameIndexText));
            Dispatcher.UIThread.Post(() =>
            {
                View?.RenderCanvas();
                View?.RenderThumbnails();
            });
        };
        _timer.Start();
        Changed(nameof(PlayLabel));
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        if (!_isPlaying) return;
        _isPlaying = false;
        Changed(nameof(PlayLabel));
    }

    // ── rendering ────────────────────────────────────────────────────────────
    /// <summary>Paint the current frame (plus onion ghosts) into a fresh bitmap.</summary>
    public Bitmap? RenderCurrentFrame()
    {
        var cell = CellPixels;
        var side = _gridSize * cell;
        var bmp = new WriteableBitmap(new PixelSize(side, side), new Vector(96, 96));
        var buf = new byte[side * side * 4];
        Fill(buf, side, 0xFF, 0xFF, 0xFF);

        if (_onionIndex >= 1 && !_isPlaying)
        {
            if (_onionIndex == 1 && _currentIndex > 0) Ghost(buf, side, cell, _frames[_currentIndex - 1], 0xFF, 0xB0, 0xB0);
            if (_onionIndex == 2)
            {
                if (_currentIndex > 0) Ghost(buf, side, cell, _frames[_currentIndex - 1], 0xFF, 0xB0, 0xB0);
                if (_currentIndex < _frames.Count - 1) Ghost(buf, side, cell, _frames[_currentIndex + 1], 0xFF, 0xD0, 0xD0);
            }
        }

        var current = _currentIndex >= 0 && _currentIndex < _frames.Count ? _frames[_currentIndex] : null;
        if (current != null) Ghost(buf, side, cell, current, 0x11, 0x11, 0x11);

        // grid lines, every 5th heavier — same cadence as the official canvas
        for (var i = 0; i <= _gridSize; i++)
        {
            var major = i % 5 == 0 || i == 0 || i == _gridSize;
            var v = major ? (byte)0xAA : (byte)0xD0;
            for (var p = 0; p < side; p++)
            {
                HLine(buf, side, i * cell, p, v);
                VLine(buf, side, p, i * cell, v);
            }
        }

        using (var locked = bmp.Lock())
        {
            var dst = locked.Address;
            var rowBytes = locked.RowBytes;
            for (var y = 0; y < side; y++)
                MarshalCopy(buf, y * side * 4, dst + (nint)(y * rowBytes), side * 4);
        }
        return bmp;
    }

    /// <summary>40×40 preview used by the frame strip.</summary>
    public Bitmap? RenderThumbnail(int[]? frame)
    {
        if (frame == null) return null;
        const int size = 40;
        var cell = Math.Max(1, size / _gridSize);
        var side = cell * _gridSize;
        var bmp = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96));
        var buf = new byte[size * size * 4];
        Fill(buf, size, 0xFF, 0xFF, 0xFF);
        Ghost(buf, size, cell, frame, 0x11, 0x11, 0x11);
        using (var locked = bmp.Lock())
        {
            var dst = locked.Address;
            var rowBytes = locked.RowBytes;
            for (var y = 0; y < size; y++)
                MarshalCopy(buf, y * size * 4, dst + (nint)(y * rowBytes), size * 4);
        }
        return bmp;
    }

    private static void Fill(byte[] buf, int side, byte r, byte g, byte b)
    {
        for (var i = 0; i < side * side; i++)
        {
            buf[i * 4 + 0] = b; // BGRA
            buf[i * 4 + 1] = g;
            buf[i * 4 + 2] = r;
            buf[i * 4 + 3] = 0xFF;
        }
    }

    private void Ghost(byte[] buf, int side, int cell, int[] frame, byte r, byte g, byte b)
    {
        for (var row = 0; row < _gridSize; row++)
        {
            for (var col = 0; col < _gridSize; col++)
            {
                if (frame[row * _gridSize + col] == 0) continue;
                for (var dy = 0; dy < cell; dy++)
                {
                    var y = row * cell + dy;
                    if (y >= side) break;
                    for (var dx = 0; dx < cell; dx++)
                    {
                        var x = col * cell + dx;
                        if (x >= side) break;
                        var o = (y * side + x) * 4;
                        buf[o + 0] = b;
                        buf[o + 1] = g;
                        buf[o + 2] = r;
                        buf[o + 3] = 0xFF;
                    }
                }
            }
        }
    }

    private static void HLine(byte[] buf, int side, int y, int x, byte v)
    {
        if (y < 0 || y >= side || x < 0 || x >= side) return;
        var o = (y * side + x) * 4;
        buf[o + 0] = v; buf[o + 1] = v; buf[o + 2] = v; buf[o + 3] = 0xFF;
    }

    private static void VLine(byte[] buf, int side, int x, int y, byte v) => HLine(buf, side, y, x, v);

    private static void MarshalCopy(byte[] src, int srcOffset, nint dst, int count)
        => System.Runtime.InteropServices.Marshal.Copy(src, srcOffset, dst, count);

    // ── export ───────────────────────────────────────────────────────────────
    /// <summary>The KHFLIP1 wire is capped at 60 frames and a 64×64 grid by every
    /// client, including the official web one. The editor itself has no cap — the
    /// limit is applied here, at the moment of export, so a long drawing is still
    /// fully editable on screen.</summary>
    public const int WireFrameLimit = 60;

    public bool HasDrawing()
    {
        foreach (var f in _frames)
            foreach (var px in f)
                if (px != 0) return true;
        return false;
    }

    public string BuildWire()
    {
        if (_frames.Count == 0) return "";
        var take = Math.Min(_frames.Count, WireFrameLimit);
        var packed = _frames.Take(take).Select(f => ChatMedia.FlipPack(f, Cells)).ToArray();
        var fb = new ChatMedia.Flipbook
        {
            Name = string.IsNullOrWhiteSpace(_flipbookName) ? "Flipbook" : _flipbookName.Trim(),
            Width = _gridSize,
            Height = _gridSize,
            Fps = _fps,
            Frames = packed
        };
        return ChatMedia.EncodeFlipbook(fb);
    }

    public void Send()
    {
        if (!HasDrawing())
        {
            StatusText = "Draw something first.";
            return;
        }
        var wire = BuildWire();
        if (string.IsNullOrEmpty(wire))
        {
            StatusText = "Couldn't pack that flipbook.";
            return;
        }
        var sent = Math.Min(_frames.Count, WireFrameLimit);
        StatusText = _frames.Count > WireFrameLimit
            ? $"Sent the first {sent} of {_frames.Count} frames — the chat format holds {WireFrameLimit}."
            : $"Sent {sent} frames.";
        SendFlipbook?.Invoke(wire);
    }

    public void CopyCode()
    {
        if (!HasDrawing())
        {
            StatusText = "Draw something first.";
            return;
        }
        var wire = BuildWire();
        if (string.IsNullOrEmpty(wire)) return;
        var view = View;
        var clip = view == null ? null : TopLevel.GetTopLevel(view)?.Clipboard;
        if (clip == null)
        {
            StatusText = "Couldn't reach the clipboard.";
            return;
        }
        StatusText = _frames.Count > WireFrameLimit
            ? $"Code copied — the chat format holds the first {WireFrameLimit} of {_frames.Count} frames."
            : "Code copied — paste it in chat to share.";
        Dispatcher.UIThread.Post(async () =>
        {
            try { await clip.SetTextAsync(wire); } catch { /* clipboard unavailable */ }
        });
    }

    public void Close()
    {
        Stop();
        CloseWindow?.Invoke();
    }

    // ── plumbing ─────────────────────────────────────────────────────────────
    internal void Refresh()
    {
        Changed(nameof(FrameCount));
        Changed(nameof(FrameCountText));
        Changed(nameof(FrameIndexText));
        Changed(nameof(GridSize));
        Changed(nameof(GridText));
        Changed(nameof(FpsText));
        Changed(nameof(OnionLabel));
        Changed(nameof(IsErase));
        Changed(nameof(CanUndo));
        Dispatcher.UIThread.Post(() =>
        {
            View?.RenderCanvas();
            View?.RenderThumbnails();
        });
    }

    private void Changed([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── commands ─────────────────────────────────────────────────────────────
    public ICommand SetPenCommand { get; }
    public ICommand SetEraseCommand { get; }
    public ICommand CycleOnionCommand { get; }
    public ICommand ClearFrameCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand PrevFrameCommand { get; }
    public ICommand NextFrameCommand { get; }
    public ICommand MoveFrameLeftCommand { get; }
    public ICommand MoveFrameRightCommand { get; }
    public ICommand AddFrameCommand { get; }
    public ICommand DuplicateFrameCommand { get; }
    public ICommand DeleteFrameCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand DecFpsCommand { get; }
    public ICommand IncFpsCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand Grid10Command { get; }
    public ICommand Grid14Command { get; }
    public ICommand Grid28Command { get; }
    public ICommand SendCommand { get; }
    public ICommand SaveCommunityCommand { get; }
    public ICommand CopyCodeCommand { get; }
    public ICommand CloseCommand { get; }

    public FlipbookEditorViewModel()
    {
        SetPenCommand = new RelayCommand(SetPen);
        SetEraseCommand = new RelayCommand(SetEraser);
        CycleOnionCommand = new RelayCommand(CycleOnion);
        ClearFrameCommand = new RelayCommand(ClearFrame);
        UndoCommand = new RelayCommand(Undo);
        PrevFrameCommand = new RelayCommand(PrevFrame);
        NextFrameCommand = new RelayCommand(NextFrame);
        MoveFrameLeftCommand = new RelayCommand(MoveFrameLeft);
        MoveFrameRightCommand = new RelayCommand(MoveFrameRight);
        AddFrameCommand = new RelayCommand(AddFrame);
        DuplicateFrameCommand = new RelayCommand(DuplicateFrame);
        DeleteFrameCommand = new RelayCommand(DeleteFrame);
        PlayCommand = new RelayCommand(() => { if (_isPlaying) Stop(); else Play(); });
        DecFpsCommand = new RelayCommand(DecFps);
        IncFpsCommand = new RelayCommand(IncFps);
        ZoomInCommand = new RelayCommand(ZoomIn);
        ZoomOutCommand = new RelayCommand(ZoomOut);
        Grid10Command = new RelayCommand(() => SetGrid(10));
        Grid14Command = new RelayCommand(() => SetGrid(14));
        Grid28Command = new RelayCommand(() => SetGrid(28));
        SendCommand = new RelayCommand(Send);
        SaveCommunityCommand = new RelayCommand(SaveToCommunityBook);
        CopyCodeCommand = new RelayCommand(CopyCode);
        CloseCommand = new RelayCommand(Close);

        _frames.Add(BlankFrame());
        Refresh();
    }

    private void SaveToCommunityBook()
    {
        if (!HasDrawing()) { StatusText = "Draw something first."; return; }
        var wire = BuildWire();
        if (string.IsNullOrEmpty(wire)) { StatusText = "Couldn't pack that flipbook."; return; }
        if (SaveToCommunity == null) { StatusText = "Open the editor from Community to save to your account."; return; }
        SaveToCommunity.Invoke(wire);
    }
}
