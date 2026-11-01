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
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class MissingBlankLineBetweenMethodsCodeReviewCheck : CodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => "missing-blank-line-between-methods";

    public override string DisplayName => "Blank line between methods";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.AddedLinesOnly;

    public bool CanFix(CodeSmellFinding finding) =>
        finding != null &&
        string.Equals(finding.RuleId, RuleId, StringComparison.OrdinalIgnoreCase) &&
        finding.LineNumber > 1;

    public bool TryFix(CodeSmellFinding finding, FileInfo resolvedFile, out string resultMessage)
    {
        if (finding == null)
        {
            resultMessage = "Finding is required.";
            return false;
        }

        if (resolvedFile == null)
        {
            resultMessage = "File path could not be resolved.";
            return false;
        }

        if (!this.TryPrepareFix(
                finding,
                resolvedFile,
                out var sourceText,
                out var targetLineIndex,
                out resultMessage))
        {
            return false;
        }

        if (targetLineIndex <= 0 || targetLineIndex >= sourceText.Lines.Count)
        {
            resultMessage = "Finding line number is out of range for this file.";
            return false;
        }

        var previousLineText = sourceText.Lines[targetLineIndex - 1].ToString();
        if (string.IsNullOrWhiteSpace(previousLineText))
        {
            resultMessage = "A blank line already exists before this method.";
            return false;
        }

        var fileText = sourceText.ToString();
        var newline = fileText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insertPosition = sourceText.Lines[targetLineIndex].Start;
        var updatedText = sourceText.WithChanges(new TextChange(new TextSpan(insertPosition, 0), newline)).ToString();

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
        {
            return false;
        }

        resultMessage = "Inserted a blank line between adjacent methods.";
        return true;
    }

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        if (context == null || report == null)
            return;

        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

            foreach (var type in types)
            {
                var methods = type.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Where(IsMethodDefinition)
                    .OrderBy(method => method.SpanStart)
                    .ToArray();

                for (var index = 0; index < methods.Length - 1; index++)
                {
                    var currentMethod = methods[index];
                    var nextMethod = methods[index + 1];

                    if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, currentMethod.Span) &&
                        !RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, nextMethod.Span))
                    {
                        continue;
                    }

                    var currentEndLine = currentMethod.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
                    var nextStartLine = RoslynCodeReviewCheckUtilities.GetStartLine(nextMethod);
                    if (nextStartLine > currentEndLine + 1)
                        continue;

                    AddFinding(
                        report,
                        CodeReviewFindingSeverity.Hint,
                        file.Path,
                        nextStartLine,
                        $"Add a blank line between methods `{currentMethod.Identifier.ValueText}` and `{nextMethod.Identifier.ValueText}`.");
                }
            }
        }
    }

    private static bool IsMethodDefinition(MethodDeclarationSyntax method) =>
        method != null &&
        method.Body != null;
}
