using CodexTPSTray;

namespace CodexTPSTray.Tests;

public sealed class FakeRunKeyStore : IRunKeyStore
{
    public string? StoredValue { get; set; }
    public bool ReadThrows { get; set; } = false;
    public bool ReadKeyIsNull { get; set; } = false;
    public bool WriteThrows { get; set; } = false;
    public bool WriteKeyIsNull { get; set; } = false;
    public bool DeleteThrows { get; set; } = false;
    public bool DeleteKeyIsNull { get; set; } = false;

    public bool TryReadValue(out string? value)
    {
        if (ReadThrows || ReadKeyIsNull)
        {
            value = null;
            return false;
        }

        value = StoredValue;
        return true;
    }

    public bool TryWriteValue(string value)
    {
        if (WriteThrows || WriteKeyIsNull)
        {
            return false;
        }

        StoredValue = value;
        return true;
    }

    public bool TryDeleteValue()
    {
        if (DeleteThrows || DeleteKeyIsNull)
        {
            return false;
        }

        StoredValue = null;
        return true;
    }
}