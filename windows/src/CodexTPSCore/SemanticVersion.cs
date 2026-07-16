using System;

namespace CodexTPSCore;

public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    public static readonly SemanticVersion Zero = new(0, 0, 0);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public SemanticVersion(int major, int minor, int patch)
    {
        if (major < 0 || minor < 0 || patch < 0)
        {
            throw new ArgumentOutOfRangeException("Components must be non-negative.");
        }
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public static SemanticVersion? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(1);
        }

        var components = normalized.Split('.', StringSplitOptions.None);
        if (components.Length != 3)
        {
            return null;
        }

        if (!int.TryParse(components[0], out var major) || major < 0 ||
            !int.TryParse(components[1], out var minor) || minor < 0 ||
            !int.TryParse(components[2], out var patch) || patch < 0)
        {
            return null;
        }

        return new SemanticVersion(major, minor, patch);
    }

    public int CompareTo(SemanticVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        return Patch.CompareTo(other.Patch);
    }

    public bool Equals(SemanticVersion other)
    {
        return Major == other.Major && Minor == other.Minor && Patch == other.Patch;
    }

    public override bool Equals(object? obj)
    {
        return obj is SemanticVersion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor, Patch);
    }

    public override string ToString()
    {
        return $"{Major}.{Minor}.{Patch}";
    }

    public static bool operator ==(SemanticVersion left, SemanticVersion right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SemanticVersion left, SemanticVersion right)
    {
        return !left.Equals(right);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(SemanticVersion left, SemanticVersion right)
    {
        return left.CompareTo(right) >= 0;
    }
}
