// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace ReviewG33k.Services;

/// <summary>
/// Captures the result of a Git command whose standard output must remain as bytes.
/// </summary>
/// <remarks>
/// Useful when review checks need file content metadata such as byte-order marks or raw newline bytes.
/// </remarks>
public sealed class GitCommandBinaryResult
{
    public GitCommandBinaryResult(int exitCode, byte[] standardOutput, string standardError, string commandLine)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput ?? [];
        StandardError = standardError ?? string.Empty;
        CommandLine = commandLine ?? string.Empty;
    }

    public int ExitCode { get; }

    public byte[] StandardOutput { get; }

    public string StandardError { get; }

    public string CommandLine { get; }

    public bool IsSuccess => ExitCode == 0;
}
