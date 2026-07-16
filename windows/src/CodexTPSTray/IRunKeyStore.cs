namespace CodexTPSTray;

public interface IRunKeyStore
{
    bool TryReadValue(out string? value);
    bool TryWriteValue(string value);
    bool TryDeleteValue();
}