// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

namespace RepoG33k.Services;

/// <summary>Identifies repository paths consistently across Git and native folder discovery.</summary>
internal sealed class RepositoryPathComparer : IEqualityComparer<string>
{
    public static RepositoryPathComparer Instance { get; } = new();
    private static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public bool Equals(string left, string right) => left == null || right == null
        ? left == right : Comparer.Equals(Normalize(left), Normalize(right));

    public int GetHashCode(string path) => path == null ? 0 : Comparer.GetHashCode(Normalize(path));
}
