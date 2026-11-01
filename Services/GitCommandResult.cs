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

namespace ReviewG33k.Services;

public sealed class GitCommandResult
{
    public GitCommandResult(int exitCode, string standardOutput, string standardError, string commandText)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput ?? string.Empty;
        StandardError = standardError ?? string.Empty;
        CommandText = commandText ?? string.Empty;
    }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public string CommandText { get; }

    public bool IsSuccess => ExitCode == 0;

    public string GetCombinedOutput()
    {
        if (string.IsNullOrWhiteSpace(StandardError))
            return StandardOutput.Trim();

        if (string.IsNullOrWhiteSpace(StandardOutput))
            return StandardError.Trim();

        return $"{StandardOutput.Trim()}{Environment.NewLine}{StandardError.Trim()}";
    }
}