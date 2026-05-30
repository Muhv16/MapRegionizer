using System;
using ReactiveUI;

namespace AvaloniaRegionizer.ViewModels;

public sealed class SettingsSectionViewModel : ReactiveObject
{
    private readonly Func<string, string> _localize;
    private bool _isSelected;

    public SettingsSectionViewModel(
        string id,
        string groupKey,
        string titleKey,
        SettingsSectionKind kind,
        int order,
        Func<string, string> localize)
    {
        Id = id;
        GroupKey = groupKey;
        TitleKey = titleKey;
        Kind = kind;
        Order = order;
        _localize = localize;
    }

    public string Id { get; }
    public string GroupKey { get; }
    public string TitleKey { get; }
    public SettingsSectionKind Kind { get; }
    public bool IsAdvanced => Kind == SettingsSectionKind.Advanced;
    public int Order { get; }
    public string GroupName => _localize(GroupKey);
    public string Title => _localize(TitleKey);

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public void RefreshLocalization()
    {
        this.RaisePropertyChanged(nameof(GroupName));
        this.RaisePropertyChanged(nameof(Title));
    }
}
