// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ReviewG33k.Services;

internal static class GitChangedFileContent
{
    public static async Task<ChangedFileContent> LoadAsync(
        GitCommandRunner gitCommandRunner,
        string repositoryPath,
        FileInfo fullPath,
        string baselineRevision,
        string relativePath,
        string currentRevision = null)
    {
        var currentBytes = !string.IsNullOrWhiteSpace(currentRevision)
            ? await TryLoadRevisionBytesAsync(gitCommandRunner, repositoryPath, currentRevision, relativePath)
            : null;
        currentBytes ??= fullPath != null && fullPath.Exists
            ? await File.ReadAllBytesAsync(fullPath.FullName)
            : [];
        var currentText = DecodeText(currentBytes) ?? string.Empty;

        var baselineBytes = await TryLoadRevisionBytesAsync(gitCommandRunner, repositoryPath, baselineRevision, relativePath);
        var baselineText = DecodeText(baselineBytes);

        return new ChangedFileContent(currentText, currentBytes, baselineText, baselineBytes);
    }

    public static string DecodeText(byte[] bytes)
    {
        if (bytes == null)
            return null;

        try
        {
            var encoding = DetectEncoding(bytes, out var preambleLength);
            return encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static async Task<byte[]> TryLoadRevisionBytesAsync(
        GitCommandRunner gitCommandRunner,
        string repositoryPath,
        string revision,
        string relativePath)
    {
        if (gitCommandRunner == null ||
            string.IsNullOrWhiteSpace(repositoryPath) ||
            string.IsNullOrWhiteSpace(revision) ||
            string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var result = await gitCommandRunner.RunBytesAsync(repositoryPath, "show", $"{revision}:{relativePath}");
        return result.IsSuccess ? result.StandardOutput : null;
    }

    private static Encoding DetectEncoding(byte[] bytes, out int preambleLength)
    {
        preambleLength = 0;
        if (bytes.Length >= 4 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xFE &&
            bytes[2] == 0x00 &&
            bytes[3] == 0x00)
        {
            preambleLength = 4;
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true);
        }

        if (bytes.Length >= 4 &&
            bytes[0] == 0x00 &&
            bytes[1] == 0x00 &&
            bytes[2] == 0xFE &&
            bytes[3] == 0xFF)
        {
            preambleLength = 4;
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            preambleLength = 2;
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            preambleLength = 2;
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            preambleLength = 3;

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }
}

internal readonly record struct ChangedFileContent(
    string Text,
    byte[] Bytes,
    string BaselineText,
    byte[] BaselineBytes);
