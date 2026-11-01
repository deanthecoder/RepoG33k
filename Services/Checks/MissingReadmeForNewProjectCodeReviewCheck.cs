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
using DTC.Core.Extensions;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Flags newly added project files that do not include a sibling README.
/// </summary>
public sealed class MissingReadmeForNewProjectCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.MissingReadmeForNewProject;

    public override string DisplayName => "New project has README";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.AddedLinesOnly;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        foreach (var file in context.Files.Where(file => file.IsAdded && IsProjectFilePath(file.Path)))
        {
            if (ProjectFolderContainsReadme(file))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                GetLineNumber(file),
                $"New project file '{Path.GetFileName(file.Path)}' was added. Consider adding a 'README.md' for it too.");
        }
    }

    private static bool IsProjectFilePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static bool ProjectFolderContainsReadme(CodeReviewChangedFile file)
    {
        var projectDirectory = Path.GetDirectoryName(file?.FullPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return false;

        return projectDirectory.ToDir().GetFile("README.md").Exists();
    }

    private static int GetLineNumber(CodeReviewChangedFile file) =>
        file?.AddedLineNumbers?.Count > 0
            ? file.AddedLineNumbers.Min()
            : 1;
}
