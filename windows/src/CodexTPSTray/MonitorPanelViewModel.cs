using System;
using System.ComponentModel;
using System.Globalization;
using CodexTPSCore;

namespace CodexTPSTray;

public class MonitorPanelViewModel : INotifyPropertyChanged
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    private UsageSnapshot? _latestSnapshot;
    private TraySettings _currentSettings;
    private bool _isRefreshing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MonitorPanelViewModel(TraySettings initialSettings)
    {
        _currentSettings = initialSettings;
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set
        {
            _isRefreshing = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public MetricWindow SelectedWindow => _currentSettings.SelectedWindow;

    public RefreshCadence SelectedCadence => _currentSettings.RefreshCadence;

    public Language Language => _currentSettings.Language;

    public string StatusText
    {
        get
        {
            int malformedLines = _latestSnapshot.HasValue ? _latestSnapshot.Value.MalformedRelevantLines : 0;
            return Localization.GetStatusText(
                _latestSnapshot.HasValue ? _latestSnapshot.Value.Status : CollectionStatus.Ready,
                _isRefreshing,
                malformedLines,
                _latestSnapshot.HasValue,
                _currentSettings.Language
            );
        }
    }

    public StatusPresentationState StatusPresentationState
    {
        get
        {
            if (_isRefreshing)
            {
                return new StatusPresentationState(
                    Status: PanelStatus.Refreshing,
                    Text: Localization.GetStatusText(
                        _latestSnapshot.HasValue ? _latestSnapshot.Value.Status : CollectionStatus.Ready,
                        true,
                        _latestSnapshot.HasValue ? _latestSnapshot.Value.MalformedRelevantLines : 0,
                        _latestSnapshot.HasValue,
                        _currentSettings.Language
                    ),
                    DotColor: StatusDotColor.Blue
                );
            }

            if (!_latestSnapshot.HasValue)
            {
                return new StatusPresentationState(
                    Status: PanelStatus.Waiting,
                    Text: Localization.GetStatusText(CollectionStatus.Ready, false, 0, false, _currentSettings.Language),
                    DotColor: StatusDotColor.Gray
                );
            }

            var snapshot = _latestSnapshot.Value;

            switch (snapshot.Status)
            {
                case CollectionStatus.Ready:
                    if (snapshot.MalformedRelevantLines > 0)
                    {
                        return new StatusPresentationState(
                            Status: PanelStatus.ReadyWithPartialData,
                            Text: Localization.GetStatusText(snapshot.Status, false, snapshot.MalformedRelevantLines, true, _currentSettings.Language),
                            DotColor: StatusDotColor.Orange
                        );
                    }
                    return new StatusPresentationState(
                        Status: PanelStatus.Ready,
                        Text: Localization.GetStatusText(snapshot.Status, false, 0, true, _currentSettings.Language),
                        DotColor: StatusDotColor.Green
                    );

                case CollectionStatus.SessionsDirectoryMissing:
                    return new StatusPresentationState(
                        Status: PanelStatus.NoSessions,
                        Text: Localization.GetStatusText(snapshot.Status, false, 0, true, _currentSettings.Language),
                        DotColor: StatusDotColor.Orange
                    );

                case CollectionStatus.ReadFailed:
                    return new StatusPresentationState(
                        Status: PanelStatus.Error,
                        Text: Localization.GetStatusText(snapshot.Status, false, 0, true, _currentSettings.Language),
                        DotColor: StatusDotColor.Red
                    );

                default:
                    return new StatusPresentationState(
                        Status: PanelStatus.Unknown,
                        Text: Localization.GetStatusText(snapshot.Status, false, 0, true, _currentSettings.Language),
                        DotColor: StatusDotColor.Gray
                    );
            }
        }
    }

    public string LastUpdateTimeText
    {
        get
        {
            if (!_latestSnapshot.HasValue)
                return "";

            DateTimeOffset generatedAt = _latestSnapshot.Value.GeneratedAt;
            DateTime localTime = generatedAt.LocalDateTime;
            return localTime.ToString("HH:mm:ss", InvariantCulture);
        }
    }

    public string TotalTpsText
    {
        get
        {
            double tps = GetCurrentMetrics().TokensPerSecond;
            return FormatCompactNumber(tps);
        }
    }

    public string InputTpsText
    {
        get
        {
            double tps = GetCurrentMetrics().InputTokensPerSecond;
            return FormatCompactNumber(tps);
        }
    }

    public string CachedTpsText
    {
        get
        {
            double tps = GetCurrentMetrics().CachedInputTokensPerSecond;
            return FormatCompactNumber(tps);
        }
    }

    public string OutputTpsText
    {
        get
        {
            double tps = GetCurrentMetrics().OutputTokensPerSecond;
            return FormatCompactNumber(tps);
        }
    }

    public string ReasoningTpsText
    {
        get
        {
            double tps = GetCurrentMetrics().ReasoningTokensPerSecond;
            return FormatCompactNumber(tps);
        }
    }

    public string RequestsPerMinuteText
    {
        get
        {
            double rpm = GetCurrentMetrics().RequestsPerMinute;
            return FormatCompactNumber(rpm);
        }
    }

    public string ActiveSessionsText
    {
        get
        {
            return _latestSnapshot.HasValue
                ? _latestSnapshot.Value.ActiveSessions.ToString(InvariantCulture)
                : "0";
        }
    }

    public string CacheRatioText
    {
        get
        {
            double ratio = GetCurrentMetrics().CacheRatio;
            return ratio.ToString("P0", InvariantCulture).Replace(" ", "");
        }
    }

    public string SelectedWindowDisplayName
    {
        get => Localization.GetMetricWindowDisplayName(_currentSettings.SelectedWindow, _currentSettings.Language);
    }

    public string SelectedCadenceDisplayName
    {
        get => Localization.GetRefreshCadenceDisplayName(_currentSettings.RefreshCadence, _currentSettings.Language);
    }

    public string InputLabel => Localization.Input(_currentSettings.Language);
    public string CachedLabel => Localization.Cached(_currentSettings.Language);
    public string OutputLabel => Localization.Output(_currentSettings.Language);
    public string ReasoningLabel => Localization.Reasoning(_currentSettings.Language);
    public string ActiveSessionsLabel => Localization.ActiveSessions(_currentSettings.Language);
    public string CacheRatioLabel => Localization.CacheRatio(_currentSettings.Language);
    public string RefreshCadenceLabel => Localization.RefreshCadenceLabel(_currentSettings.Language);
    public string LaunchAtLoginLabel => Localization.LaunchAtLogin(_currentSettings.Language);
    public string RequestsPerMinuteLabel => Localization.RequestsPerMinute(_currentSettings.Language);
    public string TokenPerSecondLabel => Localization.TokenPerSecond(_currentSettings.Language);
    public string RefreshTooltip => Localization.RefreshTooltip(_currentSettings.Language);
    public string OpenFolderTooltip => Localization.OpenSessionsFolderTooltip(_currentSettings.Language);
    public string ExitTooltip => Localization.ExitTooltip(_currentSettings.Language);

    public void UpdateSnapshot(UsageSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        RaiseAllPropertyChanged();
    }

    public void UpdateSettings(TraySettings settings)
    {
        _currentSettings = settings;
        RaiseAllPropertyChanged();
    }

    private WindowMetrics GetCurrentMetrics()
    {
        if (!_latestSnapshot.HasValue)
            return WindowMetrics.Empty(60);

        return _currentSettings.SelectedWindow.GetMetrics(_latestSnapshot.Value);
    }

    private void RaiseAllPropertyChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(LastUpdateTimeText));
        OnPropertyChanged(nameof(TotalTpsText));
        OnPropertyChanged(nameof(InputTpsText));
        OnPropertyChanged(nameof(CachedTpsText));
        OnPropertyChanged(nameof(OutputTpsText));
        OnPropertyChanged(nameof(ReasoningTpsText));
        OnPropertyChanged(nameof(RequestsPerMinuteText));
        OnPropertyChanged(nameof(ActiveSessionsText));
        OnPropertyChanged(nameof(CacheRatioText));
        OnPropertyChanged(nameof(SelectedWindowDisplayName));
        OnPropertyChanged(nameof(SelectedCadenceDisplayName));
        OnPropertyChanged(nameof(SelectedWindow));
        OnPropertyChanged(nameof(SelectedCadence));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(InputLabel));
        OnPropertyChanged(nameof(CachedLabel));
        OnPropertyChanged(nameof(OutputLabel));
        OnPropertyChanged(nameof(ReasoningLabel));
        OnPropertyChanged(nameof(ActiveSessionsLabel));
        OnPropertyChanged(nameof(CacheRatioLabel));
        OnPropertyChanged(nameof(RefreshCadenceLabel));
        OnPropertyChanged(nameof(LaunchAtLoginLabel));
        OnPropertyChanged(nameof(RequestsPerMinuteLabel));
        OnPropertyChanged(nameof(TokenPerSecondLabel));
        OnPropertyChanged(nameof(RefreshTooltip));
        OnPropertyChanged(nameof(OpenFolderTooltip));
        OnPropertyChanged(nameof(ExitTooltip));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static string FormatCompactNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "0";

        if (value >= 1000000)
            return (value / 1000000.0).ToString("F1", InvariantCulture) + "M";

        if (value >= 1000)
            return (value / 1000.0).ToString("F1", InvariantCulture) + "K";

        if (value >= 10)
            return value.ToString("F1", InvariantCulture);

        if (value > 0)
            return value.ToString("F2", InvariantCulture);

        return "0";
    }

    public static string FormatStatus(CollectionStatus status, bool isRefreshing, int malformedLines, Language language)
    {
        return Localization.GetStatusText(status, isRefreshing, malformedLines, true, language);
    }
}

public enum PanelStatus
{
    Waiting,
    Refreshing,
    Ready,
    ReadyWithPartialData,
    NoSessions,
    Error,
    Unknown
}

public enum StatusDotColor
{
    Gray,
    Blue,
    Green,
    Orange,
    Red
}

public record StatusPresentationState(
    PanelStatus Status,
    string Text,
    StatusDotColor DotColor
);