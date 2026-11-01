// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public abstract class RoslynSemanticCodeReviewCheckBase : CodeReviewCheckBase
{
    public sealed override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            if (!RoslynCodeReviewCheckUtilities.TryGetSemanticAnalysis(
                    file,
                    out var root,
                    out var semanticModel,
                    out var syntaxTree,
                    out var diagnostics))
            {
                continue;
            }

            if (RoslynCodeReviewCheckUtilities.HasSourceErrorsForTree(diagnostics, syntaxTree))
                continue;

            AnalyzeFile(context, file, root, semanticModel, report);
        }
    }

    protected abstract void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report);
}
