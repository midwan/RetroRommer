using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RetroRommer.Core;

public sealed class DownloadItemProgressViewModel : INotifyPropertyChanged
{
    private string _fileName = string.Empty;
    private string _status = string.Empty;
    private long? _totalBytes;
    private long _bytesReceived;
    private double? _bytesPerSecond;

    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public long? TotalBytes
    {
        get => _totalBytes;
        set { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressText)); }
    }

    public long BytesReceived
    {
        get => _bytesReceived;
        set { _bytesReceived = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressText)); }
    }

    public double? BytesPerSecond
    {
        get => _bytesPerSecond;
        set { _bytesPerSecond = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedText)); }
    }

    public double ProgressPercent => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value * 100.0 : 0.0;

    public string ProgressText
    {
        get
        {
            if (TotalBytes is > 0)
            {
                return $"{FormatBytes(BytesReceived)} / {FormatBytes(TotalBytes.Value)}";
            }
            return $"{FormatBytes(BytesReceived)}";
        }
    }

    public string SpeedText => BytesPerSecond is > 0 ? $"{FormatBytes((long)BytesPerSecond.Value)}/s" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return order switch
        {
            0 => $"{len:0} {sizes[order]}",
            1 => $"{len:0.0} {sizes[order]}",
            _ => $"{len:0.00} {sizes[order]}"
        };
    }
}
