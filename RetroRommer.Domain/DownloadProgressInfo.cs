namespace RetroRommer.Domain;

public sealed class DownloadProgressInfo
{
    public string FileName { get; init; } = string.Empty;
    public long? TotalBytes { get; init; }
    public long BytesReceived { get; init; }
    public double? BytesPerSecond { get; init; }

    public double? Percent => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value * 100.0 : null;
}
