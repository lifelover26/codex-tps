using System;
using System.IO;
using System.Text.Json;

namespace CodexTPSTray;

public class TraySettingsStore
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private TraySettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public static TraySettingsStore CreateDefault()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string settingsPath = Path.Combine(localAppData, "CodexTPS", "settings.json");
        return new TraySettingsStore(settingsPath);
    }

    public static TraySettingsStore CreateForTests(string settingsPath)
    {
        return new TraySettingsStore(settingsPath);
    }

    public TraySettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return TraySettings.Default;
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);
            var raw = JsonSerializer.Deserialize<RawSettingsDto>(json);

            MetricWindow window = ParseMetricWindow(raw?.SelectedWindow);
            RefreshCadence cadence = ParseRefreshCadence(raw?.RefreshCadence);

            return new TraySettings(window, cadence);
        }
        catch
        {
            return TraySettings.Default;
        }
    }

    public bool TrySave(TraySettings settings)
    {
        string directory = Path.GetDirectoryName(_settingsPath) ?? string.Empty;
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch
            {
                return false;
            }
        }

        string tempPath = Path.Combine(directory, $"{Guid.NewGuid()}.tmp");
        bool success = false;

        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new RawSettingsDto(settings), SerializerOptions);

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(tempPath, _settingsPath, null);
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }

            success = true;
        }
        catch
        {
            success = false;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        return success;
    }

    private static MetricWindow ParseMetricWindow(string? value)
    {
        if (value == null)
        {
            return TraySettings.Default.SelectedWindow;
        }

        if (Enum.TryParse<MetricWindow>(value, ignoreCase: true, out var window) && Enum.IsDefined(window))
        {
            return window;
        }

        return TraySettings.Default.SelectedWindow;
    }

    private static RefreshCadence ParseRefreshCadence(int? value)
    {
        if (value == null)
        {
            return TraySettings.Default.RefreshCadence;
        }

        return RefreshCadenceExtensions.FromSeconds(value.Value);
    }

    private sealed class RawSettingsDto
    {
        public string? SelectedWindow { get; set; }
        public int? RefreshCadence { get; set; }

        public RawSettingsDto()
        {
        }

        public RawSettingsDto(TraySettings settings)
        {
            SelectedWindow = settings.SelectedWindow.ToString();
            RefreshCadence = (int)settings.RefreshCadence;
        }
    }
}