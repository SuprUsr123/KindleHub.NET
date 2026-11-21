using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KindleHub.Client.ViewModels;

/// <summary>A single cell (0–8) on the Tic-Tac-Toe board.</summary>
public sealed class TttCell : INotifyPropertyChanged
{
    private string? _value;
    private bool _isWinning;

    public TttCell(int index) => Index = index;

    public int Index { get; }

    public string? Value
    {
        get => _value;
        set { if (_value != value) { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
    }

    public string Display => string.IsNullOrEmpty(_value) ? "" : _value;

    public bool IsWinning
    {
        get => _isWinning;
        set { if (_isWinning != value) { _isWinning = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
