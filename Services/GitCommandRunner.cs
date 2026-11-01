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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReviewG33k.Services;

public sealed class GitCommandRunner
{
    public async Task<GitCommandResult> RunAsync(string workingDirectory, params string[] arguments)
        => await RunAsync(workingDirectory, CancellationToken.None, arguments);

    public async Task<GitCommandBinaryResult> RunBytesAsync(string workingDirectory, params string[] arguments)
        => await RunBytesAsync(workingDirectory, CancellationToken.None, arguments);

    public async Task<GitCommandResult> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var cancellation = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        });

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        outputBuilder.Append(await outputTask);
        errorBuilder.Append(await errorTask);

        cancellationToken.ThrowIfCancellationRequested();

        return new GitCommandResult(process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString(), string.Join(" ", arguments));
    }

    public async Task<GitCommandBinaryResult> RunBytesAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        await using var outputStream = new MemoryStream();
        var errorBuilder = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var cancellation = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        });

        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(outputStream);
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        await outputTask;
        errorBuilder.Append(await errorTask);

        cancellationToken.ThrowIfCancellationRequested();

        return new GitCommandBinaryResult(process.ExitCode, outputStream.ToArray(), errorBuilder.ToString(), string.Join(" ", arguments));
    }
}
