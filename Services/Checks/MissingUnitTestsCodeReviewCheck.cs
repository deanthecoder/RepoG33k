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
using System.Linq;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class MissingUnitTestsCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => "missing-tests";

    public override string DisplayName => "New non-UI type has new test file";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.AddedLinesOnly;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files.Where(file => file.IsAdded))
        {
            if (CodeReviewFileClassification.IsTestFilePath(file.Path))
                continue;
            if (CodeReviewFileClassification.IsLikelyTestCodeFile(file))
                continue;
            if (CodeReviewFileClassification.IsLikelyInterfaceFilePath(file.Path))
                continue;
            if (CodeReviewFileClassification.IsCodeBehindFilePath(file.Path))
                continue;
            if (CodeReviewFileClassification.IsLikelyUiCodeFile(file))
                continue;

            if (!CodeReviewCheckUtilities.TryGetPublicTypeDeclaration(file.Text, out var typeName, out var declarationLineNumber))
                continue;
            if (string.Equals(typeName, "Program", StringComparison.Ordinal))
                continue;

            var expectedTestFileName = $"{typeName}Tests.cs";
            if (context.ChangedTestFilesByName.Contains(expectedTestFileName))
                continue;

            if (!context.HasAnyChangedTestFiles)
            {
                AddFinding(report, CodeReviewFindingSeverity.Suggestion, file.Path, declarationLineNumber, $"No new unit test(s) added for new type '{typeName}'.");
                continue;
            }

            AddFinding(report, CodeReviewFindingSeverity.Hint, file.Path, declarationLineNumber, $"No matching test file found for new type '{typeName}'.");
        }
    }
}
