using System;

namespace AvaloniaRegionizer.ViewModels;

public sealed class GenerationLogEntryViewModel
{
    public GenerationLogEntryViewModel(DateTimeOffset timestamp, string message, string level = "info")
    {
        Timestamp = timestamp;
        Message = message;
        Level = level;
    }

    public DateTimeOffset Timestamp { get; }
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string Message { get; }
    public string Level { get; }
}
