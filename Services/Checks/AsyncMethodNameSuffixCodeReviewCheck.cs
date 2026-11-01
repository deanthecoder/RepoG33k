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
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class AsyncMethodNameSuffixCodeReviewCheck : CodeReviewCheckBase
{
    private static readonly Regex TestAttributeNameRegex = new(
        @"(?:^|\.)(?:Test|TestAttribute)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override string RuleId => "async-method-name-suffix";

    public override string DisplayName => "Async method name ends with Async";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var isLikelyTestFile = CodeReviewFileClassification.IsLikelyTestCodeFile(file);

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!RoslynCodeReviewCheckUtilities.IsNodeNew(file, method))
                    continue;

                if (!method.Modifiers.Any(modifier => modifier.RawKind == (int)SyntaxKind.AsyncKeyword))
                    continue;

                if (!method.Modifiers.Any(modifier =>
                        modifier.RawKind == (int)SyntaxKind.PublicKeyword ||
                        modifier.RawKind == (int)SyntaxKind.PrivateKeyword ||
                        modifier.RawKind == (int)SyntaxKind.InternalKeyword))
                {
                    continue;
                }
                if (method.Modifiers.Any(modifier => modifier.RawKind == (int)SyntaxKind.OverrideKeyword))
                    continue;
                if (method.ExplicitInterfaceSpecifier != null)
                    continue;

                var methodName = method.Identifier.ValueText;
                if (string.IsNullOrWhiteSpace(methodName) || methodName.EndsWith("Async", StringComparison.Ordinal))
                    continue;

                if (isLikelyTestFile && HasTestAttribute(method))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    RoslynCodeReviewCheckUtilities.GetStartLine(method),
                    $"Async method '{methodName}' should end with 'Async'.");
            }
        }
    }

    private static bool HasTestAttribute(MethodDeclarationSyntax method)
    {
        if (method == null)
            return false;

        foreach (var attributeList in method.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attributeName = attribute.Name?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(attributeName))
                    continue;

                if (TestAttributeNameRegex.IsMatch(attributeName))
                    return true;
            }
        }

        return false;
    }
}
