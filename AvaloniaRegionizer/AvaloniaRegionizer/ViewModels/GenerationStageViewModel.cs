using System;
using System.Reactive;
using MapRegionizer.Core.Generation;
using ReactiveUI;

namespace AvaloniaRegionizer.ViewModels;

public enum GenerationStageStatus
{
    NotStarted,
    Running,
    Ready,
    Dirty,
    Failed,
    Future
}

public sealed class GenerationStageViewModel : ReactiveObject
{
    private readonly Func<string, string> _localize;
    private GenerationStageStatus _status;
    private TimeSpan? _duration;
    private string _error = string.Empty;

    public GenerationStageViewModel(
        string id,
        string labelKey,
        MapDataKey? dataKey,
        Func<string, string> localize,
        Func<GenerationStageViewModel, System.Threading.Tasks.Task> runUntil,
        Func<GenerationStageViewModel, System.Threading.Tasks.Task> regenerate)
    {
        Id = id;
        LabelKey = labelKey;
        DataKey = dataKey;
        _localize = localize;
        RunUntilCommand = ReactiveCommand.CreateFromTask(() => runUntil(this));
        RegenerateCommand = ReactiveCommand.CreateFromTask(() => regenerate(this));
    }

    public string Id { get; }
    public string LabelKey { get; }
    public MapDataKey? DataKey { get; }
    public ReactiveCommand<Unit, Unit> RunUntilCommand { get; }
    public ReactiveCommand<Unit, Unit> RegenerateCommand { get; }

    public string Name => _localize(LabelKey);
    public bool HasDataKey => DataKey.HasValue;
    public bool IsFuture => Status == GenerationStageStatus.Future;
    public bool CanRun => HasDataKey && Status != GenerationStageStatus.Running;
    public bool CanRegenerate => HasDataKey && Status is GenerationStageStatus.Ready or GenerationStageStatus.Dirty or GenerationStageStatus.Failed;

    public GenerationStageStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;

            this.RaiseAndSetIfChanged(ref _status, value);
            RaiseDerived();
        }
    }

    public TimeSpan? Duration
    {
        get => _duration;
        set
        {
            if (_duration == value)
                return;

            this.RaiseAndSetIfChanged(ref _duration, value);
            this.RaisePropertyChanged(nameof(DurationText));
        }
    }

    public string Error
    {
        get => _error;
        set => this.RaiseAndSetIfChanged(ref _error, value);
    }

    public string StatusText => Status switch
    {
        GenerationStageStatus.Running => _localize("Running"),
        GenerationStageStatus.Ready => _localize("StageReady"),
        GenerationStageStatus.Dirty => _localize("Dirty"),
        GenerationStageStatus.Failed => _localize("StageFailed"),
        GenerationStageStatus.Future => _localize("Unavailable"),
        _ => _localize("NotStarted")
    };

    public string StatusBrush => Status switch
    {
        GenerationStageStatus.Running => "#2563EB",
        GenerationStageStatus.Ready => "#16803A",
        GenerationStageStatus.Dirty => "#B7791F",
        GenerationStageStatus.Failed => "#C62828",
        GenerationStageStatus.Future => "#6B7280",
        _ => "#737373"
    };

    public string DurationText => Duration is null ? string.Empty : $"{Duration.Value.TotalMilliseconds:0} ms";

    public void RefreshLocalization()
    {
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void RaiseDerived()
    {
        this.RaisePropertyChanged(nameof(StatusText));
        this.RaisePropertyChanged(nameof(StatusBrush));
        this.RaisePropertyChanged(nameof(IsFuture));
        this.RaisePropertyChanged(nameof(CanRun));
        this.RaisePropertyChanged(nameof(CanRegenerate));
    }
}
