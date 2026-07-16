using System;
using System.IO;

namespace CodexTPSTray;

public sealed class StartupManager
{
    private const int MaxCommandLength = 260;

    private readonly IRunKeyStore _runKeyStore;
    private readonly Func<string?> _executablePathProvider;

    public StartupManager(IRunKeyStore runKeyStore, Func<string?> executablePathProvider)
    {
        _runKeyStore = runKeyStore;
        _executablePathProvider = executablePathProvider;
    }

    public bool TryGetIsEnabled(out bool isEnabled)
    {
        if (!_runKeyStore.TryReadValue(out string? storedCommand))
        {
            isEnabled = false;
            return false;
        }

        if (!TryGetNormalizedCommand(out string? normalizedCommand))
        {
            isEnabled = false;
            return true;
        }

        isEnabled = string.Equals(storedCommand, normalizedCommand, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    public bool TryEnable()
    {
        if (!TryGetNormalizedCommand(out string? command))
        {
            return false;
        }

        return _runKeyStore.TryWriteValue(command!);
    }

    public bool TryDisable()
    {
        return _runKeyStore.TryDeleteValue();
    }

    private bool TryGetNormalizedCommand(out string? normalizedCommand)
    {
        string? path = _executablePathProvider();

        if (string.IsNullOrEmpty(path))
        {
            normalizedCommand = null;
            return false;
        }

        if (path.Contains('"'))
        {
            normalizedCommand = null;
            return false;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            normalizedCommand = null;
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                normalizedCommand = null;
                return false;
            }

            string fullPath = Path.GetFullPath(path);
            string quotedCommand = $"\"{fullPath}\"";

            if (quotedCommand.Length > MaxCommandLength)
            {
                normalizedCommand = null;
                return false;
            }

            normalizedCommand = quotedCommand;
            return true;
        }
        catch
        {
            normalizedCommand = null;
            return false;
        }
    }
}