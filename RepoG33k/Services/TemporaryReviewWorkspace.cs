// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

namespace RepoG33k.Services;

/// <summary>Owns the temporary clones and PR worktrees for one app session.</summary>
internal sealed class TemporaryReviewWorkspace : IDisposable
{
    private string m_root;

    public string GetRepositoryRoot() =>
        m_root ??= Directory.CreateTempSubdirectory("RepoG33k-PR-").FullName;

    public void Dispose()
    {
        if (m_root == null || !Directory.Exists(m_root))
            return;

        // Git object files can be read-only, which prevents deletion on Windows.
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        foreach (var file in Directory.EnumerateFiles(m_root, "*", options))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(m_root, recursive: true);
        m_root = null;
    }
}
