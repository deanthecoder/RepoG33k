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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class PrivateGetOnlyAutoPropertyShouldBeFieldCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => "private-get-only-auto-property-should-be-field";

    public override string DisplayName => "Private get-only auto properties that should be fields";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>();
            foreach (var property in properties)
            {
                if (!RoslynCodeReviewCheckUtilities.IsNodeNew(file, property))
                    continue;
                if (!RoslynCodeReviewCheckUtilities.IsPrivateProperty(property))
                    continue;
                if (property.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)))
                    continue;
                if (!IsPrivateGetOnlyAutoProperty(property))
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(property);
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    lineNumber,
                    $"Private get-only auto property `{property.Identifier.ValueText}` may be better as a field.");
            }
        }
    }

    private static bool IsPrivateGetOnlyAutoProperty(PropertyDeclarationSyntax property)
    {
        if (property.AccessorList == null || property.ExpressionBody != null)
            return false;

        var accessors = property.AccessorList.Accessors;
        if (accessors.Count != 1)
            return false;

        var accessor = accessors[0];
        return accessor.IsKind(SyntaxKind.GetAccessorDeclaration) &&
               accessor.Body == null &&
               accessor.ExpressionBody == null;
    }
}
