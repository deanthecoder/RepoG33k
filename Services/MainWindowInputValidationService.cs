// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.Extensions;

namespace ReviewG33k.Services;

internal sealed class MainWindowInputValidationService
{
    public InputValidationResult ValidatePullRequestPrepareInputs(string repositoryRoot, string pullRequestUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return new InputValidationResult(
                false,
                "Set repo root folder first.",
                "Repository root required",
                "Set the repo root folder before preparing a review checkout.");
        }

        if (!repositoryRoot.ToDir().Exists())
        {
            return new InputValidationResult(
                false,
                $"Repo root folder does not exist: {repositoryRoot}",
                "Repository root not found",
                repositoryRoot);
        }

        if (string.IsNullOrWhiteSpace(pullRequestUrl))
        {
            return new InputValidationResult(
                false,
                "Paste or drop a Bitbucket pull request URL.",
                "Pull request URL required",
                "Paste or drop a Bitbucket pull request URL.");
        }

        return InputValidationResult.Success;
    }

    public InputValidationResult ValidateLocalRepositoryInput(string localRepositoryPath)
    {
        if (string.IsNullOrWhiteSpace(localRepositoryPath))
        {
            return new InputValidationResult(false, "Set local repository folder first.",
                "Local repository required", "Choose the local repository folder to review.");
        }
        if (!localRepositoryPath.ToDir().Exists())
        {
            return new InputValidationResult(false, $"Local repository folder does not exist: {localRepositoryPath}",
                "Local repository not found", localRepositoryPath);
        }

        if (!RepositoryUtilities.IsGitRepository(localRepositoryPath))
        {
            return new InputValidationResult(
                false,
                "Selected folder is not a Git repository.",
                "Invalid repository folder",
                "The selected folder does not appear to contain a Git repository.");
        }

        return InputValidationResult.Success;
    }

    public InputValidationResult ValidateLocalCommittedReviewInputs(string localRepositoryPath, string baseBranch)
    {
        var localRepositoryValidation = ValidateLocalRepositoryInput(localRepositoryPath);
        if (!localRepositoryValidation.IsValid)
            return localRepositoryValidation;

        if (string.IsNullOrWhiteSpace(baseBranch))
        {
            return new InputValidationResult(
                false,
                "Enter a base branch (for example: main).",
                "Base branch required",
                "Enter the branch to compare against (for example: main or develop).");
        }

        return InputValidationResult.Success;
    }
}

internal readonly record struct InputValidationResult(
    bool IsValid,
    string StatusMessage,
    string DialogTitle,
    string DialogMessage)
{
    public static InputValidationResult Success => new(true, null, null, null);
}
