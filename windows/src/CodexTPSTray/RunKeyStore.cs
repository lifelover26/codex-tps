using System;
using Microsoft.Win32;

namespace CodexTPSTray;

public sealed class RunKeyStore : IRunKeyStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexTPS";

    public bool TryReadValue(out string? value)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key == null)
            {
                value = null;
                return false;
            }

            value = key.GetValue(ValueName) as string;
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    public bool TryWriteValue(string value)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                return false;
            }

            key.SetValue(ValueName, value, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryDeleteValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                return false;
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}