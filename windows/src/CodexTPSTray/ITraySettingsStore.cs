namespace CodexTPSTray;

internal interface ITraySettingsStore
{
    TraySettings Load();
    bool TrySave(TraySettings settings);
}
