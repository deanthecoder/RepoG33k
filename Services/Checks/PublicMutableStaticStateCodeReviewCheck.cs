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

public sealed class PublicMutableStaticStateCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => "public-mutable-static-state";

    public override string DisplayName => "Public mutable static state";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);

            var fieldDeclarations = root.DescendantNodes().OfType<FieldDeclarationSyntax>();
            foreach (var fieldDeclaration in fieldDeclarations)
            {
                if (!IsPublicStatic(fieldDeclaration.Modifiers))
                    continue;
                if (fieldDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ReadOnlyKeyword) || modifier.IsKind(SyntaxKind.ConstKeyword)))
                    continue;

                foreach (var variable in fieldDeclaration.Declaration?.Variables ?? [])
                {
                    if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, variable.Span))
                        continue;

                    var lineNumber = variable.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    AddFinding(
                        report,
                        CodeReviewFindingSeverity.Important,
                        file.Path,
                        lineNumber,
                        $"Public mutable static field `{variable.Identifier.ValueText}` introduces global mutable state.");
                }
            }

            var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>();
            foreach (var property in properties)
            {
                if (!IsPublicStatic(property.Modifiers))
                    continue;
                if (!HasPublicSetter(property))
                    continue;
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, property.Span))
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(property);
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Important,
                    file.Path,
                    lineNumber,
                    $"Public mutable static property `{property.Identifier.ValueText}` introduces global mutable state.");
            }
        }
    }

    private static bool IsPublicStatic(SyntaxTokenList modifiers) =>
        modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)) &&
        modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword));

    private static bool HasPublicSetter(PropertyDeclarationSyntax property)
    {
        if (property?.AccessorList == null)
            return false;

        var setAccessor = property.AccessorList.Accessors.FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration));
        if (setAccessor == null)
            return false;

        return !setAccessor.Modifiers.Any(modifier =>
            modifier.IsKind(SyntaxKind.PrivateKeyword) ||
            modifier.IsKind(SyntaxKind.ProtectedKeyword) ||
            modifier.IsKind(SyntaxKind.InternalKeyword));
    }
}
