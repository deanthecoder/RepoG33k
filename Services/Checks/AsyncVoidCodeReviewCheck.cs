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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class AsyncVoidCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => "async-void";

    public override string DisplayName => "Async void (non-event handlers)";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                if (!RoslynCodeReviewCheckUtilities.IsNodeNew(file, method))
                    continue;
                if (!method.Modifiers.Any(modifier => modifier.RawKind == (int)SyntaxKind.AsyncKeyword))
                    continue;
                if (!IsVoidReturnType(method.ReturnType))
                    continue;
                if (IsLikelyEventHandler(method))
                    continue;
                if (IsLikelyCommandExecuteOverride(method))
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(method);
                AddFinding(report, CodeReviewFindingSeverity.Important, file.Path, lineNumber, "Suspicious 'async void' usage (non-event handler).");
            }
        }
    }

    private static bool IsVoidReturnType(TypeSyntax returnType) =>
        string.Equals(returnType?.ToString(), "void", StringComparison.Ordinal);

    private static bool IsLikelyEventHandler(MethodDeclarationSyntax method)
    {
        var parameters = method?.ParameterList?.Parameters;
        if (parameters == null || parameters.Value.Count != 2)
            return false;

        return IsObjectLike(parameters.Value[0].Type) && IsEventArgsLike(parameters.Value[1].Type);
    }

    private static bool IsLikelyCommandExecuteOverride(MethodDeclarationSyntax method)
    {
        if (method == null)
            return false;
        if (!string.Equals(method.Identifier.ValueText, "Execute", StringComparison.Ordinal))
            return false;
        if (!method.Modifiers.Any(modifier => modifier.RawKind == (int)SyntaxKind.OverrideKeyword))
            return false;

        var parameters = method.ParameterList?.Parameters;
        if (parameters == null || parameters.Value.Count != 1)
            return false;

        return IsObjectLike(parameters.Value[0].Type);
    }

    private static bool IsObjectLike(TypeSyntax type)
    {
        if (type == null)
            return false;

        var normalized = NormalizeTypeName(type.ToString());
        return string.Equals(normalized, "object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "System.Object", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEventArgsLike(TypeSyntax type)
    {
        if (type == null)
            return false;

        var normalized = NormalizeTypeName(type.ToString());
        return normalized.EndsWith("EventArgs", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return string.Empty;

        var normalized = typeName.Trim();
        normalized = normalized.Replace("global::", string.Empty, StringComparison.Ordinal);
        if (normalized.EndsWith("?", StringComparison.Ordinal))
            normalized = normalized[..^1];

        return normalized.Trim();
    }
}
