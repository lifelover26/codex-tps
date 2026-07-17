using System;

namespace CodexTPSTray;

internal interface IThemeApplicationTarget
{
    void ApplyApplicationTheme(EffectiveTheme theme);
    void ApplyOverlayTheme(EffectiveTheme theme);
}

internal sealed class ThemeApplicationCoordinator
{
    private readonly ThemeResolver _themeResolver;
    private readonly IThemeApplicationTarget _target;
    private EffectiveTheme _currentApplicationTheme = EffectiveTheme.Light;
    private EffectiveTheme _currentOverlayTheme = EffectiveTheme.Light;

    public EffectiveTheme CurrentApplicationTheme => _currentApplicationTheme;
    public EffectiveTheme CurrentOverlayTheme => _currentOverlayTheme;

    public ThemeApplicationCoordinator(ThemeResolver themeResolver, IThemeApplicationTarget target)
    {
        _themeResolver = themeResolver ?? throw new ArgumentNullException(nameof(themeResolver));
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public void Apply(TraySettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        var resolved = _themeResolver.ResolveBoth(settings.ApplicationTheme, settings.OverlayTheme);

        _currentApplicationTheme = resolved.ApplicationTheme;
        _currentOverlayTheme = resolved.OverlayTheme;

        _target.ApplyApplicationTheme(_currentApplicationTheme);
        _target.ApplyOverlayTheme(_currentOverlayTheme);
    }
}
