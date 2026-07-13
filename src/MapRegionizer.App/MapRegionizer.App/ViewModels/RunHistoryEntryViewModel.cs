namespace MapRegionizer.App.ViewModels;

public sealed class RunHistoryEntryViewModel
{
    public RunHistoryEntryViewModel(string title, string details, string outputDirectory)
    {
        Title = title;
        Details = details;
        OutputDirectory = outputDirectory;
    }

    public string Title { get; }
    public string Details { get; }
    public string OutputDirectory { get; }
}
