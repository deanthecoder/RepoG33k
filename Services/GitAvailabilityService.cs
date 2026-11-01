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
using System.Threading.Tasks;

namespace ReviewG33k.Services;

internal readonly record struct GitAvailabilityCheckResult(bool IsAvailable, string FailureDetails);

internal sealed class GitAvailabilityService
{
    private readonly Func<string, string[], Task<GitCommandResult>> m_runGitCommandAsync;

    public GitAvailabilityService(Func<string, string[], Task<GitCommandResult>> runGitCommandAsync)
    {
        m_runGitCommandAsync = runGitCommandAsync ?? throw new ArgumentNullException(nameof(runGitCommandAsync));
    }

    public async Task<GitAvailabilityCheckResult> CheckAvailabilityAsync(string workingDirectory)
    {
        try
        {
            var versionResult = await m_runGitCommandAsync(workingDirectory, ["--version"]);
            if (versionResult.IsSuccess)
                return new GitAvailabilityCheckResult(true, null);

            return new GitAvailabilityCheckResult(false, versionResult.GetCombinedOutput());
        }
        catch (Exception exception)
        {
            return new GitAvailabilityCheckResult(false, exception.Message);
        }
    }
}
