// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class ConstructorTooLongCodeReviewCheck : CodeReviewCheckBase
{
    private const int ConstructorLineThreshold = 20;

    public override string RuleId => "constructor-too-long";

    public override string DisplayName => "Large constructors";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();
            foreach (var constructor in constructors)
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, constructor.Span))
                    continue;

                var codeLineCount = RoslynCodeReviewCheckUtilities.CountConstructorCodeLines(file, constructor);
                if (codeLineCount < ConstructorLineThreshold)
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(constructor);
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    file.Path,
                    lineNumber,
                    $"Constructor '{constructor.Identifier.ValueText}' contains {codeLineCount} code lines. Consider moving work out of constructor.");
            }
        }
    }
}
