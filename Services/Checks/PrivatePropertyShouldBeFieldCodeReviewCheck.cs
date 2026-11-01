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

public sealed class PrivatePropertyShouldBeFieldCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => "private-property-should-be-field";

    public override string DisplayName => "Simple private properties that should be fields";

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

                var isSimplePrivateAutoProperty = RoslynCodeReviewCheckUtilities.IsSimpleAutoProperty(property);
                var isSimplePrivateWrapperProperty = RoslynCodeReviewCheckUtilities.TryGetSimplePropertyBackingField(property, out _);
                if (!isSimplePrivateAutoProperty && !isSimplePrivateWrapperProperty)
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(property);
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    lineNumber,
                    $"Private property '{property.Identifier.ValueText}' looks field-like and may be better as a field.");
            }
        }
    }
}
