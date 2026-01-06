namespace RetroRommer.Domain;

public enum LogStatus
{
    Info,
    Success,
    Error,
    Warning
}

public class LogDto
{
    public string Filename { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public LogStatus Status { get; set; } = LogStatus.Info;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}