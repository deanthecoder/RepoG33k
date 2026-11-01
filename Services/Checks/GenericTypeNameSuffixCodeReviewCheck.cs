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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class GenericTypeNameSuffixCodeReviewCheck : CodeReviewCheckBase
{
    private static readonly string[] GenericSuffixes = ["Utils", "Helper", "Manager", "Utilities"];

    public override string RuleId => "generic-type-name-suffix";

    public override string DisplayName => "Generic class name suffixes";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classDeclaration in classes)
            {
                if (!RoslynCodeReviewCheckUtilities.IsNodeNew(file, classDeclaration))
                    continue;

                var className = classDeclaration.Identifier.ValueText;
                var suffix = GenericSuffixes.FirstOrDefault(candidateSuffix =>
                    className.EndsWith(candidateSuffix, StringComparison.OrdinalIgnoreCase));
                if (suffix == null)
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(classDeclaration);
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    lineNumber,
                    $"Class name '{className}' ends with generic suffix '{suffix}'. Consider a more specific name.");
            }
        }
    }
}
