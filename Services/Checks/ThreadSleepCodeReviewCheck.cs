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

public sealed class ThreadSleepCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => "thread-sleep";

    public override string DisplayName => "Thread.Sleep usage";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var hasStaticThreadUsing = root.Usings.Any(IsStaticThreadUsingDirective);

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, invocation.Span))
                    continue;
                if (!IsThreadSleepInvocation(invocation, hasStaticThreadUsing))
                    continue;
                if (!IsInsideAsyncMethod(invocation))
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(invocation);
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    file.Path,
                    lineNumber,
                    "Use of `Thread.Sleep` detected. Prefer `await Task.Delay(...)` if delay is unavoidable.");
            }
        }
    }

    private static bool IsThreadSleepInvocation(InvocationExpressionSyntax invocation, bool hasStaticThreadUsing)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            string.Equals(memberAccess.Name.Identifier.ValueText, "Sleep", StringComparison.Ordinal))
        {
            var target = (memberAccess.Expression?.ToString() ?? string.Empty)
                .Replace("global::", string.Empty, StringComparison.Ordinal);
            return string.Equals(target, "Thread", StringComparison.Ordinal) ||
                   string.Equals(target, "System.Threading.Thread", StringComparison.Ordinal) ||
                   target.EndsWith(".Thread", StringComparison.Ordinal);
        }

        if (invocation.Expression is IdentifierNameSyntax identifierName &&
            hasStaticThreadUsing &&
            string.Equals(identifierName.Identifier.ValueText, "Sleep", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool IsInsideAsyncMethod(InvocationExpressionSyntax invocation)
    {
        var method = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return method?.Modifiers.Any(modifier => string.Equals(modifier.ValueText, "async", StringComparison.Ordinal)) == true;
    }

    private static bool IsStaticThreadUsingDirective(UsingDirectiveSyntax usingDirective)
    {
        if (usingDirective == null ||
            !string.Equals(usingDirective.StaticKeyword.ValueText, "static", StringComparison.Ordinal))
            return false;

        var name = usingDirective.Name?.ToString() ?? string.Empty;
        name = name.Replace("global::", string.Empty, StringComparison.Ordinal);
        return string.Equals(name, "System.Threading.Thread", StringComparison.Ordinal);
    }
}
