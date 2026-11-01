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
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Reports reflection calls that look up a member by a hard-coded name.
/// </summary>
/// <remarks>
/// Encourages reviewers to question reflection used to bypass ordinary API design, while allowing reflection enumeration.
/// </remarks>
public sealed class NamedReflectionMemberLookupCodeReviewCheck : CodeReviewCheckBase
{
    private static readonly HashSet<string> NamedMemberLookupMethods = new(StringComparer.Ordinal)
    {
        "GetMember",
        "GetMethod",
        "GetField",
        "GetProperty",
        "GetEvent",
        "GetNestedType"
    };

    public override string RuleId => CodeReviewRuleIds.NamedReflectionMemberLookup;

    public override string DisplayName => "Named reflection member lookup";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, invocation.Span))
                    continue;
                if (!IsNamedMemberLookup(invocation))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    file.Path,
                    RoslynCodeReviewCheckUtilities.GetStartLine(invocation),
                    "Avoid using reflection to look up a named member. Prefer changing the base API or member accessibility when the target is known.");
            }
        }
    }

    private static bool IsNamedMemberLookup(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;
        if (!NamedMemberLookupMethods.Contains(memberAccess.Name.Identifier.ValueText))
            return false;

        var firstArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
        return IsHardCodedMemberName(firstArgument?.Expression);
    }

    private static bool IsHardCodedMemberName(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        if (expression == null)
            return false;

        if (expression.IsKind(SyntaxKind.StringLiteralExpression))
            return true;

        return expression is InvocationExpressionSyntax invocation &&
               invocation.Expression is IdentifierNameSyntax identifier &&
               string.Equals(identifier.Identifier.ValueText, "nameof", StringComparison.Ordinal);
    }
}
