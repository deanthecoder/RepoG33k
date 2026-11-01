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
using System.Text.RegularExpressions;
using DTC.Core.Extensions;

namespace ReviewG33k.Services;

internal sealed class LogNavigationService
{
    private static readonly Regex LocationRegex = new(@"\[(?<path>[^\]]+?\.[^:\]]+):(?<line>\d+)\]", RegexOptions.Compiled);

    public bool TryParseLogLocation(string text, out string filePath, out int lineNumber)
    {
        filePath = null;
        lineNumber = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = LocationRegex.Match(text);
        if (!match.Success)
            return false;

        var pathValue = match.Groups["path"].Value.Trim();
        if (!int.TryParse(match.Groups["line"].Value, out lineNumber) || lineNumber < 1)
            return false;

        filePath = pathValue;
        return true;
    }

    public bool TryResolveLogFile(string pathFromLog, string reviewWorktreePath, out FileInfo resolvedFile)
    {
        resolvedFile = null;

        if (string.IsNullOrWhiteSpace(pathFromLog))
            return false;

        var normalizedRelativePath = pathFromLog
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalizedRelativePath))
        {
            var absoluteFile = normalizedRelativePath.ToFile();
            if (!absoluteFile.Exists())
                return false;

            resolvedFile = absoluteFile;
            return true;
        }

        if (string.IsNullOrWhiteSpace(reviewWorktreePath))
            return false;

        var candidatePath = reviewWorktreePath.ToDir().GetFile(normalizedRelativePath);
        if (!candidatePath.Exists())
            return false;

        resolvedFile = candidatePath;
        return true;
    }
}
