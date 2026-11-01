// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Holds one baseline and working-file snapshot for the DiffLens prototype.
/// </summary>
/// <remarks>
/// Saves preserve the source encoding and refuse to overwrite a changed disk snapshot.
/// </remarks>
internal sealed class DiffLensFile
{
    private byte[] m_diskBytes;
    private readonly Encoding m_encoding;
    private readonly byte[] m_preamble;

    public DiffLensFile(string fullPath, string relativePath, string baseRevision, string baseline, byte[] diskBytes, bool readOnly = false)
    {
        IsReadOnly = readOnly;
        FullPath = fullPath;
        RelativePath = relativePath;
        BaseRevision = baseRevision;
        Baseline = baseline;
        m_diskBytes = diskBytes;
        m_encoding = DetectEncoding(diskBytes ?? [], out m_preamble);
        WorkingText = diskBytes == null ? string.Empty : m_encoding.GetString(diskBytes, m_preamble.Length, diskBytes.Length - m_preamble.Length);
        if (ContainsBinaryControls(WorkingText) || ContainsBinaryControls(baseline))
            throw new InvalidOperationException("Binary file — no text preview.");
    }

    private static bool ContainsBinaryControls(string text) =>
        text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n' or '\f'));

    public string FullPath { get; }
    public string RelativePath { get; }
    public string BaseRevision { get; }
    public string Baseline { get; }
    public string WorkingText { get; private set; }
    public bool IsReadOnly { get; }
    public bool CanEdit => m_diskBytes != null && !IsReadOnly;

    public async Task SaveAsync(string text)
    {
        if (IsReadOnly)
            throw new InvalidOperationException("This Git snapshot is read-only.");
        if (!CanEdit)
            throw new InvalidOperationException("The working file is deleted. This prototype does not recreate deleted files.");

        // Hold an exclusive handle across comparison and write. An agent holding a conflicting
        // handle makes this fail; it never falls back to an unconditional overwrite.
        await using var stream = new FileStream(FullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, true);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        if (!copy.ToArray().SequenceEqual(m_diskBytes))
            throw new IOException("The file changed on disk. Your edits are retained; copy them out and reload to reconcile.");
        var bytes = m_preamble.Concat(m_encoding.GetBytes(text)).ToArray();
        stream.Position = 0;
        await stream.WriteAsync(bytes);
        stream.SetLength(bytes.Length);
        await stream.FlushAsync();
        m_diskBytes = bytes;
        WorkingText = text;
    }

    private static Encoding DetectEncoding(byte[] bytes, out byte[] preamble)
    {
        Encoding[] candidates = [new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true),
            new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true), new UTF8Encoding(true, true)];
        foreach (var candidate in candidates)
        {
            var prefix = candidate.GetPreamble();
            if (!bytes.AsSpan().StartsWith(prefix))
                continue;
            preamble = prefix;
            return candidate;
        }
        preamble = [];
        return new UTF8Encoding(false, true);
    }
}
