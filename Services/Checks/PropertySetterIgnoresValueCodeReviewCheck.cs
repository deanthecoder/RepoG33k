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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Reports property setters that do not read the implicit `value` parameter.
/// </summary>
/// <remarks>
/// Catches accidental setters that assign constants, recompute unrelated state, or otherwise ignore the value being assigned.
/// </remarks>
public sealed class PropertySetterIgnoresValueCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.PropertySetterIgnoresValue;

    public override string DisplayName => "Property setter ignores value";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                var setter = property.AccessorList?.Accessors.FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration));
                if (setter == null)
                    continue;
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, setter.Span))
                    continue;
                if (!HasSetterImplementation(setter))
                    continue;
                if (UsesValue(setter) || ThrowsWithoutUsingValue(setter))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    file.Path,
                    RoslynCodeReviewCheckUtilities.GetStartLine(setter),
                    $"Setter for property `{property.Identifier.ValueText}` does not use `value`.");
            }
        }
    }

    private static bool HasSetterImplementation(AccessorDeclarationSyntax setter) =>
        setter?.Body != null || setter?.ExpressionBody != null;

    private static bool UsesValue(AccessorDeclarationSyntax setter) =>
        setter.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => string.Equals(identifier.Identifier.ValueText, "value", StringComparison.Ordinal));

    private static bool ThrowsWithoutUsingValue(AccessorDeclarationSyntax setter)
    {
        if (setter.ExpressionBody?.Expression is ThrowExpressionSyntax)
            return true;

        var statements = setter.Body?.Statements;
        return statements.HasValue &&
               statements.Value.Count == 1 &&
               statements.Value[0] is ThrowStatementSyntax;
    }
}
