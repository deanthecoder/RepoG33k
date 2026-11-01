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
using Microsoft.CodeAnalysis.Text;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Reports if statements with empty bodies, including comment-only blocks.
/// </summary>
/// <remarks>
/// Highlights missing logic and unnecessary branches without automatically changing condition side effects.
/// </remarks>
public sealed class EmptyIfCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.EmptyIf;

    public override string DisplayName => "Empty if body";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        foreach (var file in context.Files)
        {
            if (!RoslynCodeReviewCheckUtilities.IsRoslynCSharpFile(file))
                continue;

            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            foreach (var statement in root.DescendantNodes().OfType<IfStatementSyntax>())
            {
                if (statement.Statement is not BlockSyntax { Statements.Count: 0 } and not EmptyStatementSyntax)
                    continue;
                if (statement.ContainsDiagnostics || statement.Statement.ContainsDirectives)
                    continue;

                // Changes in an else branch alone do not introduce an empty if body.
                var span = TextSpan.FromBounds(statement.SpanStart, statement.Statement.Span.End);
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, span))
                    continue;

                var message = statement.Else == null
                    ? "If body is empty; add the intended logic or remove the unnecessary branch while preserving any condition side effects."
                    : "If body is empty; consider inverting the condition and moving the else body into the if branch.";
                AddFinding(report, CodeReviewFindingSeverity.Suggestion, file.Path,
                    RoslynCodeReviewCheckUtilities.GetStartLine(statement), message);
            }
        }
    }
}
