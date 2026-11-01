// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core;
using DTC.Core.Extensions;
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class MainWindowInputValidationServiceTests
{
    [Test]
    public void ValidatePullRequestPrepareInputsWhenRepositoryRootMissingReturnsFailure()
    {
        var service = new MainWindowInputValidationService();

        var result = service.ValidatePullRequestPrepareInputs(null, "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/42");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusMessage, Is.EqualTo("Set repo root folder first."));
            Assert.That(result.DialogTitle, Is.EqualTo("Repository root required"));
        });
    }

    [Test]
    public void ValidatePullRequestPrepareInputsWhenUrlMissingReturnsFailure()
    {
        var service = new MainWindowInputValidationService();
        var existingDirectory = Environment.CurrentDirectory;

        var result = service.ValidatePullRequestPrepareInputs(existingDirectory, "");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusMessage, Is.EqualTo("Paste or drop a Bitbucket pull request URL."));
            Assert.That(result.DialogTitle, Is.EqualTo("Pull request URL required"));
        });
    }

    [Test]
    public void ValidateLocalCommittedReviewInputsWhenBaseBranchMissingReturnsFailure()
    {
        var service = new MainWindowInputValidationService();
        var existingDirectory = FindRepositoryRoot() ?? Environment.CurrentDirectory;

        var result = service.ValidateLocalCommittedReviewInputs(existingDirectory, " ");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusMessage, Is.EqualTo("Enter a base branch (for example: main)."));
            Assert.That(result.DialogTitle, Is.EqualTo("Base branch required"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current != null)
        {
            if (current.GetDir(".git").Exists())
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }
}
